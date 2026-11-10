using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace VoiceChatLauncher
{
    // New ChatGPT uses the shortcut configured in the app itself.
    internal sealed class DesktopChatGptAutomation
    {
        internal const string PackageFamily = "OpenAI.Codex_2p2nqsd0c76g0";
        internal const string NewChatUri = "codex://threads/new?mode=chat";
        private readonly Action<string> _log;

        public DesktopChatGptAutomation(Action<string> log)
        {
            _log = log;
        }

        public void StartNewVoiceChat()
        {
            try
            {
                // Send only the user's ChatGPT shortcut. Do not launch, focus,
                // navigate or inspect a window. '?' uses the keyboard layout.
                SendKeys.SendWait("^+?");
            }
            catch (Exception ex)
            {
                // Never retry: the shortcut may already have started voice.
                throw new InvalidOperationException("音声ショートカットの送信結果を確認できません。ChatGPT の画面を確認してください。", ex);
            }
            _log("New ChatGPT voice shortcut sent: Ctrl+Shift+? (voice connection requires manual confirmation).");
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
