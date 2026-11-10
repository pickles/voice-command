using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

internal static class NewWindowOptionTests
{
    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private static void Main()
    {
        string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
        var assembly = Assembly.LoadFrom(Path.Combine(root, "bin", "VoiceChatLauncher.exe"));
        Type configType = assembly.GetType("VoiceChatLauncher.AppConfig", true);
        MethodInfo load = configType.GetMethod("Load");
        FieldInfo option = configType.GetField("OpenChatInNewWindow");
        string path = Path.Combine(root, "bin", Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            string original = "UseNewChatGpt=true\nOpenWakeWordThreshold=0.91\nOpenWakeWordDevice=2\n";
            File.WriteAllText(path, original);
            object config = load.Invoke(null, new object[] { path });
            Require(!(bool)option.GetValue(config), "Old config must default to existing window");
            Require(File.ReadAllText(path) == original, "Reading must not change the user's file");
            foreach (bool enabled in new[] { true, false })
            {
                option.SetValue(config, enabled);
                configType.GetMethod("Save").Invoke(config, new object[] { path });
                config = load.Invoke(null, new object[] { path });
                Require((bool)option.GetValue(config) == enabled, "Option must survive save and reload");
                Require((string)configType.GetField("OpenWakeWordDevice").GetValue(config) == "2", "Preserve microphone");
                Require((float)configType.GetField("OpenWakeWordThreshold").GetValue(config) == 0.91f, "Preserve threshold");
            }
            string defaults = (string)configType.GetMethod("DefaultConfigText", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            Require(defaults.Replace("\r\n", "\n") == File.ReadAllText(Path.Combine(root, "config.example.ini")).Replace("\r\n", "\n"), "Default config mismatch");
            var existing = new HashSet<IntPtr> { new IntPtr(123), new IntPtr(456) };
            MethodInfo isNew = assembly.GetType("VoiceChatLauncher.DesktopWindowCreation", true).GetMethod("IsNewWindow", BindingFlags.NonPublic | BindingFlags.Static);
            Require(!(bool)isNew.Invoke(null, new object[] { IntPtr.Zero, existing }), "Reject zero HWND");
            Require(!(bool)isNew.Invoke(null, new object[] { new IntPtr(456), existing }), "Reject existing HWND");
            Require((bool)isNew.Invoke(null, new object[] { new IntPtr(789), existing }), "Accept newly created HWND");
            Console.WriteLine("PASS: old config default, true/false roundtrip, microphone preservation, default consistency, existing/zero HWND rejection");
        }
        finally { File.Delete(path); }
    }
}
