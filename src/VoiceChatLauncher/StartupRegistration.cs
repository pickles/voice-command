using System;
using System.IO;
using Microsoft.Win32;

namespace VoiceChatLauncher
{
    internal static class StartupRegistration
    {
        internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal const string ValueName = "VoiceChatLauncher";

        public static string ReadCommand()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                return ReadCommand(key);
        }

        internal static string ReadCommand(RegistryKey key)
        {
            if (key == null) return null;
            object value = key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value == null) return null;
            string command = value as string;
            if (command == null)
                throw new InvalidOperationException("自動起動の登録形式が不正です。");
            return command;
        }

        public static void SetEnabled(bool enabled, string executablePath)
        {
            // Only open for writing when the requested registration actually differs.
            string desired = enabled ? BuildCommand(executablePath) : null;
            if (string.Equals(ReadCommand(), desired, StringComparison.OrdinalIgnoreCase)) return;
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (key == null) throw new IOException("自動起動の登録先を開けませんでした。");
                WriteCommand(key, desired);
            }
        }

        internal static string BuildCommand(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathRooted(executablePath) ||
                executablePath.IndexOf('"') >= 0 || !File.Exists(executablePath))
                throw new ArgumentException("自動起動する実行ファイルが見つかりません。");
            string command = "\"" + Path.GetFullPath(executablePath) + "\"";
            if (command.Length > 260)
                throw new ArgumentException("自動起動に登録するパスが長すぎます。短い場所へ移動してください。");
            return command;
        }

        internal static void WriteCommand(RegistryKey key, string command)
        {
            if (command == null) key.DeleteValue(ValueName, false);
            else key.SetValue(ValueName, command, RegistryValueKind.String);
            if (!string.Equals(ReadCommand(key), command, StringComparison.Ordinal))
                throw new IOException("自動起動の登録結果を確認できませんでした。");
        }
    }
}
