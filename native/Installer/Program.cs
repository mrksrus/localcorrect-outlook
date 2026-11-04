using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;
using Microsoft.Win32;
namespace LocalCorrect.Setup {
    static class Program {
        const string Clsid = "{708F01D3-893E-43D0-8215-61722BC46F23}", ProgId = "LocalCorrect.Outlook.AddIn";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalCorrect.Outlook";
        const string Owner = "LocalCorrect.Outlook/708F01D3-893E-43D0-8215-61722BC46F23";
        static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalCorrect", "Outlook");
        static readonly string SetupPath = Path.Combine(Root, "LocalCorrect-Outlook-Setup.exe");
        [STAThread] static void Main(string[] args) {
            Application.EnableVisualStyles();
            try {
                if (Process.GetProcessesByName("OUTLOOK").Length != 0) throw new InvalidOperationException("Close Outlook before installing or removing LocalCorrect.");
                if (args.Contains("/uninstall")) {
                    if (Path.GetFullPath(Assembly.GetExecutingAssembly().Location).Equals(SetupPath, StringComparison.OrdinalIgnoreCase)) {
                        string temporary = Path.Combine(Path.GetTempPath(), "LocalCorrect-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
                        File.Copy(SetupPath, temporary);
                        Process.Start(new ProcessStartInfo(temporary, "/uninstall") { UseShellExecute = false }); return;
                    }
                    if (MessageBox.Show("Remove LocalCorrect for this Windows user? Saved language and shortcut preferences will remain.", "LocalCorrect", MessageBoxButtons.OKCancel) == DialogResult.OK) { Uninstall(); MessageBox.Show("LocalCorrect was removed. Any modified files were retained.", "LocalCorrect"); }
                    return;
                }
                if (Directory.Exists(Root)) throw new InvalidOperationException("An existing LocalCorrect folder is present. Remove the existing installation from Windows Apps first. No files were replaced.");
                using (var framework = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                    if (framework == null || Convert.ToInt32(framework.GetValue("Release", 0)) < 528040) throw new InvalidOperationException("Install Microsoft .NET Framework 4.8 before continuing.");
                if (MessageBox.Show("Install LocalCorrect for classic Outlook 2019 for this Windows user?\n\nEnglish and German correction runs offline. This installs an unsigned COM add-in. Your existing Office security policies remain in force.\n\nClose Outlook first. New Outlook is not supported.", "LocalCorrect setup", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
                Install();
                MessageBox.Show("Installed. Start classic Outlook, pop out a compose window, and open the LocalCorrect tab. Ctrl+Shift+L corrects selected prose or the body. Change language and shortcut in Settings.\n\nAlways review changes before sending. Ctrl+Z undoes a correction.", "LocalCorrect");
            } catch (Exception ex) { MessageBox.Show(ex.Message, "LocalCorrect setup", MessageBoxButtons.OK, MessageBoxIcon.Error); Environment.ExitCode = 1; }
        }
        static RegistryView[] Views => Environment.Is64BitOperatingSystem ? new[] { RegistryView.Registry32, RegistryView.Registry64 } : new[] { RegistryView.Registry32 };
        static void Install() {
            foreach (var view in Views) using (var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view)) {
                foreach (var key in new[] { @"Software\Classes\CLSID\" + Clsid, @"Software\Classes\" + ProgId, @"Software\Microsoft\Office\Outlook\Addins\" + ProgId, UninstallKey })
                    using (var existing = hive.OpenSubKey(key)) if (existing != null) throw new InvalidOperationException("A conflicting LocalCorrect registration exists. No registry keys were changed.");
            }
            string stage = Root + ".stage-" + Guid.NewGuid().ToString("N");
            try {
                Directory.CreateDirectory(stage);
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LocalCorrect.payload.zip"))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read)) {
                    foreach (var entry in zip.Entries) {
                        string destination = Path.GetFullPath(Path.Combine(stage, entry.FullName));
                        if (!destination.StartsWith(stage + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || entry.FullName.Contains(":")) throw new InvalidDataException("Unsafe package entry.");
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        entry.ExtractToFile(destination);
                    }
                }
                File.Copy(Assembly.GetExecutingAssembly().Location, Path.Combine(stage, Path.GetFileName(SetupPath)));
                File.WriteAllText(Path.Combine(stage, "installation-owner.txt"), Owner);
                var files = Directory.GetFiles(stage, "*", SearchOption.AllDirectories);
                File.WriteAllLines(Path.Combine(stage, "installed-files.sha256"), files.Select(p => Hash(p) + "  " + p.Substring(stage.Length + 1)));
                Directory.CreateDirectory(Path.GetDirectoryName(Root)); Directory.Move(stage, Root);
                try { Register(); } catch { RemoveOwnedRegistrations(); DeleteOwnedFiles(); throw; }
            } finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
        }
        static void Register() {
            var identity = AssemblyName.GetAssemblyName(Path.Combine(Root, "LocalCorrect.Outlook.dll"));
            foreach (var view in Views) using (var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view)) {
                using (var clsid = hive.CreateSubKey(@"Software\Classes\CLSID\" + Clsid)) {
                    clsid.SetValue("", ProgId); clsid.SetValue("LocalCorrectOwner", Owner);
                    using (var inproc = clsid.CreateSubKey("InprocServer32")) {
                        inproc.SetValue("", "mscoree.dll"); inproc.SetValue("ThreadingModel", "Both"); inproc.SetValue("Class", "LocalCorrect.Outlook.AddIn"); inproc.SetValue("Assembly", identity.FullName); inproc.SetValue("RuntimeVersion", "v4.0.30319"); inproc.SetValue("CodeBase", new Uri(Path.Combine(Root, "LocalCorrect.Outlook.dll")).AbsoluteUri);
                    }
                    using (var prog = clsid.CreateSubKey("ProgId")) prog.SetValue("", ProgId);
                }
                using (var prog = hive.CreateSubKey(@"Software\Classes\" + ProgId)) { prog.SetValue("LocalCorrectOwner", Owner); using (var c = prog.CreateSubKey("CLSID")) c.SetValue("", Clsid); }
                using (var addin = hive.CreateSubKey(@"Software\Microsoft\Office\Outlook\Addins\" + ProgId)) {
                    addin.SetValue("LocalCorrectOwner", Owner); addin.SetValue("FriendlyName", "LocalCorrect offline EN/DE"); addin.SetValue("Description", "Offline, user-triggered selected-prose correction"); addin.SetValue("LoadBehavior", 3, RegistryValueKind.DWord);
                }
            }
            using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey)) {
                key.SetValue("LocalCorrectOwner", Owner); key.SetValue("DisplayName", "LocalCorrect for classic Outlook"); key.SetValue("DisplayVersion", "1.0.0-rc.1"); key.SetValue("Publisher", "LocalCorrect contributors"); key.SetValue("InstallLocation", Root); key.SetValue("UninstallString", "\"" + SetupPath + "\" /uninstall"); key.SetValue("NoModify", 1); key.SetValue("NoRepair", 1);
            }
        }
        static void RemoveOwnedRegistrations() {
            foreach (var view in Views) using (var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view))
                foreach (var path in new[] { @"Software\Classes\CLSID\" + Clsid, @"Software\Classes\" + ProgId, @"Software\Microsoft\Office\Outlook\Addins\" + ProgId, UninstallKey }) {
                    bool owned; using (var key = hive.OpenSubKey(path)) owned = (string)key?.GetValue("LocalCorrectOwner") == Owner;
                    if (owned) hive.DeleteSubKeyTree(path, false);
                }
        }
        static void Uninstall() {
            if (!File.Exists(Path.Combine(Root, "installation-owner.txt")) || File.ReadAllText(Path.Combine(Root, "installation-owner.txt")) != Owner) throw new InvalidOperationException("Installation ownership could not be verified. No files or registry keys were removed.");
            RemoveOwnedRegistrations(); DeleteOwnedFiles();
        }
        static void DeleteOwnedFiles() {
            string inventory = Path.Combine(Root, "installed-files.sha256");
            foreach (string line in File.ReadAllLines(inventory)) {
                if (line.Length < 67) continue;
                string path = Path.GetFullPath(Path.Combine(Root, line.Substring(66)));
                if (!path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                if (File.Exists(path) && Hash(path) == line.Substring(0, 64)) File.Delete(path);
            }
            File.Delete(inventory);
            foreach (var dir in Directory.GetDirectories(Root, "*", SearchOption.AllDirectories).OrderByDescending(p => p.Length)) if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            if (!Directory.EnumerateFileSystemEntries(Root).Any()) Directory.Delete(Root);
        }
        static string Hash(string path) { using (var sha = SHA256.Create()) using (var f = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant(); }
    }
}
