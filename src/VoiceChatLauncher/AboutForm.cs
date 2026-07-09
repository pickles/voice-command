using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
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
    internal sealed class AboutForm : Form
    {
        private TextBox _licenseBox;

        public AboutForm()
        {
            Text = "Voice Chat Launcher About";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new System.Drawing.Size(560, 420);
            ClientSize = new System.Drawing.Size(720, 520);

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

            var header = new Label();
            header.Dock = DockStyle.Fill;
            header.AutoSize = true;
            header.Font = new Font(Font.FontFamily, 11.0f, FontStyle.Bold);
            header.Margin = new Padding(0, 0, 0, 8);
            header.Text = "Voice Chat Launcher " + GetVersionText();

            _licenseBox = new TextBox();
            _licenseBox.Dock = DockStyle.Fill;
            _licenseBox.Multiline = true;
            _licenseBox.ReadOnly = true;
            _licenseBox.ScrollBars = ScrollBars.Both;
            _licenseBox.WordWrap = false;
            _licenseBox.Font = new Font(FontFamily.GenericMonospace, 9.0f);

            var buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.Padding = new Padding(0, 8, 0, 0);

            var closeButton = new Button();
            closeButton.Text = "閉じる";
            closeButton.Width = 96;
            closeButton.Click += delegate { Close(); };
            buttons.Controls.Add(closeButton);

            root.Controls.Add(header, 0, 0);
            root.Controls.Add(_licenseBox, 0, 1);
            root.Controls.Add(buttons, 0, 2);
            Controls.Add(root);
        }

        private void LoadValues()
        {
            var builder = new StringBuilder();
            builder.AppendLine("Version");
            builder.AppendLine("  " + GetVersionText());
            builder.AppendLine();
            builder.AppendLine("Open Source Licenses");
            builder.AppendLine("  This application uses the following NuGet packages.");
            builder.AppendLine("  Update this list when package references or distributed DLLs change.");
            builder.AppendLine();

            foreach (ThirdPartyLicense license in ThirdPartyLicenses.All)
            {
                builder.AppendLine(license.Name + " " + license.Version);
                builder.AppendLine("  License: " + license.License);
                builder.AppendLine("  File:    " + license.LicenseFile);
                builder.AppendLine("  Author:  " + license.Author);
                builder.AppendLine("  Project: " + license.ProjectUrl);
                if (!string.IsNullOrEmpty(license.Notices))
                {
                    builder.AppendLine("  Notices: " + license.Notices);
                }

                builder.AppendLine();
            }

            _licenseBox.Text = builder.ToString();
            _licenseBox.SelectionStart = 0;
        }

        private static string GetVersionText()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            object[] attributes = assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false);
            if (attributes.Length > 0)
            {
                var informational = attributes[0] as AssemblyInformationalVersionAttribute;
                if (informational != null && !string.IsNullOrEmpty(informational.InformationalVersion))
                {
                    return informational.InformationalVersion;
                }
            }

            Version version = assembly.GetName().Version;
            return version == null ? "unknown" : version.ToString();
        }
    }
}
