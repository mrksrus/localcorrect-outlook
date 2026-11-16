using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using LocalCorrect;
using LocalCorrect.Outlook;

// Runs under .NET Framework 4.8 on Windows CI, the same runtime Outlook hosts.
// An access violation in clr.dll (0x80131506) kills the child process and fails the build.
class Program {
    const string ProductClsid = "{708F01D3-893E-43D0-8215-61722BC46F23}";
    const string BadClsid = "{708F01D3-893E-43D0-8215-61722BC46F24}";
    const string RefClsid = "{708F01D3-893E-43D0-8215-61722BC46F25}";

    [STAThread]
    static int Main(string[] args) {
        if (args.Length >= 2 && args[0] == "activate") return Activate(args[1]);
        int managed = ManagedLoad();
        if (managed != 0) return managed;
        return ComActivation();
    }

    static int ManagedLoad() {
        Exception probe = null;
        var thread = new Thread(() => {
            try {
                string text = Corrector.FromEmbeddedData().Correct("recieve", "en").Text;
                if (text != "receive") probe = new Exception("engine returned " + text);
            } catch (Exception ex) { probe = ex; }
        }, 1024 * 1024);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        if (probe != null) { Console.Error.WriteLine(probe); return 1; }

        string log = LogPath();
        try { if (File.Exists(log)) File.Delete(log); } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        var addin = new AddIn();
        object[] custom = null;
        addin.OnConnection(new object(), 0, new object(), ref custom);
        addin.OnStartupComplete(ref custom);
        if (addin.GetCustomUI("Microsoft.Outlook.Explorer") != null) return 2;
        string ui = addin.GetCustomUI("Microsoft.Outlook.Mail.Compose");
        if (ui == null || ui.IndexOf("localCorrectTab", StringComparison.Ordinal) < 0) return 3;
        var deadline = DateTime.UtcNow.AddSeconds(60);
        string seen = "";
        while (DateTime.UtcNow < deadline) {
            try { seen = File.Exists(log) ? File.ReadAllText(log) : ""; }
            catch (IOException) { seen = ""; }
            if (seen.IndexOf("word lists ready", StringComparison.Ordinal) >= 0) break;
            Thread.Sleep(200);
        }
        if (seen.IndexOf("type constructed", StringComparison.Ordinal) < 0 || seen.IndexOf("OnConnection returned", StringComparison.Ordinal) < 0) { Console.Error.WriteLine(seen); return 4; }
        if (seen.IndexOf("OnStartupComplete returned", StringComparison.Ordinal) < 0) { Console.Error.WriteLine(seen); return 5; }
        if (seen.IndexOf("word lists ready", StringComparison.Ordinal) < 0) { Console.Error.WriteLine(seen); return 6; }
        addin.OnDisconnection(0, ref custom);
        Console.WriteLine("PASS add-in load and correction under this runtime");
        return 0;
    }

    // Outlook activates via mscoree CoCreateInstance on the CLSID setup writes.
    // `new AddIn()` never enters that path. Each probe is a child process so an
    // access violation is an exit code instead of taking down the test host.
    static int ComActivation() {
        if (!Environment.Is64BitProcess) { Console.Error.WriteLine("COM activation probe must run as 64-bit"); return 7; }
        Console.WriteLine("clr " + ClrVersion());
        try {
            foreach (var attr in typeof(AddIn).Assembly.ManifestModule.GetCustomAttributes(false)) {
                if (attr.GetType().Name.IndexOf("RefSafety", StringComparison.Ordinal) >= 0) {
                    Console.Error.WriteLine("product module still has " + attr.GetType().FullName);
                    return 9;
                }
            }
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 9; }

        string dir = AppDomain.CurrentDomain.BaseDirectory;
        // A short path. The runner's checkout path is valid too; this just keeps CodeBase obvious in the log.
        string probeRoot = Path.Combine(Path.GetTempPath(), "lc-com");
        Directory.CreateDirectory(probeRoot);
        foreach (string name in new[] { "LocalCorrect.Outlook.dll", "LocalCorrect.ComProbe.Bad.dll", "LocalCorrect.ComProbe.RefSafety.dll" })
            File.Copy(Path.Combine(dir, name), Path.Combine(probeRoot, name), true);
        string product = Path.Combine(probeRoot, "LocalCorrect.Outlook.dll");
        string bad = Path.Combine(probeRoot, "LocalCorrect.ComProbe.Bad.dll");
        string refsafety = Path.Combine(probeRoot, "LocalCorrect.ComProbe.RefSafety.dll");
        ShowRegAsm(product);
        int refCode = -1, badCode = -1, productCode = -1;
        var hive = RegistryHive.CurrentUser;
        try {
            Register(hive, RefClsid, "LocalCorrect.ComProbe.ProbeRefSafety", refsafety, false);
            refCode = RunChild("refsafety");
            Console.WriteLine("PROBE refsafety " + hive + " exit " + Hex(refCode));
            // This runner's COM lookup returns 80070002 for a complete HKCU
            // CodeBase registration even though the file is there. HKLM is the
            // same mscoree path Outlook uses once it has found the DLL.
            if (refCode == 1) {
                hive = RegistryHive.LocalMachine;
                Console.WriteLine("HKCU activation did not load the file; retrying HKLM");
                try { Register(hive, RefClsid, "LocalCorrect.ComProbe.ProbeRefSafety", refsafety, false); }
                catch (Exception ex) { Console.Error.WriteLine(ex); return 8; }
                refCode = RunChild("refsafety");
                Console.WriteLine("PROBE refsafety " + hive + " exit " + Hex(refCode));
            }
            Register(hive, BadClsid, "LocalCorrect.ComProbe.BadAddIn", bad, true);
            badCode = RunChild("bad");
            Console.WriteLine("PROBE old-interface " + hive + " exit " + Hex(badCode));
            Register(hive, ProductClsid, "LocalCorrect.Outlook.AddIn", product, true);
            try { if (File.Exists(LogPath())) File.Delete(LogPath()); }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 8; }
            if (File.Exists(LogPath())) { Console.Error.WriteLine("could not reset log.txt"); return 8; }
            productCode = RunChild("product");
            Console.WriteLine("PROBE product " + hive + " exit " + Hex(productCode));
            string log = File.Exists(LogPath()) ? File.ReadAllText(LogPath()) : "";
            Console.WriteLine("PROBE product log " + (log.Length == 0 ? "(none)" : log.Replace("\r", " ").Replace("\n", " | ")));
        } finally {
            Delete(RegistryHive.CurrentUser, RefClsid);
            Delete(RegistryHive.CurrentUser, BadClsid);
            Delete(RegistryHive.CurrentUser, ProductClsid);
            Delete(RegistryHive.LocalMachine, RefClsid);
            Delete(RegistryHive.LocalMachine, BadClsid);
            Delete(RegistryHive.LocalMachine, ProductClsid);
        }
        if (productCode != 0) { Console.Error.WriteLine("COM activation of LocalCorrect.Outlook failed"); return 8; }
        string written = File.Exists(LogPath()) ? File.ReadAllText(LogPath()) : "";
        if (written.IndexOf("type constructed", StringComparison.Ordinal) < 0 || written.IndexOf("OnConnection returned", StringComparison.Ordinal) < 0) {
            Console.Error.WriteLine(written);
            return 8;
        }
        Console.WriteLine("PASS com activation through the setup registry entries");
        if (refCode != 0) Console.WriteLine("NOTE RefSafetyRules probe did not survive COM activation (" + Hex(refCode) + ")");
        if (badCode != 0) Console.WriteLine("NOTE old interface shape did not survive COM activation (" + Hex(badCode) + ")");
        return 0;
    }

    static int Activate(string which) {
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        try {
            string clsid = which == "product" ? ProductClsid : which == "bad" ? BadClsid : RefClsid;
            Console.WriteLine("child " + (Environment.Is64BitProcess ? "64-bit" : "32-bit"));
            DumpClsid(clsid);
            Console.WriteLine("before-cocreate " + which);
            Type type = Type.GetTypeFromCLSID(new Guid(clsid), true);
            object created = Activator.CreateInstance(type);
            Console.WriteLine("created " + (created == null ? "null" : created.GetType().FullName));
            if (which == "refsafety") return created == null ? 3 : 0;
            var extensibility = (OutlookExtensibility)created;
            object[] custom = null;
            Console.WriteLine("before-call");
            extensibility.OnConnection(new object(), 0, new object(), ref custom);
            Console.WriteLine("onconnection-returned");
            extensibility.OnStartupComplete(ref custom);
            Console.WriteLine("onstartup-returned");
            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    static int RunChild(string which) {
        var start = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "activate " + which) { UseShellExecute = false };
        using (var process = Process.Start(start)) {
            if (!process.WaitForExit(45000)) { try { process.Kill(); } catch { } return -2; }
            return process.ExitCode;
        }
    }

    static void Register(RegistryHive hive, string clsid, string className, string dll, bool versionSubkey) {
        var identity = AssemblyName.GetAssemblyName(dll);
        string codeBase = new Uri(dll).AbsoluteUri;
        Console.WriteLine("register " + hive + " " + clsid + " " + identity.FullName + " " + codeBase + " exists=" + File.Exists(dll) + (versionSubkey ? " +version" : ""));
        foreach (RegistryView view in Views()) {
            using (var root = RegistryKey.OpenBaseKey(hive, view)) {
                string key = @"Software\Classes\CLSID\" + clsid;
                string inproc = key + @"\InprocServer32";
                Put(root, key, "", className);
                Put(root, inproc, "", "mscoree.dll");
                Put(root, inproc, "ThreadingModel", "Both");
                Put(root, inproc, "Class", className);
                Put(root, inproc, "Assembly", identity.FullName);
                Put(root, inproc, "RuntimeVersion", "v4.0.30319");
                Put(root, inproc, "CodeBase", codeBase);
                Put(root, key + @"\ProgId", "", className);
                Put(root, key + @"\Implemented Categories\{62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}", "", "");
                Put(root, @"Software\Classes\" + className + @"\CLSID", "", clsid);
                if (!versionSubkey) continue;
                string sub = inproc + "\\" + identity.Version;
                Put(root, sub, "Class", className);
                Put(root, sub, "Assembly", identity.FullName);
                Put(root, sub, "RuntimeVersion", "v4.0.30319");
                Put(root, sub, "CodeBase", codeBase);
            }
        }
    }

    static void DumpClsid(string clsid) {
        foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (RegistryView view in Views()) {
            RegistryKey root;
            try { root = RegistryKey.OpenBaseKey(hive, view); }
            catch (Exception ex) { Console.WriteLine("child " + hive + " " + view + " " + ex.GetType().Name); continue; }
            using (root) {
                string inproc = @"Software\Classes\CLSID\" + clsid + @"\InprocServer32";
                using (var key = root.OpenSubKey(inproc)) {
                    if (key == null) { Console.WriteLine("child " + hive + " " + view + " InprocServer32 missing"); continue; }
                    string code = key.GetValue("CodeBase") as string;
                    Console.WriteLine("child " + hive + " " + view + " default=" + key.GetValue("") + " class=" + key.GetValue("Class") + " code=" + code);
                    if (!string.IsNullOrEmpty(code)) {
                        try { Console.WriteLine("child file exists=" + File.Exists(new Uri(code).LocalPath)); }
                        catch (Exception ex) { Console.WriteLine("child codebase " + ex.Message); }
                    }
                }
            }
        }
    }

    static void ShowRegAsm(string dll) {
        string regasm = Path.Combine(Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows", @"Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe");
        string reg = Path.Combine(Path.GetTempPath(), "lc-regasm.reg");
        if (!File.Exists(regasm)) { Console.WriteLine("regasm missing " + regasm); return; }
        var start = new ProcessStartInfo(regasm, "/codebase /regfile:\"" + reg + "\" \"" + dll + "\"") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using (var process = Process.Start(start)) {
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Console.WriteLine("regasm /regfile exit " + process.ExitCode);
            if (output.Length != 0) Console.WriteLine(output);
            if (error.Length != 0) Console.WriteLine(error);
        }
        if (File.Exists(reg)) Console.WriteLine(File.ReadAllText(reg));
    }

    static void Put(RegistryKey root, string path, string name, string value) {
        using (var key = root.CreateSubKey(path)) key.SetValue(name, value);
    }

    static void Delete(RegistryHive hive, string clsid) {
        foreach (RegistryView view in Views()) {
            try {
                using (var root = RegistryKey.OpenBaseKey(hive, view)) {
                    try { root.DeleteSubKeyTree(@"Software\Classes\CLSID\" + clsid, false); } catch { }
                }
            } catch (Exception ex) { Console.WriteLine("delete " + hive + " " + ex.GetType().Name); }
        }
    }

    static RegistryView[] Views() {
        return Environment.Is64BitOperatingSystem ? new[] { RegistryView.Registry32, RegistryView.Registry64 } : new[] { RegistryView.Registry32 };
    }

    static string LogPath() {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalCorrect", "Outlook", "log.txt");
    }

    static string ClrVersion() {
        string path = Path.Combine(Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows", @"Microsoft.NET\Framework64\v4.0.30319\clr.dll");
        return File.Exists(path) ? FileVersionInfo.GetVersionInfo(path).FileVersion + " " + path : "clr.dll not found";
    }

    static string Hex(int code) { return code + " (0x" + unchecked((uint)code).ToString("X8") + ")"; }

    // What 64-bit Outlook calls: the imported IDTExtensibility2, not a direct managed call.
    [ComImport, Guid("B65AD801-ABAF-11D0-BB8B-00A0C90F2744"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    interface OutlookExtensibility {
        [DispId(1)] void OnConnection([MarshalAs(UnmanagedType.IDispatch)] object application, int connectMode, [MarshalAs(UnmanagedType.IDispatch)] object addInInst, [MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref object[] custom);
        [DispId(2)] void OnDisconnection(int removeMode, [MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref object[] custom);
        [DispId(3)] void OnAddInsUpdate([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref object[] custom);
        [DispId(4)] void OnStartupComplete([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref object[] custom);
        [DispId(5)] void OnBeginShutdown([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref object[] custom);
    }
}
