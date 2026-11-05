using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace LocalCorrect.Setup {
    // Shared with filesystem regression tests. Never delete a file merely because
    // it is under the installation directory: require its recorded hash as well.
    internal static class OwnedInstallation {
        const string Marker = "installation-owner.txt", Inventory = "installed-files.sha256";
        internal static string Hash(string path) {
            using (var sha = SHA256.Create()) using (var file = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }
        static bool Linked(string path) { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        static Dictionary<string, string> Verify(string root, string owner) {
            string marker = Path.Combine(root, Marker), inventory = Path.Combine(root, Inventory);
            if (!Directory.Exists(root) || Linked(root) || !File.Exists(marker) || Linked(marker) || File.ReadAllText(marker) != owner || !File.Exists(inventory) || Linked(inventory))
                throw new InvalidOperationException("Installation ownership could not be verified. No files were removed or moved.");
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadAllLines(inventory)) {
                if (line.Length < 67 || line.Substring(64, 2) != "  " || !Regex.IsMatch(line.Substring(0, 64), "^[a-f0-9]{64}$"))
                    throw new InvalidDataException("The installation inventory is invalid. No files were removed or moved.");
                string relative = line.Substring(66);
                if (Path.IsPathRooted(relative) || relative.Contains(":") || relative.Split('/', '\\').Any(p => p == ".." || p == "." || p.Length == 0))
                    throw new InvalidDataException("The installation inventory contains an unsafe path.");
                string path = Path.GetFullPath(Path.Combine(root, relative));
                if (!path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || files.ContainsKey(path))
                    throw new InvalidDataException("The installation inventory contains an invalid path.");
                files.Add(path, line.Substring(0, 64));
            }
            // Old installers already recorded this marker in their inventory.
            if (!files.TryGetValue(Path.GetFullPath(marker), out var markerHash) || markerHash != Hash(marker))
                throw new InvalidDataException("The installation marker does not match its inventory.");
            return files;
        }
        internal static void VerifyOwnership(string root, string owner) { Verify(root, owner); }
        internal static string ArchiveRetained(string root, string owner) {
            Verify(root, owner);
            string retained = root + ".retained-" + Guid.NewGuid().ToString("N");
            Directory.Move(root, retained);
            return retained;
        }
        internal static void DeleteOwnedFiles(string root, string owner) {
            var files = Verify(root, owner);
            var parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in files) {
                string path = pair.Key;
                if (path == Path.Combine(root, Marker) || path == Path.Combine(root, Inventory)) continue;
                bool linked = false;
                for (string p = path; !p.Equals(root, StringComparison.OrdinalIgnoreCase); p = Path.GetDirectoryName(p)) {
                    if ((File.Exists(p) || Directory.Exists(p)) && Linked(p)) { linked = true; break; }
                    if (p == null) throw new InvalidDataException("Invalid installation path.");
                }
                if (linked) continue;
                if (File.Exists(path) && Hash(path) == pair.Value) File.Delete(path);
                for (string p = Path.GetDirectoryName(path); !p.Equals(root, StringComparison.OrdinalIgnoreCase); p = Path.GetDirectoryName(p)) parents.Add(p);
            }
            // Only prune directories named by the old inventory. Unknown empty
            // directories, files and reparse points are retained too.
            foreach (string parent in parents.OrderByDescending(p => p.Length))
                if (Directory.Exists(parent) && !Linked(parent) && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
            var remaining = Directory.EnumerateFileSystemEntries(root).ToArray();
            if (remaining.All(p => p == Path.Combine(root, Marker) || p == Path.Combine(root, Inventory))) {
                File.Delete(Path.Combine(root, Inventory));
                File.Delete(Path.Combine(root, Marker));
                Directory.Delete(root);
            }
            // With retained content, keep BOTH ownership records unchanged so a
            // later uninstall can retry and a reinstall can archive it safely.
        }
    }
}
