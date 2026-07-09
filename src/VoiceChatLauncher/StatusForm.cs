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
    internal sealed class StatusForm : Form
    {
        private const int MaxLogCharacters = 200000;
        private readonly string _logPath;
        private readonly System.Windows.Forms.Timer _refreshTimer;
        private Label _stateValueLabel;
        private Label _recognizerValueLabel;
        private Label _cueValueLabel;
        private Label _launchValueLabel;
        private Label _logPathValueLabel;
        private TextBox _logBox;

        public StatusForm(string logPath)
        {
            _logPath = logPath;

            Text = "Voice Chat Launcher 状態";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new System.Drawing.Size(640, 480);
            ClientSize = new System.Drawing.Size(760, 560);

            BuildUi();

            _refreshTimer = new System.Windows.Forms.Timer();
            _refreshTimer.Interval = 5000;
            _refreshTimer.Tick += delegate { RefreshLog(); };
        }

        public void UpdateStatus(string state, string recognizer, string cue, string launchCommand, string logPath)
        {
            _stateValueLabel.Text = state;
            _recognizerValueLabel.Text = recognizer;
            _cueValueLabel.Text = cue;
            _launchValueLabel.Text = launchCommand;
            _logPathValueLabel.Text = logPath;
            RefreshLog();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RefreshLog();
            _refreshTimer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            base.OnFormClosed(e);
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(12);
            root.RowCount = 3;
            root.ColumnCount = 1;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var statusPanel = new TableLayoutPanel();
            statusPanel.Dock = DockStyle.Top;
            statusPanel.AutoSize = true;
            statusPanel.ColumnCount = 2;
            statusPanel.RowCount = 0;
            statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            AddStatusRow(statusPanel, "状態", out _stateValueLabel);
            AddStatusRow(statusPanel, "音声認識", out _recognizerValueLabel);
            AddStatusRow(statusPanel, "合図", out _cueValueLabel);
            AddStatusRow(statusPanel, "起動", out _launchValueLabel);
            AddStatusRow(statusPanel, "ログ", out _logPathValueLabel);

            var toolbar = new FlowLayoutPanel();
            toolbar.Dock = DockStyle.Fill;
            toolbar.AutoSize = true;
            toolbar.FlowDirection = FlowDirection.RightToLeft;
            toolbar.Padding = new Padding(0, 8, 0, 8);

            var refreshButton = new Button();
            refreshButton.Text = "更新";
            refreshButton.Width = 96;
            refreshButton.Click += delegate { RefreshLog(); };
            toolbar.Controls.Add(refreshButton);

            _logBox = new TextBox();
            _logBox.Dock = DockStyle.Fill;
            _logBox.Multiline = true;
            _logBox.ReadOnly = true;
            _logBox.ScrollBars = ScrollBars.Both;
            _logBox.WordWrap = false;
            _logBox.Font = new Font(FontFamily.GenericMonospace, 9.0f);

            root.Controls.Add(statusPanel, 0, 0);
            root.Controls.Add(toolbar, 0, 1);
            root.Controls.Add(_logBox, 0, 2);
            Controls.Add(root);
        }

        private static void AddStatusRow(TableLayoutPanel panel, string label, out Label valueLabel)
        {
            int row = panel.RowCount++;
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var captionLabel = new Label();
            captionLabel.Text = label;
            captionLabel.TextAlign = ContentAlignment.MiddleLeft;
            captionLabel.Dock = DockStyle.Fill;
            captionLabel.AutoSize = true;
            captionLabel.Margin = new Padding(0, 4, 8, 4);

            valueLabel = new Label();
            valueLabel.TextAlign = ContentAlignment.MiddleLeft;
            valueLabel.Dock = DockStyle.Fill;
            valueLabel.AutoSize = true;
            valueLabel.Margin = new Padding(0, 4, 0, 4);

            panel.Controls.Add(captionLabel, 0, row);
            panel.Controls.Add(valueLabel, 1, row);
        }

        private void RefreshLog()
        {
            string text = ReadLogText();
            if (_logBox.Text == text)
            {
                return;
            }

            _logBox.Text = text;
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        }

        private string ReadLogText()
        {
            try
            {
                if (!File.Exists(_logPath))
                {
                    return "ログはまだありません。";
                }

                string text;
                using (var stream = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    text = reader.ReadToEnd();
                }

                if (text.Length <= MaxLogCharacters)
                {
                    return text;
                }

                int start = text.Length - MaxLogCharacters;
                int nextLine = text.IndexOf('\n', start);
                if (nextLine >= 0 && nextLine + 1 < text.Length)
                {
                    start = nextLine + 1;
                }

                return "ログが長いため末尾だけを表示しています。" + Environment.NewLine + text.Substring(start);
            }
            catch (Exception ex)
            {
                return "ログを読み込めません: " + ex.Message;
            }
        }
    }
}
