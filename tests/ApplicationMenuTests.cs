using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

internal static class ApplicationMenuTests
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        var assembly = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VoiceChatLauncher.exe"));
        var type = assembly.GetType("VoiceChatLauncher.DesktopWindowCreation");
        var file = type.GetMethod("FindFileMenu", BindingFlags.Static | BindingFlags.NonPublic);
        var command = type.GetMethod("FindNewWindowCommand", BindingFlags.Static | BindingFlags.NonPublic);
        using (var form = new Form { Text = "VoiceChatLauncher menu test", Width = 600, Height = 400 })
        {
            var bar = new MenuStrip { Dock = DockStyle.None, Width = 500, Height = 50 };
            var fileButton = new ToolStripMenuItem("ファイル");
            bar.Items.Add(fileButton);
            var menu = new Panel { AccessibleRole = AccessibleRole.MenuPopup, AccessibleName = "ファイル", Top = 60, Width = 500, Height = 150 };
            var popout = new ToolStripMenuItem("新しいウィンドウで開く");
            var create = new ToolStripMenuItem("新しいウィンドウ") { Enabled = false };
            var entries = new MenuStrip { Dock = DockStyle.Top }; entries.Items.Add(popout); entries.Items.Add(create); menu.Controls.Add(entries);
            // An exact name outside the File menu must not match.
            var decoy = new MenuStrip { Dock = DockStyle.None, Top = 230, Width = 250 }; decoy.Items.Add(new ToolStripMenuItem("新しいウィンドウ"));
            form.Controls.Add(bar); form.Controls.Add(menu); form.Controls.Add(decoy);
            int invocations = 0;
            create.Click += delegate { invocations++; };
            form.Show();
            IntPtr handle = form.Handle;
            var timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += delegate
            {
                timer.Stop();
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        var root = AutomationElement.FromHandle(handle);
                        Require(file.Invoke(null, new object[] { root }) != null, "Find Japanese File menu in menu bar");
                        Require(command.Invoke(null, new object[] { root }) == null, "Reject disabled, pop-out and out-of-menu items");
                        form.Invoke(new Action(delegate { create.Enabled = true; }));
                        var found = (AutomationElement)command.Invoke(null, new object[] { root });
                        Require(found != null, "Find enabled New Window in File menu");
                        ((InvokePattern)found.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                        Thread.Sleep(200);
                        form.Invoke(new Action(delegate { Require(invocations == 1, "Execute exactly the creation item once"); fileButton.Text = "File"; menu.AccessibleName = "File"; create.Text = "New Window"; }));
                        Require(file.Invoke(null, new object[] { root }) != null, "Find English File menu");
                        Require(command.Invoke(null, new object[] { root }) != null, "Find English New Window");
                        form.Invoke(new Action(delegate { menu.AccessibleName = "Unrelated menu"; }));
                        Require(command.Invoke(null, new object[] { root }) == null, "Reject identical command in unrelated menu");
                        Console.WriteLine("PASS: Japanese/English File menu; disabled/pop-out/out-of-menu items rejected; creation invoked exactly once");
                    }
                    catch (Exception ex) { Console.WriteLine(ex); Environment.ExitCode = 1; }
                    finally { form.BeginInvoke(new Action(delegate { Application.ExitThread(); })); }
                });
            };
            timer.Start();
            Application.Run();
            timer.Dispose();
        }
    }
}



