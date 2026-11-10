using System;
using System.IO;
using Microsoft.Win32;
using VoiceChatLauncher;

internal static class StartupRegistrationTests
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Main()
    {
        string keyPath = @"Software\VoiceChatLauncher.Tests\" + Guid.NewGuid().ToString("N");
        string directory = Path.Combine(Path.GetTempPath(), "Voice launcher test " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string executable = Path.Combine(directory, "Voice Chat Launcher.exe");
        File.WriteAllText(executable, "test fixture; never executed");
        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath))
            {
                key.SetValue("UnrelatedApp", "preserve");
                Require(StartupRegistration.ReadCommand(key) == null, "Initial state must be unregistered");
                string command = StartupRegistration.BuildCommand(executable);
                Require(command == "\"" + executable + "\"", "Paths with spaces must be quoted");
                StartupRegistration.WriteCommand(key, command);
                using (RegistryKey reopened = Registry.CurrentUser.OpenSubKey(keyPath))
                    Require(StartupRegistration.ReadCommand(reopened) == command, "Registration must persist");
                StartupRegistration.WriteCommand(key, command);
                StartupRegistration.WriteCommand(key, null);
                StartupRegistration.WriteCommand(key, null);
                Require(StartupRegistration.ReadCommand(key) == null, "Disable must remove the entry");
                Require((string)key.GetValue("UnrelatedApp") == "preserve", "Do not alter other startup entries");
                using (RegistryKey readOnly = Registry.CurrentUser.OpenSubKey(keyPath))
                {
                    bool failed = false;
                    try { StartupRegistration.WriteCommand(readOnly, command); }
                    catch (UnauthorizedAccessException) { failed = true; }
                    Require(failed, "Write errors must propagate to the UI");
                }
                key.SetValue(StartupRegistration.ValueName, 1, RegistryValueKind.DWord);
                bool invalid = false;
                try { StartupRegistration.ReadCommand(key); }
                catch (InvalidOperationException) { invalid = true; }
                Require(invalid, "Malformed registry data must not appear disabled");
            }
            bool missing = false;
            try { StartupRegistration.BuildCommand(Path.Combine(directory, "missing.exe")); }
            catch (ArgumentException) { missing = true; }
            Require(missing, "Missing executable must be rejected");
            Console.WriteLine("PASS: registration, reload, quoting, disable, idempotence, isolation, write failure, invalid data/path");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, false);
            File.Delete(executable);
            Directory.Delete(directory);
        }
    }
}
