using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class SettingsVisibilityTests
{
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr handle, int command);
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }

    [STAThread]
    private static void Main(string[] args)
    {
        string result = args[0];
        try
        {
            Application.EnableVisualStyles();
            var assembly = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VoiceChatLauncher.exe"));
            var configType = assembly.GetType("VoiceChatLauncher.AppConfig", true);
            var config = Activator.CreateInstance(configType);
            var formType = assembly.GetType("VoiceChatLauncher.SettingsForm", true);
            var show = formType.GetMethod("ShowForUser", BindingFlags.Instance | BindingFlags.NonPublic);
            // No saving, microphone capture or launcher action. Exercise the real
            // settings form in a process started with STARTUPINFO/SW_HIDE.
            using (var form = (Form)Activator.CreateInstance(formType, new object[] { config, result + ".unused.ini" }))
            {
                form.Show();
                if (args[1] == "Hidden")
                {
                    Require(form.Visible, "WinForms must think the first form is visible");
                    Require(!IsWindowVisible(form.Handle), "Reproduce SW_HIDE suppressing native visibility");
                }
                show.Invoke(form, null);
                Require(IsWindowVisible(form.Handle), "Initial settings display must be native-visible");
                ShowWindow(form.Handle, 0);
                show.Invoke(form, null);
                Require(IsWindowVisible(form.Handle), "Recover native-hidden existing form");
                form.Hide();
                show.Invoke(form, null);
                Require(form.Visible && IsWindowVisible(form.Handle), "Recover managed-hidden existing form");
                form.WindowState = FormWindowState.Minimized;
                show.Invoke(form, null);
                Require(form.WindowState == FormWindowState.Normal && IsWindowVisible(form.Handle), "Restore minimized form");
                form.Close();
            }
            File.WriteAllText(result, "PASS: " + args[1] + " startup; initial, native-hidden, managed-hidden and minimized settings display");
        }
        catch (Exception ex) { File.WriteAllText(result, ex.ToString()); Environment.ExitCode = 1; }
    }
}
