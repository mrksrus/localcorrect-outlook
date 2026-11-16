using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
namespace LocalCorrect.Setup {
    static class Program {
        const string Owner = OwnedRegistration.Owner;
        static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalCorrect", "Outlook");
        static readonly string SetupPath = Path.Combine(Root, "LocalCorrect-Outlook-Setup.exe");
        [STAThread] static void Main(string[] args) {
            Application.EnableVisualStyles();
            if (args.Any(a => string.Equals(a, "/diagnose", StringComparison.OrdinalIgnoreCase))) {
                Environment.ExitCode = Diagnose();
                return;
            }
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
                string retained;
                if (!Install(out retained)) return;
                MessageBox.Show("Installed. Start classic Outlook, pop out a compose window, and open the LocalCorrect tab. Ctrl+Shift+L corrects the selection, or the body above any quoted reply. Change language and shortcut in Settings.\n\nReview changes before sending. Ctrl+Z undoes a correction.\n\nIf Outlook had disabled the add-in, this setup cleared that block. If the splash screen still closes Outlook, run this program with /diagnose and send diagnose.txt." + (retained == null ? "" : "\n\nRetained files were moved intact to:\n" + retained), "LocalCorrect");
            } catch (Exception ex) { MessageBox.Show(ex.Message, "LocalCorrect setup", MessageBoxButtons.OK, MessageBoxIcon.Error); Environment.ExitCode = 1; }
        }
        static RegistryView[] Views => Environment.Is64BitOperatingSystem ? new[] { RegistryView.Registry32, RegistryView.Registry64 } : new[] { RegistryView.Registry32 };
        static bool Install(out string retained) {
            retained = null;
            var conflicts = CollectConflicts();
            bool folderUnverified = Directory.Exists(Root) && !FolderOwned();
            if ((conflicts.Count != 0 || folderUnverified) && MessageBox.Show(OwnedRegistration.FormatReinstallPrompt(conflicts, folderUnverified), "LocalCorrect setup", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return false;
            bool replaceExisting = conflicts.Count != 0 || folderUnverified;
            if (folderUnverified) {
                retained = Root + ".previous-" + Guid.NewGuid().ToString("N");
                Directory.Move(Root, retained);
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
                File.WriteAllLines(Path.Combine(stage, "installed-files.sha256"), files.Select(p => OwnedInstallation.Hash(p) + "  " + p.Substring(stage.Length + 1)));
                Directory.CreateDirectory(Path.GetDirectoryName(Root));
                if (Directory.Exists(Root)) retained = OwnedInstallation.ArchiveRetained(Root, Owner);
                Directory.Move(stage, Root);
                try { Register(replaceExisting); } catch { RemoveOwnedRegistrations(); DeleteOwnedFiles(); throw; }
                return true;
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
        static bool FolderOwned() {
            try { OwnedInstallation.VerifyOwnership(Root, Owner); return true; }
            catch (InvalidOperationException) { return false; }
            catch (InvalidDataException) { return false; }
        }
        static List<OwnedRegistration.Conflict> CollectConflicts() {
            var slots = OpenSlots();
            try {
                var conflicts = new List<OwnedRegistration.Conflict>();
                foreach (var slot in slots) conflicts.AddRange(OwnedRegistration.FindConflicts(slot.Hive, slot.ViewName, slot.IncludeWowNode));
                return conflicts;
            } finally { Close(slots); }
        }
        static void Register(bool replaceExisting) {
            var slots = OpenSlots();
            try { OwnedRegistration.Replace(slots, Facts(), replaceExisting); }
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
        static int Diagnose() {
            string path = Path.Combine(Root, "diagnose.txt");
            var report = new StringBuilder();
            Action<string> note = line => {
                report.AppendLine(line);
                try {
                    Directory.CreateDirectory(Root);
                    using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) {
                        byte[] bytes = Encoding.UTF8.GetBytes(line + "\r\n");
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }
                } catch { }
            };
            try { if (File.Exists(path)) File.Delete(path); } catch { }
            note("LocalCorrect diagnose " + OwnedRegistration.ProductVersion);
            note("process " + (Environment.Is64BitProcess ? "64-bit" : "32-bit") + " clr " + ClrFileVersion());
            string codeBase = null;
            foreach (var view in Views) {
                note("view " + view);
                try {
                    using (var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view)) {
                        string clsid = @"Software\Classes\CLSID\" + OwnedRegistration.Clsid;
                        string inproc = clsid + @"\InprocServer32";
                        note("  CLSID default=" + Read(root, clsid, ""));
                        note("  InprocServer32=" + Read(root, inproc, ""));
                        note("  ThreadingModel=" + Read(root, inproc, "ThreadingModel"));
                        note("  Class=" + Read(root, inproc, "Class"));
                        note("  Assembly=" + Read(root, inproc, "Assembly"));
                        note("  RuntimeVersion=" + Read(root, inproc, "RuntimeVersion"));
                        note("  CodeBase=" + Read(root, inproc, "CodeBase"));
                        string found = Read(root, inproc, "CodeBase");
                        if (found.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) codeBase = found;
                        string addin = @"Software\Microsoft\Office\Outlook\Addins\" + OwnedRegistration.ProgId;
                        note("  LoadBehavior=" + Read(root, addin, "LoadBehavior"));
                    }
                } catch (Exception ex) { note("  registry " + ex.GetType().Name + ": " + ex.Message); }
            }
            string dll = Path.Combine(Root, "LocalCorrect.Outlook.dll");
            if (!string.IsNullOrEmpty(codeBase)) {
                try { dll = new Uri(codeBase).LocalPath; }
                catch (Exception ex) { note("CodeBase is not a URI: " + ex.Message); }
            }
            note("dll " + dll + " exists=" + File.Exists(dll));
            if (File.Exists(dll)) {
                try {
                    var identity = AssemblyName.GetAssemblyName(dll);
                    note("full name " + identity.FullName);
                    note("file bytes " + new FileInfo(dll).Length);
                    string folder = Path.GetDirectoryName(dll);
                    foreach (string file in Directory.GetFiles(folder))
                        note("beside " + Path.GetFileName(file) + " " + new FileInfo(file).Length);
                } catch (Exception ex) { note("assembly name " + ex); }
            }
            string conclusion;
            try {
                if (!File.Exists(dll)) throw new FileNotFoundException("LocalCorrect.Outlook.dll was not found.", dll);
                note("before Assembly.LoadFrom");
                var loaded = Assembly.LoadFrom(dll);
                note("loaded " + loaded.FullName + " location " + loaded.Location);
                try {
                    foreach (var attr in loaded.ManifestModule.GetCustomAttributes(false))
                        note("module attribute " + attr.GetType().FullName);
                } catch (Exception ex) { note("module attributes " + ex.GetType().Name + ": " + ex.Message); }
                note("before CoCreateInstance " + OwnedRegistration.Clsid);
                Type comType = Type.GetTypeFromCLSID(new Guid(OwnedRegistration.Clsid), true);
                object created = Activator.CreateInstance(comType);
                note("created " + (created == null ? "null" : created.GetType().FullName));
                var extensibility = (DiagnoseExtensibility)created;
                object[] custom = null;
                note("before OnConnection");
                extensibility.OnConnection(new object(), 0, new object(), ref custom);
                note("OnConnection returned");
                conclusion = "COM activation succeeded. OnConnection returned.";
            } catch (Exception ex) {
                note(ex.ToString());
                conclusion = "COM activation did not finish: " + ex.GetType().Name + ": " + ex.Message;
            }
            note(conclusion);
            try { MessageBox.Show(conclusion + "\n\n" + path, "LocalCorrect diagnose"); } catch { }
            return conclusion.StartsWith("COM activation succeeded", StringComparison.Ordinal) ? 0 : 1;
        }
        static string Read(RegistryKey root, string path, string name) {
            try {
                using (var key = root.OpenSubKey(path)) return key == null ? "(missing)" : (key.GetValue(name) ?? "(null)").ToString();
            } catch (Exception ex) { return ex.GetType().Name; }
        }
        static string ClrFileVersion() {
            string file = Path.Combine(Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows", Environment.Is64BitProcess ? @"Microsoft.NET\Framework64\v4.0.30319\clr.dll" : @"Microsoft.NET\Framework\v4.0.30319\clr.dll");
            try { return File.Exists(file) ? FileVersionInfo.GetVersionInfo(file).FileVersion + " " + file : "not found " + file; }
            catch (Exception ex) { return ex.Message; }
        }
        [ComImport, Guid("B65AD801-ABAF-11D0-BB8B-00A0C90F2744"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        interface DiagnoseExtensibility {
            [DispId(1)] void OnConnection([MarshalAs(UnmanagedType.IDispatch)] object application, int connectMode, [MarshalAs(UnmanagedType.IDispatch)] object addInInst, [MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref object[] custom);
            [DispId(2)] void OnDisconnection(int removeMode, [MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref object[] custom);
            [DispId(3)] void OnAddInsUpdate([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref object[] custom);
            [DispId(4)] void OnStartupComplete([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref object[] custom);
            [DispId(5)] void OnBeginShutdown([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref object[] custom);
        }
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
