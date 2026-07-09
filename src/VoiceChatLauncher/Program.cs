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
        private const int LogRetentionHours = 24;
        private static readonly TimeSpan LogRetentionSweepInterval = TimeSpan.FromMinutes(10);
        private readonly string _baseDirectory;
        private readonly string _configPath;
        private readonly string _logPath;
        private readonly object _logSync = new object();
        private NotifyIcon _notifyIcon;
        private MenuItem _toggleListeningMenuItem;
        private AboutForm _aboutForm;
        private StatusForm _statusForm;
        private SettingsForm _settingsForm;
        private AppConfig _config;
        private OpenWakeWordListener _wakeWordListener;
        private bool _isListeningPaused;
        private bool _isRunningAction;
        private DateTime _lastLogRetentionSweepUtc = DateTime.MinValue;
        private DateTime _lastTriggered = DateTime.MinValue;

        public LauncherContext()
        {
            _baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _configPath = Path.Combine(_baseDirectory, "config.ini");
            _logPath = Path.Combine(_baseDirectory, "voice-command.log");

            ApplyLogRetention(DateTime.Now, true);
            InitializeTray();
            LoadConfiguration();
            StartListening();
            TriggerStartupActionIfNeeded();
        }

        private void InitializeTray()
        {
            var menu = new ContextMenu();
            menu.MenuItems.Add("状態を表示", delegate { OpenStatusWindow(); });
            menu.MenuItems.Add("今すぐ起動", delegate { TriggerAction("tray"); });
            _toggleListeningMenuItem = menu.MenuItems.Add("音声認識を一時停止", delegate { ToggleListeningPaused(); });
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("設定", delegate { OpenConfig(); });
            menu.MenuItems.Add("設定を再読み込み", delegate { Reload(); });
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("About / ライセンス", delegate { OpenAbout(); });
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("終了", delegate { ExitThread(); });

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = SystemIcons.Application;
            _notifyIcon.Text = "Voice Chat Launcher";
            _notifyIcon.Visible = true;
            _notifyIcon.ContextMenu = menu;
            _notifyIcon.DoubleClick += delegate { TriggerAction("tray-double-click"); };
            UpdateListeningUi();
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
            if (!_isListeningPaused)
            {
                StartListening();
            }

            UpdateListeningUi();
            ShowBalloon(
                "設定を再読み込みしました",
                _isListeningPaused ? "音声認識は一時停止中です" : "合図: " + GetCueText());
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
            if (_isListeningPaused)
            {
                Log("Listening start skipped because recognition is paused");
                UpdateListeningUi();
                return;
            }

            StartOpenWakeWordListening();
            UpdateListeningUi();
        }

        private void StopListening()
        {
            StopOpenWakeWordListening();
            UpdateListeningUi();
        }

        private void ToggleListeningPaused()
        {
            if (_isListeningPaused)
            {
                ResumeListening();
                return;
            }

            PauseListening();
        }

        private void PauseListening()
        {
            if (_isListeningPaused)
            {
                return;
            }

            _isListeningPaused = true;
            StopListening();
            Log("Listening paused by user");
            UpdateListeningUi();
            ShowBalloon("音声認識を一時停止しました", "一時停止中は音声コマンドを処理しません");
        }

        private void ResumeListening()
        {
            if (!_isListeningPaused)
            {
                return;
            }

            _isListeningPaused = false;
            Log("Listening resumed by user");
            StartListening();
            UpdateListeningUi();
            ShowBalloon("音声認識を再開しました", "合図: " + GetCueText());
        }

        private void StartOpenWakeWordListening()
        {
            try
            {
                var options = new OpenWakeWordOptions();
                options.Models = _config.OpenWakeWordModels;
                options.Threshold = _config.OpenWakeWordThreshold;
                options.CooldownMilliseconds = _config.CooldownMilliseconds;
                options.Device = _config.OpenWakeWordDevice;
                options.LogScores = _config.OpenWakeWordLogScores;
                options.MelspectrogramModelPath = ResolvePath(_config.OpenWakeWordMelspectrogramModelPath);
                options.EmbeddingModelPath = ResolvePath(_config.OpenWakeWordEmbeddingModelPath);
                options.BaseDirectory = _baseDirectory;

                _wakeWordListener = new OpenWakeWordListener(options);
                _wakeWordListener.LogMessage += OnOpenWakeWordLogMessage;
                _wakeWordListener.WakeWordDetected += OnOpenWakeWordDetected;
                _wakeWordListener.Start();

                if (_config.OpenWakeWordVadThreshold > 0)
                {
                    Log("OpenWakeWord VAD threshold is configured but is not supported by the C# runtime yet.");
                }

                Log("OpenWakeWord C# runtime started: models=" + _config.OpenWakeWordModels);
                ShowBalloon("待ち受けを開始しました", "WakeWord: " + _config.OpenWakeWordModels.Replace("_", " "));
            }
            catch (Exception ex)
            {
                Log("Failed to start OpenWakeWord C# runtime: " + ex);
                ShowBalloon("OpenWakeWord を開始できません", ShortMessage(ex));
            }
        }

        private void StopOpenWakeWordListening()
        {
            if (_wakeWordListener == null)
            {
                return;
            }

            try
            {
                _wakeWordListener.WakeWordDetected -= OnOpenWakeWordDetected;
                _wakeWordListener.LogMessage -= OnOpenWakeWordLogMessage;
                _wakeWordListener.Dispose();
            }
            catch (Exception ex)
            {
                Log("Failed to stop OpenWakeWord C# runtime: " + ex);
            }
            finally
            {
                _wakeWordListener = null;
            }
        }

        private void OnOpenWakeWordLogMessage(object sender, OpenWakeWordLogEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.Message))
            {
                Log("OpenWakeWord: " + e.Message);
            }
        }

        private void OnOpenWakeWordDetected(object sender, WakeWordDetectedEventArgs e)
        {
            string line = "WAKE " + e.Name + " " + e.Score.ToString("0.000", CultureInfo.InvariantCulture);
            Log("OpenWakeWord: " + line);
            TriggerAction("wakeword:" + line);
        }

        private void TriggerAction(string source)
        {
            if (_isListeningPaused &&
                (source.StartsWith("voice:", StringComparison.OrdinalIgnoreCase) ||
                source.StartsWith("wakeword:", StringComparison.OrdinalIgnoreCase)))
            {
                Log("Voice trigger ignored because recognition is paused: " + source);
                return;
            }

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

        private void OpenStatusWindow()
        {
            if (_statusForm != null && !_statusForm.IsDisposed)
            {
                UpdateStatusWindow();
                _statusForm.Activate();
                return;
            }

            _statusForm = new StatusForm(_logPath);
            _statusForm.FormClosed += delegate { _statusForm = null; };
            UpdateStatusWindow();
            _statusForm.Show();
            _statusForm.Activate();
        }

        private void UpdateStatusWindow()
        {
            if (_statusForm == null || _statusForm.IsDisposed)
            {
                return;
            }

            string launchCommand = _config == null ? string.Empty : _config.LaunchCommand;
            _statusForm.UpdateStatus(
                GetListeningStateText(),
                GetRecognizerText(),
                GetCueText(),
                launchCommand,
                _logPath);
        }

        private string GetRecognizerText()
        {
            if (_isListeningPaused)
            {
                return "一時停止中";
            }

            return _wakeWordListener == null || !_wakeWordListener.IsRunning
                ? "OpenWakeWord 停止中"
                : "OpenWakeWord 待ち受け中 (" + _config.OpenWakeWordModels + ")";
        }

        private string GetCueText()
        {
            if (_config == null)
            {
                return string.Empty;
            }

            return _config.OpenWakeWordModels.Replace("_", " ");
        }

        private string GetListeningStateText()
        {
            if (_isListeningPaused)
            {
                return "一時停止中";
            }

            return IsListeningActive() ? "待ち受け中" : "停止中";
        }

        private bool IsListeningActive()
        {
            return _wakeWordListener != null && _wakeWordListener.IsRunning;
        }

        private void UpdateListeningUi()
        {
            if (_toggleListeningMenuItem != null)
            {
                _toggleListeningMenuItem.Text = _isListeningPaused ? "音声認識を再開" : "音声認識を一時停止";
                _toggleListeningMenuItem.Checked = _isListeningPaused;
            }

            if (_notifyIcon != null)
            {
                _notifyIcon.Text = _isListeningPaused
                    ? "Voice Chat Launcher (一時停止中)"
                    : "Voice Chat Launcher";
            }

            UpdateStatusWindow();
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

        private void OpenAbout()
        {
            if (_aboutForm != null && !_aboutForm.IsDisposed)
            {
                _aboutForm.Activate();
                return;
            }

            _aboutForm = new AboutForm();
            _aboutForm.FormClosed += delegate { _aboutForm = null; };
            _aboutForm.Show();
            _aboutForm.Activate();
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
                DateTime now = DateTime.Now;
                string line = now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine;
                lock (_logSync)
                {
                    File.AppendAllText(_logPath, line, Encoding.UTF8);
                    ApplyLogRetention(now, false);
                }
            }
            catch
            {
            }
        }

        private void ApplyLogRetention(DateTime now, bool force)
        {
            try
            {
                DateTime utcNow = DateTime.UtcNow;
                if (!force && utcNow - _lastLogRetentionSweepUtc < LogRetentionSweepInterval)
                {
                    return;
                }

                _lastLogRetentionSweepUtc = utcNow;
                if (!File.Exists(_logPath))
                {
                    return;
                }

                string[] lines = File.ReadAllLines(_logPath, Encoding.UTF8);
                if (lines.Length == 0)
                {
                    return;
                }

                DateTime cutoff = now.AddHours(-LogRetentionHours);
                var retained = new List<string>(lines.Length);
                bool removed = false;

                foreach (string line in lines)
                {
                    if (ShouldKeepLogLine(line, cutoff))
                    {
                        retained.Add(line);
                    }
                    else
                    {
                        removed = true;
                    }
                }

                if (!removed)
                {
                    return;
                }

                string content = retained.Count == 0
                    ? string.Empty
                    : string.Join(Environment.NewLine, retained.ToArray()) + Environment.NewLine;
                File.WriteAllText(_logPath, content, Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static bool ShouldKeepLogLine(string line, DateTime cutoff)
        {
            DateTime timestamp;
            if (!TryParseLogTimestamp(line, out timestamp))
            {
                return true;
            }

            return timestamp >= cutoff;
        }

        private static bool TryParseLogTimestamp(string line, out DateTime timestamp)
        {
            timestamp = DateTime.MinValue;
            if (string.IsNullOrEmpty(line) || line.Length < 23)
            {
                return false;
            }

            string value = line.Substring(0, 23);
            return DateTime.TryParseExact(
                value,
                "yyyy-MM-dd HH:mm:ss.fff",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out timestamp);
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
}
