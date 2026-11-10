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
            var existing = SnapshotWindows();
            RequireForeground(original);
            log("Opening ChatGPT command menu to create a separate window.");
            SendKeys.SendWait("^+p");

            AutomationElement dialog = WaitForDialog(original, config.ButtonTimeoutMilliseconds);
            bool japanese = dialog.Current.Name == "コマンドのメニュー";
            // Scope the search to actions so a similarly named chat cannot be selected.
            AutomationElement actions = FindAction(dialog, japanese ? "アクション" : "Actions");
            if (actions == null)
                throw new InvalidOperationException("コマンドメニューの「アクション」が見つかりません。新規ウィンドウの作成を中止しました。");
            Activate(actions, original);
            Thread.Sleep(300);
            dialog = WaitForDialog(original, config.ButtonTimeoutMilliseconds);
            AutomationElement input = dialog.FindFirst(TreeScope.Descendants,
                new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox)));
            object value;
            if (input == null || !input.TryGetCurrentPattern(ValuePattern.Pattern, out value))
                throw new InvalidOperationException("コマンドメニューの検索欄を操作できませんでした。");
            RequireForeground(original);
            string command = japanese ? "新しいウィンドウ" : "New Window";
            ((ValuePattern)value).SetValue(command);

            var timer = Stopwatch.StartNew();
            AutomationElement action = null;
            do
            {
                RequireForeground(original);
                dialog = WaitForDialog(original, config.ButtonTimeoutMilliseconds);
                action = FindAction(dialog, command);
                if (action != null) break;
                Thread.Sleep(200);
            } while (timer.ElapsedMilliseconds < config.ButtonTimeoutMilliseconds);
            if (action == null)
                throw new InvalidOperationException("「新しいウィンドウ」コマンドが見つかりませんでした。");
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

        private static AutomationElement WaitForDialog(IntPtr handle, int timeout)
        {
            var timer = Stopwatch.StartNew();
            do
            {
                RequireForeground(handle);
                try
                {
                    AutomationElement root = AutomationElement.FromHandle(handle);
                    var matches = root.FindAll(TreeScope.Descendants, new OrCondition(
                        new PropertyCondition(AutomationElement.NameProperty, "Command menu"),
                        new PropertyCondition(AutomationElement.NameProperty, "コマンドのメニュー")));
                    foreach (AutomationElement match in matches)
                    {
                        if (!match.Current.IsOffscreen && match.FindFirst(TreeScope.Descendants,
                            new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox))) != null)
                            return match;
                    }
                }
                catch (ElementNotAvailableException) { }
                Thread.Sleep(200);
            } while (timer.ElapsedMilliseconds < timeout);
            throw new InvalidOperationException("ChatGPT のコマンドメニューを開けませんでした。Ctrl+Shift+P の割り当てを確認してください。");
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
                throw new InvalidOperationException("前面ウィンドウが切り替わったため、新規ウィンドウの作成を中止しました。");
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
    }
}
