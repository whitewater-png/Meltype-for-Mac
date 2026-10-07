// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Runtime.InteropServices;

namespace Meltype.Composition;

/// <summary>
/// Windows の UI Automation (UIAutomationCore.dll) を COM で直接使う薄い包み。
/// .NET の System.Windows.Automation は WPF 一式 (約 17MB) を必要とし、配布用パッケージが大きくなるので使わない。
/// フォーカスのある要素のプロパティ (種類・名前・パスワード欄か・読み取り専用か・位置) と、
/// テキストのキャレットの前後の文字列だけを取る。作成した MTA スレッドからだけ使う。
/// </summary>
internal sealed class UiAutomation
{
    // プロパティ ID・パターン ID・コントロールの種類 (UIAutomationClient.h)
    public const int ControlTypeEdit = 50004, ControlTypeDocument = 50030, ControlTypeComboBox = 50003;
    private const int BoundingRectangleProperty = 30001, ControlTypeProperty = 30003, NameProperty = 30005,
        IsKeyboardFocusableProperty = 30009, ClassNameProperty = 30012, IsPasswordProperty = 30019,
        ValueValueProperty = 30045, ValueIsReadOnlyProperty = 30046, IsValuePatternAvailableProperty = 30043,
        IsTextPatternAvailableProperty = 30040;
    private const int TextPatternId = 10014;
    private const int TextUnitCharacter = 0;
    private const int EndpointStart = 0, EndpointEnd = 1;

    private static readonly Guid ClsidCUIAutomation = new("FF48DBA4-60EF-4201-AA87-54103EEF594E");

    private readonly IUIAutomation _automation;

    public UiAutomation()
    {
        var type = Type.GetTypeFromCLSID(ClsidCUIAutomation, throwOnError: true)!;
        _automation = (IUIAutomation)Activator.CreateInstance(type)!;
    }

    /// <summary>フォーカスのある要素。無ければ null。</summary>
    public Element? Focused()
    {
        var hr = _automation.GetFocusedElement(out var element);
        return hr >= 0 && element is not null ? new Element(element) : null;
    }

    /// <summary>画面全体 (デスクトップ) の要素。自己診断用。</summary>
    public Element? Root()
    {
        var hr = _automation.GetRootElement(out var element);
        return hr >= 0 && element is not null ? new Element(element) : null;
    }

    public sealed class Element
    {
        private readonly IUIAutomationElement _element;

        internal Element(IUIAutomationElement element) => _element = element;

        private object? Get(int property) => _element.GetCurrentPropertyValue(property, out var value) >= 0 ? value : null;

        public int ControlType => Get(ControlTypeProperty) is int type ? type : 0;
        public string Name => Get(NameProperty) as string ?? "";
        public string ClassName => Get(ClassNameProperty) as string ?? "";
        public bool IsPassword => Get(IsPasswordProperty) is true;
        public bool IsKeyboardFocusable => Get(IsKeyboardFocusableProperty) is true;
        public bool HasValuePattern => Get(IsValuePatternAvailableProperty) is true;
        public bool HasTextPattern => Get(IsTextPatternAvailableProperty) is true;
        public bool IsReadOnly => Get(ValueIsReadOnlyProperty) is true;
        public string Value => Get(ValueValueProperty) as string ?? "";

        public Rectangle? Bounds =>
            Get(BoundingRectangleProperty) is double[] { Length: 4 } r && r[2] > 0 && r[3] > 0 && !double.IsInfinity(r[2])
                ? new Rectangle((int)r[0], (int)r[1], (int)r[2], (int)r[3])
                : null;

        /// <summary>テキストのキャレット (選択範囲) の前後の文字列 (それぞれ最大 count 文字)。テキストパターンが無ければ null。</summary>
        public (string? Before, string? After)? Surrounding(int count)
        {
            if (_element.GetCurrentPattern(TextPatternId, out var patternObject) < 0 || patternObject is not IUIAutomationTextPattern pattern) return null;
            if (pattern.GetSelection(out var ranges) < 0 || ranges is null) return null;
            if (ranges.get_Length(out var length) < 0 || length == 0 || ranges.GetElement(0, out var selection) < 0 || selection is null) return null;

            selection.Clone(out var before);
            before.MoveEndpointByRange(EndpointEnd, before, EndpointStart);
            before.MoveEndpointByUnit(EndpointStart, TextUnitCharacter, -count, out _);
            before.GetText(count * 2, out var beforeText);

            selection.Clone(out var after);
            after.MoveEndpointByRange(EndpointStart, after, EndpointEnd);
            after.MoveEndpointByUnit(EndpointEnd, TextUnitCharacter, count, out _);
            after.GetText(count * 2, out var afterText);
            return (beforeText, afterText);
        }

        /// <summary>
        /// キャレット (入力位置) の画面上の四角形 (幅 1)。テキストパターンで取れなければ null。
        /// 何も選んでいない (幅 0 の) 範囲は四角形を返さないアプリが多いので、前の 1 文字の右端か、次の 1 文字の左端を使う。
        /// </summary>
        public Rectangle? CaretBounds()
        {
            if (_element.GetCurrentPattern(TextPatternId, out var patternObject) < 0 || patternObject is not IUIAutomationTextPattern pattern) return null;
            if (pattern.GetSelection(out var ranges) < 0 || ranges is null) return null;
            if (ranges.get_Length(out var length) < 0 || length == 0 || ranges.GetElement(0, out var selection) < 0 || selection is null) return null;

            if (FirstRectangle(selection) is { } own) return new Rectangle(own.Left, own.Top, 1, own.Height);
            selection.Clone(out var before);
            before.MoveEndpointByRange(EndpointEnd, before, EndpointStart);
            before.MoveEndpointByUnit(EndpointStart, TextUnitCharacter, -1, out var movedBack);
            if (movedBack != 0 && LastRectangle(before) is { } previous) return new Rectangle(previous.Right, previous.Top, 1, previous.Height);
            selection.Clone(out var after);
            after.MoveEndpointByRange(EndpointStart, after, EndpointEnd);
            after.MoveEndpointByUnit(EndpointEnd, TextUnitCharacter, 1, out var movedForward);
            if (movedForward != 0 && FirstRectangle(after) is { } next) return new Rectangle(next.Left, next.Top, 1, next.Height);
            return null;
        }

        private static Rectangle? FirstRectangle(IUIAutomationTextRange range) => Rectangles(range).FirstOrDefault() is { Height: > 0 } r ? r : null;

        private static Rectangle? LastRectangle(IUIAutomationTextRange range) => Rectangles(range).LastOrDefault() is { Height: > 0 } r ? r : null;

        private static List<Rectangle> Rectangles(IUIAutomationTextRange range)
        {
            var list = new List<Rectangle>();
            if (range.GetBoundingRectangles(out var values) < 0 || values is null) return list;
            for (var i = 0; i + 3 < values.Length; i += 4)
            {
                if (values[i + 3] > 0) list.Add(new Rectangle((int)values[i], (int)values[i + 1], Math.Max(1, (int)values[i + 2]), (int)values[i + 3]));
            }
            return list;
        }
    }

    // ---- COM の定義 (UIAutomationClient.h の順番どおり。使わないメソッドは並びを保つための仮の宣言) ----

    [ComImport, Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        [PreserveSig] int CompareElements();
        [PreserveSig] int CompareRuntimeIds();
        [PreserveSig] int GetRootElement(out IUIAutomationElement? root);
        [PreserveSig] int ElementFromHandle();
        [PreserveSig] int ElementFromPoint();
        [PreserveSig] int GetFocusedElement(out IUIAutomationElement? element);
    }

    [ComImport, Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IUIAutomationElement
    {
        [PreserveSig] int SetFocus();
        [PreserveSig] int GetRuntimeId();
        [PreserveSig] int FindFirst();
        [PreserveSig] int FindAll();
        [PreserveSig] int FindFirstBuildCache();
        [PreserveSig] int FindAllBuildCache();
        [PreserveSig] int BuildUpdatedCache();
        [PreserveSig] int GetCurrentPropertyValue(int propertyId, [MarshalAs(UnmanagedType.Struct)] out object? value);
        [PreserveSig] int GetCurrentPropertyValueEx();
        [PreserveSig] int GetCachedPropertyValue();
        [PreserveSig] int GetCachedPropertyValueEx();
        [PreserveSig] int GetCurrentPatternAs();
        [PreserveSig] int GetCachedPatternAs();
        [PreserveSig] int GetCurrentPattern(int patternId, [MarshalAs(UnmanagedType.IUnknown)] out object? pattern);
    }

    [ComImport, Guid("32EBA289-3583-42C9-9C59-3B6D9A1E9B6A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextPattern
    {
        [PreserveSig] int RangeFromPoint();
        [PreserveSig] int RangeFromChild();
        [PreserveSig] int GetSelection(out IUIAutomationTextRangeArray? ranges);
    }

    [ComImport, Guid("CE4AE76A-E717-4C98-81EA-47371D028EB6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextRangeArray
    {
        [PreserveSig] int get_Length(out int length);
        [PreserveSig] int GetElement(int index, out IUIAutomationTextRange? range);
    }

    [ComImport, Guid("A543CC6A-F4AE-494B-8239-C814481187A8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextRange
    {
        [PreserveSig] int Clone(out IUIAutomationTextRange range);
        [PreserveSig] int Compare();
        [PreserveSig] int CompareEndpoints();
        [PreserveSig] int ExpandToEnclosingUnit();
        [PreserveSig] int FindAttribute();
        [PreserveSig] int FindText();
        [PreserveSig] int GetAttributeValue();
        [PreserveSig] int GetBoundingRectangles([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_R8)] out double[]? rectangles);
        [PreserveSig] int GetEnclosingElement();
        [PreserveSig] int GetText(int maxLength, [MarshalAs(UnmanagedType.BStr)] out string? text);
        [PreserveSig] int Move();
        [PreserveSig] int MoveEndpointByUnit(int endpoint, int unit, int count, out int moved);
        [PreserveSig] int MoveEndpointByRange(int endpoint, IUIAutomationTextRange range, int targetEndpoint);
    }
}
