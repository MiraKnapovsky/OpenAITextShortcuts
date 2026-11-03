using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace OpenAITextShortcuts
{
    internal interface ISelectionDesktop
    {
        IntPtr ForegroundWindow { get; }
        uint ClipboardSequence { get; }
        bool ModifiersDown { get; }
        void Copy();
        ClipboardSnapshot Read(IntPtr owner);
    }

    internal static class SelectionCapture
    {
        internal static async Task<ClipboardSnapshot> CopyAsync(ISelectionDesktop desktop,
            Func<bool> shortcutHeld, IntPtr owner, IntPtr target, int releaseTimeout = 5000, int copyTimeout = 2000)
        {
            var timer = Stopwatch.StartNew();
            // The user's shortcut must be released before sending Ctrl+C.
            while (shortcutHeld() || desktop.ModifiersDown)
            {
                CheckTarget(desktop, target);
                if (timer.ElapsedMilliseconds >= releaseTimeout)
                    throw new UserError("Po stisku zkratky uvolni Copilot i ostatní klávesy a zkus to znovu.");
                await Task.Delay(20);
            }
            CheckTarget(desktop, target);
            uint previous = desktop.ClipboardSequence;
            desktop.Copy();
            timer.Restart();
            while (timer.ElapsedMilliseconds < copyTimeout)
            {
                CheckTarget(desktop, target);
                if (desktop.ClipboardSequence != previous)
                {
                    ClipboardSnapshot snapshot = null;
                    // Some programs publish clipboard formats in several steps or hold the lock briefly.
                    try { snapshot = desktop.Read(owner); }
                    catch (UserError) { }
                    CheckTarget(desktop, target);
                    if (snapshot != null && snapshot.Sequence != previous &&
                        snapshot.Sequence == desktop.ClipboardSequence && !String.IsNullOrWhiteSpace(snapshot.Text))
                        return snapshot;
                }
                await Task.Delay(20);
            }
            // Never send old clipboard contents when copying the selection did not succeed.
            throw new UserError("Označený text se nepodařilo zkopírovat. Označ text v aplikaci podporující Ctrl+C a zkus zkratku znovu.");
        }
        private static void CheckTarget(ISelectionDesktop desktop, IntPtr target)
        {
            if (target == IntPtr.Zero || desktop.ForegroundWindow != target)
                throw new UserError("Během kopírování se změnilo aktivní okno. Vrať se k označenému textu a stiskni zkratku znovu.");
        }
    }

    internal sealed class WindowsSelectionDesktop : ISelectionDesktop
    {
        public IntPtr ForegroundWindow { get { return GetForegroundWindow(); } }
        public uint ClipboardSequence { get { return ClipboardAccess.GetClipboardSequenceNumber(); } }
        public bool ModifiersDown
        {
            get
            {
                foreach (int key in new int[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
                    if ((GetAsyncKeyState(key) & 0x8000) != 0) return true;
                return false;
            }
        }
        public ClipboardSnapshot Read(IntPtr owner) { return ClipboardAccess.Read(owner); }
        public void Copy()
        {
            SendChord(0x43);
        }
        internal void Paste()
        {
            SendChord(0x56);
        }
        private static void SendChord(ushort key)
        {
            Input[] inputs = { Key(0x11, false), Key(key, false), Key(key, true), Key(0x11, true) };
            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input)));
            if (sent != inputs.Length)
            {
                // Release our injected keys if Windows accepted only part of the sequence.
                if (sent > 0)
                {
                    Input[] release = { Key(key, true), Key(0x11, true) };
                    SendInput((uint)release.Length, release, Marshal.SizeOf(typeof(Input)));
                }
                throw new UserError("Do aktivní aplikace nelze poslat klávesovou zkratku. Aplikace spuštěné jako správce mohou tuto akci blokovat.");
            }
        }
        private static Input Key(ushort key, bool up)
        {
            return new Input { Type = 1, Data = new InputData { Keyboard = new KeyboardInput { Key = key, Flags = up ? 2u : 0u } } };
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct Input { internal uint Type; internal InputData Data; }
        [StructLayout(LayoutKind.Explicit)]
        private struct InputData
        {
            [FieldOffset(0)] internal KeyboardInput Keyboard;
            [FieldOffset(0)] internal MouseInput Mouse; // Required for INPUT's native size/alignment on x86 and x64.
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            internal ushort Key, Scan;
            internal uint Flags, Time;
            internal UIntPtr ExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput
        {
            internal int X, Y;
            internal uint MouseData, Flags, Time;
            internal UIntPtr ExtraInfo;
        }
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    }
}
