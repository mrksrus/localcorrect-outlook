using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;
namespace LocalCorrect.Setup {
    static class Program {
        const string Owner = OwnedRegistration.Owner;
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
                    if (MessageBox.Show("Remove LocalCorrect for this Windows user? Saved language and shortcut preferences will remain.", "LocalCorrect", MessageBoxButtons.OKCancel) == DialogResult.OK) { Uninstall(); MessageBox.Show("LocalCorrect was removed. Any modified or unrelated files and their ownership records were retained.", "LocalCorrect"); }
                    return;
                }
                if (Environment.OSVersion.Version.Major < 10) throw new InvalidOperationException("LocalCorrect requires Windows 10 or 11.");
                using (var framework = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                    if (framework == null || Convert.ToInt32(framework.GetValue("Release", 0)) < 528040) throw new InvalidOperationException("Install Microsoft .NET Framework 4.8 before continuing.");
                if (MessageBox.Show("Install LocalCorrect for classic Outlook 2019 for this Windows user?\n\nEnglish and German correction runs offline. This installs an unsigned COM add-in. Your existing Office security policies remain in force.\n\nClose Outlook first. New Outlook is not supported.", "LocalCorrect setup", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
                string retained = Install();
                MessageBox.Show("Installed. Start classic Outlook, pop out a compose window, and open the LocalCorrect tab. Ctrl+Shift+L corrects the selection, or the body above any quoted reply. Change language and shortcut in Settings.\n\nReview changes before sending. Ctrl+Z undoes a correction." + (retained == null ? "" : "\n\nRetained files were moved intact to:\n" + retained), "LocalCorrect");
            } catch (Exception ex) { MessageBox.Show(ex.Message, "LocalCorrect setup", MessageBoxButtons.OK, MessageBoxIcon.Error); Environment.ExitCode = 1; }
        }
        static RegistryView[] Views => Environment.Is64BitOperatingSystem ? new[] { RegistryView.Registry32, RegistryView.Registry64 } : new[] { RegistryView.Registry32 };
        static string Install() {
            EnsureNoForeignRegistration();
            if (Directory.Exists(Root)) OwnedInstallation.VerifyOwnership(Root, Owner);
            string retained = null;
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
                File.WriteAllLines(Path.Combine(stage, "installed-files.sha256"), files.Select(p => OwnedInstallation.Hash(p) + "  " + p.Substring(stage.Length + 1)));
                Directory.CreateDirectory(Path.GetDirectoryName(Root));
                if (Directory.Exists(Root)) retained = OwnedInstallation.ArchiveRetained(Root, Owner);
                Directory.Move(stage, Root);
                try { Register(); } catch { RemoveOwnedRegistrations(); DeleteOwnedFiles(); throw; }
                return retained;
            } catch {
                if (retained != null && !Directory.Exists(Root) && Directory.Exists(retained)) Directory.Move(retained, Root);
                throw;
            } finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
        }
        static OwnedRegistration.InstallFacts Facts() {
            var identity = AssemblyName.GetAssemblyName(Path.Combine(Root, "LocalCorrect.Outlook.dll"));
            return new OwnedRegistration.InstallFacts {
                Version = OwnedRegistration.ProductVersion,
                InstallLocation = Root,
                UninstallString = "\"" + SetupPath + "\" /uninstall",
                AssemblyFullName = identity.FullName,
                CodeBase = new Uri(Path.Combine(Root, "LocalCorrect.Outlook.dll")).AbsoluteUri
            };
        }
        static OwnedRegistration.HiveSlot[] OpenSlots() {
            var slots = new List<OwnedRegistration.HiveSlot>();
            foreach (var view in Views) slots.Add(new OwnedRegistration.HiveSlot {
                Hive = new WindowsHive(view),
                ViewName = view == RegistryView.Registry64 ? "64-bit" : "32-bit",
                IncludeWowNode = view == RegistryView.Registry64 && Environment.Is64BitOperatingSystem
            });
            return slots.ToArray();
        }
        static void Close(OwnedRegistration.HiveSlot[] slots) {
            foreach (var slot in slots) { var hive = slot.Hive as IDisposable; if (hive != null) hive.Dispose(); }
        }
        static void EnsureNoForeignRegistration() {
            var slots = OpenSlots();
            try {
                var conflicts = new List<OwnedRegistration.Conflict>();
                foreach (var slot in slots) conflicts.AddRange(OwnedRegistration.FindConflicts(slot.Hive, slot.ViewName, slot.IncludeWowNode));
                if (conflicts.Count != 0) throw new InvalidOperationException(OwnedRegistration.FormatConflict(conflicts));
            } finally { Close(slots); }
        }
        static void Register() {
            var slots = OpenSlots();
            try { OwnedRegistration.Replace(slots, Facts()); }
            finally { Close(slots); }
        }
        static void RemoveOwnedRegistrations() {
            var slots = OpenSlots();
            try { foreach (var slot in slots) { OwnedRegistration.RemoveOwned(slot.Hive, slot.IncludeWowNode); OwnedRegistration.ClearResiliency(slot.Hive); } }
            finally { Close(slots); }
        }
        static void Uninstall() {
            // Registry ownership is stamped on the keys themselves, so remove
            // those even when the install folder is already gone. Files are
            // deleted only after the folder's own ownership check.
            RemoveOwnedRegistrations();
            if (!Directory.Exists(Root)) return;
            try { OwnedInstallation.VerifyOwnership(Root, Owner); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is InvalidDataException) {
                throw new InvalidOperationException("LocalCorrect's registration was removed. The installation folder was left in place because its ownership could not be verified.");
            }
            DeleteOwnedFiles();
        }
        static void DeleteOwnedFiles() { OwnedInstallation.DeleteOwnedFiles(Root, Owner); }
        sealed class WindowsHive : OwnedRegistration.IHive, IDisposable {
            readonly RegistryKey hive;
            public WindowsHive(RegistryView view) { hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view); }
            public void Dispose() { hive.Dispose(); }
            public bool Exists(string path) { using (var key = hive.OpenSubKey(path)) return key != null; }
            public object GetValue(string path, string name) { using (var key = hive.OpenSubKey(path)) return key == null ? null : key.GetValue(name ?? ""); }
            public string[] GetValueNames(string path) { using (var key = hive.OpenSubKey(path)) return key == null ? new string[0] : key.GetValueNames(); }
            public void DeleteValue(string path, string name) { using (var key = hive.OpenSubKey(path, true)) if (key != null) key.DeleteValue(name ?? "", false); }
            public void SetValue(string path, string name, object value, bool dword) {
                using (var key = hive.CreateSubKey(path)) {
                    if (key == null) throw new InvalidOperationException("Could not create HKCU\\" + path);
                    if (dword) key.SetValue(name ?? "", value, RegistryValueKind.DWord);
                    else key.SetValue(name ?? "", value, RegistryValueKind.String);
                }
            }
            public void DeleteTree(string path) { hive.DeleteSubKeyTree(path, false); }
        }
    }
}
