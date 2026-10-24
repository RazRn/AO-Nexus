using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace AOLauncher
{
    internal sealed class PrefsTargetSuggestion
    {
        public string GamePath { get; internal set; }
        public string ServerProfileId { get; internal set; }
        public string Server { get; internal set; }
        public int Port { get; internal set; }
        // The installation's last launcher choice, not proof of this account's server.
        public string DimensionName { get; internal set; }
        public bool FromSavedAccount { get; internal set; }
    }

    internal static class PrefsTargetResolver
    {
        private const int MaxFileBytes = 256 * 1024;
        private const int MaxSettingsEntries = 4096;

        public static PrefsTargetSuggestion Resolve(string canonicalAccountPath, string selectedGamePath,
            IReadOnlyList<AccountConfig> accounts, IReadOnlyList<LoginServerProfile> profiles)
        {
            try { return ResolveCore(canonicalAccountPath, selectedGamePath, accounts, profiles); }
            catch (IOException) { return new PrefsTargetSuggestion(); }
            catch (UnauthorizedAccessException) { return new PrefsTargetSuggestion(); }
            catch (SecurityException) { return new PrefsTargetSuggestion(); }
            catch (ArgumentException) { return new PrefsTargetSuggestion(); }
            catch (NotSupportedException) { return new PrefsTargetSuggestion(); }
            catch (InvalidOperationException) { return new PrefsTargetSuggestion(); }
        }

        private static PrefsTargetSuggestion ResolveCore(string canonicalAccountPath, string selectedGamePath,
            IReadOnlyList<AccountConfig> accounts, IReadOnlyList<LoginServerProfile> profiles)
        {
            var result = new PrefsTargetSuggestion();
            string accountPath = LocalPath(canonicalAccountPath);
            string prefsRoot = AccountPrefsRoot(accountPath);
            if (prefsRoot == null || !SafeDirectoryChain(accountPath)) return result;
            string installation = Path.GetDirectoryName(prefsRoot);
            result.DimensionName = ReadDimensionName(Path.Combine(prefsRoot, "Prefs.xml"));
            if ((accounts != null && accounts.Count > MaxSettingsEntries) || (profiles != null && profiles.Count > MaxSettingsEntries)) return result;
            var saved = (accounts ?? new AccountConfig[0]).Where(item => item != null).ToArray();
            var currentProfiles = (profiles ?? new LoginServerProfile[0]).Where(item => item != null).ToArray();
            result.GamePath = ResolveGamePath(accountPath, prefsRoot, installation, selectedGamePath, saved);

            var exactAccounts = saved.Where(item => SamePath(LocalPath(item.PrefsPath), accountPath)).ToArray();
            Endpoint endpoint;
            if (exactAccounts.Length > 0)
            {
                var endpoints = exactAccounts.Select(item => ParseEndpoint(item.Server, item.Port.ToString(CultureInfo.InvariantCulture), false)).ToArray();
                // Invalid or conflicting exact mappings must not fall back to the last launcher choice.
                if (endpoints.Any(item => item == null) || endpoints.Select(item => item.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1) return result;
                endpoint = endpoints[0];
                result.FromSavedAccount = true;
            }
            else
            {
                endpoint = ReadDimensionEndpoint(Path.Combine(installation, "dimensions.txt"), result.DimensionName);
                if (endpoint == null) return result;
            }
            result.Server = endpoint.Server;
            result.Port = endpoint.Port;
            var matchingProfiles = currentProfiles.Where(item => {
                var target = ParseEndpoint(item.Server, item.Port.ToString(CultureInfo.InvariantCulture), false);
                return target != null && target.Key == endpoint.Key;
            }).ToArray();
            if (matchingProfiles.Length == 1 && !string.IsNullOrWhiteSpace(matchingProfiles[0].Id))
            {
                result.ServerProfileId = matchingProfiles[0].Id;
                result.Server = matchingProfiles[0].Server;
            }
            return result;
        }

        private static string ResolveGamePath(string account, string prefsRoot, string installation,
            string selectedGamePath, AccountConfig[] saved)
        {
            var strongPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (GameExists(installation)) strongPaths.Add(installation);
            string hash = Path.GetDirectoryName(installation);
            if (hash != null && Regex.IsMatch(Path.GetFileName(hash), @"\A[0-9a-fA-F]{8}\z"))
            {
                string discoveryRoot = Path.GetDirectoryName(hash);
                if (discoveryRoot != null && SafeDirectoryChain(discoveryRoot))
                {
                    // Inject empty registration metadata: only exact log-derived discovery evidence is relevant.
                    foreach (var candidate in PrefsDiscovery.Find(discoveryRoot, selectedGamePath, null, new string[0]))
                        if (SamePath(LocalPath(candidate.PrefsPath), account) && candidate.GameExists && GameExists(candidate.GamePath))
                            strongPaths.Add(LocalPath(candidate.GamePath));
                }
            }
            if (strongPaths.Count > 0) return strongPaths.Count == 1 ? strongPaths.First() : null;

            var configured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in saved)
            {
                if (!SamePath(AccountPrefsRoot(LocalPath(item.PrefsPath)), prefsRoot)) continue;
                string path = LocalPath(item.GamePath);
                if (GameExists(path)) configured.Add(path);
            }
            return configured.Count == 1 ? configured.First() : null;
        }

        private static string ReadDimensionName(string path)
        {
            byte[] bytes = ReadBoundedFile(path);
            if (bytes == null) return null;
            try
            {
                var settings = new XmlReaderSettings {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                    MaxCharactersInDocument = MaxFileBytes, MaxCharactersFromEntities = MaxFileBytes,
                    IgnoreComments = true, IgnoreProcessingInstructions = true
                };
                string name = null;
                int count = 0;
                using (var stream = new MemoryStream(bytes))
                using (var reader = XmlReader.Create(stream, settings))
                {
                    while (reader.Read())
                    {
                        if (reader.NodeType != XmlNodeType.Element || reader.Name != "String" || reader.GetAttribute("name") != "SelectedDimension") continue;
                        count++;
                        name = Unquote(reader.GetAttribute("value"));
                    }
                }
                return count == 1 && !string.IsNullOrWhiteSpace(name) && name.Length <= 128 && !name.Any(char.IsControl) ? name : null;
            }
            catch (XmlException) { return null; }
            catch (ArgumentException) { return null; }
        }

        private static Endpoint ReadDimensionEndpoint(string path, string dimensionName)
        {
            if (dimensionName == null) return null;
            byte[] bytes = ReadBoundedFile(path);
            if (bytes == null) return null;
            var matches = new List<Dictionary<string, string>>();
            Dictionary<string, string> block = null;
            using (var stream = new MemoryStream(bytes))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                string line;
                int lines = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    if (++lines > 4096) return null;
                    line = line.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("//", StringComparison.Ordinal)) continue;
                    if (string.Equals(line, "STARTINFO", StringComparison.OrdinalIgnoreCase))
                    {
                        if (block != null) return null;
                        block = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }
                    else if (string.Equals(line, "ENDINFO", StringComparison.OrdinalIgnoreCase))
                    {
                        if (block == null) return null;
                        string displayName;
                        if (block.TryGetValue("displayname", out displayName) && string.Equals(Unquote(displayName), dimensionName, StringComparison.OrdinalIgnoreCase)) matches.Add(block);
                        block = null;
                    }
                    else if (block != null)
                    {
                        int separator = line.IndexOf('=');
                        if (separator <= 0) return null;
                        string key = line.Substring(0, separator).Trim();
                        if (key != "displayname" && key != "connect" && key != "ports") key = key.ToLowerInvariant();
                        if (key != "displayname" && key != "connect" && key != "ports") continue;
                        if (block.ContainsKey(key)) return null;
                        block[key] = line.Substring(separator + 1).Trim();
                    }
                    else return null;
                }
            }
            if (block != null || matches.Count != 1) return null;
            string server, ports;
            return matches[0].TryGetValue("connect", out server) && matches[0].TryGetValue("ports", out ports)
                ? ParseEndpoint(Unquote(server), Unquote(ports), true) : null;
        }

        private static Endpoint ParseEndpoint(string server, string portText, bool allowOfficialAlias)
        {
            int port;
            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrEmpty(portText) ||
                portText.Any(character => character < '0' || character > '9') ||
                !int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port < 1 || port > 65535) return null;
            server = server.Trim();
            if (allowOfficialAlias && string.Equals(server, "cm.d1.funcom.com", StringComparison.OrdinalIgnoreCase) && (port == 7505 || port == 7506))
            {
                // This explicit offline alias uses the matching built-in endpoint, never DNS or a mutable profile ID/name.
                var defaults = LoginServerProfile.Defaults().Where(item => item.Port == port)
                    .Select(item => ParseEndpoint(item.Server, portText, false)).Where(item => item != null).ToArray();
                return defaults.Length == 1 ? defaults[0] : null;
            }
            string[] parts = server.Split('.');
            if (parts.Length != 4) return null;
            var octets = new byte[4];
            for (int index = 0; index < parts.Length; index++)
                if (parts[index].Length == 0 || (parts[index].Length > 1 && parts[index][0] == '0') ||
                    parts[index].Any(character => character < '0' || character > '9') ||
                    !byte.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out octets[index])) return null;
            return new Endpoint { Server = string.Join(".", octets.Select(value => value.ToString(CultureInfo.InvariantCulture))), Port = port };
        }

        private static byte[] ReadBoundedFile(string path)
        {
            try
            {
                if (!SafeDirectoryChain(Path.GetDirectoryName(path))) return null;
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) return null;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (stream.Length > MaxFileBytes) return null;
                    int length = (int)stream.Length;
                    byte[] bytes = new byte[length];
                    int read = 0;
                    while (read < length)
                    {
                        int count = stream.Read(bytes, read, length - read);
                        if (count == 0) break;
                        read += count;
                    }
                    return read == length && stream.Length == length ? bytes : null;
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (SecurityException) { return null; }
        }

        private static bool GameExists(string path)
        {
            path = LocalPath(path);
            if (path == null || !SafeDirectoryChain(path)) return false;
            try
            {
                FileAttributes attributes = File.GetAttributes(Path.Combine(path, "AnarchyOnline.exe"));
                return (attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) == 0;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (SecurityException) { return false; }
        }

        private static bool SafeDirectoryChain(string path)
        {
            path = LocalPath(path);
            if (path == null) return false;
            try
            {
                for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
                {
                    FileAttributes attributes = directory.Attributes;
                    if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0) return false;
                }
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (SecurityException) { return false; }
        }

        private static string AccountPrefsRoot(string account)
        {
            if (account == null) return null;
            string parent = Path.GetDirectoryName(account);
            return parent != null && string.Equals(Path.GetFileName(parent), "Prefs", StringComparison.OrdinalIgnoreCase) ? parent : null;
        }

        private static string LocalPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            path = path.Trim();
            // Automatic resolution must not contact UNC hosts or resolve network names.
            if (path.Length < 3 || !((path[0] >= 'a' && path[0] <= 'z') || (path[0] >= 'A' && path[0] <= 'Z')) || path[1] != ':' || (path[2] != '\\' && path[2] != '/')) return null;
            try
            {
                string full = Path.GetFullPath(path);
                return full.Length == Path.GetPathRoot(full).Length ? full : full.TrimEnd('\\', '/');
            }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (IOException) { return null; }
            catch (SecurityException) { return null; }
        }

        private static string Unquote(string value)
        {
            if (value == null) return null;
            value = value.Trim();
            if (value.Length >= 2 && ((value[0] == '"' && value[value.Length - 1] == '"') || (value[0] == '\'' && value[value.Length - 1] == '\''))) return value.Substring(1, value.Length - 2).Trim();
            return value;
        }

        private static bool SamePath(string left, string right) => left != null && right != null && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        private sealed class Endpoint
        {
            public string Server;
            public int Port;
            public string Key => Server + ":" + Port.ToString(CultureInfo.InvariantCulture);
        }
    }
}
