using AOLauncher.Localization;
using System;
using System.Collections.Generic;
using System.Net;

namespace AOLauncher
{
    // These are launcher defaults selected by the user, not a source of server rules.
    internal static class ServerAccountPolicy
    {
        internal static int DefaultLimit(string server, int port)
        {
            IPAddress address;
            string normalized = IPAddress.TryParse(server, out address) ? address.ToString() : (server ?? string.Empty).Trim();
            if (normalized == "5.161.243.214" && port == 7000) return 2;
            return 0;
        }

        internal static int DefaultCharacterLimit(string server, int port)
        {
            IPAddress address;
            string normalized = IPAddress.TryParse(server, out address) ? address.ToString() : (server ?? string.Empty).Trim();
            if (normalized == "37.18.192.41" && (port == 7505 || port == 7506)) return 1;
            if (normalized == "5.161.243.214" && port == 7000) return 1;
            return 0;
        }

        internal static int EffectiveLimit(IEnumerable<int> limits)
        {
            if (limits == null) throw new ArgumentNullException(nameof(limits));
            int effective = 0;
            foreach (int limit in limits)
            {
                ValidateLimit(limit);
                if (limit > 0 && (effective == 0 || limit < effective)) effective = limit;
            }
            return effective;
        }

        internal static void ValidateLimit(int limit)
        {
            if (limit < 0 || limit > 100)
                throw new ArgumentException(Texts.T("The account limit must be between 0 and 100; 0 means unlimited."));
        }
    }
}
