using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenAITextShortcuts
{
    internal sealed class ClipboardSnapshot
    {
        internal string Text;
        internal uint Sequence;
    }
    internal static class ClipboardAccess
    {
        internal static ClipboardSnapshot Read(IntPtr owner)
        {
            if (!OpenClipboard(owner)) throw new UserError("Schránku právě používá jiná aplikace. Zkus zkratku znovu.");
            try
            {
                IntPtr handle = GetClipboardData(13);
                if (handle == IntPtr.Zero) throw new UserError("Ve schránce není text. Nejdřív ho zkopíruj přes Ctrl+C.");
                IntPtr pointer = GlobalLock(handle);
                if (pointer == IntPtr.Zero) throw new UserError("Text ze schránky nelze přečíst.");
                try { return new ClipboardSnapshot { Text = Marshal.PtrToStringUni(pointer), Sequence = GetClipboardSequenceNumber() }; }
                finally { GlobalUnlock(handle); }
            }
            finally { CloseClipboard(); }
        }
        // Comparison and replacement use the same clipboard lock, so a newer copy cannot be overwritten.
        internal static bool Write(IntPtr owner, string text, uint? expectedSequence)
        {
            uint sequence;
            return Write(owner, text, expectedSequence, out sequence);
        }
        internal static bool Write(IntPtr owner, string text, uint? expectedSequence, out uint sequence)
        {
            sequence = 0;
            byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
            IntPtr memory = GlobalAlloc(0x42, new UIntPtr((uint)bytes.Length));
            if (memory == IntPtr.Zero) throw new UserError("Nedostatek paměti pro schránku.");
            try
            {
                IntPtr pointer = GlobalLock(memory);
                if (pointer == IntPtr.Zero) throw new UserError("Do schránky nelze zapisovat.");
                try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
                finally { GlobalUnlock(memory); }
                if (!OpenClipboard(owner)) throw new UserError("Schránka je obsazená. Výsledek lze zkopírovat z nabídky ikony u hodin.");
                try
                {
                    if (expectedSequence.HasValue && GetClipboardSequenceNumber() != expectedSequence.Value) return false;
                    if (!EmptyClipboard()) throw new UserError("Schránku nelze změnit.");
                    if (SetClipboardData(13, memory) == IntPtr.Zero) throw new UserError("Zápis do schránky selhal. Výsledek lze zkopírovat z nabídky ikony u hodin.");
                    memory = IntPtr.Zero; // Windows owns this allocation now.
                    return true;
                }
                finally
                {
                    CloseClipboard();
                    // Windows may advance the sequence again when the writer closes the clipboard.
                    if (memory == IntPtr.Zero) sequence = GetClipboardSequenceNumber();
                }
            }
            finally { if (memory != IntPtr.Zero) GlobalFree(memory); }
        }
        [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")] private static extern bool CloseClipboard();
        [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
        [DllImport("user32.dll")] private static extern bool EmptyClipboard();
        [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr memory);
        [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);
    }
}
