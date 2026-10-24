using System.Text.RegularExpressions;
using System.Threading;
using AOLauncher.Localization;

namespace AOLauncher
{
    internal sealed class GameLogFilter
    {
        // Game clients can inherit the backend's output handles. Filter only the known
        // verbose database categories, before posting work to the UI thread.
        private static readonly Regex RoutineDatabaseMessage = new Regex(
            @"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\] \[(?:RDB|DatabaseController)\] \[(?:trace|debug|info)\] ",
            RegexOptions.CultureInvariant);
        private int noticeShown;

        internal string Filter(string line)
        {
            if (line == null || !RoutineDatabaseMessage.IsMatch(line)) return line;
            return Interlocked.Exchange(ref noticeShown, 1) == 0
                ? Texts.T("Routine game database messages are hidden. Warnings and errors are still shown.")
                : null;
        }
    }
}
