using System;
using System.Collections.Generic;
using static AOLauncher.Localization.Texts;

namespace AOLauncher
{
    internal sealed class PasswordEntry
    {
        public string Password { get; set; }
        public bool RememberPassword { get; set; }
    }

    internal static class AccountPasswords
    {
        // Updating a saved copy is separate from logging in. Do not replace the
        // session cache until encrypted storage and any opt-in setting succeed.
        internal static bool TryUpdateSaved(
            AccountConfig account,
            string password,
            IDictionary<string, string> cache,
            Action<AccountConfig, string> save,
            Action<AccountConfig> persistPreference,
            Action<AccountConfig> delete,
            out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(password))
            {
                error = T("The password cannot be empty.");
                return false;
            }
            bool remembered = account.RememberPassword;
            account.RememberPassword = true;
            try { save(account, password); }
            catch
            {
                account.RememberPassword = remembered;
                error = T("The saved password could not be updated. Try again.");
                return false;
            }
            if (!remembered)
            {
                try { persistPreference(account); }
                catch
                {
                    account.RememberPassword = false;
                    error = T("The password was not kept because the account setting could not be saved. Try again.");
                    try { delete(account); }
                    catch { error = T("The account setting could not be saved, and the new saved password could not be removed. Use Settings → Forget passwords to clear saved passwords."); }
                    return false;
                }
            }
            cache[account.Id] = password;
            return true;
        }

        internal static bool TryPrepare(
            IEnumerable<AccountConfig> accounts,
            IDictionary<string, string> cache,
            Func<AccountConfig, PasswordEntry> prompt,
            Func<AccountConfig, string> read,
            Action<AccountConfig, string> save,
            Action<AccountConfig> delete,
            Action<AccountConfig> preferenceChanged,
            Action<string> report,
            out Dictionary<string, string> credentials)
        {
            credentials = new Dictionary<string, string>();
            var selected = new List<AccountConfig>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var deleted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var account in accounts)
            {
                if (!seen.Add(account.Id)) continue;
                selected.Add(account);
                if (account.RememberPassword) continue;

                // Purge every opted-out account before prompting, even if a later
                // prompt is cancelled before reaching that account.
                cache.Remove(account.Id);
                TryDelete(account, delete, report);
                deleted.Add(account.Id);
            }

            foreach (var account in selected)
            {
                string password = null;
                if (account.RememberPassword)
                {
                    cache.TryGetValue(account.Id, out password);
                    if (string.IsNullOrEmpty(password))
                    {
                        cache.Remove(account.Id);
                        try { password = read(account); }
                        catch { report(T("A saved password could not be opened. Enter it again to continue.")); }
                        if (!string.IsNullOrEmpty(password)) cache[account.Id] = password;
                    }
                }

                if (string.IsNullOrEmpty(password))
                {
                    var entry = prompt(account);
                    if (entry == null || string.IsNullOrEmpty(entry.Password))
                    {
                        credentials.Clear();
                        return false;
                    }

                    if (account.RememberPassword != entry.RememberPassword)
                    {
                        account.RememberPassword = entry.RememberPassword;
                        preferenceChanged(account);
                    }

                    password = entry.Password;
                    if (account.RememberPassword)
                    {
                        cache[account.Id] = password;
                        try { save(account, password); }
                        catch { report(T("The password could not be saved securely. It will be used for this session only.")); }
                    }
                    else
                    {
                        cache.Remove(account.Id);
                        if (deleted.Add(account.Id)) TryDelete(account, delete, report);
                    }
                }

                // Unsaved passwords live only in this launch's credentials. The
                // caller clears them after constructing the backend request.
                credentials.Add(account.Id, password);
            }
            return true;
        }

        private static void TryDelete(AccountConfig account, Action<AccountConfig> delete, Action<string> report)
        {
            try { delete(account); }
            catch { report(T("The old password could not be deleted. Use “Forget passwords” to clear saved passwords.")); }
        }
    }
}
