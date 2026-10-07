// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Runtime.InteropServices;

namespace Meltype.IME;

/// <summary>
/// Text Services Framework 経由の入力言語切替 (設計書 §10)。
/// Meltype は別プロセスなので対象アプリのスレッドマネージャーには触れられない。
/// ここでは ITfInputProcessorProfileMgr で日本語 TIP (MS-IME など) のプロファイルを
/// セッション全体に対して有効化する。IME の開閉は TSF からは他プロセスに対して行えないので扱わない。
///
/// COM オブジェクトは作成したスレッドでしか使わない (再入力ワーカーの STA スレッド)。
/// </summary>
public sealed class TsfImeController : IImeBackend
{
    private static readonly Guid ClsidInputProcessorProfiles = new("33C53A50-F456-4884-B049-85FD643ECFED");
    private static readonly Guid GuidTfcatTipKeyboard = new("34745C63-B2F0-4784-8B67-5E12C8701A31");
    private static readonly Guid ClsidMsIme = new("03B5835F-F03C-411B-9CE2-AA23E1171E36");
    private const uint TF_PROFILETYPE_INPUTPROCESSOR = 1;
    private const uint TF_IPP_FLAG_ACTIVE = 1, TF_IPP_FLAG_ENABLED = 2;
    private const uint TF_IPPMF_DONTCARECURRENTINPUTLANGUAGE = 0x0004, TF_IPPMF_FORSESSION = 0x20000000;
    private const ushort LangJapanese = 0x0411;

    private ITfInputProcessorProfileMgr? _manager;
    private int _ownerThread;
    private bool _unavailable;

    public string Name => "TSF";

    public ImeState GetState(ImeTarget target)
    {
        // 対象アプリのスレッドの言語は IMM32 と同じく GetKeyboardLayout で見るのが確実。
        var hkl = Native.GetKeyboardLayout(target.ThreadId);
        var language = (hkl.ToInt64() & 0xFFFF) == LangJapanese ? InputLanguage.Japanese : InputLanguage.Other;
        return new ImeState(language, ImeMode.Unknown, 0, hkl);
    }

    public bool TrySetLanguage(ImeTarget target, InputLanguage language, int timeoutMs)
    {
        if (language != InputLanguage.Japanese) return false;
        var manager = GetManager();
        if (manager is null) return false;
        try
        {
            var profile = FindJapaneseProfile(manager);
            if (profile is not { } p) return false;
            var clsid = p.clsid;
            var guidProfile = p.guidProfile;
            var hr = manager.ActivateProfile(p.dwProfileType, p.langid, ref clsid, ref guidProfile, p.hkl,
                TF_IPPMF_FORSESSION | TF_IPPMF_DONTCARECURRENTINPUTLANGUAGE);
            if (hr < 0)
            {
                Diagnostics.Log.Warn($"TSF ActivateProfile が失敗しました (0x{hr:X8})。");
                return false;
            }
            return ImeController.WaitFor(() => GetState(target).Language == InputLanguage.Japanese, timeoutMs);
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"TSF での言語切替に失敗しました: {ex.Message}");
            return false;
        }
    }

    public bool TrySetOpen(ImeTarget target, bool open, int? conversionMode, int timeoutMs) => false;

    /// <summary>有効な日本語 TIP を探す。MS-IME を優先し、無ければ他の TSF 対応 IME を使う。</summary>
    private static TF_INPUTPROCESSORPROFILE? FindJapaneseProfile(ITfInputProcessorProfileMgr manager)
    {
        if (manager.EnumProfiles(LangJapanese, out var enumerator) < 0 || enumerator is null) return null;
        TF_INPUTPROCESSORPROFILE? fallback = null;
        try
        {
            while (enumerator.Next(1, out var profile, out var fetched) == 0 && fetched == 1)
            {
                if (profile.dwProfileType != TF_PROFILETYPE_INPUTPROCESSOR) continue;
                if ((profile.dwFlags & TF_IPP_FLAG_ENABLED) == 0) continue;
                if (profile.catid != GuidTfcatTipKeyboard) continue;
                if (profile.clsid == ClsidMsIme) return profile;
                fallback ??= profile;
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
        return fallback;
    }

    private ITfInputProcessorProfileMgr? GetManager()
    {
        if (_unavailable) return null;
        if (_manager is not null)
        {
            if (_ownerThread == Environment.CurrentManagedThreadId) return _manager;
            Diagnostics.Log.Warn("TSF は作成したスレッド以外からは使いません。");
            return null;
        }
        try
        {
            var type = Type.GetTypeFromCLSID(ClsidInputProcessorProfiles, throwOnError: true)!;
            _manager = (ITfInputProcessorProfileMgr)Activator.CreateInstance(type)!;
            _ownerThread = Environment.CurrentManagedThreadId;
            return _manager;
        }
        catch (Exception ex)
        {
            _unavailable = true;
            Diagnostics.Log.Warn($"TSF を初期化できませんでした: {ex.Message}");
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TF_INPUTPROCESSORPROFILE
    {
        public uint dwProfileType;
        public ushort langid;
        public Guid clsid;
        public Guid guidProfile;
        public Guid catid;
        public IntPtr hklSubstitute;
        public uint dwCaps;
        public IntPtr hkl;
        public uint dwFlags;
    }

    [ComImport, Guid("71c6e74c-0f28-11d8-a82a-00065b84435c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITfInputProcessorProfileMgr
    {
        [PreserveSig] int ActivateProfile(uint dwProfileType, ushort langid, ref Guid clsid, ref Guid guidProfile, IntPtr hkl, uint dwFlags);
        [PreserveSig] int DeactivateProfile(uint dwProfileType, ushort langid, ref Guid clsid, ref Guid guidProfile, IntPtr hkl, uint dwFlags);
        [PreserveSig] int GetProfile(uint dwProfileType, ushort langid, ref Guid clsid, ref Guid guidProfile, IntPtr hkl, out TF_INPUTPROCESSORPROFILE profile);
        [PreserveSig] int EnumProfiles(ushort langid, out IEnumTfInputProcessorProfiles? enumerator);
        [PreserveSig] int ReleaseInputProcessor(ref Guid clsid, uint dwFlags);
        // RegisterProfile / UnregisterProfile は使わないが、vtable の並びを保つために宣言だけしておく。
        [PreserveSig] int RegisterProfile();
        [PreserveSig] int UnregisterProfile();
        [PreserveSig] int GetActiveProfile(ref Guid catid, out TF_INPUTPROCESSORPROFILE profile);
    }

    [ComImport, Guid("71c6e74d-0f28-11d8-a82a-00065b84435c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumTfInputProcessorProfiles
    {
        [PreserveSig] int Clone(out IEnumTfInputProcessorProfiles? enumerator);
        [PreserveSig] int Next(uint count, out TF_INPUTPROCESSORPROFILE profile, out uint fetched);
        [PreserveSig] int Reset();
        [PreserveSig] int Skip(uint count);
    }
}
