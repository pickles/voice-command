using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Speech.Recognition;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Forms;

namespace VoiceChatLauncher
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new LauncherContext());
        }
    }

    internal sealed class LauncherContext : ApplicationContext
    {
        private readonly string _baseDirectory;
        private readonly string _configPath;
        private readonly string _logPath;
        private NotifyIcon _notifyIcon;
        private SettingsForm _settingsForm;
        private AppConfig _config;
        private SpeechRecognitionEngine _recognizer;
        private Process _wakeWordProcess;
        private bool _isRunningAction;
        private DateTime _lastTriggered = DateTime.MinValue;
        private DateTime _lastAudioLevelLog = DateTime.MinValue;

        public LauncherContext()
        {
            _baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _configPath = Path.Combine(_baseDirectory, "config.ini");
            _logPath = Path.Combine(_baseDirectory, "voice-command.log");

            InitializeTray();
            LoadConfiguration();
            StartListening();
            TriggerStartupActionIfNeeded();
        }

        private void InitializeTray()
        {
            var menu = new ContextMenu();
            menu.MenuItems.Add("状態を表示", delegate { ShowStatus(); });
            menu.MenuItems.Add("今すぐ起動", delegate { TriggerAction("tray"); });
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("設定", delegate { OpenConfig(); });
            menu.MenuItems.Add("設定を再読み込み", delegate { Reload(); });
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("終了", delegate { ExitThread(); });

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = SystemIcons.Application;
            _notifyIcon.Text = "Voice Chat Launcher";
            _notifyIcon.Visible = true;
            _notifyIcon.ContextMenu = menu;
            _notifyIcon.DoubleClick += delegate { TriggerAction("tray-double-click"); };
        }

        private void LoadConfiguration()
        {
            _config = AppConfig.Load(_configPath);
            Log("Configuration loaded from " + _configPath);
        }

        private void Reload()
        {
            StopListening();
            LoadConfiguration();
            StartListening();
            ShowBalloon("設定を再読み込みしました", "合図: " + string.Join(", ", _config.Keywords));
        }

        private void TriggerStartupActionIfNeeded()
        {
            if (!_config.RunActionOnStartup)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                Thread.Sleep(_config.StartupActionDelayMilliseconds);
                TriggerAction("startup");
            });
        }

        private void StartListening()
        {
            if (_config.UseOpenWakeWord)
            {
                StartOpenWakeWordListening();
                return;
            }

            try
            {
                if (_config.Keywords.Count == 0)
                {
                    throw new InvalidOperationException("config.ini の Keywords が空です。");
                }

                RecognizerInfo recognizerInfo = FindRecognizer(_config.Culture);
                _recognizer = recognizerInfo == null
                    ? new SpeechRecognitionEngine()
                    : new SpeechRecognitionEngine(recognizerInfo);

                var choices = new Choices(_config.Keywords.ToArray());
                var grammarBuilder = new GrammarBuilder(choices);
                grammarBuilder.Culture = _recognizer.RecognizerInfo.Culture;

                var grammar = new Grammar(grammarBuilder);
                grammar.Name = "keywords";
                _recognizer.LoadGrammar(grammar);
                if (_config.EnableDictationFallback)
                {
                    try
                    {
                        var dictationGrammar = new DictationGrammar();
                        dictationGrammar.Name = "dictation";
                        _recognizer.LoadGrammar(dictationGrammar);
                        Log("Dictation fallback enabled");
                    }
                    catch (Exception ex)
                    {
                        Log("Failed to enable dictation fallback: " + ex.Message);
                    }
                }

                _recognizer.SetInputToDefaultAudioDevice();
                _recognizer.AudioLevelUpdated += OnAudioLevelUpdated;
                _recognizer.AudioSignalProblemOccurred += OnAudioSignalProblemOccurred;
                _recognizer.AudioStateChanged += OnAudioStateChanged;
                _recognizer.SpeechDetected += OnSpeechDetected;
                _recognizer.SpeechRecognized += OnSpeechRecognized;
                _recognizer.SpeechRecognitionRejected += OnSpeechRecognitionRejected;
                _recognizer.RecognizeCompleted += OnRecognizeCompleted;
                _recognizer.RecognizeAsync(RecognizeMode.Multiple);

                Log("Listening with " + _recognizer.RecognizerInfo.Name + " (" + _recognizer.RecognizerInfo.Culture.Name + ")");
                ShowBalloon("待ち受けを開始しました", "合図: " + string.Join(", ", _config.Keywords));
            }
            catch (Exception ex)
            {
                Log("Failed to start listening: " + ex);
                ShowBalloon("待ち受けを開始できません", ShortMessage(ex));
            }
        }

        private void StopListening()
        {
            StopOpenWakeWordListening();

            if (_recognizer == null)
            {
                return;
            }

            try
            {
                _recognizer.SpeechRecognized -= OnSpeechRecognized;
                _recognizer.SpeechRecognitionRejected -= OnSpeechRecognitionRejected;
                _recognizer.RecognizeCompleted -= OnRecognizeCompleted;
                _recognizer.AudioLevelUpdated -= OnAudioLevelUpdated;
                _recognizer.AudioSignalProblemOccurred -= OnAudioSignalProblemOccurred;
                _recognizer.AudioStateChanged -= OnAudioStateChanged;
                _recognizer.SpeechDetected -= OnSpeechDetected;
                _recognizer.RecognizeAsyncCancel();
                _recognizer.Dispose();
            }
            catch (Exception ex)
            {
                Log("Failed to stop recognizer: " + ex);
            }
            finally
            {
                _recognizer = null;
            }
        }

        private void StartOpenWakeWordListening()
        {
            try
            {
                string pythonPath = ResolvePath(_config.OpenWakeWordPythonPath);
                string scriptPath = ResolvePath(_config.OpenWakeWordScriptPath);
                if (!File.Exists(pythonPath))
                {
                    throw new FileNotFoundException("OpenWakeWord Python が見つかりません。setup_openwakeword.ps1 を実行してください。", pythonPath);
                }

                if (!File.Exists(scriptPath))
                {
                    throw new FileNotFoundException("OpenWakeWord listener script が見つかりません。", scriptPath);
                }

                var startInfo = new ProcessStartInfo();
                startInfo.FileName = pythonPath;
                startInfo.Arguments =
                    QuoteArgument(scriptPath) +
                    " --models " + QuoteArgument(_config.OpenWakeWordModels) +
                    " --threshold " + _config.OpenWakeWordThreshold.ToString("0.###", CultureInfo.InvariantCulture) +
                    " --cooldown-ms " + _config.CooldownMilliseconds.ToString(CultureInfo.InvariantCulture) +
                    " --vad-threshold " + _config.OpenWakeWordVadThreshold.ToString("0.###", CultureInfo.InvariantCulture);

                if (!string.IsNullOrWhiteSpace(_config.OpenWakeWordDevice))
                {
                    startInfo.Arguments += " --device " + QuoteArgument(_config.OpenWakeWordDevice);
                }

                if (_config.OpenWakeWordLogScores)
                {
                    startInfo.Arguments += " --log-scores";
                }

                startInfo.WorkingDirectory = _baseDirectory;
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;

                _wakeWordProcess = new Process();
                _wakeWordProcess.StartInfo = startInfo;
                _wakeWordProcess.EnableRaisingEvents = true;
                _wakeWordProcess.OutputDataReceived += OnOpenWakeWordOutput;
                _wakeWordProcess.ErrorDataReceived += OnOpenWakeWordError;
                _wakeWordProcess.Exited += OnOpenWakeWordExited;
                _wakeWordProcess.Start();
                _wakeWordProcess.BeginOutputReadLine();
                _wakeWordProcess.BeginErrorReadLine();

                Log("OpenWakeWord listener started: " + startInfo.FileName + " " + startInfo.Arguments);
                ShowBalloon("待ち受けを開始しました", "WakeWord: " + _config.OpenWakeWordModels.Replace("_", " "));
            }
            catch (Exception ex)
            {
                Log("Failed to start OpenWakeWord listener: " + ex);
                ShowBalloon("OpenWakeWord を開始できません", ShortMessage(ex));
            }
        }

        private void StopOpenWakeWordListening()
        {
            if (_wakeWordProcess == null)
            {
                return;
            }

            try
            {
                _wakeWordProcess.OutputDataReceived -= OnOpenWakeWordOutput;
                _wakeWordProcess.ErrorDataReceived -= OnOpenWakeWordError;
                _wakeWordProcess.Exited -= OnOpenWakeWordExited;
                if (!_wakeWordProcess.HasExited)
                {
                    _wakeWordProcess.Kill();
                }

                _wakeWordProcess.Dispose();
            }
            catch (Exception ex)
            {
                Log("Failed to stop OpenWakeWord listener: " + ex);
            }
            finally
            {
                _wakeWordProcess = null;
            }
        }

        private void OnOpenWakeWordOutput(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Data))
            {
                return;
            }

            Log("OpenWakeWord: " + e.Data);
            if (e.Data.StartsWith("WAKE ", StringComparison.OrdinalIgnoreCase))
            {
                TriggerAction("wakeword:" + e.Data);
            }
        }

        private void OnOpenWakeWordError(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                Log("OpenWakeWord error: " + e.Data);
            }
        }

        private void OnOpenWakeWordExited(object sender, EventArgs e)
        {
            try
            {
                int exitCode = _wakeWordProcess == null ? -1 : _wakeWordProcess.ExitCode;
                Log("OpenWakeWord listener exited: " + exitCode);
            }
            catch
            {
                Log("OpenWakeWord listener exited");
            }
        }

        private RecognizerInfo FindRecognizer(string cultureName)
        {
            var recognizers = SpeechRecognitionEngine.InstalledRecognizers();
            if (string.IsNullOrWhiteSpace(cultureName))
            {
                foreach (RecognizerInfo info in recognizers)
                {
                    return info;
                }

                return null;
            }

            foreach (RecognizerInfo info in recognizers)
            {
                if (string.Equals(info.Culture.Name, cultureName, StringComparison.OrdinalIgnoreCase))
                {
                    return info;
                }
            }

            Log("Requested recognizer culture was not found: " + cultureName);
            return null;
        }

        private void OnSpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            if (e.Result == null)
            {
                return;
            }

            string grammarName = e.Result.Grammar == null ? string.Empty : e.Result.Grammar.Name;
            Log("Recognized '" + e.Result.Text + "' grammar=" + grammarName + " confidence=" + e.Result.Confidence.ToString("0.00", CultureInfo.InvariantCulture));
            if (e.Result.Confidence < _config.MinimumConfidence)
            {
                return;
            }

            if (string.Equals(grammarName, "dictation", StringComparison.OrdinalIgnoreCase) &&
                !TextMatchesKeyword(e.Result.Text))
            {
                Log("Dictation text did not match keywords: '" + e.Result.Text + "'");
                return;
            }

            TriggerAction("voice:" + e.Result.Text);
        }

        private void OnSpeechRecognitionRejected(object sender, SpeechRecognitionRejectedEventArgs e)
        {
            if (e.Result == null)
            {
                Log("Speech rejected: no result");
                return;
            }

            Log("Speech rejected: '" + e.Result.Text + "' confidence=" + e.Result.Confidence.ToString("0.00", CultureInfo.InvariantCulture));
        }

        private void OnSpeechDetected(object sender, SpeechDetectedEventArgs e)
        {
            Log("Speech detected at audio position " + e.AudioPosition);
        }

        private void OnAudioStateChanged(object sender, AudioStateChangedEventArgs e)
        {
            Log("Audio state changed: " + e.AudioState);
        }

        private void OnAudioSignalProblemOccurred(object sender, AudioSignalProblemOccurredEventArgs e)
        {
            Log("Audio signal problem: " + e.AudioSignalProblem);
        }

        private void OnAudioLevelUpdated(object sender, AudioLevelUpdatedEventArgs e)
        {
            if (e.AudioLevel <= 0)
            {
                return;
            }

            if ((DateTime.Now - _lastAudioLevelLog).TotalSeconds < 5)
            {
                return;
            }

            _lastAudioLevelLog = DateTime.Now;
            Log("Audio level: " + e.AudioLevel);
        }

        private bool TextMatchesKeyword(string text)
        {
            string normalizedText = NormalizeForKeywordMatch(text);
            foreach (string keyword in _config.Keywords)
            {
                string normalizedKeyword = NormalizeForKeywordMatch(keyword);
                if (normalizedText.IndexOf(normalizedKeyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeForKeywordMatch(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string normalized = text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            var builder = new StringBuilder(normalized.Length);
            foreach (char ch in normalized)
            {
                if (!char.IsWhiteSpace(ch) && !char.IsPunctuation(ch) && !char.IsSymbol(ch))
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }

        private void OnRecognizeCompleted(object sender, RecognizeCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                Log("Recognition stopped with error: " + e.Error);
                ShowBalloon("音声認識が停止しました", ShortMessage(e.Error));
            }
        }

        private void TriggerAction(string source)
        {
            if ((DateTime.Now - _lastTriggered).TotalMilliseconds < _config.CooldownMilliseconds)
            {
                Log("Trigger ignored during cooldown: " + source);
                return;
            }

            if (_isRunningAction)
            {
                Log("Trigger ignored because action is already running: " + source);
                return;
            }

            _lastTriggered = DateTime.Now;
            _isRunningAction = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    Log("Action started: " + source);
                    RunAction();
                    Log("Action finished");
                }
                catch (Exception ex)
                {
                    Log("Action failed: " + ex);
                    ShowBalloon("起動操作に失敗しました", ShortMessage(ex));
                }
                finally
                {
                    _isRunningAction = false;
                }
            });
        }

        private void RunAction()
        {
            AutomationElement window = FindChatGptWindow();
            if (window == null)
            {
                Log("ChatGPT window was not found; launching ChatGPT even if a background process remains.");
                LaunchChatGpt();
                Thread.Sleep(_config.StartupDelayMilliseconds);
                window = WaitForChatGptWindow();
            }
            else
            {
                Log("Existing ChatGPT window found: " + SafeName(window));
            }

            if (window == null)
            {
                throw new InvalidOperationException("ChatGPT のウィンドウが見つかりませんでした。config.ini の WindowTitleKeyword / ProcessNames を確認してください。");
            }

            BringToFront(window);
            Thread.Sleep(_config.AfterBringToFrontDelayMilliseconds);
            AutomationElement button = WaitForVoiceButton(window);
            if (button == null)
            {
                if (_config.CoordinateFallbackEnabled)
                {
                    ClickVoiceButtonByCoordinates(window);
                    return;
                }

                throw new InvalidOperationException("音声ボタンが見つかりませんでした。config.ini の VoiceButtonNames を確認してください。");
            }

            PressButton(button);
        }

        private void LaunchChatGpt()
        {
            if (string.IsNullOrWhiteSpace(_config.LaunchCommand))
            {
                Log("LaunchCommand is empty; only searching for an existing window.");
                return;
            }

            var startInfo = new ProcessStartInfo();
            if (_config.LaunchCommand.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.FileName = "explorer.exe";
                startInfo.Arguments = QuoteArgument(_config.LaunchCommand);
            }
            else
            {
                startInfo.FileName = _config.LaunchCommand;
                startInfo.Arguments = _config.LaunchArguments ?? string.Empty;
            }

            startInfo.UseShellExecute = true;
            Process.Start(startInfo);
            Log("Launch command sent: " + _config.LaunchCommand);
        }

        private bool IsChatGptProcessRunning()
        {
            if (_config.ProcessNames.Count == 0)
            {
                return false;
            }

            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    foreach (string expected in _config.ProcessNames)
                    {
                        if (MatchesProcessName(process.ProcessName, expected))
                        {
                            Log("ChatGPT process found: " + process.ProcessName + " (" + process.Id + ")");
                            return true;
                        }
                    }
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }

            return false;
        }

        private AutomationElement WaitForChatGptWindow()
        {
            DateTime until = DateTime.Now.AddMilliseconds(_config.WindowTimeoutMilliseconds);
            while (DateTime.Now < until)
            {
                AutomationElement window = FindChatGptWindow();
                if (window != null)
                {
                    Log("Window found: " + SafeName(window));
                    return window;
                }

                Thread.Sleep(300);
            }

            return null;
        }

        private AutomationElement FindChatGptWindow()
        {
            WindowCandidate bestCandidate = null;
            double bestScore = double.MinValue;

            foreach (WindowCandidate candidate in EnumerateChatGptWindowCandidates())
            {
                if (candidate.Width < _config.MinimumWindowWidth ||
                    candidate.Height < _config.MinimumWindowHeight)
                {
                    Log("Skipping non-main ChatGPT HWND: " + candidate);
                    continue;
                }

                double score = candidate.Width * candidate.Height;
                if (candidate.IsVisible)
                {
                    score += 100000000;
                }

                if (!candidate.IsMinimized)
                {
                    score += 10000000;
                }

                if (score > bestScore)
                {
                    bestCandidate = candidate;
                    bestScore = score;
                }
            }

            if (bestCandidate == null)
            {
                return null;
            }

            Log("Selected ChatGPT HWND: " + bestCandidate);
            try
            {
                return AutomationElement.FromHandle(bestCandidate.Handle);
            }
            catch (Exception ex)
            {
                Log("Failed to create AutomationElement from HWND: " + ex.Message);
                return null;
            }
        }

        private List<WindowCandidate> EnumerateChatGptWindowCandidates()
        {
            var candidates = new List<WindowCandidate>();
            NativeMethods.EnumWindows(delegate (IntPtr handle, IntPtr lParam)
            {
                try
                {
                    WindowCandidate candidate = CreateWindowCandidate(handle);
                    if (candidate != null)
                    {
                        candidates.Add(candidate);
                        Log("Found ChatGPT HWND candidate: " + candidate);
                    }
                }
                catch (Exception ex)
                {
                    Log("Failed to inspect HWND " + handle + ": " + ex.Message);
                }

                return true;
            }, IntPtr.Zero);

            return candidates;
        }

        private WindowCandidate CreateWindowCandidate(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            bool isVisible = NativeMethods.IsWindowVisible(handle);
            bool isMinimized = NativeMethods.IsIconic(handle);
            if (!isVisible && !isMinimized)
            {
                return null;
            }

            int processId;
            NativeMethods.GetWindowThreadProcessId(handle, out processId);
            if (processId <= 0)
            {
                return null;
            }

            string processName;
            try
            {
                processName = Process.GetProcessById(processId).ProcessName;
            }
            catch
            {
                return null;
            }

            bool processMatches = false;
            foreach (string expected in _config.ProcessNames)
            {
                if (MatchesProcessName(processName, expected))
                {
                    processMatches = true;
                    break;
                }
            }

            if (!processMatches)
            {
                return null;
            }

            NativeMethods.RECT rect = GetEffectiveWindowRect(handle);
            int width = Math.Max(0, rect.Right - rect.Left);
            int height = Math.Max(0, rect.Bottom - rect.Top);
            return new WindowCandidate
            {
                Handle = handle,
                ProcessId = processId,
                ProcessName = processName,
                Title = GetWindowText(handle),
                ClassName = GetClassName(handle),
                IsVisible = isVisible,
                IsMinimized = isMinimized,
                Left = rect.Left,
                Top = rect.Top,
                Width = width,
                Height = height
            };
        }

        private NativeMethods.RECT GetEffectiveWindowRect(IntPtr handle)
        {
            var placement = new NativeMethods.WINDOWPLACEMENT();
            placement.length = Marshal.SizeOf(typeof(NativeMethods.WINDOWPLACEMENT));
            if (NativeMethods.GetWindowPlacement(handle, ref placement) && NativeMethods.IsIconic(handle))
            {
                return placement.rcNormalPosition;
            }

            NativeMethods.RECT rect;
            if (NativeMethods.GetWindowRect(handle, out rect))
            {
                return rect;
            }

            return new NativeMethods.RECT();
        }

        private string GetWindowText(IntPtr handle)
        {
            var builder = new StringBuilder(512);
            NativeMethods.GetWindowText(handle, builder, builder.Capacity);
            return builder.ToString();
        }

        private string GetClassName(IntPtr handle)
        {
            var builder = new StringBuilder(256);
            NativeMethods.GetClassName(handle, builder, builder.Capacity);
            return builder.ToString();
        }

        private bool MatchesWindow(AutomationElement window)
        {
            string title = SafeName(window);
            if (!string.IsNullOrWhiteSpace(_config.WindowTitleKeyword) &&
                title.IndexOf(_config.WindowTitleKeyword, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            int processId = 0;
            try
            {
                processId = window.Current.ProcessId;
            }
            catch
            {
                return false;
            }

            if (processId <= 0 || _config.ProcessNames.Count == 0)
            {
                return false;
            }

            try
            {
                string processName = Process.GetProcessById(processId).ProcessName;
                foreach (string expected in _config.ProcessNames)
                {
                    if (MatchesProcessName(processName, expected))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private bool MatchesProcessName(string actual, string expected)
        {
            if (string.IsNullOrWhiteSpace(actual) || string.IsNullOrWhiteSpace(expected))
            {
                return false;
            }

            string normalizedExpected = expected.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? expected.Substring(0, expected.Length - 4)
                : expected;

            return actual.IndexOf(normalizedExpected, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void BringToFront(AutomationElement window)
        {
            IntPtr handle = new IntPtr(window.Current.NativeWindowHandle);
            if (handle == IntPtr.Zero)
            {
                return;
            }

            Log("Window state before foreground: " + GetWindowStateDescription(window, handle));
            NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
            NativeMethods.ShowWindow(handle, NativeMethods.SW_SHOW);
            if (_config.CenterWindowOnForeground)
            {
                CenterWindow(handle);
            }

            NativeMethods.SetForegroundWindow(handle);
            Log("Window state after foreground: " + GetWindowStateDescription(window, handle));
        }

        private void CenterWindow(IntPtr handle)
        {
            NativeMethods.RECT windowRect;
            if (!NativeMethods.GetWindowRect(handle, out windowRect))
            {
                Log("Window center skipped: GetWindowRect failed");
                return;
            }

            int width = Math.Max(1, windowRect.Right - windowRect.Left);
            int height = Math.Max(1, windowRect.Bottom - windowRect.Top);
            IntPtr monitor = NativeMethods.MonitorFromWindow(handle, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var monitorInfo = new NativeMethods.MONITORINFO();
            monitorInfo.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));
            if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
            {
                Log("Window center skipped: GetMonitorInfo failed");
                return;
            }

            NativeMethods.RECT workArea = monitorInfo.rcWork;
            int workWidth = workArea.Right - workArea.Left;
            int workHeight = workArea.Bottom - workArea.Top;
            int x = workArea.Left + Math.Max(0, (workWidth - width) / 2);
            int y = workArea.Top + Math.Max(0, (workHeight - height) / 2);

            NativeMethods.SetWindowPos(
                handle,
                IntPtr.Zero,
                x,
                y,
                width,
                height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            Log("Window centered at " + x + "," + y + " size=" + width + "x" + height);
        }

        private string GetWindowStateDescription(AutomationElement window, IntPtr handle)
        {
            var parts = new List<string>();

            try
            {
                parts.Add("isOffscreen=" + window.Current.IsOffscreen);
            }
            catch
            {
            }

            try
            {
                parts.Add("bounds=" + window.Current.BoundingRectangle);
            }
            catch
            {
            }

            try
            {
                object pattern;
                if (window.TryGetCurrentPattern(WindowPattern.Pattern, out pattern))
                {
                    parts.Add("visualState=" + ((WindowPattern)pattern).Current.WindowVisualState);
                }
            }
            catch
            {
            }

            try
            {
                parts.Add("iconic=" + NativeMethods.IsIconic(handle));
            }
            catch
            {
            }

            return string.Join(" ", parts.ToArray());
        }

        private AutomationElement WaitForVoiceButton(AutomationElement window)
        {
            DateTime until = DateTime.Now.AddMilliseconds(_config.ButtonTimeoutMilliseconds);
            while (DateTime.Now < until)
            {
                AutomationElement button = FindVoiceButton(window);
                if (button != null)
                {
                    Log("Button found: " + SafeName(button));
                    return button;
                }

                Thread.Sleep(300);
            }

            return null;
        }

        private AutomationElement FindVoiceButton(AutomationElement window)
        {
            AutomationElementCollection buttons = window.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            AutomationElement containsMatch = null;
            AutomationElement rightmostFallback = null;
            double rightmostFallbackX = double.MinValue;
            var candidates = new List<string>();

            foreach (AutomationElement element in buttons)
            {
                string name = SafeName(element);
                ControlType controlType = SafeControlType(element);
                if (IsExcludedButtonName(name))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(name) && IsUsefulCandidate(name, controlType))
                {
                    candidates.Add(SafeControlTypeName(controlType) + " '" + name + "'");
                }

                if (controlType == ControlType.Button && IsRightmostFallbackCandidate(window, element, name))
                {
                    Rect bounds = SafeBounds(element);
                    if (!bounds.IsEmpty && bounds.Right > rightmostFallbackX)
                    {
                        rightmostFallback = element;
                        rightmostFallbackX = bounds.Right;
                    }
                }

                if (!string.IsNullOrWhiteSpace(name))
                {
                    foreach (string expected in _config.VoiceButtonNames)
                    {
                        if (string.Equals(name, expected, StringComparison.OrdinalIgnoreCase))
                        {
                            return element;
                        }

                        if (containsMatch == null &&
                            name.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            containsMatch = element;
                        }
                    }
                }
            }

            if (containsMatch != null)
            {
                return containsMatch;
            }

            if (_config.RightmostVoiceButtonFallback && rightmostFallback != null)
            {
                Log("Using rightmost bottom button fallback: " + SafeControlTypeName(SafeControlType(rightmostFallback)) + " '" + SafeName(rightmostFallback) + "'");
                return rightmostFallback;
            }

            if (candidates.Count > 0)
            {
                Log("Visible voice/button candidates: " + string.Join(" | ", candidates.ToArray()));
            }
            else
            {
                Log("No visible voice/button candidates were exposed through UI Automation.");
            }

            return null;
        }

        private bool IsRightmostFallbackCandidate(AutomationElement window, AutomationElement element, string name)
        {
            if (!_config.RightmostVoiceButtonFallback)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(name) && !IsUsefulCandidate(name, ControlType.Button))
            {
                return false;
            }

            Rect windowBounds = SafeBounds(window);
            Rect elementBounds = SafeBounds(element);
            if (windowBounds.IsEmpty || elementBounds.IsEmpty)
            {
                return false;
            }

            double width = elementBounds.Width;
            double height = elementBounds.Height;
            if (width < 16 || height < 16 || width > 96 || height > 96)
            {
                return false;
            }

            double elementCenterY = elementBounds.Top + elementBounds.Height / 2;
            double lowerHalfStart = windowBounds.Top + windowBounds.Height * 0.50;
            return elementCenterY >= lowerHalfStart &&
                elementBounds.Left >= windowBounds.Left &&
                elementBounds.Right <= windowBounds.Right &&
                elementBounds.Top >= windowBounds.Top &&
                elementBounds.Bottom <= windowBounds.Bottom;
        }

        private bool IsUsefulCandidate(string name, ControlType controlType)
        {
            if (IsExcludedButtonName(name))
            {
                return false;
            }

            if (controlType == ControlType.Button)
            {
                return true;
            }

            return name.IndexOf("音声", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("入力", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("voice", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("mic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("microphone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("dictat", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsExcludedButtonName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            foreach (string excluded in _config.ExcludedButtonNames)
            {
                if (!string.IsNullOrWhiteSpace(excluded) &&
                    name.IndexOf(excluded, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private ControlType SafeControlType(AutomationElement element)
        {
            try
            {
                return element.Current.ControlType;
            }
            catch
            {
                return null;
            }
        }

        private Rect SafeBounds(AutomationElement element)
        {
            try
            {
                return element.Current.BoundingRectangle;
            }
            catch
            {
                return Rect.Empty;
            }
        }

        private string SafeControlTypeName(ControlType controlType)
        {
            if (controlType == null)
            {
                return "Unknown";
            }

            string name = controlType.ProgrammaticName ?? string.Empty;
            return name.Replace("ControlType.", string.Empty);
        }

        private void PressButton(AutomationElement button)
        {
            object invokePattern;
            if (button.TryGetCurrentPattern(InvokePattern.Pattern, out invokePattern))
            {
                ((InvokePattern)invokePattern).Invoke();
                Log("Button invoked through UI Automation");
                return;
            }

            Rect bounds = button.Current.BoundingRectangle;
            if (bounds.IsEmpty)
            {
                throw new InvalidOperationException("ボタンの位置を取得できませんでした。");
            }

            int x = (int)(bounds.Left + bounds.Width / 2);
            int y = (int)(bounds.Top + bounds.Height / 2);
            NativeMethods.SetCursorPos(x, y);
            NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTDOWN, x, y, 0, UIntPtr.Zero);
            NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTUP, x, y, 0, UIntPtr.Zero);
            Log("Button clicked at " + x + "," + y);
        }

        private void ClickVoiceButtonByCoordinates(AutomationElement window)
        {
            Rect bounds = SafeBounds(window);
            if (bounds.IsEmpty)
            {
                throw new InvalidOperationException("ChatGPT ウィンドウの位置を取得できませんでした。");
            }

            double composerWidth = Math.Min(
                Math.Max(240, bounds.Width - _config.CoordinateFallbackHorizontalMargin),
                _config.CoordinateFallbackMaxComposerWidth);

            int x = (int)(bounds.Left + bounds.Width / 2 + composerWidth / 2 - _config.CoordinateFallbackRightOffset);
            int y = (int)(bounds.Bottom - _config.CoordinateFallbackBottomOffset);

            NativeMethods.SetCursorPos(x, y);
            Thread.Sleep(80);
            NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTDOWN, x, y, 0, UIntPtr.Zero);
            NativeMethods.MouseEvent(NativeMethods.MOUSEEVENTF_LEFTUP, x, y, 0, UIntPtr.Zero);
            Log("Voice button clicked by coordinate fallback at " + x + "," + y + " window=" + bounds);
        }

        private void ShowStatus()
        {
            string recognizer;
            if (_config.UseOpenWakeWord)
            {
                recognizer = _wakeWordProcess == null || _wakeWordProcess.HasExited
                    ? "OpenWakeWord 停止中"
                    : "OpenWakeWord 待ち受け中 (" + _config.OpenWakeWordModels + ")";
            }
            else
            {
                recognizer = _recognizer == null
                    ? "停止中"
                    : _recognizer.RecognizerInfo.Name + " (" + _recognizer.RecognizerInfo.Culture.Name + ")";
            }

            MessageBox.Show(
                "状態: " + ((_config.UseOpenWakeWord && _wakeWordProcess != null && !_wakeWordProcess.HasExited) || _recognizer != null ? "待ち受け中" : "停止中") + Environment.NewLine +
                "音声認識: " + recognizer + Environment.NewLine +
                "合図: " + (_config.UseOpenWakeWord ? _config.OpenWakeWordModels.Replace("_", " ") : string.Join(", ", _config.Keywords)) + Environment.NewLine +
                "起動: " + _config.LaunchCommand + Environment.NewLine +
                "ログ: " + _logPath,
                "Voice Chat Launcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void OpenConfig()
        {
            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                _settingsForm.Activate();
                return;
            }

            try
            {
                _settingsForm = new SettingsForm(AppConfig.Load(_configPath), _configPath);
                _settingsForm.FormClosed += delegate(object sender, FormClosedEventArgs e)
                {
                    var form = sender as SettingsForm;
                    if (form != null && form.SettingsSaved)
                    {
                        Reload();
                    }

                    _settingsForm = null;
                };
                _settingsForm.Show();
                _settingsForm.Activate();
            }
            catch (Exception ex)
            {
                Log("Failed to open settings window: " + ex);
                MessageBox.Show(
                    ShortMessage(ex),
                    "設定を開けません",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void ShowBalloon(string title, string text)
        {
            try
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.ShowBalloonTip(4000, title, text, ToolTipIcon.Info);
                }
            }
            catch
            {
            }
        }

        private void Log(string message)
        {
            try
            {
                File.AppendAllText(
                    _logPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static string SafeName(AutomationElement element)
        {
            try
            {
                return element.Current.Name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private bool SafeIsOffscreen(AutomationElement element)
        {
            try
            {
                return element.Current.IsOffscreen;
            }
            catch
            {
                return false;
            }
        }

        private static string ShortMessage(Exception ex)
        {
            if (ex == null)
            {
                return string.Empty;
            }

            string message = ex.Message ?? ex.GetType().Name;
            return message.Length > 180 ? message.Substring(0, 180) : message;
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "\"\"";
            }

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private string ResolvePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            string expanded = Environment.ExpandEnvironmentVariables(value);
            if (Path.IsPathRooted(expanded))
            {
                return expanded;
            }

            return Path.GetFullPath(Path.Combine(_baseDirectory, expanded));
        }

        protected override void ExitThreadCore()
        {
            StopListening();
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }

            base.ExitThreadCore();
        }
    }

    internal sealed class SettingsForm : Form
    {
        private readonly AppConfig _config;
        private readonly string _configPath;
        private ComboBox _listenEngineBox;
        private TextBox _openWakeWordPythonPathBox;
        private TextBox _openWakeWordScriptPathBox;
        private TextBox _openWakeWordModelsBox;
        private NumericUpDown _openWakeWordThresholdBox;
        private TextBox _openWakeWordDeviceBox;
        private NumericUpDown _openWakeWordVadThresholdBox;
        private CheckBox _openWakeWordLogScoresBox;
        private TextBox _keywordsBox;
        private TextBox _cultureBox;
        private NumericUpDown _minimumConfidenceBox;
        private CheckBox _enableDictationFallbackBox;
        private NumericUpDown _cooldownMillisecondsBox;

        public bool SettingsSaved { get; private set; }

        public SettingsForm(AppConfig config, string configPath)
        {
            _config = config;
            _configPath = configPath;

            Text = "Voice Chat Launcher 設定";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new System.Drawing.Size(560, 560);

            BuildUi();
            LoadValues();
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(12);
            root.RowCount = 3;
            root.ColumnCount = 1;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var enginePanel = new TableLayoutPanel();
            enginePanel.Dock = DockStyle.Top;
            enginePanel.ColumnCount = 2;
            enginePanel.RowCount = 1;
            enginePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            enginePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            enginePanel.Controls.Add(CreateLabel("音声認識エンジン"), 0, 0);
            _listenEngineBox = new ComboBox();
            _listenEngineBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _listenEngineBox.Items.Add("openwakeword");
            _listenEngineBox.Items.Add("windows");
            _listenEngineBox.Dock = DockStyle.Fill;
            enginePanel.Controls.Add(_listenEngineBox, 1, 0);

            var tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.TabPages.Add(CreateOpenWakeWordTab());
            tabs.TabPages.Add(CreateWindowsSpeechTab());

            var buttons = new FlowLayoutPanel();
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.Padding = new Padding(0, 8, 0, 0);

            var cancelButton = new Button();
            cancelButton.Text = "キャンセル";
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.Width = 96;

            var saveButton = new Button();
            saveButton.Text = "保存";
            saveButton.Width = 96;
            saveButton.Click += delegate { SaveSettings(); };

            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(saveButton);

            root.Controls.Add(enginePanel, 0, 0);
            root.Controls.Add(tabs, 0, 1);
            root.Controls.Add(buttons, 0, 2);

            AcceptButton = saveButton;
            CancelButton = cancelButton;
            Controls.Add(root);
        }

        private TabPage CreateOpenWakeWordTab()
        {
            var tab = new TabPage("OpenWakeWord");
            var panel = CreateFormPanel();

            _openWakeWordModelsBox = AddTextBox(panel, "モデル", "例: hey_jarvis または ..\\models\\wakeword.onnx");
            _openWakeWordThresholdBox = AddDecimalBox(panel, "しきい値", 0, 1, 2, 0.01m);
            _openWakeWordDeviceBox = AddTextBox(panel, "マイクデバイス", "空なら既定のマイク");
            _openWakeWordVadThresholdBox = AddDecimalBox(panel, "VADしきい値", 0, 1, 2, 0.01m);
            _openWakeWordLogScoresBox = AddCheckBox(panel, "スコアをログ出力する");
            _openWakeWordPythonPathBox = AddTextBox(panel, "Pythonパス", "例: ..\\.venv\\Scripts\\python.exe");
            _openWakeWordScriptPathBox = AddTextBox(panel, "スクリプトパス", "例: ..\\scripts\\openwakeword_listener.py");

            tab.Controls.Add(panel);
            return tab;
        }

        private TabPage CreateWindowsSpeechTab()
        {
            var tab = new TabPage("Windows 音声認識");
            var panel = CreateFormPanel();

            _keywordsBox = AddMultilineTextBox(panel, "合図", "| 区切りで複数指定できます");
            _cultureBox = AddTextBox(panel, "認識言語", "例: ja-JP。空なら既定");
            _minimumConfidenceBox = AddDecimalBox(panel, "最低信頼度", 0, 1, 2, 0.01m);
            _enableDictationFallbackBox = AddCheckBox(panel, "自由認識から合図を探す");
            _cooldownMillisecondsBox = AddIntBox(panel, "クールダウン(ms)", 0, 60000, 100);

            tab.Controls.Add(panel);
            return tab;
        }

        private static TableLayoutPanel CreateFormPanel()
        {
            var panel = new TableLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.Padding = new Padding(12);
            panel.AutoScroll = true;
            panel.ColumnCount = 2;
            panel.RowCount = 0;
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return panel;
        }

        private static Label CreateLabel(string text)
        {
            var label = new Label();
            label.Text = text;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Dock = DockStyle.Fill;
            label.AutoSize = true;
            label.Margin = new Padding(0, 6, 8, 6);
            return label;
        }

        private static TextBox AddTextBox(TableLayoutPanel panel, string label, string placeholder)
        {
            var box = new TextBox();
            box.Dock = DockStyle.Fill;
            box.Margin = new Padding(0, 4, 0, 4);
            box.Tag = placeholder;
            AddRow(panel, label, box);
            return box;
        }

        private static TextBox AddMultilineTextBox(TableLayoutPanel panel, string label, string placeholder)
        {
            var box = new TextBox();
            box.Dock = DockStyle.Fill;
            box.Margin = new Padding(0, 4, 0, 4);
            box.Multiline = true;
            box.Height = 72;
            box.ScrollBars = ScrollBars.Vertical;
            box.Tag = placeholder;
            AddRow(panel, label, box);
            return box;
        }

        private static NumericUpDown AddDecimalBox(TableLayoutPanel panel, string label, decimal minimum, decimal maximum, int decimalPlaces, decimal increment)
        {
            var box = new NumericUpDown();
            box.Dock = DockStyle.Left;
            box.Minimum = minimum;
            box.Maximum = maximum;
            box.DecimalPlaces = decimalPlaces;
            box.Increment = increment;
            box.Width = 120;
            box.Margin = new Padding(0, 4, 0, 4);
            AddRow(panel, label, box);
            return box;
        }

        private static NumericUpDown AddIntBox(TableLayoutPanel panel, string label, int minimum, int maximum, int increment)
        {
            var box = new NumericUpDown();
            box.Dock = DockStyle.Left;
            box.Minimum = minimum;
            box.Maximum = maximum;
            box.Increment = increment;
            box.Width = 120;
            box.Margin = new Padding(0, 4, 0, 4);
            AddRow(panel, label, box);
            return box;
        }

        private static CheckBox AddCheckBox(TableLayoutPanel panel, string label)
        {
            var box = new CheckBox();
            box.Dock = DockStyle.Fill;
            box.Text = label;
            box.Margin = new Padding(0, 6, 0, 6);
            AddRow(panel, string.Empty, box);
            return box;
        }

        private static void AddRow(TableLayoutPanel panel, string label, Control control)
        {
            int row = panel.RowCount++;
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.Controls.Add(CreateLabel(label), 0, row);
            panel.Controls.Add(control, 1, row);
        }

        private void LoadValues()
        {
            _listenEngineBox.SelectedItem = _config.UseOpenWakeWord ? "openwakeword" : "windows";
            _openWakeWordPythonPathBox.Text = _config.OpenWakeWordPythonPath;
            _openWakeWordScriptPathBox.Text = _config.OpenWakeWordScriptPath;
            _openWakeWordModelsBox.Text = _config.OpenWakeWordModels;
            _openWakeWordThresholdBox.Value = ClampDecimal((decimal)_config.OpenWakeWordThreshold, _openWakeWordThresholdBox.Minimum, _openWakeWordThresholdBox.Maximum);
            _openWakeWordDeviceBox.Text = _config.OpenWakeWordDevice;
            _openWakeWordVadThresholdBox.Value = ClampDecimal((decimal)_config.OpenWakeWordVadThreshold, _openWakeWordVadThresholdBox.Minimum, _openWakeWordVadThresholdBox.Maximum);
            _openWakeWordLogScoresBox.Checked = _config.OpenWakeWordLogScores;

            _keywordsBox.Text = string.Join("|", _config.Keywords.ToArray());
            _cultureBox.Text = _config.Culture;
            _minimumConfidenceBox.Value = ClampDecimal((decimal)_config.MinimumConfidence, _minimumConfidenceBox.Minimum, _minimumConfidenceBox.Maximum);
            _enableDictationFallbackBox.Checked = _config.EnableDictationFallback;
            _cooldownMillisecondsBox.Value = ClampDecimal(_config.CooldownMilliseconds, _cooldownMillisecondsBox.Minimum, _cooldownMillisecondsBox.Maximum);
        }

        private void SaveSettings()
        {
            try
            {
                string listenEngine = Convert.ToString(_listenEngineBox.SelectedItem, CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(listenEngine))
                {
                    listenEngine = "openwakeword";
                }

                string models = _openWakeWordModelsBox.Text.Trim();
                string keywords = _keywordsBox.Text.Trim();

                if (string.Equals(listenEngine, "openwakeword", StringComparison.OrdinalIgnoreCase) && models.Length == 0)
                {
                    ShowValidationError("OpenWakeWord のモデルを入力してください。");
                    return;
                }

                if (string.Equals(listenEngine, "windows", StringComparison.OrdinalIgnoreCase) && SplitList(keywords).Count == 0)
                {
                    ShowValidationError("Windows 音声認識の合図を1つ以上入力してください。");
                    return;
                }

                _config.ListenEngine = listenEngine;
                _config.OpenWakeWordPythonPath = _openWakeWordPythonPathBox.Text.Trim();
                _config.OpenWakeWordScriptPath = _openWakeWordScriptPathBox.Text.Trim();
                _config.OpenWakeWordModels = models;
                _config.OpenWakeWordThreshold = (float)_openWakeWordThresholdBox.Value;
                _config.OpenWakeWordDevice = _openWakeWordDeviceBox.Text.Trim();
                _config.OpenWakeWordVadThreshold = (float)_openWakeWordVadThresholdBox.Value;
                _config.OpenWakeWordLogScores = _openWakeWordLogScoresBox.Checked;

                _config.Keywords = SplitList(keywords);
                _config.Culture = _cultureBox.Text.Trim();
                _config.MinimumConfidence = (float)_minimumConfidenceBox.Value;
                _config.EnableDictationFallback = _enableDictationFallbackBox.Checked;
                _config.CooldownMilliseconds = (int)_cooldownMillisecondsBox.Value;

                _config.Save(_configPath);
                SettingsSaved = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "設定を保存できません",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static decimal ClampDecimal(decimal value, decimal minimum, decimal maximum)
        {
            if (value < minimum)
            {
                return minimum;
            }

            if (value > maximum)
            {
                return maximum;
            }

            return value;
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

        private static void ShowValidationError(string message)
        {
            MessageBox.Show(
                message,
                "設定を確認してください",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    internal sealed class WindowCandidate
    {
        public IntPtr Handle;
        public int ProcessId;
        public string ProcessName;
        public string Title;
        public string ClassName;
        public bool IsVisible;
        public bool IsMinimized;
        public int Left;
        public int Top;
        public int Width;
        public int Height;

        public override string ToString()
        {
            return "hwnd=" + Handle +
                " pid=" + ProcessId +
                " process='" + ProcessName + "'" +
                " title='" + Title + "'" +
                " class='" + ClassName + "'" +
                " visible=" + IsVisible +
                " minimized=" + IsMinimized +
                " rect=" + Left + "," + Top + "," + Width + "x" + Height;
        }
    }

    internal sealed class AppConfig
    {
        public List<string> Keywords = new List<string>();
        public string ListenEngine = "openwakeword";
        public string Culture = string.Empty;
        public float MinimumConfidence = 0.72f;
        public bool EnableDictationFallback = true;
        public int CooldownMilliseconds = 4000;
        public string OpenWakeWordPythonPath = @"..\.venv\Scripts\python.exe";
        public string OpenWakeWordScriptPath = @"..\scripts\openwakeword_listener.py";
        public string OpenWakeWordModels = "hey_jarvis";
        public float OpenWakeWordThreshold = 0.5f;
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

        public bool UseOpenWakeWord
        {
            get
            {
                return string.Equals(ListenEngine, "openwakeword", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ListenEngine, "wakeword", StringComparison.OrdinalIgnoreCase);
            }
        }

        public static AppConfig Load(string path)
        {
            if (!File.Exists(path))
            {
                File.WriteAllText(path, DefaultConfigText(), Encoding.UTF8);
            }

            var config = new AppConfig();
            config.Keywords.Add("チャット ジーピーティー");
            config.Keywords.Add("チャットジーピーティー");
            config.Keywords.Add("チャットGPT");
            config.Keywords.Add("チャット gpt");
            config.Keywords.Add("chat gpt");
            config.Keywords.Add("chatgpt");
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
            AppendValue(builder, "ListenEngine", ListenEngine);
            AppendValue(builder, "OpenWakeWordPythonPath", OpenWakeWordPythonPath);
            AppendValue(builder, "OpenWakeWordScriptPath", OpenWakeWordScriptPath);
            AppendValue(builder, "OpenWakeWordModels", OpenWakeWordModels);
            AppendValue(builder, "OpenWakeWordThreshold", FormatFloat(OpenWakeWordThreshold));
            AppendValue(builder, "OpenWakeWordDevice", OpenWakeWordDevice);
            AppendValue(builder, "OpenWakeWordVadThreshold", FormatFloat(OpenWakeWordVadThreshold));
            AppendValue(builder, "OpenWakeWordLogScores", FormatBool(OpenWakeWordLogScores));
            builder.AppendLine();
            builder.AppendLine("# Legacy Windows Speech settings. Used only when ListenEngine=windows.");
            AppendValue(builder, "Keywords", JoinList(Keywords));
            AppendValue(builder, "Culture", Culture);
            AppendValue(builder, "MinimumConfidence", FormatFloat(MinimumConfidence));
            AppendValue(builder, "EnableDictationFallback", FormatBool(EnableDictationFallback));
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
            builder.AppendLine("# Used to find and press the voice conversation button, not the dictation button.");
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
            if (EqualsKey(key, "Keywords"))
            {
                config.Keywords = SplitList(value);
            }
            else if (EqualsKey(key, "ListenEngine"))
            {
                config.ListenEngine = value;
            }
            else if (EqualsKey(key, "Culture"))
            {
                config.Culture = value;
            }
            else if (EqualsKey(key, "MinimumConfidence"))
            {
                float result;
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                {
                    config.MinimumConfidence = result;
                }
            }
            else if (EqualsKey(key, "EnableDictationFallback"))
            {
                config.EnableDictationFallback = ParseBool(value, config.EnableDictationFallback);
            }
            else if (EqualsKey(key, "OpenWakeWordPythonPath"))
            {
                config.OpenWakeWordPythonPath = value;
            }
            else if (EqualsKey(key, "OpenWakeWordScriptPath"))
            {
                config.OpenWakeWordScriptPath = value;
            }
            else if (EqualsKey(key, "OpenWakeWordModels"))
            {
                config.OpenWakeWordModels = value;
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

# The words or phrases that trigger ChatGPT voice mode.
# ListenEngine=openwakeword uses OpenWakeWord and ignores Keywords.
# Say ""hey jarvis"" by default. Other built-in models include alexa, hey_mycroft, hey_rhasspy, timer, weather.
ListenEngine=openwakeword
OpenWakeWordPythonPath=..\.venv\Scripts\python.exe
OpenWakeWordScriptPath=..\scripts\openwakeword_listener.py
OpenWakeWordModels=hey_jarvis
OpenWakeWordThreshold=0.80
OpenWakeWordDevice=
OpenWakeWordVadThreshold=0.0
OpenWakeWordLogScores=false

# Legacy Windows Speech settings. Used only when ListenEngine=windows.
Keywords=チャット ジーピーティー|チャットジーピーティー|チャットGPT|チャット gpt|chat gpt|chatgpt
Culture=
MinimumConfidence=0.60
EnableDictationFallback=true
CooldownMilliseconds=4000

# LaunchCommand can be a URI such as chatgpt:, an .exe path, or shell:AppsFolder\<AppID>.
# If ChatGPT is already open, you can leave LaunchCommand empty and the app will only search for the window.
LaunchCommand=chatgpt:
LaunchArguments=

# Used to find the ChatGPT window after launch.
WindowTitleKeyword=ChatGPT
ProcessNames=ChatGPT|OpenAI
MinimumWindowWidth=320
MinimumWindowHeight=200
CenterWindowOnForeground=true
RunActionOnStartup=false
StartupActionDelayMilliseconds=1200
StartupDelayMilliseconds=2500
AfterBringToFrontDelayMilliseconds=1200
WindowTimeoutMilliseconds=15000

# Used to find and press the voice conversation button, not the dictation button.
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

    internal static class NativeMethods
    {
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        public const int SW_RESTORE = 9;
        public const int SW_SHOW = 5;
        public const int MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const int MOUSEEVENTF_LEFTUP = 0x0004;
        public const int MONITOR_DEFAULTTONEAREST = 2;
        public const int SWP_NOZORDER = 0x0004;
        public const int SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, int uFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll", EntryPoint = "mouse_event")]
        public static extern void MouseEvent(int dwFlags, int dx, int dy, int dwData, UIntPtr dwExtraInfo);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;
            public POINT ptMinPosition;
            public POINT ptMaxPosition;
            public RECT rcNormalPosition;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }
    }
}
