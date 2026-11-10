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
            embedded.Controls.Add(group);
            unrelated.Controls.Add(new TextBox());
            labelOnly.Controls.Add(new Label { Text = "No search field" });
            standalone.Show(); embedded.Show(); unrelated.Show(); labelOnly.Show();
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
                        Console.WriteLine("PASS: standalone root and embedded dialogs detected; unrelated input and label-only window rejected");
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
