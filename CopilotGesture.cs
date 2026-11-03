using System;
using System.Collections.Generic;

namespace OpenAITextShortcuts
{
    internal sealed class KeyDecision
    {
        internal bool Handled;
        internal bool CopilotStarted;
        internal string Operation;
    }
    internal sealed class CopilotGesture
    {
        private readonly HashSet<int> swallowed = new HashSet<int>();
        private bool held, armed;
        private long deadline;
        private IntPtr sourceWindow;
        internal bool ShortcutHeld { get { return held || swallowed.Count != 0; } }
        internal bool Pending(long milliseconds) { return armed && (held || milliseconds <= deadline); }
        internal void Reset() { held = false; armed = false; swallowed.Clear(); }

        internal KeyDecision Handle(int key, bool down, bool up, long milliseconds, IntPtr foreground)
        {
            var result = new KeyDecision();
            if (key == 0x86)
            {
                result.Handled = true;
                if (down && !held)
                {
                    held = true;
                    armed = true;
                    deadline = milliseconds + 3000;
                    sourceWindow = foreground;
                    result.CopilotStarted = true;
                }
                // Some hardware sends F23 UP immediately, even while the physical key is held.
                if (up) held = false;
                return result;
            }
            if (up && swallowed.Remove(key)) { result.Handled = true; return result; }
            if (!down) return result;
            if (swallowed.Contains(key)) { result.Handled = true; return result; }
            if (held && (key == 0x5B || key == 0x5C || key == 0xA0 || key == 0xA1))
            { result.Handled = true; return result; }
            if (IsModifier(key) || key == 0xE8) return result;
            if (armed && (held || milliseconds <= deadline) && foreground == sourceWindow)
            {
                switch (key)
                {
                    case 0x4F: result.Operation = "correct"; break;
                    case 0x50: result.Operation = "translate"; break;
                    case 0x4C: result.Operation = "improve"; break;
                    case 0x5A: result.Operation = "restore"; break;
                }
                if (result.Operation != null) { swallowed.Add(key); result.Handled = true; }
            }
            // A prefix applies only to the next non-modifier key in the same window.
            armed = false;
            return result;
        }
        private static bool IsModifier(int key)
        {
            return key == 0x10 || key == 0x11 || key == 0x12 || key == 0x5B || key == 0x5C || (key >= 0xA0 && key <= 0xA5);
        }
    }
}
