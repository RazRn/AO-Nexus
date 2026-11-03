using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AOLauncher
{
    internal sealed class CombatReportRow
    {
        internal string Name, Profession;
        internal CombatStats Stats;
    }
    internal sealed class CombatReportScope
    {
        internal string Key, Context;
        internal List<CombatReportRow> Rows = new List<CombatReportRow>();
        internal CombatStats Total;
    }
    internal sealed class CombatScriptReport
    {
        internal string Command, Title, Preview;
        internal List<string> Links = new List<string>();
        public override string ToString() => Command + "  (" + Links.Count + " chat links)";
    }

    // Self-contained text:// pages: recipients never need a local script, Nexus or AO#.
    // Keep under AO's 1024-byte chat limit, including markup. Split on whole rows,
    // never by substring (which could cut a link or silently lose somebody's stats).
    internal static class CombatScripts
    {
        internal const int MaxChatBytes = 1000, PagesPerCommand = 5;
        static readonly CultureInfo Numbers = CultureInfo.InvariantCulture;
        internal static string Safe(string value, int length = 48) => new string((value ?? "")
            .Select(c => c >= 32 && c <= 126 && "<>\"'&%".IndexOf(c) < 0 ? c : ' ').Take(length).ToArray()).Trim();
        static string Color(string color, string value) => "<font color=#" + color + ">" + value + "</font>";
        static string Number(double value) => value.ToString(value >= 1000000000 ? "0,,,.##'b'" : value >= 1000000 ? "0,,.##'m'" : value >= 10000 ? "0,.#'k'" : "0.#", Numbers);
        static string Rate(double? value) => value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value) ? Number(value.Value) : "--";
        static long Value(CombatStats s, string metric)
        {
            switch (metric) {
                case "nano": return s.NanoDamage; case "pets": return s.PetDamage; case "healing": return s.Healing;
                case "xp": return s.Xp; case "axp": return s.Axp; case "sk": return s.Sk; case "taken": return s.DamageTaken;
                default: return s.Damage;
            }
        }
        static double? PerSecond(CombatStats s, string metric)
        {
            switch (metric) {
                case "nano": return s.NanoDps; case "pets": return s.PetDps; case "healing": return s.Hps;
                case "xp": return s.XpHour / 3600; case "axp": return s.AxpHour / 3600; case "sk": return s.SkHour / 3600;
                case "taken": return s.TakenDps; default: return s.Dps;
            }
        }
        static string Label(string metric) => metric == "dmg" ? "Total damage" : metric == "dealt" ? "Damage dealt" : metric == "taken" ? "Damage taken"
            : metric == "nano" ? "Nano damage" : metric == "pets" ? "Pet damage" : metric == "healing" ? "Healing" : metric.ToUpperInvariant();
        static string Link(string title, string body) => "<a href=\"text://" + body + "\">" + title + "</a>";

        internal static List<CombatScriptReport> Build(IEnumerable<CombatReportScope> scopes, DateTime captured)
        {
            var reports = new List<CombatScriptReport>();
            foreach (var scope in scopes) {
                if (scope.Key != "team" && scope.Key != "all" && scope.Key != "npc") throw new ArgumentException("Unknown report scope.");
                var metrics = scope.Key == "npc" ? new[] { "dealt", "taken", "nano", "healing" }
                    : scope.Key == "team" ? new[] { "dmg", "nano", "pets", "healing", "xp", "axp", "sk" }
                    : new[] { "dmg", "nano", "pets", "healing" };
                foreach (string metric in metrics) BuildMetric(scope, metric, captured, reports);
            }
            return reports;
        }
        static void BuildMetric(CombatReportScope scope, string metric, DateTime captured, List<CombatScriptReport> reports)
        {
            bool experience = metric == "xp" || metric == "axp" || metric == "sk";
            var available = scope.Rows.Where(r => r.Stats != null && (!experience || r.Stats.Native)).ToList();
            var ranked = available.OrderByDescending(r => Value(r.Stats, metric)).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
            double sum = ranked.Sum(r => (double)Value(r.Stats, metric));
            string title = scope.Key.ToUpperInvariant() + " / " + Label(metric);
            string context=scope.Key=="team" && !string.IsNullOrWhiteSpace(scope.Context)?" | "+Safe(scope.Context,48):"";
            string header = Color("49D5ED", "AO NEXUS | " + title) + "<br>" + Color("A4B3BE", captured.ToString("yyyy-MM-dd HH:mm:ss", Numbers)
                + context) + "<br>";
            string footer = "";
            var blocks = new List<string>();
            var plain = new StringBuilder(title + "\r\n" + captured.ToString("g") + context + "\r\n\r\n");
            string summary = "Total " + Number(sum) + " | " + available.Count + "/" + scope.Rows.Count + " measured";
            blocks.Add(Color("FFD36A", summary) + "<br>"); plain.AppendLine(summary);
            if (scope.Total != null && scope.Total.Observed > 0 && (!experience || scope.Total.Native)) {
                bool average=metric=="dmg";
                double? panelRate=PerSecond(scope.Total,metric)*60/(average?scope.Total.Observed:1);
                string panel = "Control Panel total " + Number(Value(scope.Total, metric)) + " | " + Rate(panelRate) + (average?" avg/m":"/m");
                blocks.Add(panel + "<br>"); plain.AppendLine(panel);
            }
            if (available.Count == 0) { blocks.Add("No measurements available.<br>"); plain.AppendLine("No measurements available."); }
            int rank = 0;
            foreach (var row in ranked) {
                long value = Value(row.Stats, metric); double? rate = PerSecond(row.Stats, metric);
                string name = (++rank) + ". " + Safe(row.Name, 32) + (string.IsNullOrEmpty(row.Profession) ? "" : " - " + Safe(row.Profession, 8));
                string totals = Number(value) + " | " + (sum > 0 ? (value * 100.0 / sum).ToString("0.#", Numbers) : "0") + "%";
                string rates = Rate(rate) + "/s | " + Rate(rate * 60) + "/m | " + Rate(rate * 3600) + "/h";
                blocks.Add(Color("79E6B1", name) + "<br>" + totals + "<br>" + Color("A4B3BE", rates) + "<br>");
                plain.AppendLine().AppendLine(name).AppendLine(totals).AppendLine(rates);
                if (metric == "pets") foreach (var pet in TeamStatsTable.PetRows(row.Stats)) {
                    string petName = "  " + Safe(pet.Name, 32) + " (" + Safe(row.Name, 24) + ")";
                    string petRates = Number(pet.Stats.Damage) + " | " + Rate(pet.Stats.Dps) + "/s | " + Rate(pet.Stats.Dpm) + "/m | " + Rate(pet.Stats.Dph) + "/h";
                    blocks.Add(Color("BBA5FA", petName) + "<br>" + petRates + "<br>");
                    plain.AppendLine(petName).AppendLine(petRates);
                }
            }
            foreach (var missing in scope.Rows.Except(available)) {
                string line = Safe(missing.Name,32) + " - not measured"; blocks.Add(Color("A4B3BE", line) + "<br>"); plain.AppendLine(line);
            }
            var pages = new List<string>(); string page = "";
            foreach (string block in blocks) {
                if (Encoding.ASCII.GetByteCount(Link("Nexus " + title + " [999/999]", header + page + block + footer)) > MaxChatBytes) {
                    if (page.Length == 0) throw new InvalidOperationException("A combat report row exceeds the chat limit.");
                    pages.Add(page); page = "";
                }
                page += block;
            }
            if (page.Length > 0) pages.Add(page);
            for (int start = 0; start < pages.Count; start += PagesPerCommand) {
                string command = "nexus-" + scope.Key + "-" + metric + (start == 0 ? "" : "-" + (start / PagesPerCommand + 1));
                var report = new CombatScriptReport { Command = command, Title = title, Preview = plain.ToString() };
                for (int i = start; i < Math.Min(start + PagesPerCommand, pages.Count); i++)
                    report.Links.Add(Link("Nexus " + title + " [" + (i + 1) + "/" + pages.Count + "]", header + pages[i] + footer));
                reports.Add(report);
            }
        }
        internal static Dictionary<string, byte[]> Files(List<CombatScriptReport> reports)
        {
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var report in reports) {
                if (report.Links.Any(l => Encoding.ASCII.GetByteCount(l) > MaxChatBytes || l.Contains("\n") || l.Contains("\r"))) throw new InvalidOperationException("Invalid chat page.");
                files.Add(report.Command, Encoding.ASCII.GetBytes(string.Join("\r\n/delay 2000\r\n", report.Links) + "\r\n"));
                files.Add(report.Command + "-preview", Encoding.ASCII.GetBytes(string.Join("\r\n", report.Links.Select(l => "/text " + l)) + "\r\n"));
            }
            foreach (string scope in new[] { "", "team", "all", "npc" }) {
                var menu = new List<string> { "/text AO NEXUS reports | Saved snapshots. Commands post to your active chat channel.", "/text Add -preview to any report command for private viewing only. Each command posts at most 5 page links." };
                menu.AddRange(reports.Where(r => scope == "" || r.Command.StartsWith("nexus-" + scope + "-", StringComparison.Ordinal))
                    .Select(r => "/text /" + r.Command + " | " + r.Links.Count + " pages"));
                files.Add("nexus" + (scope == "" ? "" : "-" + scope), Encoding.ASCII.GetBytes(string.Join("\r\n", menu) + "\r\n"));
            }
            return files;
        }
    }
}
