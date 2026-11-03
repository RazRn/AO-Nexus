using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml;

namespace AOLauncher.Localization
{
    internal static class Texts
    {
        const string Prefix = "AOLauncher.Strings.";
        static readonly Dictionary<string, Dictionary<string, string>> catalogs = LoadCatalogs();
        static readonly Dictionary<string, string> aliases = LoadAliases();
        static string language = ResolveLanguage("auto", CultureInfo.CurrentUICulture.Name);
        internal static string CurrentLanguage => language;
        internal static IDictionary<string, string> Translations
        {
            get
            {
                var result = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var alias in aliases) result[alias.Key] = Value(alias.Value, "da");
                return result;
            }
        }

        internal static string NormalizePreference(string preference)
        {
            string selected = (preference ?? "").ToLowerInvariant();
            return catalogs.ContainsKey(selected) ? selected : "auto";
        }
        internal static string ResolveLanguage(string preference, string localeName)
        {
            string selected = NormalizePreference(preference);
            if (selected != "auto") return selected;
            string locale = (localeName ?? "").ToLowerInvariant();
            if (catalogs.ContainsKey(locale)) return locale;
            int separator = locale.IndexOf('-');
            if (separator >= 0) locale = locale.Substring(0, separator);
            return catalogs.ContainsKey(locale) ? locale : "en";
        }
        internal static void SetLanguage(string preference) => language = ResolveLanguage(preference, CultureInfo.CurrentUICulture.Name);
        internal static string Get(string key) => Value(key, language);
        internal static string T(string english)
        {
            if (english == null) return null;
            return aliases.TryGetValue(english, out var key) ? Get(key) : english;
        }
        internal static string F(string english, params object[] values)
            => string.Format(CultureInfo.GetCultureInfo(language), T(english), values);

        static string Value(string key, string selected)
        {
            if (catalogs.TryGetValue(selected, out var catalog) && catalog.TryGetValue(key, out var translated)) return translated;
            if (catalogs.TryGetValue("en", out var fallback) && fallback.TryGetValue(key, out var english)) return english;
            throw new InvalidDataException("Missing translation resource: " + key);
        }
        static Dictionary<string, Dictionary<string, string>> LoadCatalogs()
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in typeof(Texts).Assembly.GetManifestResourceNames())
            {
                if (!name.StartsWith(Prefix, StringComparison.Ordinal) || !name.EndsWith(".xml", StringComparison.Ordinal)) continue;
                var catalog = new Dictionary<string, string>(StringComparer.Ordinal);
                Read(name, (key, value, legacy) => catalog.Add(key, value));
                result.Add(name.Substring(Prefix.Length, name.Length - Prefix.Length - 4), catalog);
            }
            // Small standalone legacy harnesses that only use English T() can still run.
            // Get() deliberately fails if a production resource was omitted from the build.
            return result;
        }
        static Dictionary<string, string> LoadAliases()
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            Read(Prefix + "en.xml", (key, value, legacy) => { if (legacy) result[value] = key; });
            return result;
        }
        static void Read(string name, Action<string, string, bool> accept)
        {
            using (var stream = typeof(Texts).Assembly.GetManifestResourceStream(name))
            {
                if (stream == null) return;
                using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                {
                    while (!reader.EOF)
                    {
                        if (reader.NodeType == XmlNodeType.Element && reader.Name == "string")
                        {
                            string key = reader.GetAttribute("key");
                            bool legacy = reader.GetAttribute("legacy") == "true";
                            string value = reader.ReadElementContentAsString();
                            accept(key, value, legacy);
                        }
                        else reader.Read();
                    }
                }
            }
        }
    }
}
