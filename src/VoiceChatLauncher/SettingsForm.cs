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
    internal sealed class SettingsForm : Form
    {
        private readonly AppConfig _config;
        private readonly string _configPath;
        private TextBox _openWakeWordModelsBox;
        private TextBox _openWakeWordMelspectrogramModelPathBox;
        private TextBox _openWakeWordEmbeddingModelPathBox;
        private NumericUpDown _openWakeWordThresholdBox;
        private TextBox _openWakeWordDeviceBox;
        private NumericUpDown _openWakeWordVadThresholdBox;
        private CheckBox _openWakeWordLogScoresBox;
        private NumericUpDown _cooldownMillisecondsBox;
        private TextBox _launchCommandBox;
        private TextBox _launchArgumentsBox;
        private CheckBox _runActionOnStartupBox;
        private NumericUpDown _startupActionDelayMillisecondsBox;
        private NumericUpDown _startupDelayMillisecondsBox;
        private NumericUpDown _afterBringToFrontDelayMillisecondsBox;
        private TextBox _windowTitleKeywordBox;
        private TextBox _processNamesBox;
        private NumericUpDown _minimumWindowWidthBox;
        private NumericUpDown _minimumWindowHeightBox;
        private CheckBox _centerWindowOnForegroundBox;
        private NumericUpDown _windowTimeoutMillisecondsBox;
        private TextBox _voiceButtonNamesBox;
        private TextBox _excludedButtonNamesBox;
        private CheckBox _rightmostVoiceButtonFallbackBox;
        private NumericUpDown _buttonTimeoutMillisecondsBox;
        private CheckBox _coordinateFallbackEnabledBox;
        private NumericUpDown _coordinateFallbackMaxComposerWidthBox;
        private NumericUpDown _coordinateFallbackHorizontalMarginBox;
        private NumericUpDown _coordinateFallbackRightOffsetBox;
        private NumericUpDown _coordinateFallbackBottomOffsetBox;

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
            ClientSize = new System.Drawing.Size(640, 600);

            BuildUi();
            LoadValues();
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(12);
            root.RowCount = 2;
            root.ColumnCount = 1;
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.TabPages.Add(CreateOpenWakeWordTab());
            tabs.TabPages.Add(CreateLaunchTab());
            tabs.TabPages.Add(CreateWindowTab());
            tabs.TabPages.Add(CreateVoiceButtonTab());
            tabs.TabPages.Add(CreateCoordinateFallbackTab());

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

            root.Controls.Add(tabs, 0, 0);
            root.Controls.Add(buttons, 0, 1);

            AcceptButton = saveButton;
            CancelButton = cancelButton;
            Controls.Add(root);
        }

        private TabPage CreateOpenWakeWordTab()
        {
            var tab = new TabPage("OpenWakeWord");
            var panel = CreateFormPanel();

            _openWakeWordModelsBox = AddTextBox(panel, "モデル", "例: ..\\models\\Hey_Lucy_20260609_095011.onnx");
            _openWakeWordMelspectrogramModelPathBox = AddTextBox(panel, "Melモデル", "例: ..\\models\\melspectrogram.onnx");
            _openWakeWordEmbeddingModelPathBox = AddTextBox(panel, "Embeddingモデル", "例: ..\\models\\embedding_model.onnx");
            _openWakeWordThresholdBox = AddDecimalBox(panel, "しきい値", 0, 1, 2, 0.01m);
            _openWakeWordDeviceBox = AddTextBox(panel, "マイクデバイス", "空なら既定のマイク");
            _openWakeWordVadThresholdBox = AddDecimalBox(panel, "VADしきい値", 0, 1, 2, 0.01m);
            _openWakeWordLogScoresBox = AddCheckBox(panel, "スコアをログ出力する");
            _cooldownMillisecondsBox = AddIntBox(panel, "クールダウン(ms)", 0, 60000, 100);

            tab.Controls.Add(panel);
            return tab;
        }

        private TabPage CreateLaunchTab()
        {
            var tab = new TabPage("起動");
            var panel = CreateFormPanel();

            _launchCommandBox = AddTextBox(panel, "起動コマンド", "例: chatgpt:");
            _launchArgumentsBox = AddTextBox(panel, "起動引数", "必要な場合のみ指定");
            _runActionOnStartupBox = AddCheckBox(panel, "起動時に音声ボタンを押す");
            _startupActionDelayMillisecondsBox = AddIntBox(panel, "起動時実行の待機(ms)", 0, 600000, 100);
            _startupDelayMillisecondsBox = AddIntBox(panel, "ChatGPT起動後待機(ms)", 0, 600000, 100);
            _afterBringToFrontDelayMillisecondsBox = AddIntBox(panel, "前面化後待機(ms)", 0, 600000, 100);

            tab.Controls.Add(panel);
            return tab;
        }

        private TabPage CreateWindowTab()
        {
            var tab = new TabPage("ウィンドウ");
            var panel = CreateFormPanel();

            _windowTitleKeywordBox = AddTextBox(panel, "タイトルキーワード", "例: ChatGPT");
            _processNamesBox = AddMultilineTextBox(panel, "プロセス名", "例: chatgpt.exe");
            _minimumWindowWidthBox = AddIntBox(panel, "最小幅", 0, 10000, 10);
            _minimumWindowHeightBox = AddIntBox(panel, "最小高さ", 0, 10000, 10);
            _centerWindowOnForegroundBox = AddCheckBox(panel, "前面化時に画面中央へ移動する");
            _windowTimeoutMillisecondsBox = AddIntBox(panel, "検出タイムアウト(ms)", 0, 600000, 100);

            tab.Controls.Add(panel);
            return tab;
        }

        private TabPage CreateVoiceButtonTab()
        {
            var tab = new TabPage("音声ボタン");
            var panel = CreateFormPanel();

            _voiceButtonNamesBox = AddMultilineTextBox(panel, "ボタン名", "1行に1つ、または | 区切り");
            _excludedButtonNamesBox = AddMultilineTextBox(panel, "除外ボタン名", "1行に1つ、または | 区切り");
            _rightmostVoiceButtonFallbackBox = AddCheckBox(panel, "右下の見えるボタンを候補にする");
            _buttonTimeoutMillisecondsBox = AddIntBox(panel, "ボタン待機(ms)", 0, 600000, 100);

            tab.Controls.Add(panel);
            return tab;
        }

        private TabPage CreateCoordinateFallbackTab()
        {
            var tab = new TabPage("位置クリック");
            var panel = CreateFormPanel();

            _coordinateFallbackEnabledBox = AddCheckBox(panel, "位置クリックを有効にする");
            _coordinateFallbackMaxComposerWidthBox = AddIntBox(panel, "入力欄の最大幅", 0, 10000, 10);
            _coordinateFallbackHorizontalMarginBox = AddIntBox(panel, "左右余白", 0, 5000, 1);
            _coordinateFallbackRightOffsetBox = AddIntBox(panel, "右オフセット", -5000, 5000, 1);
            _coordinateFallbackBottomOffsetBox = AddIntBox(panel, "下オフセット", -5000, 5000, 1);

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
            _openWakeWordModelsBox.Text = _config.OpenWakeWordModels;
            _openWakeWordMelspectrogramModelPathBox.Text = _config.OpenWakeWordMelspectrogramModelPath;
            _openWakeWordEmbeddingModelPathBox.Text = _config.OpenWakeWordEmbeddingModelPath;
            _openWakeWordThresholdBox.Value = ClampDecimal((decimal)_config.OpenWakeWordThreshold, _openWakeWordThresholdBox.Minimum, _openWakeWordThresholdBox.Maximum);
            _openWakeWordDeviceBox.Text = _config.OpenWakeWordDevice;
            _openWakeWordVadThresholdBox.Value = ClampDecimal((decimal)_config.OpenWakeWordVadThreshold, _openWakeWordVadThresholdBox.Minimum, _openWakeWordVadThresholdBox.Maximum);
            _openWakeWordLogScoresBox.Checked = _config.OpenWakeWordLogScores;
            _cooldownMillisecondsBox.Value = ClampDecimal(_config.CooldownMilliseconds, _cooldownMillisecondsBox.Minimum, _cooldownMillisecondsBox.Maximum);
            _launchCommandBox.Text = _config.LaunchCommand;
            _launchArgumentsBox.Text = _config.LaunchArguments;
            _runActionOnStartupBox.Checked = _config.RunActionOnStartup;
            _startupActionDelayMillisecondsBox.Value = ClampDecimal(_config.StartupActionDelayMilliseconds, _startupActionDelayMillisecondsBox.Minimum, _startupActionDelayMillisecondsBox.Maximum);
            _startupDelayMillisecondsBox.Value = ClampDecimal(_config.StartupDelayMilliseconds, _startupDelayMillisecondsBox.Minimum, _startupDelayMillisecondsBox.Maximum);
            _afterBringToFrontDelayMillisecondsBox.Value = ClampDecimal(_config.AfterBringToFrontDelayMilliseconds, _afterBringToFrontDelayMillisecondsBox.Minimum, _afterBringToFrontDelayMillisecondsBox.Maximum);
            _windowTitleKeywordBox.Text = _config.WindowTitleKeyword;
            _processNamesBox.Text = FormatListForEditor(_config.ProcessNames);
            _minimumWindowWidthBox.Value = ClampDecimal(_config.MinimumWindowWidth, _minimumWindowWidthBox.Minimum, _minimumWindowWidthBox.Maximum);
            _minimumWindowHeightBox.Value = ClampDecimal(_config.MinimumWindowHeight, _minimumWindowHeightBox.Minimum, _minimumWindowHeightBox.Maximum);
            _centerWindowOnForegroundBox.Checked = _config.CenterWindowOnForeground;
            _windowTimeoutMillisecondsBox.Value = ClampDecimal(_config.WindowTimeoutMilliseconds, _windowTimeoutMillisecondsBox.Minimum, _windowTimeoutMillisecondsBox.Maximum);
            _voiceButtonNamesBox.Text = FormatListForEditor(_config.VoiceButtonNames);
            _excludedButtonNamesBox.Text = FormatListForEditor(_config.ExcludedButtonNames);
            _rightmostVoiceButtonFallbackBox.Checked = _config.RightmostVoiceButtonFallback;
            _buttonTimeoutMillisecondsBox.Value = ClampDecimal(_config.ButtonTimeoutMilliseconds, _buttonTimeoutMillisecondsBox.Minimum, _buttonTimeoutMillisecondsBox.Maximum);
            _coordinateFallbackEnabledBox.Checked = _config.CoordinateFallbackEnabled;
            _coordinateFallbackMaxComposerWidthBox.Value = ClampDecimal(_config.CoordinateFallbackMaxComposerWidth, _coordinateFallbackMaxComposerWidthBox.Minimum, _coordinateFallbackMaxComposerWidthBox.Maximum);
            _coordinateFallbackHorizontalMarginBox.Value = ClampDecimal(_config.CoordinateFallbackHorizontalMargin, _coordinateFallbackHorizontalMarginBox.Minimum, _coordinateFallbackHorizontalMarginBox.Maximum);
            _coordinateFallbackRightOffsetBox.Value = ClampDecimal(_config.CoordinateFallbackRightOffset, _coordinateFallbackRightOffsetBox.Minimum, _coordinateFallbackRightOffsetBox.Maximum);
            _coordinateFallbackBottomOffsetBox.Value = ClampDecimal(_config.CoordinateFallbackBottomOffset, _coordinateFallbackBottomOffsetBox.Minimum, _coordinateFallbackBottomOffsetBox.Maximum);
        }

        private void SaveSettings()
        {
            try
            {
                string models = _openWakeWordModelsBox.Text.Trim();
                string melspectrogramModel = _openWakeWordMelspectrogramModelPathBox.Text.Trim();
                string embeddingModel = _openWakeWordEmbeddingModelPathBox.Text.Trim();
                List<string> voiceButtonNames = SplitList(_voiceButtonNamesBox.Text);
                if (models.Length == 0)
                {
                    ShowValidationError("OpenWakeWord のモデルを入力してください。");
                    return;
                }

                if (melspectrogramModel.Length == 0)
                {
                    ShowValidationError("OpenWakeWord の Mel モデルを入力してください。");
                    return;
                }

                if (embeddingModel.Length == 0)
                {
                    ShowValidationError("OpenWakeWord の Embedding モデルを入力してください。");
                    return;
                }

                if (voiceButtonNames.Count == 0 && !_rightmostVoiceButtonFallbackBox.Checked && !_coordinateFallbackEnabledBox.Checked)
                {
                    ShowValidationError("音声ボタン名を入力するか、フォールバックを有効にしてください。");
                    return;
                }

                _config.OpenWakeWordModels = models;
                _config.OpenWakeWordMelspectrogramModelPath = melspectrogramModel;
                _config.OpenWakeWordEmbeddingModelPath = embeddingModel;
                _config.OpenWakeWordThreshold = (float)_openWakeWordThresholdBox.Value;
                _config.OpenWakeWordDevice = _openWakeWordDeviceBox.Text.Trim();
                _config.OpenWakeWordVadThreshold = (float)_openWakeWordVadThresholdBox.Value;
                _config.OpenWakeWordLogScores = _openWakeWordLogScoresBox.Checked;
                _config.CooldownMilliseconds = (int)_cooldownMillisecondsBox.Value;
                _config.LaunchCommand = _launchCommandBox.Text.Trim();
                _config.LaunchArguments = _launchArgumentsBox.Text.Trim();
                _config.RunActionOnStartup = _runActionOnStartupBox.Checked;
                _config.StartupActionDelayMilliseconds = (int)_startupActionDelayMillisecondsBox.Value;
                _config.StartupDelayMilliseconds = (int)_startupDelayMillisecondsBox.Value;
                _config.AfterBringToFrontDelayMilliseconds = (int)_afterBringToFrontDelayMillisecondsBox.Value;
                _config.WindowTitleKeyword = _windowTitleKeywordBox.Text.Trim();
                _config.ProcessNames = SplitList(_processNamesBox.Text);
                _config.MinimumWindowWidth = (int)_minimumWindowWidthBox.Value;
                _config.MinimumWindowHeight = (int)_minimumWindowHeightBox.Value;
                _config.CenterWindowOnForeground = _centerWindowOnForegroundBox.Checked;
                _config.WindowTimeoutMilliseconds = (int)_windowTimeoutMillisecondsBox.Value;
                _config.VoiceButtonNames = voiceButtonNames;
                _config.ExcludedButtonNames = SplitList(_excludedButtonNamesBox.Text);
                _config.RightmostVoiceButtonFallback = _rightmostVoiceButtonFallbackBox.Checked;
                _config.ButtonTimeoutMilliseconds = (int)_buttonTimeoutMillisecondsBox.Value;
                _config.CoordinateFallbackEnabled = _coordinateFallbackEnabledBox.Checked;
                _config.CoordinateFallbackMaxComposerWidth = (int)_coordinateFallbackMaxComposerWidthBox.Value;
                _config.CoordinateFallbackHorizontalMargin = (int)_coordinateFallbackHorizontalMarginBox.Value;
                _config.CoordinateFallbackRightOffset = (int)_coordinateFallbackRightOffsetBox.Value;
                _config.CoordinateFallbackBottomOffset = (int)_coordinateFallbackBottomOffsetBox.Value;

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
            string[] parts = value.Split(new[] { '|', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
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

        private static string FormatListForEditor(List<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(Environment.NewLine, values.ToArray());
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
}
