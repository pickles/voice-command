using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

internal static class CommandDialogTests
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        var assembly = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VoiceChatLauncher.exe"));
        var find = assembly.GetType("VoiceChatLauncher.DesktopWindowCreation").GetMethod("FindCommandDialog", BindingFlags.Static | BindingFlags.NonPublic);
        using (var standalone = new Form { Text = "VoiceChatLauncher test: native dialog", AccessibleName = "Command menu" })
        using (var embedded = new Form { Text = "VoiceChatLauncher test: embedded dialog" })
        using (var unrelated = new Form { Text = "VoiceChatLauncher test: unrelated input" })
        using (var labelOnly = new Form { Text = "VoiceChatLauncher test: label only", AccessibleName = "Command menu" })
        {
            standalone.Controls.Add(new TextBox());
            var group = new GroupBox { Text = "コマンドのメニュー", Dock = DockStyle.Fill };
            group.Controls.Add(new TextBox());
            group.Controls.Add(new Label { Text = "新しいウィンドウ", Top = 60, AutoSize = true });
            embedded.Controls.Add(group);
            unrelated.Controls.Add(new TextBox());
            labelOnly.Controls.Add(new Label { Text = "No search field" });
            standalone.Show(); embedded.Show(); unrelated.Show(); labelOnly.Show();
            embedded.Activate();
            IntPtr[] handles = { standalone.Handle, embedded.Handle, unrelated.Handle, labelOnly.Handle };
            var timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += delegate
            {
                timer.Stop();
                // UIA clients must not block the thread serving the test forms.
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        for (int i = 0; i < handles.Length; i++)
                        {
                            object found = find.Invoke(null, new object[] { AutomationElement.FromHandle(handles[i]) });
                            if ((found != null) != (i < 2))
                                throw new Exception("Unexpected dialog match in fixture " + i);
                        }
                        var dialog = find.Invoke(null, new object[] { AutomationElement.FromHandle(handles[1]) });
                        var findAction = assembly.GetType("VoiceChatLauncher.DesktopWindowCreation").GetMethod("FindAction", BindingFlags.Static | BindingFlags.NonPublic);
                        if (findAction.Invoke(null, new object[] { dialog, "新しいウィンドウ" }) == null)
                            throw new Exception("Exact text result under generic container must be actionable");
                        if (findAction.Invoke(null, new object[] { dialog, "新しいウィンドウで開く" }) != null)
                            throw new Exception("Do not match a different command");
                        Console.WriteLine("PASS: standalone/embedded dialogs; unrelated inputs rejected; exact result text actionable; different command rejected");
                    }
                    catch (Exception ex) { Console.WriteLine(ex); Environment.ExitCode = 1; }
                    finally { standalone.BeginInvoke(new Action(delegate { Application.ExitThread(); })); }
                });
            };
            timer.Start();
            Application.Run();
            timer.Dispose();
        }
    }
}
