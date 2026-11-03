using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OpenAITextShortcuts
{
    internal static class Program
    {
        internal static readonly uint ActivateMessage = RegisterWindowMessage("OpenAITextShortcuts.Settings.1");
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            bool created;
            using (var mutex = new Mutex(true, "Local\\OpenAITextShortcuts.1", out created))
            {
                if (!created)
                {
                    // Automatic starts must not open Settings and disable shortcuts in an existing instance.
                    if (Array.IndexOf(args, "--background") < 0)
                        PostMessage(new IntPtr(0xffff), ActivateMessage, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                try { using (var app = new TrayApp(Array.IndexOf(args, "--settings") >= 0)) Application.Run(app); }
                catch (UserError ex) { MessageBox.Show(ex.Message, "OpenAI text", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                catch { MessageBox.Show("Aplikaci nelze spustit. Zavři ji a spusť znovu.", "OpenAI text", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    }

    internal sealed class DispatchWindow : Form
    {
        internal event Action ActivateSettings;
        internal DispatchWindow() { ShowInTaskbar = false; var handle = Handle; }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == Program.ActivateMessage)
            {
                var action = ActivateSettings;
                if (action != null) BeginInvoke(action);
            }
            base.WndProc(ref message);
        }
    }

    internal sealed class CursorStatus : Form
    {
        private readonly Label text = new Label();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 1000 };
        internal CursorStatus()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.FromArgb(35, 38, 43);
            text.ForeColor = Color.White;
            text.BackColor = BackColor;
            text.Font = new Font("Segoe UI", 11);
            text.AutoSize = true;
            text.Location = new Point(12, 8);
            Controls.Add(text);
            timer.Tick += delegate { timer.Stop(); Hide(); };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                // Do not take focus or intercept mouse clicks over the selected text.
                parameters.ExStyle |= 0x08000000 | 0x00000020 | 0x00000080;
                return parameters;
            }
        }
        internal void Display(string message)
        {
            timer.Stop();
            text.Text = message;
            ClientSize = new Size(text.PreferredWidth + 24, text.PreferredHeight + 16);
            Point cursor = Cursor.Position;
            Rectangle area = Screen.FromPoint(cursor).WorkingArea;
            int x = cursor.X + 16;
            int y = cursor.Y + 24;
            if (y + Height > area.Bottom) y = cursor.Y - Height - 12;
            Location = new Point(Math.Max(area.Left, Math.Min(x, area.Right - Width)),
                Math.Max(area.Top, Math.Min(y, area.Bottom - Height)));
            Show();
            timer.Start();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); text.Font.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal sealed class TrayApp : ApplicationContext
    {
        private readonly DispatchWindow window = new DispatchWindow();
        private readonly NotifyIcon tray;
        private readonly CursorStatus cursorStatus = new CursorStatus();
        private readonly Keyboard keyboard;
        private readonly TextApi api = new TextApi();
        private readonly WindowsSelectionDesktop desktop = new WindowsSelectionDesktop();
        private readonly ToolStripMenuItem copyLast;
        private readonly ToolStripMenuItem detection;
        private Settings settings;
        private SettingsForm settingsForm;
        private string lastResult;
        private string lastOriginal;
        private bool busy;
        private volatile bool exiting;
        private bool detected;

        internal TrayApp(bool showSettings)
        {
            settings = Settings.Load();
            var menu = new ContextMenuStrip();
            menu.Items.Add("Opravit text ze schránky", null, delegate { ProcessText("correct"); });
            menu.Items.Add("Přeložit text ze schránky CZ ↔ EN", null, delegate { ProcessText("translate"); });
            menu.Items.Add("Vylepšit text ze schránky", null, delegate { ProcessText("improve"); });
            menu.Items.Add(new ToolStripSeparator());
            copyLast = new ToolStripMenuItem("Zkopírovat poslední výsledek", null, delegate { CopyLast(); }) { Enabled = false };
            menu.Items.Add(copyLast);
            menu.Items.Add("Vrátit původní text do schránky (Copilot + Z)", null, delegate { ProcessText("restore"); });
            detection = new ToolStripMenuItem("Copilot: zatím nestisknutý") { Enabled = false };
            menu.Items.Add(detection);
            menu.Items.Add("Obnovit zachytávání kláves", null, delegate { keyboard.Reset(); Notify("Zachytávání kláves se obnovuje. Zkus Copilot + O, P nebo L."); });
            menu.Items.Add("Nastavení a API klíč…", null, delegate { ShowSettings(); });
            menu.Items.Add("Ukončit", null, delegate { ExitThread(); });
            tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "OpenAI text — Copilot+O / P / L / Z", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { ShowSettings(); };
            tray.BalloonTipClicked += delegate { if (!File.Exists(Settings.KeyFile)) ShowSettings(); };
            window.ActivateSettings += ShowSettings;
            keyboard = new Keyboard();
            keyboard.Pressed += operation => {
                if (exiting) return;
                IntPtr target = desktop.ForegroundWindow;
                window.BeginInvoke(new Action(() => ProcessText(operation, target, true)));
            };
            keyboard.Detected += () => { if (!exiting) window.BeginInvoke(new Action(() => {
                detected = true;
                detection.Text = "Copilot: klávesa rozpoznána";
                if (settingsForm != null) settingsForm.MarkDetected();
            })); };
            if (showSettings || !File.Exists(Settings.KeyFile)) window.BeginInvoke(new Action(ShowSettings));
        }
        private void Notify(string message, bool error = false)
        {
            if (!exiting) tray.ShowBalloonTip(5000, "OpenAI text", message, error ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }
        private void ShowSettings()
        {
            if (settingsForm != null) { settingsForm.Activate(); return; }
            if (busy) { Notify("Počkej na dokončení úpravy textu."); return; }
            using (settingsForm = new SettingsForm(settings))
            {
                if (detected) settingsForm.MarkDetected();
                settingsForm.ShowDialog();
            }
            settingsForm = null;
        }
        private async void ProcessText(string operation, IntPtr target = default(IntPtr), bool selection = false)
        {
            if (settingsForm != null) { Notify("Klávesová zkratka byla rozpoznána. Zavři Nastavení a pak ji použij na označený text."); return; }
            if (busy) { Notify("Předchozí text se ještě zpracovává."); return; }
            if (operation == "restore")
            {
                if (lastOriginal == null) { cursorStatus.Display("Žádný originál k obnovení"); return; }
                try
                {
                    ClipboardAccess.Write(window.Handle, lastOriginal, null);
                    cursorStatus.Display("Originál obnoven");
                }
                catch (UserError ex) { Notify(ex.Message, true); }
                return;
            }
            busy = true;
            try
            {
                if (!File.Exists(Settings.KeyFile)) { busy = false; ShowSettings(); return; }
                cursorStatus.Display(operation == "translate" ? "Překlad" : operation == "improve" ? "Vylepšení" : "Oprava");
                var snapshot = selection
                    ? await SelectionCapture.CopyAsync(desktop, () => exiting || keyboard.ShortcutHeld, window.Handle, target)
                    : ClipboardAccess.Read(window.Handle);
                if (exiting) return;
                string key = Settings.LoadKey();
                // Avoid sending the API key if it is still the current clipboard content.
                if (String.Equals(snapshot.Text.Trim(), key, StringComparison.Ordinal))
                    throw new UserError("Vybraný text obsahuje tvůj API klíč. Označ text, který chceš upravit.");
                string result = await api.Transform(key, settings.Model, operation, snapshot.Text);
                key = null;
                if (exiting) return;
                lastResult = result;
                lastOriginal = snapshot.Text;
                copyLast.Enabled = true;
                uint resultSequence;
                if (ClipboardAccess.Write(window.Handle, result, snapshot.Sequence, out resultSequence))
                {
                    if (selection && desktop.ForegroundWindow == target && !desktop.ModifiersDown && !keyboard.ShortcutHeld
                        && desktop.ClipboardSequence == resultSequence)
                    {
                        desktop.Paste();
                        cursorStatus.Display("Hotovo");
                    }
                    else cursorStatus.Display("Hotovo — Ctrl+V");
                }
                else Notify("Mezitím jsi zkopíroval jiný text. Výsledek najdeš v nabídce ikony → Zkopírovat poslední výsledek.");
            }
            catch (UserError ex) { Notify(ex.Message, true); }
            catch (TaskCanceledException) { Notify("API neodpovědělo včas. Schránka zůstala beze změny.", true); }
            catch (HttpRequestException) { Notify("Připojení k OpenAI selhalo. Zkontroluj internet; schránka zůstala beze změny.", true); }
            catch { Notify("Úprava textu selhala. Zkus to znovu.", true); }
            finally { busy = false; }
        }
        private void CopyLast()
        {
            if (lastResult == null) return;
            try { ClipboardAccess.Write(window.Handle, lastResult, null); Notify("Výsledek je ve schránce — Ctrl+V."); }
            catch (UserError ex) { Notify(ex.Message, true); }
        }
        protected override void ExitThreadCore()
        {
            exiting = true;
            keyboard.Dispose();
            api.Dispose();
            tray.Visible = false;
            tray.Dispose();
            cursorStatus.Dispose();
            window.Dispose();
            lastResult = null;
            lastOriginal = null;
            base.ExitThreadCore();
        }
    }

    internal sealed class SettingsForm : Form
    {
        private readonly TextBox keyBox;
        private readonly TextBox modelBox;
        private readonly Label status;
        private readonly Label keyDetection;
        private readonly CheckBox startup;
        private readonly Settings settings;
        internal SettingsForm(Settings current)
        {
            settings = current;
            Text = "OpenAI text — bezpečné zadání API klíče";
            ClientSize = new Size(580, 440);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10);
            AddLabel("Copilot+O  oprava  •  Copilot+P  překlad CZ ↔ EN  •  Copilot+L  vylepšení", 18, 16, 550, 26);
            AddLabel("Označ text, stiskni Copilot + O, P nebo L a klávesy uvolni.\nPo oznámení Hotovo vlož výsledek přes Ctrl+V.", 18, 48, 550, 50);
            keyDetection = AddLabel("Zkus stisknout Copilot: čekám na rozpoznání klávesy…", 18, 108, 550, 26);
            AddLabel("OpenAI API klíč (zadávej pouze sem, neposílej ho do chatu):", 18, 144, 550, 26);
            keyBox = new TextBox { Left = 18, Top = 173, Width = 542, UseSystemPasswordChar = true, MaxLength = 1024 };
            Controls.Add(keyBox);
            AddLabel("Klíč se ukládá zašifrovaný pro tvůj účet Windows.\nPrázdné pole při dalším otevření znamená: ponechat uložený klíč.", 18, 208, 550, 44);
            AddLabel("Model:", 18, 263, 65, 26);
            modelBox = new TextBox { Left = 85, Top = 260, Width = 290, Text = current.Model };
            Controls.Add(modelBox);
            startup = new CheckBox { Left = 18, Top = 298, Width = 540, Text = "Spouštět automaticky po přihlášení do Windows" };
            startup.Checked = StartupRegistration.IsEnabled();
            Controls.Add(startup);
            status = AddLabel(File.Exists(Settings.KeyFile) ? "API klíč je uložený. Jeho platnost se ověří při první úpravě." : "API klíč zatím není uložený.", 18, 331, 540, 40);
            var save = new Button { Text = "Uložit", Left = 342, Top = 385, Width = 100, Height = 34 };
            var close = new Button { Text = "Zavřít", Left = 456, Top = 385, Width = 104, Height = 34, DialogResult = DialogResult.Cancel };
            save.Click += Save;
            Controls.Add(save);
            Controls.Add(close);
            AcceptButton = save;
            CancelButton = close;
        }
        internal void MarkDetected() { keyDetection.Text = "✓ Klávesa Copilot je rozpoznána. Zkratky vyzkoušej po zavření Nastavení."; keyDetection.ForeColor = Color.DarkGreen; }
        private Label AddLabel(string text, int x, int y, int width, int height)
        {
            var label = new Label { Text = text, Left = x, Top = y, Width = width, Height = height };
            Controls.Add(label);
            return label;
        }
        private void Save(object sender, EventArgs args)
        {
            try
            {
                string model = modelBox.Text.Trim();
                if (!Regex.IsMatch(model, @"^[A-Za-z0-9][A-Za-z0-9._:-]{0,99}$")) throw new UserError("Vyplň platný název modelu.");
                string enteredKey = keyBox.Text.Trim();
                if (enteredKey.Length > 0)
                {
                    Settings.SaveKey(enteredKey);
                    keyBox.Clear();
                    try
                    {
                        var snapshot = ClipboardAccess.Read(Handle);
                        if (snapshot.Text.Trim() == enteredKey) ClipboardAccess.Write(Handle, "", snapshot.Sequence);
                    }
                    catch (UserError) { }
                }
                if (!File.Exists(Settings.KeyFile)) throw new UserError("Nejdřív vlož API klíč.");
                settings.Model = model;
                settings.Save();
                StartupRegistration.SetEnabled(startup.Checked, Application.ExecutablePath);
                status.ForeColor = Color.DarkGreen;
                status.Text = "Uloženo. Zavři toto okno a vyzkoušej zkratky na označeném textu.";
            }
            catch (UserError ex) { status.ForeColor = Color.Firebrick; status.Text = ex.Message; }
            catch { status.ForeColor = Color.Firebrick; status.Text = "Nastavení se nepodařilo úplně uložit. Zkus to znovu."; }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && keyBox != null) keyBox.Clear();
            base.Dispose(disposing);
        }
    }
}
