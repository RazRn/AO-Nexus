using AOLauncher.Localization;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace AOLauncher
{
    internal static class CredentialStore
    {
        // Persisted DPAPI key: changing this legacy name would make existing passwords unreadable.
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AORebirthLauncher.Credentials.v1");
        private static readonly object Sync = new object();
        private static string DirectoryPath => Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "AONexus", "Credentials");

        internal static bool HasSavedPasswords
        {
            get
            {
                try { return Directory.Exists(DirectoryPath) && Directory.EnumerateFiles(DirectoryPath, "*.bin").Any(); }
                catch { return false; }
            }
        }

        internal static string Read(AccountConfig account) => ReadFromDirectory(account, DirectoryPath);
        internal static void Save(AccountConfig account, string password) => SaveToDirectory(account, password, DirectoryPath);

        internal static void Delete(AccountConfig account) => DeleteFromDirectory(account, DirectoryPath);

        internal static void DeleteFromDirectory(AccountConfig account, string directory)
        {
            lock (Sync) File.Delete(AccountPath(account, directory));
        }

        internal static void ForgetAll()
        {
            lock (Sync)
            {
                if (!Directory.Exists(DirectoryPath)) return;
                foreach (string file in Directory.EnumerateFiles(DirectoryPath, "*.bin")) File.Delete(file);
            }
        }

        // Directory parameters let the regression checks use isolated temporary data.
        internal static string ReadFromDirectory(AccountConfig account, string directory)
        {
            if (account != null && !account.RememberPassword) return null;
            lock (Sync)
            {
                string path = AccountPath(account, directory);
                if (!File.Exists(path)) return null;
                if (new FileInfo(path).Length > 1024 * 1024) return null;
                byte[] plaintext = null;
                try
                {
                    plaintext = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
                    var record = new JavaScriptSerializer().Deserialize<StoredPassword>(Encoding.UTF8.GetString(plaintext));
                    if (record == null || !string.Equals(record.Username, account.Username, StringComparison.OrdinalIgnoreCase)) return null;
                    string target = AccountTargets.Endpoint(account);
                    if (record.Version == 2)
                        return string.Equals(record.AccountId, account.Id, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(record.Target, target, StringComparison.OrdinalIgnoreCase) ? record.Password : null;
                    if (record.Version != 1 || !string.Equals(account.LegacyCredentialTarget, target, StringComparison.OrdinalIgnoreCase)) return null;
                    SaveToDirectory(account, record.Password, directory);
                    return record.Password;
                }
                catch (CryptographicException) { return null; }
                catch (ArgumentException) { return null; }
                finally { if (plaintext != null) Array.Clear(plaintext, 0, plaintext.Length); }
            }
        }

        internal static void SaveToDirectory(AccountConfig account, string password, string directory)
        {
            if (account != null && !account.RememberPassword)
            {
                DeleteFromDirectory(account, directory);
                return;
            }
            if (string.IsNullOrEmpty(password)) throw new ArgumentException(Texts.T("The password cannot be empty."));
            lock (Sync)
            {
                string destination = AccountPath(account, directory);
                byte[] plaintext = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new StoredPassword {
                    Version = 2, AccountId = account.Id, Username = account.Username,
                    Target = AccountTargets.Endpoint(account), Password = password
                }));
                string temporaryPath = null;
                try
                {
                    byte[] ciphertext = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
                    Directory.CreateDirectory(directory);
                    temporaryPath = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
                    File.WriteAllBytes(temporaryPath, ciphertext);
                    if (File.Exists(destination)) File.Replace(temporaryPath, destination, null);
                    else File.Move(temporaryPath, destination);
                    temporaryPath = null;
                }
                finally
                {
                    Array.Clear(plaintext, 0, plaintext.Length);
                    if (temporaryPath != null && File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
            }
        }

        private static string AccountPath(AccountConfig account, string directory)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.Id) || string.IsNullOrWhiteSpace(account.Username))
                throw new ArgumentException(Texts.T("The account is missing an ID or username."));
            using (var sha = SHA256.Create())
            {
                string key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(account.Id))).Replace("-", "");
                return Path.Combine(directory, key + ".bin");
            }
        }

        private sealed class StoredPassword
        {
            public int Version { get; set; }
            public string AccountId { get; set; }
            public string Target { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
        }
    }
}
