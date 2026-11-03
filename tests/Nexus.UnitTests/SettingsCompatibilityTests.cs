using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AOLauncher;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nexus.Tests
{
    [TestClass]
    public class SettingsCompatibilityTests
    {
        [TestMethod]
        public void LegacyProfileMigrationKeepsAccountsSelectionsAndTeamOrder()
        {
            const string json = "{\"Version\":1,\"GamePath\":\"C:\\\\fixture-game\",\"Server\":\"192.0.2.10\",\"Port\":7500,"
                + "\"Accounts\":[{\"Id\":\"a\",\"Username\":\"fixture\",\"Label\":\"Keep label\",\"Collapsed\":true,\"Characters\":[{\"CharacterId\":1,\"Name\":\"Tank\",\"Selected\":true},{\"CharacterId\":2,\"Name\":\"Doc\",\"Selected\":false}]}],"
                + "\"Presets\":[{\"Id\":\"team\",\"Name\":\"Keep team\",\"Characters\":[{\"AccountId\":\"a\",\"CharacterId\":2},{\"AccountId\":\"a\",\"CharacterId\":1}]}],\"SelectedPresetId\":\"team\"}";
            var settings = SettingsStore.Deserialize(json);
            Assert.AreEqual(2, settings.Version);
            Assert.AreEqual("Keep label", settings.Accounts[0].Label);
            Assert.IsTrue(settings.Accounts[0].Collapsed);
            Assert.IsTrue(settings.Accounts[0].Characters[0].Selected);
            Assert.IsFalse(settings.Accounts[0].Characters[1].Selected);
            Assert.AreEqual("192.0.2.10", settings.Accounts[0].Server);
            Assert.AreEqual("team", settings.SelectedPresetId);
            CollectionAssert.AreEqual(new[] { 2, 1 }, settings.Presets[0].Characters.Select(c => c.CharacterId).ToArray());
        }

        [TestMethod]
        public void ControlPanelAndSharedPluginChoicesSurviveActualStorageRoundTrip()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Nexus.Settings.Tests-" + Guid.NewGuid().ToString("N"));
            try
            {
                var settings = new LauncherSettings
                {
                    Language = "da", TeamCardOrder = new List<string> { "tank", "doc" },
                    SharedPlugins = new List<string> { "fixture:sync" },
                    ControlPanel = new ControlPanelPreferences { LivePreviews = false, AlwaysOnTop = true, AutoRegroup = true, Tab = 2, Width = 1100, Height = 800 }
                };
                SettingsStore.SaveToDirectory(settings, directory);
                var loaded = SettingsStore.ReadFromDirectory(directory);
                Assert.AreEqual("da", loaded.Language);
                Assert.IsFalse(loaded.ControlPanel.LivePreviews);
                Assert.IsTrue(loaded.ControlPanel.AlwaysOnTop);
                Assert.IsTrue(loaded.ControlPanel.AutoRegroup);
                Assert.AreEqual(2, loaded.ControlPanel.Tab);
                CollectionAssert.AreEqual(settings.TeamCardOrder, loaded.TeamCardOrder);
                CollectionAssert.AreEqual(settings.SharedPlugins, loaded.SharedPlugins);
                Assert.IsFalse(File.ReadAllText(Path.Combine(directory, "AONexus.json")).Contains("\"Password\":"));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
    }
}
