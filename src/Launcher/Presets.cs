using AOLauncher.Localization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AOLauncher
{
    internal sealed class PresetApplyResult
    {
        public int SelectedCount { get; internal set; }
        public int MissingCount { get; internal set; }
    }

    internal static class PresetService
    {
        internal static string ValidateName(string name)
        {
            if (name != null && name.Any(char.IsControl))
                throw new ArgumentException(Texts.T("Preset names cannot contain line breaks or control characters."));
            string trimmed = name?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                throw new ArgumentException(Texts.T("Enter a name for this preset."));
            if (trimmed.Length > 80)
                throw new ArgumentException(Texts.T("Preset names can contain up to 80 characters."));
            return trimmed;
        }

        internal static LaunchPreset Create(LauncherSettings settings, string name)
        {
            EnsureSettings(settings);
            string validName = UniqueName(settings, name, null);
            var preset = new LaunchPreset { Name = validName, Characters = CaptureSelection(settings) };
            settings.Presets.Add(preset);
            settings.SelectedPresetId = preset.Id;
            return preset;
        }

        internal static void Update(LauncherSettings settings, LaunchPreset preset)
        {
            RequirePreset(settings, preset);
            preset.Characters = CaptureSelection(settings);
            settings.SelectedPresetId = preset.Id;
        }

        internal static void Rename(LauncherSettings settings, LaunchPreset preset, string name)
        {
            RequirePreset(settings, preset);
            preset.Name = UniqueName(settings, name, preset);
        }

        internal static void Delete(LauncherSettings settings, LaunchPreset preset)
        {
            RequirePreset(settings, preset);
            settings.Presets.Remove(preset);
            if (string.Equals(settings.SelectedPresetId, preset.Id, StringComparison.OrdinalIgnoreCase))
                settings.SelectedPresetId = null;
            if (string.Equals(settings.DefaultPresetId, preset.Id, StringComparison.OrdinalIgnoreCase))
                settings.DefaultPresetId = null;
        }

        internal static void SetStartupDefault(LauncherSettings settings, LaunchPreset preset, bool enabled)
        {
            RequirePreset(settings, preset);
            if (enabled) settings.DefaultPresetId = preset.Id;
            else if (string.Equals(settings.DefaultPresetId, preset.Id, StringComparison.OrdinalIgnoreCase)) settings.DefaultPresetId = null;
        }

        internal static PresetApplyResult ApplyStartupSelection(LauncherSettings settings)
        {
            EnsureSettings(settings);
            var preset = settings.Presets.FirstOrDefault(item => string.Equals(item.Id, settings.DefaultPresetId, StringComparison.OrdinalIgnoreCase));
            settings.SelectedPresetId = null;
            if (preset != null) return Apply(settings, preset);
            // A last-used preset does not become an implicit startup default. Preserve manual character checks.
            settings.DefaultPresetId = null;
            return new PresetApplyResult();
        }

        internal static PresetApplyResult Apply(LauncherSettings settings, LaunchPreset preset)
        {
            RequirePreset(settings, preset);
            var wanted = Keys(preset.Characters);
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int selected = 0;
            foreach (var account in settings.Accounts)
                foreach (var character in account.Characters)
                {
                    string key = Key(account.Id, character.CharacterId);
                    character.Selected = wanted.Contains(key);
                    if (character.Selected) { selected++; found.Add(key); }
                }
            settings.SelectedPresetId = preset.Id;
            return new PresetApplyResult { SelectedCount = selected, MissingCount = wanted.Count - found.Count };
        }

        internal static bool Matches(LauncherSettings settings, LaunchPreset preset)
        {
            RequirePreset(settings, preset);
            return Keys(preset.Characters).SetEquals(Keys(CaptureSelection(settings)));
        }

        // Old settings remain valid. Preserve last-used checkboxes on startup,
        // and retain valid references to removed characters so Apply can report them.
        internal static void Normalize(LauncherSettings settings)
        {
            EnsureSettings(settings);
            var valid = new List<LaunchPreset>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var preset in settings.Presets)
            {
                if (preset == null) continue;
                string name;
                try { name = ValidateName(preset.Name); }
                catch (ArgumentException) { continue; }
                if (!names.Add(name)) continue;
                preset.Name = name;
                if (string.IsNullOrWhiteSpace(preset.Id) || !ids.Add(preset.Id))
                {
                    preset.Id = Guid.NewGuid().ToString("D");
                    ids.Add(preset.Id);
                }
                var references = new List<PresetCharacter>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var reference in preset.Characters ?? Enumerable.Empty<PresetCharacter>())
                {
                    if (reference == null || string.IsNullOrWhiteSpace(reference.AccountId) || reference.CharacterId <= 0)
                        continue;
                    if (seen.Add(Key(reference.AccountId, reference.CharacterId)))
                        references.Add(new PresetCharacter { AccountId = reference.AccountId, CharacterId = reference.CharacterId });
                }
                preset.Characters = references;
                valid.Add(preset);
            }
            settings.Presets = valid;
            var selectedPreset = valid.FirstOrDefault(preset => string.Equals(preset.Id, settings.SelectedPresetId, StringComparison.OrdinalIgnoreCase));
            settings.SelectedPresetId = selectedPreset?.Id;
            settings.DefaultPresetId = valid.FirstOrDefault(preset => string.Equals(preset.Id, settings.DefaultPresetId, StringComparison.OrdinalIgnoreCase))?.Id;
        }

        private static List<PresetCharacter> CaptureSelection(LauncherSettings settings)
        {
            var result = new List<PresetCharacter>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var account in settings.Accounts)
                foreach (var character in account.Characters.Where(character => character.Selected))
                    if (seen.Add(Key(account.Id, character.CharacterId)))
                        result.Add(new PresetCharacter { AccountId = account.Id, CharacterId = character.CharacterId });
            return result;
        }

        private static HashSet<string> Keys(IEnumerable<PresetCharacter> references)
        {
            return new HashSet<string>((references ?? Enumerable.Empty<PresetCharacter>())
                .Where(reference => reference != null && !string.IsNullOrWhiteSpace(reference.AccountId) && reference.CharacterId > 0)
                .Select(reference => Key(reference.AccountId, reference.CharacterId)), StringComparer.OrdinalIgnoreCase);
        }

        private static string Key(string accountId, int characterId)
        {
            return accountId + ":" + characterId.ToString(CultureInfo.InvariantCulture);
        }

        private static string UniqueName(LauncherSettings settings, string name, LaunchPreset except)
        {
            string validName = ValidateName(name);
            if (settings.Presets.Any(preset => !ReferenceEquals(preset, except) && string.Equals(preset.Name, validName, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException(Texts.T("A preset with this name already exists. Choose another name or use Update."));
            return validName;
        }

        private static void EnsureSettings(LauncherSettings settings)
        {
            if (settings == null || settings.Accounts == null)
                throw new ArgumentException(Texts.T("The account settings are unavailable."));
            if (settings.Presets == null) settings.Presets = new List<LaunchPreset>();
        }

        private static void RequirePreset(LauncherSettings settings, LaunchPreset preset)
        {
            EnsureSettings(settings);
            if (preset == null || !settings.Presets.Contains(preset))
                throw new InvalidOperationException(Texts.T("This preset is no longer available. Choose another preset."));
        }
    }
}
