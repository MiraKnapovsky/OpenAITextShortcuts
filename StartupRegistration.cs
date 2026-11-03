using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace OpenAITextShortcuts
{
    internal static class StartupRegistration
    {
        private const string LegacyRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal static string TaskName { get { return "OpenAITextShortcuts-" + WindowsIdentity.GetCurrent().User.Value; } }

        internal static bool IsEnabled()
        {
            dynamic service = null, folder = null, task = null;
            try
            {
                service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
                service.Connect();
                folder = service.GetFolder(@"\");
                task = FindTask(folder);
                if (task != null && (bool)task.Enabled) return true;
            }
            catch { /* Older installations can still use the Run entry. */ }
            finally { Release(task); Release(folder); Release(service); }
            using (var run = Registry.CurrentUser.OpenSubKey(LegacyRunKey))
                return run != null && run.GetValue("OpenAITextShortcuts") != null;
        }

        internal static void SetEnabled(bool enabled, string executablePath)
        {
            if (enabled && !File.Exists(executablePath)) throw new UserError("Soubor aplikace pro automatické spuštění neexistuje.");
            dynamic service = null, folder = null, definition = null, registered = null, existing = null;
            try
            {
                service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
                service.Connect();
                folder = service.GetFolder(@"\");
                if (enabled)
                {
                    string user = WindowsIdentity.GetCurrent().User.Value;
                    definition = service.NewTask(0);
                    definition.RegistrationInfo.Description = "OpenAI text: automatické spuštění zkratek Copilot po přihlášení uživatele.";
                    definition.Principal.UserId = user;
                    definition.Principal.LogonType = 3; // InteractiveToken: desktop access without storing a password.
                    definition.Principal.RunLevel = 0; // Normal user permissions.
                    definition.Settings.Enabled = true;
                    definition.Settings.AllowDemandStart = true;
                    definition.Settings.StartWhenAvailable = true;
                    definition.Settings.DisallowStartIfOnBatteries = false;
                    definition.Settings.StopIfGoingOnBatteries = false;
                    definition.Settings.ExecutionTimeLimit = "PT0S";
                    definition.Settings.MultipleInstances = 2; // IgnoreNew.
                    dynamic trigger = definition.Triggers.Create(9); // At logon.
                    trigger.UserId = user;
                    trigger.Enabled = true;
                    trigger.Delay = "PT0S";
                    dynamic action = definition.Actions.Create(0);
                    action.Path = Path.GetFullPath(executablePath);
                    action.Arguments = "--background";
                    action.WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath));
                    registered = folder.RegisterTaskDefinition(TaskName, definition, 6, user, null, 3, null);
                    Release(action);
                    Release(trigger);
                }
                else
                {
                    existing = FindTask(folder);
                    if (existing != null) folder.DeleteTask(TaskName, 0);
                }
                // Remove the delayed legacy entry only after the new registration succeeded.
                using (var run = Registry.CurrentUser.OpenSubKey(LegacyRunKey, true))
                    if (run != null) run.DeleteValue("OpenAITextShortcuts", false);
            }
            catch (UserError) { throw; }
            catch { throw new UserError("Automatické spuštění se nepodařilo změnit v Plánovači úloh Windows."); }
            finally { Release(existing); Release(registered); Release(definition); Release(folder); Release(service); }
        }
        private static object FindTask(dynamic folder)
        {
            try { return folder.GetTask(TaskName); }
            catch (COMException error)
            {
                if (error.ErrorCode == unchecked((int)0x80070002)) return null;
                throw;
            }
        }
        private static void Release(object value)
        {
            if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }
    }
}
