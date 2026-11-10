using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;

namespace VoiceChatLauncher
{
    internal static class DesktopWindowCreation
    {
        public static IntPtr Create(AppConfig config, Func<IntPtr> waitForWindow, Action<string> log)
        {
            using (Process launched = Process.Start(new ProcessStartInfo("explorer.exe",
                @"shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App") { UseShellExecute = true })) { }
            IntPtr original = waitForWindow();
            Thread.Sleep(Math.Max(0, config.StartupDelayMilliseconds));
            original = waitForWindow();
            RequireForeground(original);

            // New Window is an application File-menu command. The command palette
            // only offers Open in new window, which pops out the current conversation.
            AutomationElement file = WaitForElement(original, config.ButtonTimeoutMilliseconds,
                FindFileMenu, "アプリの「ファイル」メニューが見つかりませんでした。", log);
            log("Opening ChatGPT File menu: hwnd=" + original);
            Activate(file, original, true);
            AutomationElement action = WaitForElement(original, config.ButtonTimeoutMilliseconds,
                FindNewWindowCommand, "「ファイル → 新しいウィンドウ」が見つかりませんでした。", log);
            var existing = SnapshotWindows();
            log("Invoking File > New Window: hwnd=" + original);
            Activate(action, original, false); // Never retry a possibly successful invocation.

            var timer = Stopwatch.StartNew();
            do
            {
                IntPtr handle = GetForegroundWindow();
                int processId;
                NativeMethods.GetWindowThreadProcessId(handle, out processId);
                if (IsNewWindow(handle, existing) && DesktopChatGptAutomation.IsDesktopProcess(processId))
                {
                    log("Created separate ChatGPT window: " + handle);
                    return handle;
                }
                Thread.Sleep(200);
            } while (timer.ElapsedMilliseconds < config.WindowTimeoutMilliseconds);
            throw new InvalidOperationException("新規ウィンドウを確認できませんでした。既存ウィンドウでの音声開始は行いません。");
        }

        internal static bool IsNewWindow(IntPtr handle, HashSet<IntPtr> existing)
        {
            return handle != IntPtr.Zero && !existing.Contains(handle);
        }

        private static HashSet<IntPtr> SnapshotWindows()
        {
            var result = new HashSet<IntPtr>();
            NativeMethods.EnumWindows(delegate(IntPtr handle, IntPtr parameter)
            {
                result.Add(handle);
                return true;
            }, IntPtr.Zero);
            return result;
        }

        private static AutomationElement WaitForElement(IntPtr handle, int timeout,
            Func<AutomationElement, AutomationElement> find, string error, Action<string> log)
        {
            var timer = Stopwatch.StartNew();
            do
            {
                RequireForeground(handle);
                try
                {
                    AutomationElement match = find(AutomationElement.FromHandle(handle));
                    if (match != null) return match;
                }
                catch (ElementNotAvailableException) { }
                Thread.Sleep(200);
            } while (timer.ElapsedMilliseconds < timeout);
            LogMenuItems(AutomationElement.FromHandle(handle), log);
            throw new InvalidOperationException(error);
        }

        internal static AutomationElement FindFileMenu(AutomationElement root)
        {
            var bars = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuBar));
            foreach (AutomationElement bar in bars)
            {
                AutomationElement item = FindMenuItem(bar, "File", "ファイル");
                if (item != null) return item;
            }
            return null;
        }

        internal static AutomationElement FindNewWindowCommand(AutomationElement root)
        {
            // The desktop app renders its application menu in this window's webview.
            // Scope to the File popup, never a chat title or a command-palette result.
            var menus = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Menu));
            foreach (AutomationElement menu in menus)
            {
                var current = menu.Current;
                if (current.IsOffscreen ||
                    !(current.Name == "File" || current.Name == "ファイル" ||
                      current.AutomationId == "application-menu-content")) continue;
                AutomationElement item = FindMenuItem(menu, "New Window", "新しいウィンドウ");
                if (item != null) return item;
            }
            return null;
        }

        private static AutomationElement FindMenuItem(AutomationElement root, string english, string japanese)
        {
            var items = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem));
            foreach (AutomationElement item in items)
            {
                var current = item.Current;
                if (!current.IsOffscreen && current.IsEnabled &&
                    (string.Equals(current.Name, english, StringComparison.OrdinalIgnoreCase) || current.Name == japanese))
                    return item;
            }
            return null;
        }

        private static void LogMenuItems(AutomationElement root, Action<string> log)
        {
            var items = root.FindAll(TreeScope.Descendants, new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuBar),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Menu),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem)));
            int count = 0;
            foreach (AutomationElement item in items)
            {
                var current = item.Current;
                if (current.IsOffscreen) continue;
                log("Application menu candidate: type=" + current.ControlType.ProgrammaticName +
                    " name=" + current.Name + " id=" + current.AutomationId + " enabled=" + current.IsEnabled);
                if (++count >= 30) break;
            }
        }

        private static void Activate(AutomationElement element, IntPtr window, bool expand)
        {
            RequireForeground(window);
            object pattern;
            if (expand && element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out pattern))
            {
                var expansion = (ExpandCollapsePattern)pattern;
                if (expansion.Current.ExpandCollapseState != ExpandCollapseState.Expanded) expansion.Expand();
                return;
            }
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out pattern))
            {
                ((InvokePattern)pattern).Invoke();
                return;
            }
            Point point;
            if (!element.TryGetClickablePoint(out point))
                throw new InvalidOperationException("メニュー項目のクリック位置を取得できませんでした。");
            RequireForeground(window);
            NativeMethods.SetCursorPos((int)point.X, (int)point.Y);
            NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTDOWN, (int)point.X, (int)point.Y, 0, UIntPtr.Zero);
            NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTUP, (int)point.X, (int)point.Y, 0, UIntPtr.Zero);
        }

        private static void RequireForeground(IntPtr handle)
        {
            if (GetForegroundWindow() != handle)
                throw new InvalidOperationException("前面ウィンドウが切り替わったため、新規ウィンドウの作成を中止しました。expected_hwnd=" + handle + " actual_hwnd=" + GetForegroundWindow());
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
    }
}
