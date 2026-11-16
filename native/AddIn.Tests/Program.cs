using System;
using System.IO;
using System.Threading;
using LocalCorrect;
using LocalCorrect.Outlook;

// Runs under .NET Framework 4.8 on Windows CI, the same runtime Outlook hosts.
// An access violation in clr.dll (0x80131506) kills this process and fails the build.
class Program {
    [STAThread]
    static int Main() {
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

        string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalCorrect", "Outlook", "log.txt");
        try { if (File.Exists(log)) File.Delete(log); } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        var addin = new AddIn();
        Array custom = null;
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
        if (seen.IndexOf("OnConnection returned", StringComparison.Ordinal) < 0) { Console.Error.WriteLine(seen); return 4; }
        if (seen.IndexOf("OnStartupComplete returned", StringComparison.Ordinal) < 0) { Console.Error.WriteLine(seen); return 5; }
        if (seen.IndexOf("word lists ready", StringComparison.Ordinal) < 0) { Console.Error.WriteLine(seen); return 6; }
        addin.OnDisconnection(0, ref custom);
        Console.WriteLine("PASS add-in load and correction under this runtime");
        return 0;
    }
}
