using System;

namespace LocalCorrect.Setup {
    internal static class VisualCppPrerequisite {
        internal const string DownloadUrl = "https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist";
        internal const string RegistryPath = @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64";
        // The pinned ORT 1.31.1 win-x64 DLL's PE linker version is 14.44.
        // This is a toolset-family floor, not an upstream claim that every
        // servicing build works. The latest supported Microsoft v14 is advised.
        internal const int MinimumMinor = 44;
        internal static readonly string[] SystemDlls = { "msvcp140.dll", "msvcp140_1.dll", "vcruntime140.dll", "vcruntime140_1.dll" };
        internal static bool IsSupported(object installed, object version, Func<string, bool> hasSystemDll) {
            if (!(installed is int installedValue) || installedValue != 1 || !(version is string text)) return false;
            text = text.Trim();
            if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase)) text = text.Substring(1);
            if (!Version.TryParse(text, out Version parsed) || parsed.Major != 14 || parsed.Minor < MinimumMinor) return false;
            foreach (string name in SystemDlls) if (!hasSystemDll(name)) return false;
            return true;
        }
        internal static string MissingMessage => "Install or repair the latest supported Microsoft Visual C++ v14 Redistributable (x64) from Microsoft, then run LocalCorrect setup again. The bundled ONNX Runtime requires the 14.44 toolset family or later. The x86 runtime alone is insufficient, even for 32-bit Outlook.\n\n" + DownloadUrl + "\n\nThe separate Microsoft installation may require administrator rights. LocalCorrect does not download or install it. No LocalCorrect installation files or registry keys were changed.";
    }
}
