// LocalCorrect engine, C# port of extension/corrector.js from localcorrect-browser (pinned in
// engine-source.json). Keep the two in step: tests/parity.json holds the JS outputs and
// Engine.Tests requires identical results. Plain text in, edits out; no Office types.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace LocalCorrect {
    public sealed class Edit {
        public int Offset, Length;
        public string Original, Replacement;
    }
    public sealed class Result {
        public string Text, Language;
        public List<Edit> Edits;
    }

    public sealed class Corrector {
        sealed class Lang {
            public readonly Dictionary<string, long> Counts = new Dictionary<string, long>(StringComparer.Ordinal);
            public readonly Dictionary<string, List<string>> Forms = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            public string Alphabet;
        }
        const RegexOptions U = RegexOptions.CultureInvariant;
        static readonly string[][] Umlauts = { new[] { "ae", "ä" }, new[] { "oe", "ö" }, new[] { "ue", "ü" }, new[] { "ss", "ß" }, new[] { "a", "ä" }, new[] { "o", "ö" }, new[] { "u", "ü" } };
        static readonly Dictionary<string, string> Contractions = BuildContractions();
        static readonly HashSet<string> GermanColloquial = new HashSet<string>("gehts gibts wars klappts passts stimmts hats gings machts ists".Split(' '));
        static readonly HashSet<string> Tech = new HashSet<string>(("npm npx dev devs repo repos config configs api apis cli url urls auth env prod admin app apps " +
            "sql sudo git github gitlab yaml json http https async todo todos bugfix hotfix frontend backend localhost webhook " +
            "webhooks refactor refactoring changelog codebase docker kubernetes linux macos ios wifi online offline login logout " +
            "email emails inbox spam blog vlog podcast smartphone laptop tablet browser chatbot").Split(' '));
        // JS "$" at the end of these patterns means end of input: \z here. JS \d (with /u) is ASCII only.
        static readonly Regex Titles = new Regex(@"(?:Herr|Herrn|Frau|Dr|Prof|Mr|Mrs|Ms|Dear|Liebe|Lieber|Hallo|Hi|Hello)\.?\s+\z", U);
        static readonly Regex Skip = new Regex(@"(?:[a-z][a-z0-9+.-]*://|www\.)\S+|(?<!\S)--?\p{L}[\p{L}0-9-]*|[^\s@]+@\S+|[@#$%][\p{L}0-9_]+|\S*[0-9_\\/]\S*|[\p{L}0-9-]+(?:\.[\p{L}0-9-]+)+", U | RegexOptions.IgnoreCase);
        static readonly Regex Quoted = new Regex(@"^[ \t]*>[^\n\r  ]*", U | RegexOptions.Multiline);
        static readonly Regex Word = new Regex(@"[\p{L}\p{M}]+(?:['’][\p{L}]+)?", U);
        static readonly Regex LoneI = new Regex(@"(?<![\p{L}'’.-])i(?![\p{L}'’.-])", U);
        static readonly Regex SentenceEnd = new Regex(@"(?:[.!?:]\s+|\n\s*)\z", U);
        static readonly Regex Upper = new Regex(@"\p{Lu}", U);

        readonly Dictionary<string, Lang> lang = new Dictionary<string, Lang>();

        static Dictionary<string, string> BuildContractions() {
            var map = new Dictionary<string, string>();
            foreach (var w in "don't can't i'm i've i'll i'd isn't didn't doesn't wouldn't couldn't shouldn't haven't hasn't hadn't wasn't weren't aren't you're they're that's what's there's here's it'll you've we've they've you'll they'll let's who's won't ain't".Split(' '))
                map[w.Replace("'", "")] = w;
            foreach (var k in new[] { "wont", "lets", "ill", "id", "well" }) map.Remove(k);
            return map;
        }

        public static Corrector FromEmbeddedData() {
            var assembly = typeof(Corrector).Assembly;
            string Read(string name) {
                using (var s = assembly.GetManifestResourceStream("LocalCorrect." + name) ?? throw new InvalidOperationException("Missing word list " + name))
                using (var r = new StreamReader(s)) return r.ReadToEnd();
            }
            return new Corrector(Read("en.lex"), Read("de.lex"));
        }

        public Corrector(string en, string de) {
            lang["en"] = Parse(en, "abcdefghijklmnopqrstuvwxyz");
            lang["de"] = Parse(de, "abcdefghijklmnopqrstuvwxyzäöüß");
        }
        static string Lower(string s) { return s.ToLowerInvariant(); }
        static string UpperJs(string s) { return s.ToUpperInvariant().Replace("ß", "SS"); } // JS maps ß to SS
        static Lang Parse(string source, string alphabet) {
            var L = new Lang { Alphabet = alphabet };
            foreach (var line in source.Split('\n')) {
                int space = line.IndexOf(' ');
                if (space < 1) continue;
                string word = line.Substring(0, space), lower = Lower(word);
                long count = long.Parse(line.Substring(space + 1).Trim(), CultureInfo.InvariantCulture);
                long old; L.Counts[lower] = L.Counts.TryGetValue(lower, out old) ? Math.Max(old, count) : count;
                List<string> list;
                if (L.Forms.TryGetValue(lower, out list)) list.Add(word); else L.Forms[lower] = new List<string> { word };
            }
            return L;
        }

        bool Known(Lang L, string word) {
            List<string> list;
            if (!L.Forms.TryGetValue(Lower(word), out list)) return false;
            if (list.Contains(word) || word == UpperJs(word)) return true;
            string decap = char.ToLowerInvariant(word[0]) + word.Substring(1);
            return word != decap && list.Contains(decap);
        }
        bool Compound(Lang L, string lower, int depth = 0) {
            if (lower.Length < 8 || depth > 3) return false;
            for (int i = lower.Length - 3; i >= 3; i--) {
                string head = lower.Substring(0, i), tail = lower.Substring(i);
                bool headOk = L.Counts.ContainsKey(head) || (head[head.Length - 1] == 's' && L.Counts.ContainsKey(head.Substring(0, head.Length - 1)));
                if (headOk && (L.Counts.ContainsKey(tail) || Compound(L, tail, depth + 1))) return true;
            }
            return false;
        }
        bool Valid(string code, string word) {
            string lw = Lower(word);
            return Known(lang[code], word) || Tech.Contains(lw) ||
                (code == "de" && (GermanColloquial.Contains(lw) || Compound(lang["de"], lw)));
        }

        // Insertion-ordered like a JS Set: ties go to the candidate generated first.
        static List<string> Edits1(string word, string alphabet) {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var order = new List<string>();
            void Add(string s) { if (seen.Add(s)) order.Add(s); }
            for (int i = 0; i <= word.Length; i++) {
                string a = word.Substring(0, i), b = word.Substring(i);
                if (b.Length > 0) Add(a + b.Substring(1));
                if (b.Length > 1) Add(a + b[1] + b[0] + b.Substring(2));
                foreach (char c in alphabet) { if (b.Length > 0) Add(a + c + b.Substring(1)); Add(a + c + b); }
            }
            order.Remove(word);
            return order;
        }
        static bool Swapped(string a, string b) {
            if (a.Length != b.Length) return false;
            var x = a.ToCharArray(); var y = b.ToCharArray();
            Array.Sort(x); Array.Sort(y); // ordinal, like JS string sort of single letters
            return new string(x) == new string(y);
        }
        static string MostFrequent(Lang L, IEnumerable<string> words, long minimum = 1, string typed = "") {
            string top = null; long best = minimum - 1;
            foreach (var w in words) {
                long count; L.Counts.TryGetValue(w, out count);
                long n = count * (Swapped(w, typed) ? 50 : 1);
                if (n > best) { top = w; best = n; }
            }
            return top;
        }
        string Suggest(string code, string lower, bool umlautOnly, out int distance) {
            var L = lang[code];
            distance = 1;
            if (code == "de") foreach (var u in Umlauts) {
                string c = lower.Replace(u[0], u[1]);
                if (c != lower && L.Counts.ContainsKey(c)) return c;
            }
            distance = 0;
            if (umlautOnly) return null;
            string contraction;
            if (code == "en" && Contractions.TryGetValue(lower, out contraction)) { distance = 1; return contraction; }
            if (L.Counts.ContainsKey(lower)) return lower; // only the casing was wrong
            Func<string, bool> plain = w => { List<string> f; return code != "en" || (L.Forms.TryGetValue(w, out f) && f.Contains(w)); };
            var one = Edits1(lower, L.Alphabet);
            string best = MostFrequent(L, one.Where(w => plain(w) && (lower.Length > 3 || w.Length > 3 || Swapped(w, lower))), 1, lower);
            if (best != null) { distance = 1; return best; }
            if (lower.Length < 5) return null;
            var seen = new HashSet<string>(StringComparer.Ordinal); var two = new List<string>();
            foreach (var e in one) foreach (var x in Edits1(e, L.Alphabet)) if (L.Counts.ContainsKey(x) && plain(x) && seen.Add(x)) two.Add(x);
            distance = 2;
            return MostFrequent(L, two, 20);
        }
        string Cased(string code, string typed, string lower) {
            List<string> list;
            if (!lang[code].Forms.TryGetValue(lower, out list)) list = new List<string> { lower };
            string plain = list.Contains(lower) ? lower : list[0];
            if (typed.Length > 1 && typed == UpperJs(typed)) return UpperJs(plain);
            if (plain.StartsWith("i'", StringComparison.Ordinal)) return "I" + plain.Substring(1);
            bool typedCap = typed[0] != char.ToLowerInvariant(typed[0]);
            if (typedCap && plain == lower) return char.ToUpperInvariant(plain[0]) + plain.Substring(1);
            return plain;
        }

        public string Detect(string text) {
            int en = 0, de = 0;
            foreach (Match m in Word.Matches(Lower(text))) {
                if (lang["en"].Counts.ContainsKey(m.Value)) en++;
                if (lang["de"].Counts.ContainsKey(m.Value)) de++;
            }
            return de > en ? "de" : "en";
        }

        // language: "auto", "en" or "de". Edit offsets refer to the original text.
        public Result Correct(string text, string language = "auto") {
            string code = language == "en" || language == "de" ? language : Detect(text);
            string other = code == "en" ? "de" : "en";
            var skip = new List<int[]>();
            foreach (Match m in Skip.Matches(text)) skip.Add(new[] { m.Index, m.Index + m.Length });
            foreach (Match m in Quoted.Matches(text)) skip.Add(new[] { m.Index, m.Index + m.Length });
            var edits = new List<Edit>();
            if (code == "en") foreach (Match m in LoneI.Matches(text))
                if (!skip.Any(r => m.Index < r[1] && m.Index >= r[0])) edits.Add(new Edit { Offset = m.Index, Length = 1, Original = "i", Replacement = "I" });
            foreach (Match m in Word.Matches(text)) {
                string typed = m.Value; int start = m.Index, end = start + typed.Length;
                if ((typed.Length < 3 && !(code == "en" && Contractions.ContainsKey(Lower(typed)))) || skip.Any(r => start < r[1] && end > r[0])) continue;
                if (Upper.IsMatch(typed.Substring(1))) continue; // acronyms, CamelCase, brand names
                if (typed.IndexOf('\'') >= 0 || typed.IndexOf('’') >= 0) continue;
                string word = typed;
                if (Valid(code, word)) continue;
                string before = text.Substring(Math.Max(0, start - 40), start - Math.Max(0, start - 40));
                bool sentenceStart = start == 0 || SentenceEnd.IsMatch(before);
                bool capitalised = word[0] != char.ToLowerInvariant(word[0]);
                if (capitalised && (Titles.IsMatch(before) || (code == "en" && !sentenceStart))) continue;
                bool contraction = code == "en" && Contractions.ContainsKey(Lower(word));
                int distance;
                string lower = Suggest(code, Lower(word), !contraction && Valid(other, word), out distance);
                if (lower == null) continue;
                if (code == "de" && capitalised && !sentenceStart && (distance > 1 || word.Length < 4)) continue;
                string replacement = Cased(code, typed, lower);
                if (replacement != typed) edits.Add(new Edit { Offset = start, Length = typed.Length, Original = typed, Replacement = replacement });
            }
            edits = edits.OrderBy(e => e.Offset).ToList(); // stable, like Array.prototype.sort
            var output = new System.Text.StringBuilder(); int position = 0;
            foreach (var e in edits) { output.Append(text, position, e.Offset - position).Append(e.Replacement); position = e.Offset + e.Length; }
            output.Append(text, position, text.Length - position);
            return new Result { Text = output.ToString(), Edits = edits, Language = code };
        }
    }
}
