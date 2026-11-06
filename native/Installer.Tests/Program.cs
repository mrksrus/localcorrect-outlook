using System;
using System.IO;
using System.Linq;
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
    foreach (var version in new[] { "v14.44.35229.0", "14.51.36247.0", " v14.44.0 " })
        Assert(VisualCppPrerequisite.IsSupported(1, version, _ => true), "Supported x64 VC runtime was refused");
    foreach (var version in new object[] { null, "", "not a version", "v14.43.99999.0", "13.99.0", "15.0.0", 1444 })
        Assert(!VisualCppPrerequisite.IsSupported(1, version, _ => true), "Missing, malformed or unsupported VC version was accepted");
    foreach (var installed in new object[] { null, 0, 2, "1", true })
        Assert(!VisualCppPrerequisite.IsSupported(installed, "14.44.35229.0", _ => true), "Missing or invalid VC installed state was accepted");
    foreach (string missingDll in VisualCppPrerequisite.SystemDlls)
        Assert(!VisualCppPrerequisite.IsSupported(1, "14.44.35229.0", name => name != missingDll), "A missing system VC DLL was accepted");
    Assert(VisualCppPrerequisite.MissingMessage.Contains(VisualCppPrerequisite.DownloadUrl) && VisualCppPrerequisite.MissingMessage.Contains("x64"), "VC prerequisite guidance lost its official URL or architecture");
    Console.WriteLine("PASS VC prerequisite supported versions, invalid/missing registry data and each missing system DLL");

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
    Console.WriteLine($"PASS {passed} installer filesystem ownership/retry/reinstall cases");
} finally { Directory.Delete(testRoot, true); }
