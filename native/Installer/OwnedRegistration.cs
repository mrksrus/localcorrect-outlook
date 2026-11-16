using System;
using System.Collections.Generic;
using System.Text;

namespace LocalCorrect.Setup {
    // HKCU\Software\Classes\CLSID is a different key in the 32-bit and 64-bit
    // views. The ProgId, Outlook add-in and uninstall keys are shared. Every
    // native installer since 1.0.0-rc.2 writes LocalCorrectOwner. A key with
    // that stamp, or with LocalCorrect's own ProgId, CLSID or display name,
    // belongs to LocalCorrect and may be replaced. Any other key is left as it is.
    internal static class OwnedRegistration {
        internal const string Clsid = "{708F01D3-893E-43D0-8215-61722BC46F23}";
        internal const string ProgId = "LocalCorrect.Outlook.AddIn";
        internal const string Owner = "LocalCorrect.Outlook/708F01D3-893E-43D0-8215-61722BC46F23";
        internal const string ProductVersion = "2.0.0-rc.4";
        internal const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalCorrect.Outlook";
        internal const string OwnerValue = "LocalCorrectOwner";
        internal interface IHive {
            bool Exists(string path);
            object GetValue(string path, string name);
            string[] GetValueNames(string path);
            void SetValue(string path, string name, object value, bool dword);
            void DeleteValue(string path, string name);
            void DeleteTree(string path);
        }
        internal sealed class HiveSlot {
            public IHive Hive;
            public string ViewName;
            public bool IncludeWowNode;
        }
        internal sealed class InstallFacts {
            public string Version, InstallLocation, UninstallString, AssemblyFullName, CodeBase;
        }
        internal sealed class Conflict {
            public string ViewName, Path, Detail;
        }
        internal sealed class MemoryHive : IHive {
            readonly Dictionary<string, Dictionary<string, object>> keys = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
            static string Norm(string path) { return (path ?? "").Replace('/', '\\').Trim('\\'); }
            public bool Exists(string path) {
                path = Norm(path);
                if (keys.ContainsKey(path)) return true;
                string prefix = path + "\\";
                foreach (string key in keys.Keys)
                    if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
            public object GetValue(string path, string name) {
                Dictionary<string, object> values;
                if (!keys.TryGetValue(Norm(path), out values)) return null;
                object value;
                values.TryGetValue(name ?? "", out value);
                return value;
            }
            public string[] GetValueNames(string path) {
                Dictionary<string, object> values;
                if (!keys.TryGetValue(Norm(path), out values)) return new string[0];
                var names = new string[values.Count];
                values.Keys.CopyTo(names, 0);
                return names;
            }
            public void DeleteValue(string path, string name) {
                Dictionary<string, object> values;
                if (keys.TryGetValue(Norm(path), out values)) values.Remove(name ?? "");
            }
            public void SetValue(string path, string name, object value, bool dword) {
                path = Norm(path);
                Dictionary<string, object> values;
                if (!keys.TryGetValue(path, out values)) keys[path] = values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                values[name ?? ""] = dword ? (object)Convert.ToInt32(value) : value;
            }
            public void DeleteTree(string path) {
                path = Norm(path);
                var dead = new List<string>();
                foreach (string key in keys.Keys)
                    if (key.Equals(path, StringComparison.OrdinalIgnoreCase) || key.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase)) dead.Add(key);
                foreach (string key in dead) keys.Remove(key);
            }
        }
        internal static void Replace(IReadOnlyList<HiveSlot> slots, InstallFacts facts) { Replace(slots, facts, false); }
        // replaceExisting is the user's "remove the installed version and continue".
        // It deletes only LocalCorrect's own registration paths, then writes them again.
        internal static void Replace(IReadOnlyList<HiveSlot> slots, InstallFacts facts, bool replaceExisting) {
            var conflicts = new List<Conflict>();
            foreach (var slot in slots) conflicts.AddRange(FindConflicts(slot.Hive, slot.ViewName, slot.IncludeWowNode));
            if (conflicts.Count != 0 && !replaceExisting) throw new InvalidOperationException(FormatConflict(conflicts));
            // Remove keys before writing. On 64-bit Windows the literal
            // Wow6432Node class key is the 32-bit view, so deleting it after
            // the 32-bit view was rewritten would erase the new registration.
            foreach (var slot in slots) RemoveOwned(slot.Hive, slot.IncludeWowNode);
            if (replaceExisting) foreach (var slot in slots) RemoveRegistration(slot.Hive, slot.IncludeWowNode);
            foreach (var slot in slots) Write(slot.Hive, facts);
            foreach (var slot in slots) ClearResiliency(slot.Hive);
        }
        internal static string FormatReinstallPrompt(IReadOnlyList<Conflict> conflicts, bool folderUnverified) {
            var text = new StringBuilder("LocalCorrect is already installed for this Windows user.\n\nRemove the installed version and continue?");
            foreach (Conflict conflict in conflicts) {
                text.Append("\n\nHKCU ");
                text.Append(conflict.ViewName);
                text.Append(" ");
                text.Append(conflict.Path);
            }
            if (folderUnverified) text.Append("\n\nThe installation folder could not be verified. It will be renamed and kept, not deleted.");
            return text.ToString();
        }
        internal static void RemoveRegistration(IHive hive, bool includeWowNode) {
            foreach (string path in Paths(includeWowNode)) if (hive.Exists(path)) hive.DeleteTree(path);
        }
        // Outlook records a startup failure under Office\16.0\Outlook\Resiliency
        // and then refuses to load the add-in. Remove only entries that name
        // our DLL or ProgId. Other products' disabled items stay.
        internal static void ClearResiliency(IHive hive) {
            try { ClearResiliencyCore(hive); } catch (Exception) { }
        }
        static void ClearResiliencyCore(IHive hive) {
            foreach (string version in new[] { "14.0", "15.0", "16.0" }) {
                string root = @"Software\Microsoft\Office\" + version + @"\Outlook\Resiliency\";
                RemoveOurValues(hive, root + "DisabledItems");
                RemoveOurValues(hive, root + "CrashingAddinList");
            }
        }
        static void RemoveOurValues(IHive hive, string path) {
            if (!hive.Exists(path)) return;
            foreach (string name in hive.GetValueNames(path))
                if (PointsAtUs(name, hive.GetValue(path, name))) hive.DeleteValue(path, name);
        }
        static bool PointsAtUs(string name, object data) {
            if (Hits(name)) return true;
            var text = data as string;
            if (text != null && Hits(text)) return true;
            var bytes = data as byte[];
            if (bytes == null) return false;
            return Hits(Encoding.Unicode.GetString(bytes)) || Hits(Encoding.ASCII.GetString(bytes));
        }
        static bool Hits(string text) {
            if (string.IsNullOrEmpty(text)) return false;
            return text.IndexOf("localcorrect.outlook.dll", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("localcorrect.outlook.addin", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        internal static List<Conflict> FindConflicts(IHive hive, string viewName, bool includeWowNode) {
            var found = new List<Conflict>();
            foreach (string path in Paths(includeWowNode)) {
                Conflict conflict = Classify(hive, viewName, path);
                if (conflict != null) found.Add(conflict);
            }
            return found;
        }
        internal static void RemoveOwned(IHive hive, bool includeWowNode) {
            foreach (string path in Paths(includeWowNode))
                if (hive.Exists(path) && Classify(hive, "", path) == null) hive.DeleteTree(path);
        }
        internal static void Write(IHive hive, InstallFacts facts) {
            string clsid = @"Software\Classes\CLSID\" + Clsid;
            hive.SetValue(clsid, "", ProgId, false);
            hive.SetValue(clsid, OwnerValue, Owner, false);
            string inproc = clsid + @"\InprocServer32";
            hive.SetValue(inproc, "", "mscoree.dll", false);
            hive.SetValue(inproc, "ThreadingModel", "Both", false);
            hive.SetValue(inproc, "Class", ProgId, false);
            hive.SetValue(inproc, "Assembly", facts.AssemblyFullName, false);
            hive.SetValue(inproc, "RuntimeVersion", "v4.0.30319", false);
            hive.SetValue(inproc, "CodeBase", facts.CodeBase, false);
            hive.SetValue(clsid + @"\ProgId", "", ProgId, false);
            string prog = @"Software\Classes\" + ProgId;
            hive.SetValue(prog, OwnerValue, Owner, false);
            hive.SetValue(prog + @"\CLSID", "", Clsid, false);
            string addin = @"Software\Microsoft\Office\Outlook\Addins\" + ProgId;
            hive.SetValue(addin, OwnerValue, Owner, false);
            hive.SetValue(addin, "FriendlyName", "LocalCorrect offline EN/DE", false);
            hive.SetValue(addin, "Description", "Offline, user-triggered selected-prose correction", false);
            hive.SetValue(addin, "LoadBehavior", 3, true);
            hive.SetValue(UninstallKey, OwnerValue, Owner, false);
            hive.SetValue(UninstallKey, "DisplayName", "LocalCorrect for classic Outlook", false);
            hive.SetValue(UninstallKey, "DisplayVersion", facts.Version, false);
            hive.SetValue(UninstallKey, "Publisher", "LocalCorrect contributors", false);
            hive.SetValue(UninstallKey, "InstallLocation", facts.InstallLocation, false);
            hive.SetValue(UninstallKey, "UninstallString", facts.UninstallString, false);
            hive.SetValue(UninstallKey, "NoModify", 1, true);
            hive.SetValue(UninstallKey, "NoRepair", 1, true);
        }
        internal static string FormatConflict(IReadOnlyList<Conflict> conflicts) {
            var text = new StringBuilder("A conflicting LocalCorrect registration exists. No registry keys were changed.");
            foreach (Conflict conflict in conflicts) {
                text.Append("\n\nHKCU ");
                text.Append(conflict.ViewName);
                text.Append(" ");
                text.Append(conflict.Path);
                text.Append("\n");
                text.Append(conflict.Detail);
            }
            text.Append("\n\nSetup replaces a registration only when it belongs to LocalCorrect. If this key is a leftover from an earlier LocalCorrect install, delete it and run setup again. If it belongs to another product, leave it.");
            return text.ToString();
        }
        static IEnumerable<string> Paths(bool includeWowNode) {
            yield return @"Software\Classes\CLSID\" + Clsid;
            yield return @"Software\Classes\" + ProgId;
            yield return @"Software\Microsoft\Office\Outlook\Addins\" + ProgId;
            yield return UninstallKey;
            if (!includeWowNode) yield break;
            yield return @"Software\Classes\Wow6432Node\CLSID\" + Clsid;
            yield return @"Software\Classes\Wow6432Node\" + ProgId;
        }
        // null means the key is absent or ours. A Conflict means it must be left alone.
        static Conflict Classify(IHive hive, string viewName, string path) {
            if (!hive.Exists(path)) return null;
            string owner = AsString(hive.GetValue(path, OwnerValue));
            if (owner == Owner) return null;
            if (owner != null) return new Conflict { ViewName = viewName, Path = path, Detail = "LocalCorrectOwner is " + Shown(owner) + ", which is not LocalCorrect's." };
            if (StructurallyOurs(hive, path)) return null;
            return new Conflict { ViewName = viewName, Path = path, Detail = "LocalCorrectOwner is absent, and the key is not a LocalCorrect registration." };
        }
        static bool StructurallyOurs(IHive hive, string path) {
            string clsid = @"Software\Classes\CLSID\" + Clsid;
            string wowClsid = @"Software\Classes\Wow6432Node\CLSID\" + Clsid;
            if (path.Equals(clsid, StringComparison.OrdinalIgnoreCase) || path.Equals(wowClsid, StringComparison.OrdinalIgnoreCase)) {
                if (AsString(hive.GetValue(path, "")) == ProgId) return true;
                string inproc = path + @"\InprocServer32";
                if (AsString(hive.GetValue(inproc, "Class")) == ProgId) return true;
                string codeBase = AsString(hive.GetValue(inproc, "CodeBase"));
                return codeBase != null && codeBase.IndexOf("LocalCorrect.Outlook.dll", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            string prog = @"Software\Classes\" + ProgId;
            string wowProg = @"Software\Classes\Wow6432Node\" + ProgId;
            if (path.Equals(prog, StringComparison.OrdinalIgnoreCase) || path.Equals(wowProg, StringComparison.OrdinalIgnoreCase)) {
                string id = AsString(hive.GetValue(path + @"\CLSID", ""));
                return id != null && id.Equals(Clsid, StringComparison.OrdinalIgnoreCase);
            }
            string addin = @"Software\Microsoft\Office\Outlook\Addins\" + ProgId;
            if (path.Equals(addin, StringComparison.OrdinalIgnoreCase)) {
                if (AsString(hive.GetValue(path, "FriendlyName")) == "LocalCorrect offline EN/DE") return true;
                return AsString(hive.GetValue(path, "Description")) == "Offline, user-triggered selected-prose correction";
            }
            if (path.Equals(UninstallKey, StringComparison.OrdinalIgnoreCase)) {
                if (AsString(hive.GetValue(path, "DisplayName")) == "LocalCorrect for classic Outlook") return true;
                string command = AsString(hive.GetValue(path, "UninstallString"));
                return command != null && command.IndexOf("LocalCorrect-Outlook-Setup.exe", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            return false;
        }
        static string AsString(object value) { return value as string; }
        static string Shown(string value) {
            string text = value.Replace('\r', ' ').Replace('\n', ' ');
            if (text.Length > 80) text = text.Substring(0, 80) + "...";
            return "\"" + text + "\"";
        }
    }
}
