// Runs every case recorded from the browser engine (parity.tsv) through the C# port and
// requires identical language, output and edits. Also checks load time and speed.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using LocalCorrect;

static class Program {
    static string Decode(string s) { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
    static int Main() {
        var clock = Stopwatch.StartNew();
        var corrector = Corrector.FromEmbeddedData();
        Console.WriteLine($"load {clock.ElapsedMilliseconds} ms");
        int failed = 0, total = 0;
        foreach (var line in File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "parity.tsv"))) {
            if (line.Length == 0) continue;
            var f = line.Split('\t');
            string input = Decode(f[1]);
            var r = corrector.Correct(input, f[0]);
            string edits = string.Join(",", r.Edits.Select(e => $"{e.Offset}:{e.Length}:{Convert.ToBase64String(Encoding.UTF8.GetBytes(e.Replacement))}"));
            total++;
            if (r.Language != f[2] || r.Text != Decode(f[3]) || edits != (f.Length > 4 ? f[4] : "")) {
                if (++failed <= 10) Console.WriteLine($"MISMATCH [{f[0]}] {input}\n  js: {Decode(f[3])} ({f[2]})\n  c#: {r.Text} ({r.Language})");
            }
        }
        Console.WriteLine($"parity {total - failed}/{total}");
        foreach (var (text, want) in new[] {
            ("Hi,\nteh draft.\n\nFrom: Anna <a@b.c>\nSent: Monday\nTo: me\nSubject: x\n\nteh old text", 16),
            ("Hallo,\nDanke.\n\nVon: Anna\nGesendet: Montag\nAn: ich\n\nalt", 15),
            ("ok\n-----Original Message-----\nold", 3),
            ("ok\nAm 12.11.2026 um 10:00 schrieb Anna Muster <a@b.c>:\n> alt", 3),
            ("ok\nOn Mon, Nov 12, 2026 at 10:00 AM Anna wrote:\n> old", 3),
            ("From now on we meet weekly.\nSent from my phone", -1),
            ("No reply here, just prose.", -1),
        }) {
            total++;
            int got = Mail.ReplyStart(text);
            if (got != want) { failed++; Console.WriteLine($"REPLY {got} != {want}: {text}"); }
        }
        var email = string.Concat(Enumerable.Repeat("Hallo zusammen, ich habe die Unterlagen gestren bekomen und schike sie morgne weiter. ", 80));
        clock.Restart(); corrector.Correct(email); long ms = clock.ElapsedMilliseconds;
        Console.WriteLine($"1,000-word email {ms} ms");
        if (ms > 2000) { Console.WriteLine("FAIL too slow"); failed++; }
        return failed == 0 ? 0 : 1;
    }
}
