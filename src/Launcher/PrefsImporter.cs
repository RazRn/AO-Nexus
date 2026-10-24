using AOLauncher.Localization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AOLauncher
{
    internal sealed class PrefsImportResult
    {
        public string AccountPath { get; internal set; }
        public string SuggestedUsername { get; internal set; }
        public IReadOnlyList<int> CharacterIds { get; internal set; }
    }

    internal static class PrefsImporter
    {
        internal static PrefsImportResult Scan(string input, string username)
        {
            string folder = NormalizePath(input);
            if (File.Exists(folder))
            {
                if (!string.Equals(Path.GetFileName(folder), "Prefs.xml", StringComparison.OrdinalIgnoreCase))
                    throw new IOException(Texts.T("Choose the account folder or its Prefs.xml file."));
                folder = Path.GetDirectoryName(folder);
            }
            if (!Directory.Exists(folder))
                throw new IOException(Texts.T("The Prefs folder was not found. Paste its full path or browse to it."));

            var directory = new DirectoryInfo(folder);
            int ignored;
            if (TryCharacterId(directory.Name, out ignored))
                directory = directory.Parent ?? directory;

            if (string.Equals(directory.Name, "Prefs", StringComparison.OrdinalIgnoreCase))
            {
                var accountFolders = directory.EnumerateDirectories().ToArray();
                string requestedUsername = username?.Trim();
                if (!string.IsNullOrWhiteSpace(requestedUsername))
                {
                    var selected = accountFolders.FirstOrDefault(candidate =>
                        string.Equals(candidate.Name, requestedUsername, StringComparison.OrdinalIgnoreCase));
                    if (selected == null)
                        throw new IOException(Texts.T("No folder matches this username under Prefs. Choose the account folder."));
                    return ReadAccount(selected);
                }

                PrefsImportResult onlyAccount = null;
                foreach (var candidate in accountFolders)
                {
                    int[] ids = ReadIds(candidate);
                    if (ids.Length == 0) continue;
                    if (onlyAccount != null)
                        throw new IOException(Texts.T("Prefs contains multiple accounts. Enter a username or choose one account folder."));
                    onlyAccount = CreateResult(candidate, ids);
                }
                if (onlyAccount != null) return onlyAccount;
                throw NoCharacters();
            }

            return ReadAccount(directory);
        }

        private static PrefsImportResult ReadAccount(DirectoryInfo directory)
        {
            int[] ids = ReadIds(directory);
            if (ids.Length == 0) throw NoCharacters();
            return CreateResult(directory, ids);
        }

        private static PrefsImportResult CreateResult(DirectoryInfo directory, int[] ids)
        {
            return new PrefsImportResult {
                AccountPath = directory.FullName,
                SuggestedUsername = directory.Name,
                CharacterIds = Array.AsReadOnly(ids)
            };
        }

        private static int[] ReadIds(DirectoryInfo directory)
        {
            // Only directory names immediately below this account are used. We never
            // read Login.cfg, chat history, credential files, or character file contents.
            var ids = new HashSet<int>();
            foreach (var child in directory.EnumerateDirectories())
            {
                int id;
                if (TryCharacterId(child.Name, out id)) ids.Add(id);
            }
            return ids.OrderBy(id => id).ToArray();
        }

        private static bool TryCharacterId(string name, out int id)
        {
            id = 0;
            if (name == null || name.Length <= 4 || !name.StartsWith("Char", StringComparison.OrdinalIgnoreCase)) return false;
            string digits = name.Substring(4);
            if (digits.Any(character => character < '0' || character > '9')) return false;
            return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
        }

        private static string NormalizePath(string input)
        {
            string value = input?.Trim();
            if (string.IsNullOrWhiteSpace(value)) throw new IOException(Texts.T("Paste the path to the account's Prefs folder."));
            if (value.Length >= 2 && ((value[0] == '"' && value[value.Length - 1] == '"') ||
                (value[0] == '\'' && value[value.Length - 1] == '\'')))
                value = value.Substring(1, value.Length - 2).Trim();
            value = Environment.ExpandEnvironmentVariables(value);

            Uri uri;
            if (Uri.TryCreate(value, UriKind.Absolute, out uri))
            {
                if (!uri.IsFile) throw new IOException(Texts.T("Use a folder path on this computer or a file:// link."));
                if (value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.Query))
                        throw new IOException(Texts.T("The file link is ambiguous. Paste the folder path from File Explorer."));
                    value = uri.LocalPath;
                }
            }
            if (!Path.IsPathRooted(value)) throw new IOException(Texts.T("Use the full path to the Prefs folder."));
            return Path.GetFullPath(value);
        }

        private static IOException NoCharacters()
        {
            return new IOException("No Char123 folders found. Choose the account's own folder under Prefs.");
        }
    }
}
