using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Forms;

namespace VoiceChatLauncher
{
    internal static class DesktopWindowCreation
    {
        public static IntPtr Create(AppConfig config, Func<IntPtr> waitForWindow, Action<string> log)
        {
            // Activate without navigating away from the existing conversation.
            using (Process launched = Process.Start(new ProcessStartInfo("explorer.exe",
                @"shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App") { UseShellExecute = true })) { }
            IntPtr original = waitForWindow();
            Thread.Sleep(Math.Max(0, config.StartupDelayMilliseconds));
            // Shell activation is asynchronous and can select another existing window.
            original = waitForWindow();
            RequireForeground(original);
            log("Opening ChatGPT command menu: hwnd=" + original);
            SendKeys.SendWait("^+p");

            AutomationElement dialog = WaitForDialog(ref original, config.ButtonTimeoutMilliseconds, log);
            bool japanese = dialog.Current.Name == "コマンドのメニュー";
            // Scope the search to actions so a similarly named chat cannot be selected.
            AutomationElement actions = FindAction(dialog, japanese ? "アクション" : "Actions");
            if (actions == null)
                throw new InvalidOperationException("コマンドメニューの「アクション」が見つかりません。新規ウィンドウの作成を中止しました。");
            log("Selecting command menu Actions: hwnd=" + original);
            Activate(actions, original);
            Thread.Sleep(300);
            dialog = WaitForDialog(ref original, config.ButtonTimeoutMilliseconds, log);
            AutomationElement input = dialog.FindFirst(TreeScope.Descendants,
                new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox)));
            object value;
            if (input == null || !input.TryGetCurrentPattern(ValuePattern.Pattern, out value))
                throw new InvalidOperationException("コマンドメニューの検索欄を操作できませんでした。");
            RequireForeground(original);
            string command = japanese ? "新しいウィンドウ" : "New Window";
            log("Searching New Window command: hwnd=" + original);
            ((ValuePattern)value).SetValue(command);

            var timer = Stopwatch.StartNew();
            AutomationElement action = null;
            do
            {
                dialog = WaitForDialog(ref original, config.ButtonTimeoutMilliseconds, log);
                action = FindAction(dialog, command);
                if (action != null) break;
                Thread.Sleep(200);
            } while (timer.ElapsedMilliseconds < config.ButtonTimeoutMilliseconds);
            if (action == null)
                throw new InvalidOperationException("「新しいウィンドウ」コマンドが見つかりませんでした。");
            // A command-menu popup must not itself be counted as the new chat window.
            var existing = SnapshotWindows();
            log("Invoking New Window command: hwnd=" + original);
            Activate(action, original); // Never repeat a possibly successful creation action.

            timer.Restart();
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
            // Include hidden windows too: merely showing an existing window is not creation.
            NativeMethods.EnumWindows(delegate(IntPtr handle, IntPtr parameter)
            {
                result.Add(handle);
                return true;
            }, IntPtr.Zero);
            return result;
        }

        private static AutomationElement WaitForDialog(ref IntPtr handle, int timeout, Action<string> log)
        {
            var timer = Stopwatch.StartNew();
            do
            {
                IntPtr foreground = GetForegroundWindow();
                int processId;
                NativeMethods.GetWindowThreadProcessId(foreground, out processId);
                if (foreground == IntPtr.Zero || !DesktopChatGptAutomation.IsDesktopProcess(processId))
                    throw new InvalidOperationException("ChatGPT 以外のアプリに切り替わったため、コマンドメニューの操作を中止しました。hwnd=" + foreground);
                try
                {
                    AutomationElement match = FindCommandDialog(AutomationElement.FromHandle(foreground));
                    if (match != null && GetForegroundWindow() == foreground)
                    {
                        if (handle != foreground)
                            log("Following ChatGPT command menu: old_hwnd=" + handle + " new_hwnd=" + foreground);
                        handle = foreground;
                        log("Command menu found: hwnd=" + handle);
                        return match;
                    }
                }
                catch (ElementNotAvailableException) { }
                Thread.Sleep(200);
            } while (timer.ElapsedMilliseconds < timeout);
            throw new InvalidOperationException("ChatGPT のコマンドメニューを開けませんでした。Ctrl+Shift+P の割り当てを確認してください。");
        }

        internal static AutomationElement FindCommandDialog(AutomationElement root)
        {
            // The menu can be an embedded dialog or a separate native window root.
            var matches = root.FindAll(TreeScope.Subtree, new OrCondition(
                new PropertyCondition(AutomationElement.NameProperty, "Command menu"),
                new PropertyCondition(AutomationElement.NameProperty, "コマンドのメニュー")));
            foreach (AutomationElement match in matches)
            {
                if (!match.Current.IsOffscreen && match.FindFirst(TreeScope.Descendants,
                    new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox))) != null)
                    return match;
            }
            return null;
        }

        private static AutomationElement FindAction(AutomationElement dialog, string name)
        {
            var matches = dialog.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, name, PropertyConditionFlags.IgnoreCase));
            foreach (AutomationElement match in matches)
            {
                AutomationElement element = match;
                while (element != null && !Automation.Compare(element, dialog))
                {
                    var current = element.Current;
                    if (current.ControlType == ControlType.Button || current.ControlType == ControlType.ListItem ||
                        current.ControlType == ControlType.MenuItem || current.ControlType == ControlType.TabItem)
                    {
                        if (!current.IsOffscreen && current.IsEnabled) return element;
                        break;
                    }
                    element = TreeWalker.ControlViewWalker.GetParent(element);
                }
            }
            return null;
        }

        private static void Activate(AutomationElement element, IntPtr window)
        {
            RequireForeground(window);
            object pattern;
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out pattern))
            {
                ((InvokePattern)pattern).Invoke();
                return;
            }
            if (element.Current.ControlType == ControlType.TabItem &&
                element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern))
            {
                ((SelectionItemPattern)pattern).Select();
                return;
            }
            Point point;
            if (!element.TryGetClickablePoint(out point))
                throw new InvalidOperationException("コマンドのクリック位置を取得できませんでした。");
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
