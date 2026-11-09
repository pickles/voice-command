using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

namespace VoiceChatLauncher
{
    // The desktop app and Classic both use ChatGPT.exe. Identify the package,
    // and only invoke the new-chat composer action, never a positional fallback.
    internal sealed class DesktopChatGptAutomation
    {
        internal const string PackageFamily = "OpenAI.Codex_2p2nqsd0c76g0";
        internal const string NewChatUri = "codex://threads/new?mode=chat";
        private readonly AppConfig _config;
        private readonly Action<string> _log;

        public DesktopChatGptAutomation(AppConfig config, Action<string> log)
        {
            _config = config;
            _log = log;
        }

        public void StartNewVoiceChat()
        {
            // Always open a new normal chat, even when a Work/Codex task is active.
            using (Process launched = Process.Start(new ProcessStartInfo(NewChatUri) { UseShellExecute = true })) { }
            _log("Opening new ChatGPT chat: " + NewChatUri);
            Thread.Sleep(Math.Max(0, _config.StartupDelayMilliseconds));

            IntPtr handle = WaitForDesktopWindow();
            Thread.Sleep(Math.Max(0, _config.AfterBringToFrontDelayMilliseconds));
            var timer = Stopwatch.StartNew();
            string lastError = null;
            do
            {
                // A user switching apps must never redirect input to another window.
                if (GetForegroundWindow() != handle)
                    throw new InvalidOperationException("ChatGPT 以外のウィンドウに切り替わったため、音声開始を中止しました。");
                try
                {
                    AutomationElement window = AutomationElement.FromHandle(handle);
                    AutomationElementCollection buttons = window.FindAll(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
                    foreach (AutomationElement button in buttons)
                    {
                        var current = button.Current;
                        if (current.IsOffscreen || !current.IsEnabled || !IsNewVoiceButtonName(current.Name))
                            continue;
                        object pattern;
                        if (!button.TryGetCurrentPattern(InvokePattern.Pattern, out pattern))
                        {
                            lastError = "新規音声ボタンが InvokePattern を公開していません。";
                            continue;
                        }
                        if (GetForegroundWindow() != handle)
                            throw new InvalidOperationException("前面ウィンドウが切り替わりました。");
                        // Do not retry an invocation: even a failure may have started voice.
                        InvokeVoiceButton((InvokePattern)pattern);
                        return;
                    }
                }
                catch (ElementNotAvailableException ex)
                {
                    lastError = ex.Message;
                }
                Thread.Sleep(300);
            } while (timer.ElapsedMilliseconds < _config.ButtonTimeoutMilliseconds);

            throw new InvalidOperationException("新しい ChatGPT の「新しい音声チャットを開始」ボタンが見つかりませんでした。" +
                "通常チャット画面・音声機能の利用可否・進行中の音声チャットを確認してください。" + lastError);
        }

        private void InvokeVoiceButton(InvokePattern pattern)
        {
            try
            {
                pattern.Invoke();
            }
            catch (Exception ex)
            {
                // Wrap stale-element errors too, to keep them out of the discovery retry.
                throw new InvalidOperationException("音声開始操作の結果を確認できません。ChatGPT の画面を確認してください。", ex);
            }
            _log("New ChatGPT voice button invoked (voice connection requires manual confirmation).");
        }

        private IntPtr WaitForDesktopWindow()
        {
            var timer = Stopwatch.StartNew();
            do
            {
                IntPtr handle = GetForegroundWindow();
                int processId;
                NativeMethods.GetWindowThreadProcessId(handle, out processId);
                if (handle != IntPtr.Zero && IsDesktopProcess(processId))
                {
                    _log("New ChatGPT foreground window found: " + handle);
                    return handle;
                }
                Thread.Sleep(300);
            } while (timer.ElapsedMilliseconds < _config.WindowTimeoutMilliseconds);
            throw new InvalidOperationException("新しい ChatGPT を前面に開けませんでした。Microsoft Store 版 ChatGPT と codex: の関連付けを確認してください。");
        }

        internal static bool IsNewVoiceButtonName(string name)
        {
            return string.Equals(name, "Start new voice chat", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "新しい音声チャットを開始", StringComparison.Ordinal);
        }

        internal static bool IsDesktopProcess(int processId)
        {
            IntPtr process = OpenProcess(0x1000, false, processId); // QUERY_LIMITED_INFORMATION
            if (process == IntPtr.Zero) return false;
            try
            {
                uint length = 256;
                var family = new StringBuilder((int)length);
                return GetPackageFamilyName(process, ref length, family) == 0 &&
                    string.Equals(family.ToString(), PackageFamily, StringComparison.OrdinalIgnoreCase);
            }
            finally { CloseHandle(process); }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetPackageFamilyName(IntPtr process, ref uint length, StringBuilder family);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
