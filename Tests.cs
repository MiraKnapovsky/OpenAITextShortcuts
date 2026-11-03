using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace OpenAITextShortcuts
{
    internal sealed class FakeSelectionDesktop : ISelectionDesktop
    {
        public IntPtr ForegroundWindow { get; set; }
        public uint ClipboardSequence { get; set; }
        public bool ModifiersDown { get; set; }
        internal int Copies, Reads;
        internal string Text = "old clipboard text";
        internal Action OnCopy;
        internal bool BusyOnce;
        internal FakeSelectionDesktop() { ForegroundWindow = new IntPtr(42); ClipboardSequence = 10; }
        public void Copy() { Copies++; if (OnCopy != null) OnCopy(); }
        public ClipboardSnapshot Read(IntPtr owner)
        {
            Reads++;
            if (BusyOnce) { BusyOnce = false; throw new UserError("Clipboard temporarily locked"); }
            return new ClipboardSnapshot { Text = Text, Sequence = ClipboardSequence };
        }
    }
    internal sealed class FakeApi : HttpMessageHandler
    {
        internal int Calls;
        internal string Body;
        internal string Reply = "{\"status\":\"completed\",\"output\":[{\"type\":\"reasoning\",\"summary\":[]},{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"Příliš žluťoučký kůň.\"}]}]}";
        internal HttpStatusCode Status = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Calls++;
            if (request.RequestUri.AbsoluteUri != "https://api.openai.com/v1/responses") throw new Exception("Wrong endpoint");
            if (request.Method != HttpMethod.Post) throw new Exception("Wrong HTTP method");
            if (request.Headers.Authorization.Scheme != "Bearer" || request.Headers.Authorization.Parameter != "test-token") throw new Exception("Wrong authentication");
            Body = await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(Status) { Content = new StringContent(Reply) };
        }
    }
    internal static class Tests
    {
        private static int checks;
        private static void Check(bool condition, string label)
        {
            if (!condition) throw new Exception(label);
            checks++;
            Console.WriteLine("PASS " + label);
        }
        private static void Rejected(Action action, string label)
        {
            try { action(); }
            catch (UserError) { Check(true, label); return; }
            throw new Exception("Expected failure: " + label);
        }
        private static int Main()
        {
            try
            {
                var fake = new FakeApi();
                using (var api = new TextApi(fake))
                {
                    string source = "Příliš žluťoučký kůň.\n\"Ahoj\" — https://example.com/?a=1&b=2";
                    string result = api.Transform("test-token", "gpt-4.1-mini", "correct", source).GetAwaiter().GetResult();
                    Check(result == "Příliš žluťoučký kůň.", "Unicode response with preceding reasoning item");
                    var body = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(fake.Body);
                    Check((string)body["input"] == source && (bool)body["store"] == false, "Unicode input preserved and response storage disabled");
                    string correction = (string)body["instructions"];
                    api.Transform("test-token", "gpt-4.1-mini", "translate", source).GetAwaiter().GetResult();
                    body = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(fake.Body);
                    string translation = (string)body["instructions"];
                    api.Transform("test-token", "gpt-4.1-mini", "improve", source).GetAwaiter().GetResult();
                    body = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(fake.Body);
                    Check(correction != translation && translation != (string)body["instructions"], "Three operations dispatch distinct instructions");
                    int calls = fake.Calls;
                    Rejected(() => api.Transform("test-token", "gpt-4.1-mini", "correct", "  ").GetAwaiter().GetResult(), "Empty clipboard rejected");
                    Rejected(() => api.Transform("test-token", "gpt-4.1-mini", "correct", new string('x', 16001)).GetAwaiter().GetResult(), "Oversized clipboard rejected");
                    Check(calls == fake.Calls, "Rejected input does not contact API");
                    fake.Status = HttpStatusCode.Unauthorized;
                    fake.Reply = "secret server error body";
                    try { api.Transform("test-token", "gpt-4.1-mini", "correct", source).GetAwaiter().GetResult(); throw new Exception("Expected 401"); }
                    catch (UserError error) { Check(!error.Message.Contains("secret") && !error.Message.Contains("test-token"), "HTTP error does not expose raw server body or token"); }
                    Check(fake.Calls == calls + 1, "Failed request is not retried automatically");
                }
                Rejected(() => TextApi.Parse("{\"status\":\"incomplete\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"truncated\"}]}]}"), "Truncated output never replaces clipboard");
                Rejected(() => TextApi.Parse("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"refusal\",\"refusal\":\"no\"}]}]}"), "Refusal never replaces clipboard");
                Rejected(() => TextApi.Parse("{\"status\":\"completed\",\"output\":[]}"), "Empty output never replaces clipboard");
                Rejected(() => TextApi.Parse("not JSON"), "Malformed output never replaces clipboard");
                var desktop = new FakeSelectionDesktop();
                int heldPolls = 0;
                desktop.OnCopy = () => {
                    if (heldPolls < 3) throw new Exception("Copy sent before shortcut release");
                    desktop.Text = "Označený text — žluťoučký kůň";
                    desktop.ClipboardSequence++;
                };
                var selected = SelectionCapture.CopyAsync(desktop, () => ++heldPolls < 3, IntPtr.Zero, new IntPtr(42)).GetAwaiter().GetResult();
                Check(selected.Text == desktop.Text && selected.Sequence == 11 && desktop.Copies == 1,
                    "Selection is copied once after releasing the shortcut");

                desktop = new FakeSelectionDesktop();
                Rejected(() => SelectionCapture.CopyAsync(desktop, () => false, IntPtr.Zero, new IntPtr(42), 50, 50).GetAwaiter().GetResult(),
                    "Failed copy rejects stale clipboard contents");
                Check(desktop.Reads == 0, "Old clipboard text is not read when copy produces no update");

                desktop = new FakeSelectionDesktop();
                Rejected(() => SelectionCapture.CopyAsync(desktop, () => {
                    desktop.ForegroundWindow = new IntPtr(99); return true;
                }, IntPtr.Zero, new IntPtr(42)).GetAwaiter().GetResult(), "Focus change before copy aborts capture");
                Check(desktop.Copies == 0, "Ctrl+C is not sent to a different application");

                desktop = new FakeSelectionDesktop();
                desktop.OnCopy = () => { desktop.ClipboardSequence++; desktop.ForegroundWindow = new IntPtr(99); };
                Rejected(() => SelectionCapture.CopyAsync(desktop, () => false, IntPtr.Zero, new IntPtr(42)).GetAwaiter().GetResult(),
                    "Focus change while copying aborts capture");

                desktop = new FakeSelectionDesktop { BusyOnce = true };
                desktop.OnCopy = () => { desktop.ClipboardSequence++; };
                selected = SelectionCapture.CopyAsync(desktop, () => false, IntPtr.Zero, new IntPtr(42)).GetAwaiter().GetResult();
                Check(desktop.Reads == 2 && selected.Sequence == 11, "Temporarily locked clipboard is retried, including identical copied text");

                desktop = new FakeSelectionDesktop { ModifiersDown = true };
                Rejected(() => SelectionCapture.CopyAsync(desktop, () => false, IntPtr.Zero, new IntPtr(42), 30).GetAwaiter().GetResult(),
                    "Held modifiers time out without sending Ctrl+C");
                Check(desktop.Copies == 0, "No injection while modifiers are held");

                var inputType = typeof(WindowsSelectionDesktop).GetNestedType("Input", System.Reflection.BindingFlags.NonPublic);
                Check(System.Runtime.InteropServices.Marshal.SizeOf(inputType) == (IntPtr.Size == 8 ? 40 : 28),
                    "SendInput structure matches native architecture alignment");
                var gesture = new CopilotGesture();
                var target = new IntPtr(42);
                Check(!gesture.Handle(0x5A, true, false, 0, target).Handled, "Ordinary Z without Copilot passes through");
                gesture.Handle(0x86, true, false, 0, target);
                var restoreDecision = gesture.Handle(0x5A, true, false, 1, target);
                Check(restoreDecision.Handled && restoreDecision.Operation == "restore", "Copilot plus Z dispatches local restore");
                gesture.Reset();
                Check(!gesture.Handle(0x4F, true, false, 0, target).Handled, "Ordinary O without Copilot passes through");
                gesture.Handle(0x86, true, false, 100, target);
                var decision = gesture.Handle(0x4F, true, false, 6000, target);
                Check(decision.Handled && decision.Operation == "correct" && gesture.ShortcutHeld,
                    "Held Copilot plus O works even after a long hold");
                Check(gesture.Handle(0x4F, true, false, 6010, target).Operation == null,
                    "Repeating O does not submit another request");
                gesture.Handle(0x86, false, true, 6020, target);
                Check(gesture.Handle(0x4F, false, true, 6030, target).Handled && !gesture.ShortcutHeld,
                    "Letter release is swallowed even when Copilot releases first");
                foreach (int key in new int[] { 0x4F, 0x50, 0x4C, 0x5A })
                {
                    gesture.Reset();
                    gesture.Handle(0x86, true, false, 0, target);
                    gesture.Handle(0x86, false, true, 1, target);
                    gesture.Handle(0x5B, false, true, 2, target);
                    gesture.Handle(0xA0, false, true, 3, target);
                    decision = gesture.Handle(key, true, false, 2100, target);
                    Check(decision.Handled && decision.Operation != null, "Short Copilot pulse accepts delayed command " + key);
                    gesture.Handle(key, false, true, 2150, target);
                    Check(!gesture.ShortcutHeld && !gesture.Handle(key, true, false, 2200, target).Handled,
                        "Copilot pulse is consumed once and does not capture normal typing " + key);
                }
                gesture.Reset();
                gesture.Handle(0x86, true, false, 0, target);
                gesture.Handle(0x86, false, true, 1, target);
                Check(!gesture.Handle(0x4F, true, false, 3001, target).Handled, "Unused Copilot pulse expires");
                gesture.Reset();
                gesture.Handle(0x86, true, false, 0, target);
                gesture.Handle(0x86, false, true, 1, target);
                gesture.Handle(0x41, true, false, 2, target);
                Check(!gesture.Handle(0x4F, true, false, 3, target).Handled, "Another letter cancels the Copilot prefix");
                gesture.Reset();
                gesture.Handle(0x86, true, false, 0, target);
                gesture.Handle(0x86, false, true, 1, target);
                Check(!gesture.Handle(0x4F, true, false, 2, new IntPtr(99)).Handled, "Copilot prefix does not follow a changed foreground window");
                gesture.Handle(0x86, true, false, 10, target);
                gesture.Reset();
                Check(!gesture.ShortcutHeld && !gesture.Handle(0x4F, true, false, 11, target).Handled,
                    "Recovery clears stale held keys and pending commands");
                Console.WriteLine(checks + " checks passed; no network or real API key used.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
