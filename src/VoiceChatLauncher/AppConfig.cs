using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Forms;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;


namespace VoiceChatLauncher
{
    internal sealed class AppConfig
    {
        public int CooldownMilliseconds = 4000;
        public string OpenWakeWordModels = @"..\models\Hey_Lucy_20260609_095011.onnx";
        public string OpenWakeWordMelspectrogramModelPath = @"..\models\melspectrogram.onnx";
        public string OpenWakeWordEmbeddingModelPath = @"..\models\embedding_model.onnx";
        public float OpenWakeWordThreshold = 0.8f;
        public string OpenWakeWordDevice = string.Empty;
        public float OpenWakeWordVadThreshold = 0.0f;
        public bool OpenWakeWordLogScores = false;
        public string LaunchCommand = "chatgpt:";
        public string LaunchArguments = string.Empty;
        public string WindowTitleKeyword = "ChatGPT";
        public List<string> ProcessNames = new List<string>();
        public List<string> VoiceButtonNames = new List<string>();
        public List<string> ExcludedButtonNames = new List<string>();
        public bool RightmostVoiceButtonFallback = true;
        public bool CoordinateFallbackEnabled = true;
        public int CoordinateFallbackMaxComposerWidth = 760;
        public int CoordinateFallbackHorizontalMargin = 52;
        public int CoordinateFallbackRightOffset = 20;
        public int CoordinateFallbackBottomOffset = 70;
        public int MinimumWindowWidth = 320;
        public int MinimumWindowHeight = 200;
        public bool CenterWindowOnForeground = true;
        public bool RunActionOnStartup = false;
        public int StartupActionDelayMilliseconds = 1200;
        public int StartupDelayMilliseconds = 2500;
        public int AfterBringToFrontDelayMilliseconds = 1200;
        public int WindowTimeoutMilliseconds = 15000;
        public int ButtonTimeoutMilliseconds = 15000;

        public static AppConfig Load(string path)
        {
            if (!File.Exists(path))
            {
                File.WriteAllText(path, DefaultConfigText(), Encoding.UTF8);
            }

            var config = new AppConfig();
            config.ProcessNames.Add("chatgpt.exe");
            config.ProcessNames.Add("ChatGPT");
            config.ProcessNames.Add("OpenAI");
            config.VoiceButtonNames.Add("音声を使用する");
            config.VoiceButtonNames.Add("音声を使用");
            config.VoiceButtonNames.Add("音声を開始する");
            config.VoiceButtonNames.Add("音声を開始");
            config.VoiceButtonNames.Add("Start voice mode");
            config.VoiceButtonNames.Add("Use voice mode");
            config.VoiceButtonNames.Add("Talk to ChatGPT");
            config.VoiceButtonNames.Add("Voice mode");
            config.VoiceButtonNames.Add("Start voice chat");
            config.VoiceButtonNames.Add("Start voice");
            config.ExcludedButtonNames.Add("音声入力を開始");
            config.ExcludedButtonNames.Add("音声を入力する");
            config.ExcludedButtonNames.Add("音声入力");
            config.ExcludedButtonNames.Add("プロンプトを送信する");
            config.ExcludedButtonNames.Add("プロファイルメニューを開く");

            foreach (string rawLine in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                {
                    continue;
                }

                int index = line.IndexOf('=');
                if (index <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, index).Trim();
                string value = line.Substring(index + 1).Trim();
                Apply(config, key, value);
            }

            return config;
        }

        public void Save(string path)
        {
            File.WriteAllText(path, ToConfigText(), Encoding.UTF8);
        }

        private string ToConfigText()
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Voice Chat Launcher settings");
            builder.AppendLine("# Separate multiple values with |");
            builder.AppendLine();
            builder.AppendLine("# Voice settings. Edit from the tray Settings screen or update this file directly.");
            AppendValue(builder, "OpenWakeWordModels", OpenWakeWordModels);
            AppendValue(builder, "OpenWakeWordMelspectrogramModelPath", OpenWakeWordMelspectrogramModelPath);
            AppendValue(builder, "OpenWakeWordEmbeddingModelPath", OpenWakeWordEmbeddingModelPath);
            AppendValue(builder, "OpenWakeWordThreshold", FormatFloat(OpenWakeWordThreshold));
            AppendValue(builder, "OpenWakeWordDevice", OpenWakeWordDevice);
            AppendValue(builder, "OpenWakeWordVadThreshold", FormatFloat(OpenWakeWordVadThreshold));
            AppendValue(builder, "OpenWakeWordLogScores", FormatBool(OpenWakeWordLogScores));
            AppendValue(builder, "CooldownMilliseconds", CooldownMilliseconds.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();
            builder.AppendLine("# LaunchCommand can be a URI such as chatgpt:, an .exe path, or shell:AppsFolder\\<AppID>.");
            AppendValue(builder, "LaunchCommand", LaunchCommand);
            AppendValue(builder, "LaunchArguments", LaunchArguments);
            builder.AppendLine();
            builder.AppendLine("# Used to find the ChatGPT window after launch.");
            AppendValue(builder, "WindowTitleKeyword", WindowTitleKeyword);
            AppendValue(builder, "ProcessNames", JoinList(ProcessNames));
            AppendValue(builder, "MinimumWindowWidth", MinimumWindowWidth.ToString(CultureInfo.InvariantCulture));
            AppendValue(builder, "MinimumWindowHeight", MinimumWindowHeight.ToString(CultureInfo.InvariantCulture));
            AppendValue(builder, "CenterWindowOnForeground", FormatBool(CenterWindowOnForeground));
            AppendValue(builder, "RunActionOnStartup", FormatBool(RunActionOnStartup));
            AppendValue(builder, "StartupActionDelayMilliseconds", StartupActionDelayMilliseconds.ToString(CultureInfo.InvariantCulture));
            AppendValue(builder, "StartupDelayMilliseconds", StartupDelayMilliseconds.ToString(CultureInfo.InvariantCulture));
            AppendValue(builder, "AfterBringToFrontDelayMilliseconds", AfterBringToFrontDelayMilliseconds.ToString(CultureInfo.InvariantCulture));
            AppendValue(builder, "WindowTimeoutMilliseconds", WindowTimeoutMilliseconds.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();
            builder.AppendLine("# Used to find and press the voice conversation button.");
            AppendValue(builder, "VoiceButtonNames", JoinList(VoiceButtonNames));
            AppendValue(builder, "ExcludedButtonNames", JoinList(ExcludedButtonNames));
            AppendValue(builder, "RightmostVoiceButtonFallback", FormatBool(RightmostVoiceButtonFallback));
            builder.AppendLine();
            builder.AppendLine("# If ChatGPT does not expose button names, click the visual button position instead.");
            AppendValue(builder, "CoordinateFallbackEnabled", FormatBool(CoordinateFallbackEnabled));
            AppendValue(builder, "CoordinateFallbackMaxComposerWidth", CoordinateFallbackMaxComposerWidth.ToString(CultureInfo.InvariantCulture));
            AppendValue(builder, "CoordinateFallbackHorizontalMargin", CoordinateFallbackHorizontalMargin.ToString(CultureInfo.InvariantCulture));
            AppendValue(builder, "CoordinateFallbackRightOffset", CoordinateFallbackRightOffset.ToString(CultureInfo.InvariantCulture));
            AppendValue(builder, "CoordinateFallbackBottomOffset", CoordinateFallbackBottomOffset.ToString(CultureInfo.InvariantCulture));
            AppendValue(builder, "ButtonTimeoutMilliseconds", ButtonTimeoutMilliseconds.ToString(CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static void AppendValue(StringBuilder builder, string key, string value)
        {
            builder.Append(key);
            builder.Append('=');
            builder.AppendLine(value ?? string.Empty);
        }

        private static void Apply(AppConfig config, string key, string value)
        {
            if (EqualsKey(key, "OpenWakeWordModels"))
            {
                config.OpenWakeWordModels = value;
            }
            else if (EqualsKey(key, "OpenWakeWordMelspectrogramModelPath"))
            {
                config.OpenWakeWordMelspectrogramModelPath = value;
            }
            else if (EqualsKey(key, "OpenWakeWordEmbeddingModelPath"))
            {
                config.OpenWakeWordEmbeddingModelPath = value;
            }
            else if (EqualsKey(key, "OpenWakeWordThreshold"))
            {
                float result;
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                {
                    config.OpenWakeWordThreshold = result;
                }
            }
            else if (EqualsKey(key, "OpenWakeWordDevice"))
            {
                config.OpenWakeWordDevice = value;
            }
            else if (EqualsKey(key, "OpenWakeWordVadThreshold"))
            {
                float result;
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                {
                    config.OpenWakeWordVadThreshold = result;
                }
            }
            else if (EqualsKey(key, "OpenWakeWordLogScores"))
            {
                config.OpenWakeWordLogScores = ParseBool(value, config.OpenWakeWordLogScores);
            }
            else if (EqualsKey(key, "CooldownMilliseconds"))
            {
                config.CooldownMilliseconds = ParseInt(value, config.CooldownMilliseconds);
            }
            else if (EqualsKey(key, "LaunchCommand"))
            {
                config.LaunchCommand = value;
            }
            else if (EqualsKey(key, "LaunchArguments"))
            {
                config.LaunchArguments = value;
            }
            else if (EqualsKey(key, "WindowTitleKeyword"))
            {
                config.WindowTitleKeyword = value;
            }
            else if (EqualsKey(key, "ProcessNames"))
            {
                config.ProcessNames = SplitList(value);
            }
            else if (EqualsKey(key, "VoiceButtonNames"))
            {
                config.VoiceButtonNames = SplitList(value);
            }
            else if (EqualsKey(key, "ExcludedButtonNames"))
            {
                config.ExcludedButtonNames = SplitList(value);
            }
            else if (EqualsKey(key, "RightmostVoiceButtonFallback"))
            {
                config.RightmostVoiceButtonFallback = ParseBool(value, config.RightmostVoiceButtonFallback);
            }
            else if (EqualsKey(key, "CoordinateFallbackEnabled"))
            {
                config.CoordinateFallbackEnabled = ParseBool(value, config.CoordinateFallbackEnabled);
            }
            else if (EqualsKey(key, "CoordinateFallbackMaxComposerWidth"))
            {
                config.CoordinateFallbackMaxComposerWidth = ParseInt(value, config.CoordinateFallbackMaxComposerWidth);
            }
            else if (EqualsKey(key, "CoordinateFallbackHorizontalMargin"))
            {
                config.CoordinateFallbackHorizontalMargin = ParseInt(value, config.CoordinateFallbackHorizontalMargin);
            }
            else if (EqualsKey(key, "CoordinateFallbackRightOffset"))
            {
                config.CoordinateFallbackRightOffset = ParseInt(value, config.CoordinateFallbackRightOffset);
            }
            else if (EqualsKey(key, "CoordinateFallbackBottomOffset"))
            {
                config.CoordinateFallbackBottomOffset = ParseInt(value, config.CoordinateFallbackBottomOffset);
            }
            else if (EqualsKey(key, "MinimumWindowWidth"))
            {
                config.MinimumWindowWidth = ParseInt(value, config.MinimumWindowWidth);
            }
            else if (EqualsKey(key, "MinimumWindowHeight"))
            {
                config.MinimumWindowHeight = ParseInt(value, config.MinimumWindowHeight);
            }
            else if (EqualsKey(key, "CenterWindowOnForeground"))
            {
                config.CenterWindowOnForeground = ParseBool(value, config.CenterWindowOnForeground);
            }
            else if (EqualsKey(key, "RunActionOnStartup"))
            {
                config.RunActionOnStartup = ParseBool(value, config.RunActionOnStartup);
            }
            else if (EqualsKey(key, "StartupActionDelayMilliseconds"))
            {
                config.StartupActionDelayMilliseconds = ParseInt(value, config.StartupActionDelayMilliseconds);
            }
            else if (EqualsKey(key, "StartupDelayMilliseconds"))
            {
                config.StartupDelayMilliseconds = ParseInt(value, config.StartupDelayMilliseconds);
            }
            else if (EqualsKey(key, "AfterBringToFrontDelayMilliseconds"))
            {
                config.AfterBringToFrontDelayMilliseconds = ParseInt(value, config.AfterBringToFrontDelayMilliseconds);
            }
            else if (EqualsKey(key, "WindowTimeoutMilliseconds"))
            {
                config.WindowTimeoutMilliseconds = ParseInt(value, config.WindowTimeoutMilliseconds);
            }
            else if (EqualsKey(key, "ButtonTimeoutMilliseconds"))
            {
                config.ButtonTimeoutMilliseconds = ParseInt(value, config.ButtonTimeoutMilliseconds);
            }
        }

        private static bool EqualsKey(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static int ParseInt(string value, int fallback)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : fallback;
        }

        private static bool ParseBool(string value, bool fallback)
        {
            bool parsed;
            if (bool.TryParse(value, out parsed))
            {
                return parsed;
            }

            if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "on", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return fallback;
        }

        private static string FormatBool(bool value)
        {
            return value ? "true" : "false";
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string JoinList(List<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return string.Empty;
            }

            return string.Join("|", values.ToArray());
        }

        private static List<string> SplitList(string value)
        {
            var list = new List<string>();
            string[] parts = value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0)
                {
                    list.Add(trimmed);
                }
            }

            return list;
        }

        private static string DefaultConfigText()
        {
            return
@"# Voice Chat Launcher settings
# Separate multiple values with |

# The wake word that triggers ChatGPT voice mode.
# Say ""Hey Lucy"" by default.
OpenWakeWordModels=..\models\Hey_Lucy_20260609_095011.onnx
OpenWakeWordMelspectrogramModelPath=..\models\melspectrogram.onnx
OpenWakeWordEmbeddingModelPath=..\models\embedding_model.onnx
OpenWakeWordThreshold=0.80
OpenWakeWordDevice=
OpenWakeWordVadThreshold=0.0
OpenWakeWordLogScores=false
CooldownMilliseconds=4000

# LaunchCommand can be a URI such as chatgpt:, an .exe path, or shell:AppsFolder\<AppID>.
# If ChatGPT is already open, you can leave LaunchCommand empty and the app will only search for the window.
LaunchCommand=chatgpt:
LaunchArguments=

# Used to find the ChatGPT window after launch.
WindowTitleKeyword=ChatGPT
ProcessNames=chatgpt.exe|ChatGPT|OpenAI
MinimumWindowWidth=320
MinimumWindowHeight=200
CenterWindowOnForeground=true
RunActionOnStartup=false
StartupActionDelayMilliseconds=1200
StartupDelayMilliseconds=2500
AfterBringToFrontDelayMilliseconds=1200
WindowTimeoutMilliseconds=15000

# Used to find and press the voice conversation button.
VoiceButtonNames=音声を使用する|音声を使用|音声を開始する|音声を開始|音声モードを開始|音声モードを開始する|音声チャットを開始|音声で会話する|Start voice chat|Start voice|Start voice mode|Use voice mode|Talk to ChatGPT|Voice mode
ExcludedButtonNames=音声入力を開始|音声を入力する|音声入力|プロンプトを送信する|プロファイルメニューを開く
RightmostVoiceButtonFallback=false

# If ChatGPT does not expose button names, click the visual button position instead.
CoordinateFallbackEnabled=true
CoordinateFallbackMaxComposerWidth=760
CoordinateFallbackHorizontalMargin=52
CoordinateFallbackRightOffset=20
CoordinateFallbackBottomOffset=70
ButtonTimeoutMilliseconds=15000
";
        }
    }
}
