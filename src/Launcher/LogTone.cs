using System;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace AOLauncher
{
    // Colours activity-log lines: green when something worked, yellow when something was
    // skipped or needs a look, red when something failed. Lines are classified by their
    // wording (English and Danish), so the colours also apply to engine output and to
    // older entries in the log window.
    internal enum LogToneKind { Neutral, Good, Warning, Error }

    internal static class LogTone
    {
        internal static readonly Color Neutral = Color.FromArgb(198, 208, 202);
        internal static readonly Color Time = Color.FromArgb(104, 122, 113);
        internal static readonly Color Good = Color.FromArgb(104, 218, 136);
        internal static readonly Color Warning = Color.FromArgb(240, 198, 84);
        internal static readonly Color Error = Color.FromArgb(255, 112, 102);

        static readonly Regex ErrorWords = new Regex(
            @"\b(fail(ed|s|ure)?|errors?|not loaded|ikke indlæst|did not start|startede ikke|not started|ikke startet|exception|crash(ed)?|could ?n[o']t|cannot|can't|not found|denied|timed out|not responding|stopped responding|exited unexpectedly|kunne ikke|fejl(ede)?|mislykke\w*|afvist|svarer ikke|ikke fundet)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        static readonly Regex WarningWords = new Regex(
            @"\b(skip(ped|s)?|springes over|sprunget over|warning|advarsel|still logging in|still starting|stadig|waiting|venter|retry(ing)?|prøver igen|not confirmed|ikke bekræftet|another program|andet program|closing|lukker|by force|tvunget|unsaved|ikke gemt|slow|langsom|delayed|forsinket|stuck|hænger)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        static readonly Regex GoodWords = new Regex(
            @"\b(loaded|indlæst|ready|klar|in game|i spillet|logged in|logget ind|logging in now|logger ind nu|responding|svarer|success(ful(ly)?)?|done|færdig|saved|gemt|updated|opdateret|validation ok|latest version|nyeste version|connected|forbundet|closed|lukket|reloaded|no changes|ingen ændringer|already open|allerede åben)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        internal static LogToneKind Classify(string line)
        {
            if (string.IsNullOrEmpty(line)) return LogToneKind.Neutral;
            if (ErrorWords.IsMatch(line)) return LogToneKind.Error;
            if (WarningWords.IsMatch(line)) return LogToneKind.Warning;
            if (GoodWords.IsMatch(line)) return LogToneKind.Good;
            return LogToneKind.Neutral;
        }

        internal static Color ColorOf(LogToneKind kind)
        {
            switch (kind)
            {
                case LogToneKind.Good: return Good;
                case LogToneKind.Warning: return Warning;
                case LogToneKind.Error: return Error;
                default: return Neutral;
            }
        }

        // Builds RTF for the log text: the "[HH:mm:ss]" stamp muted, the message in its tone.
        internal static string Rtf(string text, float fontPoints)
        {
            var rtf = new StringBuilder(text.Length * 5 / 4 + 512);
            rtf.Append(@"{\rtf1\ansi\deff0{\fonttbl{\f0\fmodern Consolas;}}{\colortbl;");
            foreach (var color in new[] { Neutral, Time, Good, Warning, Error })
                rtf.Append(@"\red").Append(color.R).Append(@"\green").Append(color.G).Append(@"\blue").Append(color.B).Append(';');
            rtf.Append(@"}\f0\fs").Append((int)Math.Round(fontPoints * 2)).Append(' ');
            string[] lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            int count = lines.Length;
            while (count > 0 && lines[count - 1].Length == 0) count--;
            for (int i = 0; i < count; i++)
            {
                string line = lines[i];
                string body = line;
                if (line.StartsWith("[") && line.IndexOf("] ", StringComparison.Ordinal) is int close && close > 0 && close <= 12)
                {
                    rtf.Append(@"\cf2 "); Escape(rtf, line.Substring(0, close + 2));
                    body = line.Substring(close + 2);
                }
                int colour;
                switch (Classify(body))
                {
                    case LogToneKind.Good: colour = 3; break;
                    case LogToneKind.Warning: colour = 4; break;
                    case LogToneKind.Error: colour = 5; break;
                    default: colour = 1; break;
                }
                rtf.Append(@"\cf").Append(colour).Append(' ');
                Escape(rtf, body);
                if (i < count - 1) rtf.Append(@"\par ");
            }
            rtf.Append('}');
            return rtf.ToString();
        }

        static void Escape(StringBuilder rtf, string value)
        {
            foreach (char c in value)
            {
                if (c == '\\' || c == '{' || c == '}') rtf.Append('\\').Append(c);
                else if (c == '\t') rtf.Append(@"\tab ");
                else if (c < 32) continue;
                else if (c > 126) rtf.Append(@"\u").Append((int)(short)c).Append('?');
                else rtf.Append(c);
            }
        }

        // Replaces the box's content with coloured text and keeps it scrolled to the newest line.
        internal static void Show(RichTextBox box, string text, float fontPoints, bool scrollToEnd)
        {
            box.Rtf = Rtf(text ?? "", fontPoints);
            if (!scrollToEnd) return;
            box.SelectionStart = box.TextLength; box.SelectionLength = 0; box.ScrollToCaret();
        }
    }
}
