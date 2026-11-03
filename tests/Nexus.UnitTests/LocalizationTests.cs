using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AOLauncher.Localization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nexus.Tests
{
    [TestClass]
    public class LocalizationTests
    {
        [TestCleanup] public void Reset() => Texts.SetLanguage("en");
        static Dictionary<string, string> Catalog(string language)
        {
            using (var stream = typeof(Texts).Assembly.GetManifestResourceStream("AOLauncher.Strings." + language + ".xml"))
                return XDocument.Load(stream).Root.Elements("string").ToDictionary(e => (string)e.Attribute("key"), e => e.Value);
        }

        [TestMethod]
        public void EveryResourceResolvesExactlyInBothLanguages()
        {
            var english = Catalog("en"); var danish = Catalog("da");
            Assert.IsGreaterThan(1600, english.Count);
            CollectionAssert.AreEquivalent(english.Keys.ToList(), danish.Keys.ToList());
            foreach (string language in new[] { "en", "da" })
            {
                Texts.SetLanguage(language);
                foreach (var pair in Catalog(language)) Assert.AreEqual(pair.Value, Texts.Get(pair.Key), pair.Key);
            }
        }

        [TestMethod]
        public void TranslationFormatArgumentsMatch()
        {
            var danish = Catalog("da");
            foreach (var pair in Catalog("en"))
            {
                Func<string, string[]> placeholders = text => Regex.Matches(text, @"(?<!\{)\{(\d+)(?:[^{}]*)\}(?!\})").Cast<Match>().Select(m => m.Groups[1].Value).OrderBy(v => v).ToArray();
                CollectionAssert.AreEqual(placeholders(pair.Value), placeholders(danish[pair.Key]), pair.Key);
            }
        }

        [TestMethod]
        public void LegacyLookupAndAutomaticLanguageSelectionUseTheSameCatalog()
        {
            Texts.SetLanguage("da");
            Assert.AreEqual("Den valgte indstilling findes ikke.", Texts.T("Den valgte indstilling findes ikke."));
            Assert.AreEqual("Fejl: test", Texts.F("Error: {0}", "test"));
            Assert.AreEqual("da", Texts.ResolveLanguage("auto", "da-DK"));
            Assert.AreEqual("en", Texts.ResolveLanguage("auto", "fr-FR"));
            Assert.AreEqual("en", Texts.ResolveLanguage("en", "da-DK"));
        }
    }
}
