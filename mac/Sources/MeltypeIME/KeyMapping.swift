// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit
import Carbon.HIToolbox

/// Mac のキー (キーコード・文字) を、Meltype の本体が使う Windows の仮想キーコードにする。
/// 本体が見るのは「特別なキーか (Enter・矢印・F7 …)」と「英字・数字のキーか」だけで、入力する文字は別に渡す。
enum KeyMapping {
    /// 文字を入力するキーで、特別な扱いのないもの (記号など)。本体では文字だけを見る。
    private static let otherCharacterKey: Int32 = 0x07

    /// JIS キーボードのかな入力用: 物理キー (keyCode) から、本体の JIS かな配列 (KanaDetector) が引く仮想キーコードにする。
    /// 文字ではなく物理キーの位置で決める (かなは刻印でなくキーの位置で決まるため。配列を変えていても同じ位置が同じかなになる)。
    /// かなのキーでなければ nil。US 配列などでは記号キーの位置が違うので、JIS キーボード専用。
    static func kanaVirtualKey(for keyCode: Int) -> Int32? {
        switch keyCode {
        case kVK_ANSI_1: return 0x31
        case kVK_ANSI_2: return 0x32
        case kVK_ANSI_3: return 0x33
        case kVK_ANSI_4: return 0x34
        case kVK_ANSI_5: return 0x35
        case kVK_ANSI_6: return 0x36
        case kVK_ANSI_7: return 0x37
        case kVK_ANSI_8: return 0x38
        case kVK_ANSI_9: return 0x39
        case kVK_ANSI_0: return 0x30
        case kVK_ANSI_A: return 0x41
        case kVK_ANSI_B: return 0x42
        case kVK_ANSI_C: return 0x43
        case kVK_ANSI_D: return 0x44
        case kVK_ANSI_E: return 0x45
        case kVK_ANSI_F: return 0x46
        case kVK_ANSI_G: return 0x47
        case kVK_ANSI_H: return 0x48
        case kVK_ANSI_I: return 0x49
        case kVK_ANSI_J: return 0x4A
        case kVK_ANSI_K: return 0x4B
        case kVK_ANSI_L: return 0x4C
        case kVK_ANSI_M: return 0x4D
        case kVK_ANSI_N: return 0x4E
        case kVK_ANSI_O: return 0x4F
        case kVK_ANSI_P: return 0x50
        case kVK_ANSI_Q: return 0x51
        case kVK_ANSI_R: return 0x52
        case kVK_ANSI_S: return 0x53
        case kVK_ANSI_T: return 0x54
        case kVK_ANSI_U: return 0x55
        case kVK_ANSI_V: return 0x56
        case kVK_ANSI_W: return 0x57
        case kVK_ANSI_X: return 0x58
        case kVK_ANSI_Y: return 0x59
        case kVK_ANSI_Z: return 0x5A
        // 以下は JIS キーボードでの刻印。ANSI の名前は keyCode の位置を指す。
        case kVK_ANSI_Minus: return 0xBD        // -   ほ
        case kVK_ANSI_Equal: return 0xDE        // ^   へ
        case kVK_JIS_Yen: return 0xDC           // ¥   ー
        case kVK_ANSI_LeftBracket: return 0xC0  // @   ゛
        case kVK_ANSI_RightBracket: return 0xDB // [   ゜
        case kVK_ANSI_Semicolon: return 0xBB    // ;   れ
        case kVK_ANSI_Quote: return 0xBA        // :   け
        case kVK_ANSI_Backslash: return 0xDD    // ]   む
        case kVK_ANSI_Comma: return 0xBC        // ,   ね
        case kVK_ANSI_Period: return 0xBE       // .   る
        case kVK_ANSI_Slash: return 0xBF        // /   め
        case kVK_JIS_Underscore: return 0xE2    // _   ろ
        default: return nil
        }
    }

    /// 物理キーボードが JIS か。かな入力 (JIS) は記号キーの位置が JIS 前提なので、US (ANSI) などでは使わない (記号キーがずれて誤入力になるため)。
    /// KBGetLayoutType は、キーボードの種類の ID (LMGetKbdType) から物理配列を返すだけの軽い呼び出し。
    /// 入力のたびに呼んでも重くないが、キーボードを付け替える場合に備えて覚えず毎回見る。
    static var isJISKeyboard: Bool {
        KBGetLayoutType(Int16(truncatingIfNeeded: LMGetKbdType())) == OSType(kKeyboardJIS)
    }

    /// kana が true のとき (設定が「かな入力 (JIS)」で、Control・Option・Command を押していないとき) は、かなのキーを物理キーで引く。
    /// それ以外のキー (Enter・Space・矢印・F キーなど) と、Control などを押したキー (Ctrl+J などの代替キー) は、今までどおり。
    static func virtualKey(for event: NSEvent, kana: Bool = false) -> Int32? {
        if kana, event.modifierFlags.intersection([.control, .option, .command]).isEmpty,
           let vk = kanaVirtualKey(for: Int(event.keyCode)) {
            return vk
        }
        return virtualKey(for: event)
    }

    static func virtualKey(for event: NSEvent) -> Int32? {
        switch Int(event.keyCode) {
        case kVK_Return, kVK_ANSI_KeypadEnter: return 0x0D
        case kVK_Tab: return 0x09
        case kVK_Space: return 0x20
        case kVK_Delete: return 0x08            // BackSpace
        case kVK_ForwardDelete: return 0x2E
        case kVK_Escape: return 0x1B
        case kVK_LeftArrow: return 0x25
        case kVK_UpArrow: return 0x26
        case kVK_RightArrow: return 0x27
        case kVK_DownArrow: return 0x28
        case kVK_Home: return 0x24
        case kVK_End: return 0x23
        case kVK_PageUp: return 0x21
        case kVK_PageDown: return 0x22
        case kVK_F6: return 0x75                // ひらがな
        case kVK_F7: return 0x76                // カタカナ
        case kVK_F8: return 0x77                // 半角カナ
        case kVK_F9: return 0x78                // 全角英数
        case kVK_F10: return 0x79               // 半角英数
        // Ctrl 代替キー用。文字ではなくキーコードで見る (US の Ctrl+' は文字が ":" にならないため。JIS では kVK_ANSI_Quote の位置が ":" キー)
        case kVK_ANSI_Quote: return 0xDE
        case kVK_ANSI_Semicolon: return 0xBA
        default: break
        }
        guard let scalar = event.charactersIgnoringModifiers?.lowercased().unicodeScalars.first else { return nil }
        switch scalar {
        case "a"..."z": return Int32(scalar.value) - 0x20   // A-Z = 0x41-0x5A
        case "0"..."9": return Int32(scalar.value)          // 0-9 = 0x30-0x39
        case ",": return 0xBC
        case ".": return 0xBE
        case "-": return 0xBD
        case "/": return 0xBF
        case "[": return 0xDB
        case "]": return 0xDD
        default:
            return scalar.value >= 0x20 ? otherCharacterKey : nil
        }
    }
}
