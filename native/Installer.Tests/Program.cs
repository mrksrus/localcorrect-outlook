using System;
using System.IO;
using System.Linq;
using System.Text;
using LocalCorrect.Setup;

const string Owner = "synthetic-owner";
string testRoot = Path.Combine(Path.GetTempPath(), "LocalCorrect-ownership-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
int passed = 0;
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
string Fixture(string name) {
    string root = Path.Combine(testRoot, name); Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, "installation-owner.txt"), Owner);
    File.WriteAllText(Path.Combine(root, "owned.dll"), "original synthetic payload");
    File.WriteAllLines(Path.Combine(root, "installed-files.sha256"), Directory.GetFiles(root).Select(p => OwnedInstallation.Hash(p) + "  " + Path.GetFileName(p)));
    return root;
}
void Refuses(Action action) { bool refused = false; try { action(); } catch (InvalidOperationException) { refused = true; } catch (InvalidDataException) { refused = true; } Assert(refused, "Unsafe ownership state was accepted"); }
try {

    string clean = Fixture("clean"); OwnedInstallation.DeleteOwnedFiles(clean, Owner);
    Assert(!Directory.Exists(clean), "Clean uninstall retained the installation"); passed++;

    string retained = Fixture("retained");
    string inventory = File.ReadAllText(Path.Combine(retained, "installed-files.sha256"));
    File.WriteAllText(Path.Combine(retained, "owned.dll"), "user modified payload");
    File.WriteAllText(Path.Combine(retained, "personal.txt"), "unrelated user file");
    OwnedInstallation.DeleteOwnedFiles(retained, Owner);
    Assert(File.ReadAllText(Path.Combine(retained, "owned.dll")) == "user modified payload", "Modified file was deleted");
    Assert(File.ReadAllText(Path.Combine(retained, "personal.txt")) == "unrelated user file", "Unknown file was deleted");
    Assert(File.ReadAllText(Path.Combine(retained, "installation-owner.txt")) == Owner && File.ReadAllText(Path.Combine(retained, "installed-files.sha256")) == inventory, "Retained installation lost ownership evidence");
    // A retry succeeds even when it cannot remove retained content yet.
    OwnedInstallation.DeleteOwnedFiles(retained, Owner);
    File.WriteAllText(Path.Combine(retained, "owned.dll"), "original synthetic payload");
    OwnedInstallation.DeleteOwnedFiles(retained, Owner);
    Assert(!File.Exists(Path.Combine(retained, "owned.dll")) && File.Exists(Path.Combine(retained, "personal.txt")), "Retry did not respect file hashes");
    // Simulate the owner moving their own unrelated file away, then retry.
    File.Move(Path.Combine(retained, "personal.txt"), Path.Combine(testRoot, "personal.saved"));
    OwnedInstallation.DeleteOwnedFiles(retained, Owner);
    Assert(!Directory.Exists(retained), "Retry could not finish after retained content was removed"); passed++;

    string old = Fixture("reinstall"); File.WriteAllText(Path.Combine(old, "owned.dll"), "modified old version");
    File.WriteAllText(Path.Combine(old, "unknown.txt"), "keep me"); Directory.CreateDirectory(Path.Combine(old, "unknown-empty-directory"));
    OwnedInstallation.DeleteOwnedFiles(old, Owner);
    string archive = OwnedInstallation.ArchiveRetained(old, Owner);
    Assert(!Directory.Exists(old) && File.ReadAllText(Path.Combine(archive, "owned.dll")) == "modified old version" && File.ReadAllText(Path.Combine(archive, "unknown.txt")) == "keep me" && Directory.Exists(Path.Combine(archive, "unknown-empty-directory")), "Archive changed retained content");
    Fixture("reinstall");
    Assert(File.ReadAllText(Path.Combine(old, "owned.dll")) == "original synthetic payload", "Fresh reinstall could not use original path");
    OwnedInstallation.VerifyOwnership(archive, Owner); passed++;

    string bad = Fixture("bad-owner"); File.WriteAllText(Path.Combine(bad, "installation-owner.txt"), "someone else");
    Refuses(() => OwnedInstallation.ArchiveRetained(bad, Owner)); Refuses(() => OwnedInstallation.DeleteOwnedFiles(bad, Owner));
    Assert(File.Exists(Path.Combine(bad, "owned.dll")), "Refused ownership state changed a file"); passed++;

    string missing = Fixture("missing-inventory"); File.Delete(Path.Combine(missing, "installed-files.sha256"));
    Refuses(() => OwnedInstallation.ArchiveRetained(missing, Owner)); Assert(Directory.Exists(missing), "Missing evidence directory was moved"); passed++;

    string traversal = Fixture("traversal"); string outside = Path.Combine(testRoot, "outside.txt"); File.WriteAllText(outside, "outside");
    File.AppendAllText(Path.Combine(traversal, "installed-files.sha256"), OwnedInstallation.Hash(outside) + "  ../outside.txt\n");
    Refuses(() => OwnedInstallation.DeleteOwnedFiles(traversal, Owner));
    Assert(File.ReadAllText(outside) == "outside" && File.Exists(Path.Combine(traversal, "owned.dll")), "Invalid inventory caused partial deletion"); passed++;

    string clsidPath = @"Software\Classes\CLSID\" + OwnedRegistration.Clsid;
    string progPath = @"Software\Classes\" + OwnedRegistration.ProgId;
    string addinPath = @"Software\Microsoft\Office\Outlook\Addins\" + OwnedRegistration.ProgId;
    string inprocPath = clsidPath + @"\InprocServer32";
    string wowClsid = @"Software\Classes\Wow6432Node\CLSID\" + OwnedRegistration.Clsid;
    var facts = new OwnedRegistration.InstallFacts {
        Version = OwnedRegistration.ProductVersion, InstallLocation = @"C:\Users\Sam\AppData\Local\LocalCorrect\Outlook",
        UninstallString = "\"C:\\Users\\Sam\\AppData\\Local\\LocalCorrect\\Outlook\\LocalCorrect-Outlook-Setup.exe\" /uninstall",
        AssemblyFullName = "LocalCorrect.Outlook, Version=2.0.0.0, Culture=neutral, PublicKeyToken=null",
        CodeBase = "file:///C:/Users/Sam/AppData/Local/LocalCorrect/Outlook/LocalCorrect.Outlook.dll"
    };
    void SeedV1(OwnedRegistration.IHive hive, string version) {
        hive.SetValue(clsidPath, "", OwnedRegistration.ProgId, false);
        hive.SetValue(clsidPath, OwnedRegistration.OwnerValue, OwnedRegistration.Owner, false);
        hive.SetValue(inprocPath, "", "mscoree.dll", false);
        hive.SetValue(inprocPath, "Class", OwnedRegistration.ProgId, false);
        hive.SetValue(inprocPath, "Assembly", "LocalCorrect.Outlook, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", false);
        hive.SetValue(inprocPath, "CodeBase", "file:///C:/Users/Sam/AppData/Local/LocalCorrect/Outlook/LocalCorrect.Outlook.dll", false);
        hive.SetValue(progPath, OwnedRegistration.OwnerValue, OwnedRegistration.Owner, false);
        hive.SetValue(progPath + @"\CLSID", "", OwnedRegistration.Clsid, false);
        hive.SetValue(addinPath, OwnedRegistration.OwnerValue, OwnedRegistration.Owner, false);
        hive.SetValue(addinPath, "FriendlyName", "LocalCorrect offline EN/DE", false);
        hive.SetValue(addinPath, "LoadBehavior", 3, true);
        hive.SetValue(OwnedRegistration.UninstallKey, OwnedRegistration.OwnerValue, OwnedRegistration.Owner, false);
        hive.SetValue(OwnedRegistration.UninstallKey, "DisplayName", "LocalCorrect for classic Outlook", false);
        hive.SetValue(OwnedRegistration.UninstallKey, "DisplayVersion", version, false);
        hive.SetValue(OwnedRegistration.UninstallKey, "UninstallString", facts.UninstallString, false);
    }
    bool Registered(OwnedRegistration.IHive hive) {
        return (string)hive.GetValue(OwnedRegistration.UninstallKey, "DisplayVersion") == OwnedRegistration.ProductVersion
            && (string)hive.GetValue(clsidPath, OwnedRegistration.OwnerValue) == OwnedRegistration.Owner
            && (string)hive.GetValue(inprocPath, "Assembly") == facts.AssemblyFullName
            && (string)hive.GetValue(inprocPath, "CodeBase") == facts.CodeBase
            && (string)hive.GetValue(progPath + @"\CLSID", "") == OwnedRegistration.Clsid
            && (int)hive.GetValue(addinPath, "LoadBehavior") == 3
            && (int)hive.GetValue(OwnedRegistration.UninstallKey, "NoModify") == 1;
    }
    void ClearRegistration(OwnedRegistration.IHive hive, bool wow) { OwnedRegistration.RemoveOwned(hive, wow); }

    var fromV1_32 = new OwnedRegistration.MemoryHive();
    var fromV1_64 = new OwnedRegistration.MemoryHive();
    SeedV1(fromV1_32, "1.0.0-rc.3");
    SeedV1(fromV1_64, "1.0.0-rc.6");
    fromV1_32.SetValue(@"Software\Classes\CLSID\{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}", "", "Other.AddIn", false);
    fromV1_64.SetValue(@"Software\Microsoft\Office\Outlook\Addins\Other.AddIn", "FriendlyName", "Other", false);
    var upgrade = new[] {
        new OwnedRegistration.HiveSlot { Hive = fromV1_32, ViewName = "32-bit", IncludeWowNode = false },
        new OwnedRegistration.HiveSlot { Hive = fromV1_64, ViewName = "64-bit", IncludeWowNode = true }
    };
    OwnedRegistration.Replace(upgrade, facts);
    Assert(Registered(fromV1_32) && Registered(fromV1_64), "Upgrade from v1 did not rewrite both views");
    Assert((string)fromV1_32.GetValue(@"Software\Classes\CLSID\{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}", "") == "Other.AddIn", "Upgrade removed an unrelated class");
    Assert((string)fromV1_64.GetValue(@"Software\Microsoft\Office\Outlook\Addins\Other.AddIn", "FriendlyName") == "Other", "Upgrade removed an unrelated add-in");
    ClearRegistration(fromV1_32, false); ClearRegistration(fromV1_64, true);
    Assert(!fromV1_32.Exists(clsidPath) && !fromV1_32.Exists(inprocPath) && !fromV1_32.Exists(progPath) && !fromV1_32.Exists(addinPath) && !fromV1_32.Exists(OwnedRegistration.UninstallKey), "Uninstall left a 32-bit key");
    Assert(!fromV1_64.Exists(clsidPath) && !fromV1_64.Exists(progPath) && !fromV1_64.Exists(addinPath) && !fromV1_64.Exists(OwnedRegistration.UninstallKey), "Uninstall left a 64-bit key");
    Assert(fromV1_32.Exists(@"Software\Classes\CLSID\{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}") && fromV1_64.Exists(@"Software\Microsoft\Office\Outlook\Addins\Other.AddIn"), "Uninstall removed an unrelated key"); passed++;

    var again32 = new OwnedRegistration.MemoryHive();
    var again64 = new OwnedRegistration.MemoryHive();
    var reinstall = new[] {
        new OwnedRegistration.HiveSlot { Hive = again32, ViewName = "32-bit", IncludeWowNode = false },
        new OwnedRegistration.HiveSlot { Hive = again64, ViewName = "64-bit", IncludeWowNode = true }
    };
    OwnedRegistration.Replace(reinstall, facts);
    facts.Version = OwnedRegistration.ProductVersion;
    OwnedRegistration.Replace(reinstall, facts);
    Assert(Registered(again32) && Registered(again64), "Reinstall over v2 did not keep a LocalCorrect registration");
    ClearRegistration(again32, false); ClearRegistration(again64, true);
    Assert(!again32.Exists(clsidPath) && !again64.Exists(wowClsid) && !again64.Exists(clsidPath), "Reinstall cleanup left a class key"); passed++;

    var shared = new OwnedRegistration.MemoryHive();
    var sharedSlots = new[] {
        new OwnedRegistration.HiveSlot { Hive = shared, ViewName = "32-bit", IncludeWowNode = false },
        new OwnedRegistration.HiveSlot { Hive = shared, ViewName = "64-bit", IncludeWowNode = false }
    };
    OwnedRegistration.Replace(sharedSlots, facts);
    Assert(Registered(shared), "Shared registry view was not registered");
    OwnedRegistration.RemoveOwned(shared, false);
    Assert(!shared.Exists(OwnedRegistration.UninstallKey) && !shared.Exists(clsidPath), "Shared key survived removal"); passed++;

    var bit32 = new OwnedRegistration.MemoryHive();
    var bit64native = new OwnedRegistration.MemoryHive();
    var bit64 = new WowAliasHive(bit64native, bit32);
    SeedV1(bit32, "1.0.0-rc.5");
    var views = new[] {
        new OwnedRegistration.HiveSlot { Hive = bit32, ViewName = "32-bit", IncludeWowNode = false },
        new OwnedRegistration.HiveSlot { Hive = bit64, ViewName = "64-bit", IncludeWowNode = true }
    };
    OwnedRegistration.Replace(views, facts);
    Assert(Registered(bit32) && Registered(bit64native), "32-bit class key was erased by Wow6432Node cleanup");
    Assert((string)bit64.GetValue(wowClsid, OwnedRegistration.OwnerValue) == OwnedRegistration.Owner, "32-bit class key is not visible at Wow6432Node");
    OwnedRegistration.RemoveOwned(bit64, true); OwnedRegistration.RemoveOwned(bit32, false);
    Assert(!bit32.Exists(clsidPath) && !bit64.Exists(wowClsid), "Uninstall left the 32-bit class key"); passed++;

    var stampedOnly = new OwnedRegistration.MemoryHive();
    stampedOnly.SetValue(clsidPath, "", OwnedRegistration.ProgId, false);
    stampedOnly.SetValue(inprocPath, "Class", OwnedRegistration.ProgId, false);
    stampedOnly.SetValue(inprocPath, "CodeBase", "file:///C:/Users/Sam/AppData/Local/LocalCorrect/Outlook/LocalCorrect.Outlook.dll", false);
    stampedOnly.SetValue(progPath + @"\CLSID", "", OwnedRegistration.Clsid.ToLowerInvariant(), false);
    stampedOnly.SetValue(addinPath, "Description", "Offline, user-triggered selected-prose correction", false);
    stampedOnly.SetValue(OwnedRegistration.UninstallKey, "UninstallString", facts.UninstallString, false);
    var unstamped = new[] { new OwnedRegistration.HiveSlot { Hive = stampedOnly, ViewName = "32-bit", IncludeWowNode = false } };
    OwnedRegistration.Replace(unstamped, facts);
    Assert(Registered(stampedOnly), "Registration without LocalCorrectOwner was not recognized as LocalCorrect"); passed++;

    var foreign32 = new OwnedRegistration.MemoryHive();
    var foreign64 = new OwnedRegistration.MemoryHive();
    SeedV1(foreign32, "1.0.0-rc.4");
    foreign64.SetValue(clsidPath, "", "Contoso.Mail.AddIn", false);
    foreign64.SetValue(clsidPath, OwnedRegistration.OwnerValue, "Contoso", false);
    foreign64.SetValue(inprocPath, "Class", "Contoso.Mail.AddIn", false);
    string preserved = (string)foreign32.GetValue(OwnedRegistration.UninstallKey, "DisplayVersion");
    string message = null;
    try { OwnedRegistration.Replace(new[] {
        new OwnedRegistration.HiveSlot { Hive = foreign32, ViewName = "32-bit", IncludeWowNode = false },
        new OwnedRegistration.HiveSlot { Hive = foreign64, ViewName = "64-bit", IncludeWowNode = true }
    }, facts); }
    catch (InvalidOperationException ex) { message = ex.Message; }
    Assert(message != null && message.Contains("No registry keys were changed") && message.Contains("64-bit") && message.Contains(clsidPath) && message.Contains("Contoso") && message.Contains("delete"), "Conflict did not name the key or what to do");
    Assert((string)foreign32.GetValue(OwnedRegistration.UninstallKey, "DisplayVersion") == preserved, "A foreign key caused LocalCorrect's other view to be rewritten");
    Assert((string)foreign64.GetValue(clsidPath, OwnedRegistration.OwnerValue) == "Contoso" && (string)foreign64.GetValue(inprocPath, "Class") == "Contoso.Mail.AddIn", "A foreign registration was overwritten"); passed++;

    var empty = new OwnedRegistration.MemoryHive();
    empty.SetValue(addinPath, "LoadBehavior", 2, true);
    string emptyMessage = null;
    try { OwnedRegistration.Replace(new[] { new OwnedRegistration.HiveSlot { Hive = empty, ViewName = "32-bit", IncludeWowNode = false } }, facts); }
    catch (InvalidOperationException ex) { emptyMessage = ex.Message; }
    Assert(emptyMessage != null && emptyMessage.Contains(addinPath) && emptyMessage.Contains("absent") && (int)empty.GetValue(addinPath, "LoadBehavior") == 2, "An unidentified add-in key was replaced"); passed++;

    var update = new OwnedRegistration.MemoryHive();
    update.SetValue(clsidPath, "", "Contoso.Mail.AddIn", false);
    update.SetValue(clsidPath, OwnedRegistration.OwnerValue, "Contoso", false);
    update.SetValue(@"Software\Classes\CLSID\{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}", "", "Other.AddIn", false);
    string prompt = OwnedRegistration.FormatReinstallPrompt(OwnedRegistration.FindConflicts(update, "64-bit", false), false);
    Assert(prompt.Contains("Remove the installed version and continue?") && prompt.Contains(clsidPath) && !prompt.Contains("conflict"), "Reinstall prompt still looks like a hard conflict error");
    OwnedRegistration.Replace(new[] { new OwnedRegistration.HiveSlot { Hive = update, ViewName = "64-bit", IncludeWowNode = false } }, facts, true);
    Assert(Registered(update), "Confirmed reinstall did not replace the installed registration");
    Assert((string)update.GetValue(@"Software\Classes\CLSID\{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}", "") == "Other.AddIn", "Confirmed reinstall removed an unrelated class"); passed++;

    var resiliency = new OwnedRegistration.MemoryHive();
    string disabled = @"Software\Microsoft\Office\16.0\Outlook\Resiliency\DisabledItems";
    string crashing = @"Software\Microsoft\Office\16.0\Outlook\Resiliency\CrashingAddinList";
    byte[] ourDisabled = Encoding.Unicode.GetBytes("\u0001\u0000\u0000\u0000C:\\Users\\Sam\\AppData\\Local\\LocalCorrect\\Outlook\\LocalCorrect.Outlook.dll\u0000LocalCorrect.Outlook.AddIn");
    byte[] otherDisabled = Encoding.Unicode.GetBytes("C:\\Program Files\\Contoso\\Contoso.Outlook.dll");
    resiliency.SetValue(disabled, "1", ourDisabled, false);
    resiliency.SetValue(disabled, "2", otherDisabled, false);
    resiliency.SetValue(crashing, @"c:\users\sam\appdata\local\localcorrect\outlook\localcorrect.outlook.dll", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, false);
    resiliency.SetValue(crashing, @"C:\Program Files\Contoso\Contoso.Outlook.dll", new byte[] { 1 }, false);
    resiliency.SetValue(@"Software\Microsoft\Office\15.0\Outlook\Resiliency\DisabledItems", "9", Encoding.ASCII.GetBytes("LocalCorrect.Outlook.AddIn"), false);
    OwnedRegistration.ClearResiliency(resiliency);
    Assert(resiliency.GetValue(disabled, "1") == null && resiliency.GetValue(crashing, @"c:\users\sam\appdata\local\localcorrect\outlook\localcorrect.outlook.dll") == null, "Outlook resiliency entries for LocalCorrect were kept");
    Assert(resiliency.GetValue(disabled, "2") != null && resiliency.GetValue(crashing, @"C:\Program Files\Contoso\Contoso.Outlook.dll") != null, "Another product's resiliency entry was removed");
    Assert(resiliency.GetValue(@"Software\Microsoft\Office\15.0\Outlook\Resiliency\DisabledItems", "9") == null, "Older Office resiliency entry for LocalCorrect was kept"); passed++;

    Console.WriteLine($"PASS {passed} installer filesystem and registration ownership cases");
} finally { Directory.Delete(testRoot, true); }

sealed class WowAliasHive : OwnedRegistration.IHive {
    readonly OwnedRegistration.MemoryHive native, bit32;
    public WowAliasHive(OwnedRegistration.MemoryHive native, OwnedRegistration.MemoryHive bit32) { this.native = native; this.bit32 = bit32; }
    // HKCU\Software\Classes\Wow6432Node is the physical 32-bit Classes view.
    OwnedRegistration.IHive Map(string path, out string mapped) {
        const string root = @"Software\Classes\Wow6432Node";
        if (path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) {
            string rest = path.Length == root.Length ? "" : path.Substring(root.Length + 1);
            mapped = @"Software\Classes" + (rest.Length == 0 ? "" : "\\" + rest);
            return bit32;
        }
        mapped = path; return native;
    }
    public bool Exists(string path) { string mapped; return Map(path, out mapped).Exists(mapped); }
    public object GetValue(string path, string name) { string mapped; return Map(path, out mapped).GetValue(mapped, name); }
    public string[] GetValueNames(string path) { string mapped; return Map(path, out mapped).GetValueNames(mapped); }
    public void SetValue(string path, string name, object value, bool dword) { string mapped; Map(path, out mapped).SetValue(mapped, name, value, dword); }
    public void DeleteValue(string path, string name) { string mapped; Map(path, out mapped).DeleteValue(mapped, name); }
    public void DeleteTree(string path) { string mapped; Map(path, out mapped).DeleteTree(mapped); }
}
