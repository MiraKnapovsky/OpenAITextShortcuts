using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace OpenAITextShortcuts
{
    internal sealed class Keyboard : IDisposable
    {
        private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        private readonly HookProc callback;
        private IntPtr hook;
        private readonly CopilotGesture gesture = new CopilotGesture();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly ManualResetEvent ready = new ManualResetEvent(false);
        private readonly Thread thread;
        private Exception startupError;
        private uint threadId;
        private long lastInput;
        private volatile bool shortcutHeld, disposed;
        internal bool ShortcutHeld { get { return shortcutHeld; } }
        internal event Action Detected;
        internal event Action<string> Pressed;

        internal Keyboard()
        {
            callback = OnKey;
            thread = new Thread(Run) { IsBackground = true, Name = "Copilot keyboard" };
            thread.Start();
            if (!ready.WaitOne(5000)) { Dispose(); throw new UserError("Zachytávání kláves se nepodařilo spustit včas."); }
            if (startupError != null) { Dispose(); throw new UserError("Zachytávání kláves nelze spustit. Zkus aplikaci spustit znovu."); }
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
        }
        private void Run()
        {
            Timer refresh = null;
            try
            {
                threadId = GetCurrentThreadId();
                NativeMessage message;
                PeekMessage(out message, IntPtr.Zero, 0, 0, 0); // Create the thread's message queue before signaling ready.
                Install();
                refresh = new Timer(delegate { PostThreadMessage(threadId, 0x8001, UIntPtr.Zero, IntPtr.Zero); }, null, 30000, 30000);
                ready.Set();
                while (GetMessage(out message, IntPtr.Zero, 0, 0) > 0)
                {
                    if (message.Id == 0x8001)
                    {
                        bool forced = message.WParam != UIntPtr.Zero;
                        if (forced || (!shortcutHeld && !gesture.Pending(clock.ElapsedMilliseconds) && clock.ElapsedMilliseconds - lastInput > 1500))
                        {
                            try { Install(); } catch { /* Retry on the next timer or session event. */ }
                        }
                    }
                    else { TranslateMessage(ref message); DispatchMessage(ref message); }
                }
            }
            catch (Exception error) { startupError = error; ready.Set(); }
            finally
            {
                if (refresh != null) refresh.Dispose();
                if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
                shortcutHeld = false;
            }
        }
        private void Install()
        {
            IntPtr replacement = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
            if (replacement == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
            hook = replacement;
            gesture.Reset();
            shortcutHeld = false;
        }
        internal void Reset() { if (!disposed) PostThreadMessage(threadId, 0x8001, new UIntPtr(1), IntPtr.Zero); }
        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs args)
        { if (args.Mode == PowerModes.Resume) Reset(); }
        private void OnSessionSwitch(object sender, SessionSwitchEventArgs args)
        { if (args.Reason == SessionSwitchReason.SessionUnlock || args.Reason == SessionSwitchReason.ConsoleConnect || args.Reason == SessionSwitchReason.RemoteConnect) Reset(); }
        private static bool Down(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }
        private IntPtr OnKey(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && !disposed)
            {
                int key = Marshal.ReadInt32(data);
                int msg = message.ToInt32();
                bool down = msg == 0x100 || msg == 0x104;
                bool up = msg == 0x101 || msg == 0x105;
                lastInput = clock.ElapsedMilliseconds;
                KeyDecision decision = gesture.Handle(key, down, up, lastInput, GetForegroundWindow());
                shortcutHeld = gesture.ShortcutHeld;
                if (decision.CopilotStarted)
                {
                    MaskMenu();
                    // The hardware key's synthetic modifiers must not turn Copilot+L into Win+L.
                    foreach (byte modifier in new byte[] { 0x5B, 0x5C, 0xA0, 0xA1 })
                        if (Down(modifier)) keybd_event(modifier, 0, 2, UIntPtr.Zero);
                    var detected = Detected;
                    if (detected != null) detected();
                }
                if (decision.Operation != null)
                {
                    MaskMenu();
                    var handler = Pressed;
                    if (handler != null) handler(decision.Operation); // Queue only; UI and HTTP never run on the hook thread.
                }
                if (decision.Handled) return new IntPtr(1);
            }
            return CallNextHookEx(hook, code, message, data);
        }
        private static void MaskMenu()
        {
            keybd_event(0xE8, 0, 0, UIntPtr.Zero);
            keybd_event(0xE8, 0, 2, UIntPtr.Zero);
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            if (threadId != 0) PostThreadMessage(threadId, 0x12, UIntPtr.Zero, IntPtr.Zero);
            if (Thread.CurrentThread != thread) thread.Join(2000);
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage
        {
            internal IntPtr Window;
            internal uint Id;
            internal UIntPtr WParam;
            internal IntPtr LParam;
            internal uint Time;
            internal int X, Y;
            internal uint Private;
        }
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint message, UIntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint min, uint max, uint remove);
        [DllImport("user32.dll")] private static extern int GetMessage(out NativeMessage message, IntPtr window, uint min, uint max);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref NativeMessage message);
        [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref NativeMessage message);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr handle);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr handle, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    }
}
