using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Nexus.Core
{
    public static class LaunchPlan
    {
        // Never silently drop an entry or duplicate it when a saved preset is stale.
        public static List<T> InPresetOrder<T>(IEnumerable<T> selected, IEnumerable<string> presetKeys, Func<T, string> key)
        {
            var entries = selected.ToList();
            var order = presetKeys.ToList();
            if (entries.Count != order.Count || order.Distinct(StringComparer.Ordinal).Count() != order.Count)
                return null;
            var byKey = entries.GroupBy(key, StringComparer.Ordinal).ToList();
            if (byKey.Any(group => group.Count() != 1)) return null;
            var lookup = byKey.ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
            return order.All(lookup.ContainsKey) ? order.Select(id => lookup[id]).ToList() : null;
        }

        public static ProcessStartInfo BackendStartInfo(string executable)
        {
            return new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "--launch-stdin",
                WorkingDirectory = Path.GetDirectoryName(executable),
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }
    }
}
