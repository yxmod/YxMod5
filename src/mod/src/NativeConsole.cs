// ===========================================================================
// YxMod5 C# 模组层 —— 原生控制台桥接
// ---------------------------------------------------------------------------
// 原生启动层通过 AllocConsole 分配了 "YXMOD5 Debug Console"。
// 但 .NET 的 Console.Out 可能在 AllocConsole 之前就已缓存了无效句柄，导致
// Console.WriteLine 写不进控制台。这里直接使用 Win32 GetStdHandle +
// WriteConsoleW 写入，保证中文也能正确显示；失败时回退到 Console.Write。
// ===========================================================================
using System;
using System.Runtime.InteropServices;

namespace YxMod5
{
    internal static class NativeConsole
    {
        private const int StdOutputHandle = -11;
        private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WriteConsoleW(
            IntPtr hConsoleOutput,
            string lpBuffer,
            uint nNumberOfCharsToWrite,
            out uint lpNumberOfCharsWritten,
            IntPtr lpReserved);

        /// <summary>向调试控制台写入文本（自动换行）。</summary>
        public static void WriteLine(string text)
        {
            Write(text + Environment.NewLine);
        }

        /// <summary>向调试控制台写入文本。</summary>
        public static void Write(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            try
            {
                IntPtr handle = GetStdHandle(StdOutputHandle);
                if (handle != IntPtr.Zero && handle != InvalidHandleValue)
                {
                    // WriteConsoleW 单次写入上限约 64K 字符，超长时分段写入。
                    const int ChunkSize = 16000;
                    int offset = 0;
                    while (offset < text.Length)
                    {
                        int length = Math.Min(ChunkSize, text.Length - offset);
                        string chunk = text.Substring(offset, length);
                        uint written;
                        if (!WriteConsoleW(handle, chunk, (uint)chunk.Length, out written, IntPtr.Zero))
                        {
                            break;
                        }
                        offset += length;
                    }

                    if (offset >= text.Length)
                    {
                        return;
                    }
                }
            }
            catch
            {
                // 回退到 Console。
            }

            try
            {
                Console.Write(text);
            }
            catch
            {
                // 无控制台时忽略。
            }
        }
    }
}
