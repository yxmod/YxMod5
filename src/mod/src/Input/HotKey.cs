// ===========================================================================
// YxMod5 C# 模组层 —— 热键定义（roadmap 1.6）
// ---------------------------------------------------------------------------
// 支持单键 / 组合键（Ctrl / Alt / Shift）/ 数字编号。
// ===========================================================================
using System;
using UnityEngine;

namespace YxMod5.Input
{
    /// <summary>热键描述（不可变值类型）。</summary>
    public struct HotKey : IEquatable<HotKey>
    {
        /// <summary>主键。</summary>
        public KeyCode Key;

        /// <summary>是否要求 Ctrl。</summary>
        public bool Ctrl;

        /// <summary>是否要求 Alt。</summary>
        public bool Alt;

        /// <summary>是否要求 Shift。</summary>
        public bool Shift;

        /// <summary>
        /// 数字编号：0 表示非数字键；1-9 表示主键盘数字键
        /// （Key 会映射为 Alpha1-Alpha9）。用于「数字编号热键」。
        /// </summary>
        public int Number;

        public bool IsValid => Key != KeyCode.None;

        public static HotKey Single(KeyCode key) => new HotKey { Key = key };

        public static HotKey Numbered(int number, bool ctrl = false, bool alt = false, bool shift = false)
        {
            return new HotKey
            {
                Key = NumberToKey(number),
                Number = number,
                Ctrl = ctrl,
                Alt = alt,
                Shift = shift
            };
        }

        public static KeyCode NumberToKey(int number)
        {
            switch (number)
            {
                case 1: return KeyCode.Alpha1;
                case 2: return KeyCode.Alpha2;
                case 3: return KeyCode.Alpha3;
                case 4: return KeyCode.Alpha4;
                case 5: return KeyCode.Alpha5;
                case 6: return KeyCode.Alpha6;
                case 7: return KeyCode.Alpha7;
                case 8: return KeyCode.Alpha8;
                case 9: return KeyCode.Alpha9;
                case 0: return KeyCode.Alpha0;
                default: return KeyCode.None;
            }
        }

        /// <summary>解析字符串（如 "F1"、"Ctrl+F"、"Alt+Shift+2"）。</summary>
        public static bool TryParse(string text, out HotKey hotKey)
        {
            hotKey = default(HotKey);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            bool ctrl = false, alt = false, shift = false;
            string main = text.Trim();
            foreach (string part in main.Split('+'))
            {
                string token = part.Trim();
                if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("Control", StringComparison.OrdinalIgnoreCase))
                {
                    ctrl = true;
                }
                else if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                {
                    alt = true;
                }
                else if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                {
                    shift = true;
                }
                else
                {
                    main = token;
                }
            }

            int number = 0;
            if (int.TryParse(main, out number) && number >= 0 && number <= 9)
            {
                hotKey = Numbered(number, ctrl, alt, shift);
                return true;
            }

            try
            {
                KeyCode key = (KeyCode)Enum.Parse(typeof(KeyCode), main, ignoreCase: true);
                if (key == KeyCode.None)
                {
                    return false;
                }
                hotKey = new HotKey { Key = key, Ctrl = ctrl, Alt = alt, Shift = shift };
                return true;
            }
            catch
            {
                return false;
            }
        }

        public override string ToString()
        {
            if (!IsValid)
            {
                return "(无)";
            }

            string result = string.Empty;
            if (Ctrl)
            {
                result += "Ctrl+";
            }
            if (Alt)
            {
                result += "Alt+";
            }
            if (Shift)
            {
                result += "Shift+";
            }
            result += Number > 0 ? Number.ToString() : Key.ToString();
            return result;
        }

        public bool Equals(HotKey other) =>
            Key == other.Key && Ctrl == other.Ctrl && Alt == other.Alt &&
            Shift == other.Shift && Number == other.Number;

        public override bool Equals(object obj) => obj is HotKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Key;
                hash = (hash * 397) ^ (Ctrl ? 1 : 0);
                hash = (hash * 397) ^ (Alt ? 1 : 0);
                hash = (hash * 397) ^ (Shift ? 1 : 0);
                hash = (hash * 397) ^ Number;
                return hash;
            }
        }
    }
}
