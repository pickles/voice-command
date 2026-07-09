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
    internal sealed class ThirdPartyLicense
    {
        public string Name;
        public string Version;
        public string License;
        public string LicenseFile;
        public string Author;
        public string ProjectUrl;
        public string Notices;
    }

    internal static class ThirdPartyLicenses
    {
        public static readonly ThirdPartyLicense[] All =
        {
            new ThirdPartyLicense
            {
                Name = "Microsoft.ML.OnnxRuntime",
                Version = "1.18.1",
                License = "MIT",
                LicenseFile = "packages/microsoft.ml.onnxruntime/1.18.1/LICENSE",
                Author = "Microsoft",
                ProjectUrl = "https://github.com/Microsoft/onnxruntime",
                Notices = "packages/microsoft.ml.onnxruntime/1.18.1/ThirdPartyNotices.txt"
            },
            new ThirdPartyLicense
            {
                Name = "Microsoft.ML.OnnxRuntime.Managed",
                Version = "1.18.1",
                License = "MIT",
                LicenseFile = "packages/microsoft.ml.onnxruntime.managed/1.18.1/LICENSE.txt",
                Author = "Microsoft",
                ProjectUrl = "https://github.com/Microsoft/onnxruntime",
                Notices = "packages/microsoft.ml.onnxruntime.managed/1.18.1/ThirdPartyNotices.txt"
            },
            new ThirdPartyLicense
            {
                Name = "System.Buffers",
                Version = "4.5.1",
                License = "MIT",
                LicenseFile = "packages/system.buffers/4.5.1/LICENSE.TXT",
                Author = "Microsoft",
                ProjectUrl = "https://dot.net/",
                Notices = "packages/system.buffers/4.5.1/THIRD-PARTY-NOTICES.TXT"
            },
            new ThirdPartyLicense
            {
                Name = "System.Memory",
                Version = "4.5.5",
                License = "MIT",
                LicenseFile = "packages/system.memory/4.5.5/LICENSE.TXT",
                Author = "Microsoft",
                ProjectUrl = "https://dot.net/",
                Notices = "packages/system.memory/4.5.5/THIRD-PARTY-NOTICES.TXT"
            },
            new ThirdPartyLicense
            {
                Name = "System.Numerics.Vectors",
                Version = "4.5.0",
                License = "MIT",
                LicenseFile = "packages/system.numerics.vectors/4.5.0/LICENSE.TXT",
                Author = "Microsoft",
                ProjectUrl = "https://dot.net/",
                Notices = "packages/system.numerics.vectors/4.5.0/THIRD-PARTY-NOTICES.TXT"
            },
            new ThirdPartyLicense
            {
                Name = "System.Runtime.CompilerServices.Unsafe",
                Version = "4.5.3",
                License = "MIT",
                LicenseFile = "packages/system.runtime.compilerservices.unsafe/4.5.3/LICENSE.TXT",
                Author = "Microsoft",
                ProjectUrl = "https://dot.net/",
                Notices = "packages/system.runtime.compilerservices.unsafe/4.5.3/THIRD-PARTY-NOTICES.TXT"
            }
        };
    }
}
