using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LocalCorrect.Outlook {
    [ComVisible(true), Guid("708F01D3-893E-43D0-8215-61722BC46F23"), ProgId("LocalCorrect.Outlook.AddIn"), ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class AddIn : IDTExtensibility2, IRibbonExtensibility {
        dynamic application;
        Settings settings;
        HotkeyWindow window;
        Timer timer;
        Corrector corrector;
        AppDomain engineDomain;
        readonly object engineGate = new object();
        bool busy;
        volatile bool disconnected;
        public void OnConnection(object app, int connectMode, object addInInst, ref Array custom) {
            application = app; settings = Settings.Load(); disconnected = false;
            window = new HotkeyWindow(() => Correct(true));
            timer = new Timer { Interval = 250 };
            timer.Tick += (s, e) => UpdateHotkey(); timer.Start();
            // Initialize static dictionaries off the Office UI thread. No email
            // is inspected. A first action waits for this same serialized worker.
            Task.Run(() => { lock (engineGate) { if (!disconnected) { try { EnsureEngine(); } catch { ResetEngine(); } } } });
        }
        public void OnDisconnection(int removeMode, ref Array custom) {
            disconnected = true; timer?.Stop(); timer?.Dispose(); window?.Dispose(); application = null;
            Task.Run(() => { lock (engineGate) { ResetEngine(); } });
        }
        public void OnBeginShutdown(ref Array custom) { OnDisconnection(0, ref custom); }
        public void OnAddInsUpdate(ref Array custom) { }
        public void OnStartupComplete(ref Array custom) { }
        public string GetCustomUI(string ribbonId) {
            if (ribbonId != "Microsoft.Outlook.Mail.Compose") return null;
            return @"<customUI xmlns='http://schemas.microsoft.com/office/2009/07/customui'><ribbon><tabs><tab id='localCorrectTab' label='LocalCorrect'><group id='localCorrectGroup' label='Offline EN / DE'><button id='lcCorrect' label='Correct selection / body' size='large' onAction='CorrectAction' screentip='Correct selected prose, or the editable body when nothing is selected'/><button id='lcSettings' label='Settings / Einstellungen' onAction='SettingsAction'/></group></tab></tabs></ribbon></customUI>";
        }
        public void CorrectAction(object control) { Correct(false); }
        public void SettingsAction(object control) { window?.Unregister(); settings.Show(); UpdateHotkey(); }
        void UpdateHotkey() {
            try { dynamic inspector, doc; IntPtr hwnd; bool active = TryTarget(out inspector, out doc, out hwnd); window?.Update(active && !busy && HotkeyWindow.IsEditorFocused(hwnd), settings); }
            catch { window?.Unregister(); }
        }
        bool TryTarget(out dynamic inspector, out dynamic doc, out IntPtr hwnd) {
            inspector = null; doc = null; hwnd = IntPtr.Zero;
            if (disconnected || application == null) return false;
            inspector = application.ActiveInspector();
            if (inspector == null) return false;
            ((IOleWindow)inspector).GetWindow(out hwnd);
            if (hwnd != HotkeyWindow.GetForegroundWindow()) return false;
            dynamic item = inspector.CurrentItem;
            if ((int)item.Class != 43 || (bool)item.Sent || !(bool)inspector.IsWordMail()) return false;
            doc = inspector.WordEditor;
            return doc != null && !(bool)doc.ReadOnly && (int)doc.ProtectionType == -1 && !(bool)doc.TrackRevisions;
        }
        static string Normalized(string text) { return text.Replace('\r', '\n'); }
        async void Correct(bool fromShortcut) {
            if (busy || disconnected) return;
            try {
                dynamic inspector, doc; IntPtr hwnd;
                if (!TryTarget(out inspector, out doc, out hwnd)) { if (!fromShortcut) Info("Open an editable email in its own compose window first. Tracked or protected documents are excluded."); return; }
                if (fromShortcut && !HotkeyWindow.IsEditorFocused(hwnd)) return;
                dynamic selection = doc.Application.Selection;
                if (!Object.ReferenceEquals(selection.Document, doc) && !SameComObject(selection.Document, doc)) throw new InvalidOperationException("The email editor does not own the selection.");
                dynamic target = selection.Range.Duplicate;
                if ((int)target.Start == (int)target.End) target = doc.Content.Duplicate;
                if ((int)target.StoryType != 1) throw new InvalidOperationException("Select prose in the email body.");
                int start = (int)target.Start, end = (int)target.End;
                int selectionStart = (int)selection.Start, selectionEnd = (int)selection.End;
                string source = (string)target.Text ?? "";
                if (source.Length > 20000) throw new InvalidOperationException("Select at most 20,000 characters.");
                if (source.Length != end - start) throw new InvalidOperationException("This editor's character mapping is unsupported. Select a smaller plain-text passage.");
                string documentBefore = (string)doc.Content.Text;
                string language = settings.Language, mode = settings.Mode;
                busy = true; window.Unregister();
                // No Office COM object crosses to the worker. All editor operations stay on the Office UI thread.
                var result = await RunEngine(source, language, mode, documentBefore, start - (int)doc.Content.Start);
                if (disconnected) return;
                dynamic currentInspector, currentDoc; IntPtr currentHwnd;
                if (!TryTarget(out currentInspector, out currentDoc, out currentHwnd) || currentHwnd != hwnd || (fromShortcut && !HotkeyWindow.IsEditorFocused(hwnd)) || !SameComObject(doc, currentDoc) || (string)doc.Content.Text != documentBefore || (int)doc.Application.Selection.Start != selectionStart || (int)doc.Application.Selection.End != selectionEnd)
                    throw new InvalidOperationException("The email or selection changed. Nothing was applied. Run correction again.");
                var accepted = new List<Edit>();
                foreach (var edit in result.edits) {
                    dynamic range = doc.Range(start + edit.offset, start + edit.offset + edit.length);
                    if ((string)range.Text != edit.original) throw new InvalidOperationException("The editor text mapping changed. Nothing was applied.");
                    if (!Protected(doc, range)) accepted.Add(edit);
                }
                if (accepted.Count == 0) { Info("No safe corrections found. Valid words, names, links, signatures and quoted text are kept."); return; }
                dynamic undo = doc.Application.UndoRecord;
                // Fail closed if this host cannot provide a single undo record.
                if ((int)undo.CustomRecordLevel != 0) throw new InvalidOperationException("Another editor action is in progress. Try again.");
                undo.StartCustomRecord("LocalCorrect");
                bool changed = false;
                try {
                    for (int i = accepted.Count - 1; i >= 0; i--) {
                        var edit = accepted[i]; dynamic range = doc.Range(start + edit.offset, start + edit.offset + edit.length);
                        range.Text = edit.replacement; changed = true;
                    }
                } catch {
                    undo.EndCustomRecord();
                    if (changed) { object count = 1; doc.Undo(ref count); }
                    throw;
                }
                undo.EndCustomRecord();
            } catch (Exception ex) { if (!disconnected) Info("Correction stopped. " + SafeError(ex)); }
            finally { busy = false; if (!disconnected) UpdateHotkey(); }
        }
        void EnsureEngine() {
            if (corrector != null) return;
            var folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var setup = new AppDomainSetup { ApplicationBase = folder, ConfigurationFile = Path.Combine(folder, "LocalCorrect.Outlook.dll.config") };
            engineDomain = AppDomain.CreateDomain("LocalCorrect offline engine", null, setup);
            corrector = (Corrector)engineDomain.CreateInstanceAndUnwrap(typeof(Corrector).Assembly.FullName, typeof(Corrector).FullName);
        }
        void ResetEngine() {
            try { corrector?.Dispose(); } catch { }
            corrector = null; var domain = engineDomain; engineDomain = null;
            if (domain != null) { try { AppDomain.Unload(domain); } catch { } }
        }
        Task<Result> RunEngine(string source, string language, string mode, string context, int offset) {
            // WinForms context keeps the caller's continuation on the Office UI
            // thread. Only immutable strings enter the serialized engine worker.
            if (System.Threading.SynchronizationContext.Current == null)
                System.Threading.SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            return Task.Run(() => {
                lock (engineGate) {
                    try {
                        if (disconnected) throw new OperationCanceledException();
                        EnsureEngine();
                        if (disconnected) throw new OperationCanceledException();
                        return corrector.Correct(Normalized(source), language, mode, Normalized(context), offset);
                    } catch { ResetEngine(); throw; }
                }
            });
        }
        static bool SameComObject(object a, object b) {
            IntPtr x = IntPtr.Zero, y = IntPtr.Zero;
            try { x = Marshal.GetIUnknownForObject(a); y = Marshal.GetIUnknownForObject(b); return x == y; }
            finally { if (x != IntPtr.Zero) Marshal.Release(x); if (y != IntPtr.Zero) Marshal.Release(y); }
        }
        static bool Protected(dynamic doc, dynamic range) {
            if ((int)range.Fields.Count > 0 || (int)range.Hyperlinks.Count > 0 || (int)range.ContentControls.Count > 0 || (int)range.InlineShapes.Count > 0) return true;
            // Range collections can omit a container when the edit is inside it. Check enclosing document objects too.
            foreach (dynamic field in doc.Fields) if (Overlaps(range, field.Result) || Overlaps(range, field.Code)) return true;
            foreach (dynamic link in doc.Hyperlinks) if (Overlaps(range, link.Range)) return true;
            foreach (dynamic control in doc.ContentControls) if (Overlaps(range, control.Range)) return true;
            // Outlook creates this hidden bookmark for automatic signatures.
            if ((bool)doc.Bookmarks.Exists("_MailAutoSig") && Overlaps(range, doc.Bookmarks["_MailAutoSig"].Range)) return true;
            string font = (string)range.Font.Name ?? "";
            if (font.Length == 0 || font.IndexOf("Consolas", StringComparison.OrdinalIgnoreCase) >= 0 || font.IndexOf("Courier", StringComparison.OrdinalIgnoreCase) >= 0 || font.IndexOf("Mono", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
        static bool Overlaps(dynamic a, dynamic b) { return (int)a.Start < (int)b.End && (int)a.End > (int)b.Start; }
        static string SafeError(Exception ex) {
            // Do not display JS exceptions, which can contain fragments of private input.
            if (ex is InvalidOperationException || ex is ArgumentException) return ex.Message;
            return "The editor or offline engine could not complete this operation. Try a smaller selection. Review the message before sending.";
        }
        static void Info(string text) { MessageBox.Show(text, "LocalCorrect", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }
    internal sealed class HotkeyWindow : Control {
        const int Id = 0x4c43;
        bool registered; uint modifiers; Keys key;
        readonly Action action;
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr GetFocus();
        [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int size);
        internal static bool IsEditorFocused(IntPtr inspector) {
            var focus = GetFocus();
            for (int i = 0; focus != IntPtr.Zero && focus != inspector && i < 16; i++, focus = GetParent(focus)) {
                var name = new System.Text.StringBuilder(128); GetClassName(focus, name, name.Capacity);
                if (name.ToString() == "_WwG") return true;
            }
            return false;
        }
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window, int id);
        internal HotkeyWindow(Action action) { this.action = action; var handle = Handle; }
        internal void Post(Action callback) { if (!IsDisposed) BeginInvoke(callback); }
        internal void Update(bool active, Settings settings) {
            if (!active || modifiers != settings.Modifiers || key != settings.Key) Unregister();
            if (active && !registered) { modifiers = settings.Modifiers; key = settings.Key; registered = RegisterHotKey(Handle, Id, modifiers | 0x4000, (uint)key); }
        }
        internal void Unregister() { if (registered) { UnregisterHotKey(Handle, Id); registered = false; } }
        protected override void WndProc(ref Message m) { if (m.Msg == 0x0312 && m.WParam.ToInt32() == Id) action(); base.WndProc(ref m); }
        protected override void Dispose(bool disposing) { Unregister(); base.Dispose(disposing); }
    }
}
