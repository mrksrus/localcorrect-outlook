using System;
using System.Windows.Forms;
using Microsoft.Win32;
namespace LocalCorrect.Outlook {
    internal sealed class Settings {
        const string Path = @"Software\LocalCorrect\Outlook";
        public string Language = "en", Mode = "conservative";
        public bool MixedLanguage = true;
        public Keys Key = Keys.L; public uint Modifiers = 6; // Ctrl + Shift
        public static Settings Load() {
            var s = new Settings();
            try { using (var k = Registry.CurrentUser.OpenSubKey(Path)) if (k != null) {
                s.Language = (string)k.GetValue("Language", "en") == "de" ? "de" : "en";
                s.Mode = (string)k.GetValue("Mode", "conservative") == "balanced" ? "balanced" : "conservative";
                s.MixedLanguage = MixedLanguageSetting.Read(k.GetValue("MixedLanguage"));
                s.Key = (Keys)Convert.ToInt32(k.GetValue("Key", (int)Keys.L));
                s.Modifiers = Convert.ToUInt32(k.GetValue("Modifiers", 6));
                if (s.Key < Keys.A || s.Key > Keys.Z || (s.Modifiers != 6 && s.Modifiers != 3)) { s.Key = Keys.L; s.Modifiers = 6; }
            }
            } catch { return new Settings(); }
            return s;
        }
        public bool Show() {
            using (var f = new Form { Text = "LocalCorrect settings", Width = 450, Height = 365, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterScreen }) {
                var language = new ComboBox { Left = 160, Top = 20, Width = 190, DropDownStyle = ComboBoxStyle.DropDownList }; language.Items.AddRange(new object[] { "English", "Deutsch" }); language.SelectedIndex = Language == "de" ? 1 : 0;
                var mixedLanguage = new CheckBox { Text = "Protect English and German words (mixed text)", Left = 15, Top = 58, Width = 410, Height = 25, Checked = MixedLanguage };
                var mode = new ComboBox { Left = 160, Top = 145, Width = 190, DropDownStyle = ComboBoxStyle.DropDownList }; mode.Items.AddRange(new object[] { "Conservative", "Balanced" }); mode.SelectedIndex = Mode == "balanced" ? 1 : 0;
                var modifiers = new ComboBox { Left = 160, Top = 185, Width = 125, DropDownStyle = ComboBoxStyle.DropDownList }; modifiers.Items.AddRange(new object[] { "Ctrl + Shift", "Ctrl + Alt" }); modifiers.SelectedIndex = Modifiers == 3 ? 1 : 0;
                var key = new ComboBox { Left = 290, Top = 185, Width = 60, DropDownStyle = ComboBoxStyle.DropDownList }; for (int i = 0; i < 26; i++) key.Items.Add(((char)('A' + i)).ToString()); key.SelectedIndex = (int)Key - (int)Keys.A;
                f.Controls.AddRange(new Control[] { new Label { Text = "Language / Sprache", Left = 15, Top = 24, Width = 140 }, mixedLanguage, new Label { Text = "Clear for vocabulary in the selected language only.\nNames, links and quoted text remain protected.", Left = 15, Top = 88, Width = 410, Height = 42 }, new Label { Text = "Correction mode", Left = 15, Top = 149 }, new Label { Text = "Shortcut", Left = 15, Top = 189 }, language, mode, modifiers, key, new Label { Text = "Shortcut corrects the selection, or the editable body.\nOnly active in a popped-out compose window.", Left = 15, Top = 230, Width = 410, Height = 40 } });
                var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Left = 345, Top = 280 }; f.Controls.Add(ok); f.AcceptButton = ok;
                if (f.ShowDialog() != DialogResult.OK) return false;
                Language = language.SelectedIndex == 1 ? "de" : "en"; Mode = mode.SelectedIndex == 1 ? "balanced" : "conservative"; Key = Keys.A + key.SelectedIndex; Modifiers = modifiers.SelectedIndex == 1 ? 3u : 6u;
                MixedLanguage = mixedLanguage.Checked;
                using (var k = Registry.CurrentUser.CreateSubKey(Path)) { k.SetValue("Language", Language); k.SetValue("Mode", Mode); k.SetValue("MixedLanguage", MixedLanguageSetting.Write(MixedLanguage), RegistryValueKind.DWord); k.SetValue("Key", (int)Key); k.SetValue("Modifiers", (int)Modifiers); }
                return true;
            }
        }
    }
}
