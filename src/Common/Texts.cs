using System;
using System.Collections.Generic;
using System.Globalization;

namespace AOLauncher.Localization
{
    internal static class Texts
    {
        private static string language = ResolveLanguage("auto", CultureInfo.CurrentUICulture.Name);
        internal static string CurrentLanguage => language;

        internal static string NormalizePreference(string preference)
        {
            if (string.Equals(preference, "da", StringComparison.OrdinalIgnoreCase)) return "da";
            if (string.Equals(preference, "en", StringComparison.OrdinalIgnoreCase)) return "en";
            return "auto";
        }

        internal static string ResolveLanguage(string preference, string localeName)
        {
            string selected = NormalizePreference(preference);
            if (selected != "auto") return selected;
            return string.Equals(localeName, "da", StringComparison.OrdinalIgnoreCase) ||
                (localeName != null && localeName.StartsWith("da-", StringComparison.OrdinalIgnoreCase)) ? "da" : "en";
        }

        internal static void SetLanguage(string preference)
        {
            language = ResolveLanguage(preference, CultureInfo.CurrentUICulture.Name);
        }

        internal static string T(string english)
        {
            if (english == null) return null;
            string translated;
            return language == "da" && Danish.TryGetValue(english, out translated) ? translated : english;
        }

        internal static string F(string english, params object[] values)
        {
            return string.Format(language == "da" ? CultureInfo.GetCultureInfo("da-DK") : CultureInfo.GetCultureInfo("en-US"), T(english), values);
        }

        // Register UI-only messages once, before background work begins.
        internal static void AddTranslations(IDictionary<string, string> translations)
        {
            foreach (var entry in translations) Danish[entry.Key] = entry.Value;
        }

        private static readonly Dictionary<string, string> Danish = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "The new character could not be saved to the active client list. The game remains open; add the character manually.", "Den nye char kunne ikke gemmes på listen over aktive klienter. Spillet forbliver åbent; tilføj char'en manuelt." },
            { "Client started for character creation. Continue in the game window; the new character will be added automatically when it enters the game.", "Spilklienten er startet til char-oprettelse. Fortsæt i spilvinduet; den nye char tilføjes automatisk, når den kommer ind i spillet." },
            { "Automatic character detection stopped. The game remains open; you can add the character manually.", "Automatisk registrering af den nye char stoppede. Spillet forbliver åbent; du kan tilføje char'en manuelt." },
            { "Automatic character detection timed out. The game remains open; you can add the character manually.", "Automatisk registrering af den nye char fik timeout. Spillet forbliver åbent; du kan tilføje char'en manuelt." },
            { "Character detection stopped because the game closed. No character was added.", "Registrering af den nye char stoppede, fordi spillet blev lukket. Ingen char blev tilføjet." },
            { "Character creation is still open for this account on {0}. Finish it or close its game window before logging in another character.", "Char-oprettelse er stadig åben for denne konto på {0}. Færdiggør den, eller luk dens spilvindue, før du logger ind med en anden char." },
            { "Open character creation for one account at a time, separately from character login.", "Åbn char-oprettelse for én konto ad gangen, adskilt fra login af chars." },
            { "A character-creation request must use character ID 0.", "En anmodning om char-oprettelse skal bruge char-ID 0." },
            { "Create new char", "Opret ny char" },
            { "Target: {0}:{1} | character creation.", "Mål: {0}:{1} | char-oprettelse." },
            { "A character-creation client is already open for this account on {0}. Continue in that game window or close it before trying again.", "Et vindue til char-oprettelse er allerede åbent for denne konto på {0}. Fortsæt i spilvinduet, eller luk det, før du prøver igen." },
            { "Client started for character creation. Continue in the game window; if the character list appears, choose Create new character there.", "Spilklienten er startet til char-oprettelse. Fortsæt i spilvinduet; vises char-listen, skal du vælge oprettelse af en ny char dér." },
            { "Could not start character creation. Check the game window; an already opened client stays open.", "Kunne ikke starte char-oprettelse. Kontrollér spilvinduet; en allerede åbnet klient bliver stående." },
            { "An explicit character target requires a game folder, IPv4 server and port.", "Et specifikt mål for en char kræver en spilmappe, IPv4-server og port." },
            { "The account identifier is too long or contains invalid control characters.", "Kontoens ID er for langt eller indeholder ugyldige kontroltegn." },
            { "The account limit must be between 0 and 100; 0 means unlimited.", "Kontogrænsen skal være mellem 0 og 100; 0 betyder ubegrænset." },
            { "Server {0} allows at most {1} simultaneous account(s). The selection and active tracked clients total {2} accounts.", "Server {0} tillader højst {1} samtidige konti. Dine valg og allerede registrerede, åbne spilklienter omfatter i alt {2} konti." },
            { "The active client list could not be checked. Try again before starting more accounts.", "Listen over åbne spilklienter kunne ikke kontrolleres. Prøv igen, før du starter flere konti." },
            { "Account on server {0} allows at most {1} simultaneous character(s). The selection and active tracked clients total {2} characters.", "En konto på server {0} tillader højst {1} samtidige chars. Dine valg og allerede registrerede, åbne spilklienter omfatter i alt {2} chars." },
            { "Account {0} on {1} allows at most {2} selected characters. You selected {3}.", "Konto {0} på {1} tillader højst {2} valgte chars. Du har valgt {3}." },
            { "ID {0}: a tracked game client is still logging in; skipped. Check its game window.", "ID {0}: en registreret spilklient er stadig ved at logge ind; springes over. Kontrollér dens spilvindue." },
            { "{0} ({1}) allows at most {2} selected accounts. You selected {3}. Deselect accounts or change the server's account limit in Settings.", "{0} ({1}) tillader højst {2} valgte konti. Du har valgt {3}. Fravælg konti, eller ændr serverens kontogrænse i Indstillinger." },
            { "Custom server", "Brugerdefineret server" },
            { "Error: {0}", "Fejl: {0}" },
            { "Game: {0}", "Spil: {0}" },
            { "Endpoint: {0}:{1}", "Server: {0}:{1}" },
            { "Arguments: {0}", "Argumenter: {0}" },
            { "Validation only; no game was started.", "Kun validering; spillet blev ikke startet." },
            { "Character ID must be a positive number.", "Charens ID skal være et positivt tal." },
            { "Use either --rk19 or a custom --server/--port.", "Brug enten --rk19 eller en selvvalgt --server/--port." },
            { "The settings file has an unsupported format.", "Indstillingsfilen har et ukendt format." },
            { "The settings file contains an invalid account.", "Indstillingsfilen indeholder en ugyldig konto." },
            { "The settings file contains an invalid character.", "Indstillingsfilen indeholder en ugyldig char." },
            { "Preset names cannot contain line breaks or control characters.", "Preset-navne må ikke indeholde linjeskift eller kontroltegn." },
            { "Enter a name for this preset.", "Indtast et navn til dette preset." },
            { "Preset names can contain up to 80 characters.", "Preset-navne må højst indeholde 80 tegn." },
            { "A preset with this name already exists. Choose another name or use Update.", "Et preset med dette navn findes allerede. Vælg et andet navn, eller brug Opdatér." },
            { "The account settings are unavailable.", "Kontoindstillingerne er ikke tilgængelige." },
            { "This preset is no longer available. Choose another preset.", "Dette preset findes ikke længere. Vælg et andet preset." },
            { "BackendRunner can only be used once.", "Loginprocessen kan kun startes én gang." },
            { "AOQuickLauncher could not be started.", "AOQuickLauncher kunne ikke startes." },
            { "The password cannot be empty.", "Adgangskoden må ikke være tom." },
            { "The account is missing an ID or username.", "Kontoen mangler et ID eller kontonavn." },
            { "Choose the account folder or its Prefs.xml file.", "Vælg kontoens mappe eller dens Prefs.xml-fil." },
            { "The Prefs folder was not found. Paste its full path or browse to it.", "Prefs-mappen findes ikke. Indsæt hele stien, eller vælg mappen." },
            { "No folder matches this username under Prefs. Choose the account folder.", "Ingen mappe matcher kontonavnet under Prefs. Vælg kontoens egen mappe." },
            { "Prefs contains multiple accounts. Enter a username or choose one account folder.", "Prefs indeholder flere konti. Angiv kontonavnet, eller vælg én kontos mappe." },
            { "Paste the path to the account's Prefs folder.", "Indsæt stien til kontoens Prefs-mappe." },
            { "Use a folder path on this computer or a file:// link.", "Brug en mappesti på computeren eller et file://-link." },
            { "The file link is ambiguous. Paste the folder path from File Explorer.", "Fil-linket er ikke entydigt. Kopiér mappestien fra Stifinder." },
            { "Use the full path to the Prefs folder.", "Brug hele stien til Prefs-mappen." },
            { "No Char123 folders found. Choose the account's own folder under Prefs.", "Ingen Char123-mapper fundet. Vælg kontoens egen mappe under Prefs." },
            { "AnarchyOnline.exe was not found in the selected game folder.", "AnarchyOnline.exe findes ikke i den valgte spilmappe." },
            { "LoginHandler.dll is missing beside AOQuickLauncher.exe.", "LoginHandler.dll mangler ved siden af AOQuickLauncher.exe." },
            { "The login server must be an IPv4 address.", "Loginserveren skal være en IPv4-adresse." },
            { "The login port must be between 1 and 65535.", "Loginporten skal være mellem 1 og 65535." },
            { "Start this function from the launcher window.", "Start denne funktion fra launchervinduet." },
            { "The launch request is missing or too large.", "Startlisten mangler eller er for stor." },
            { "The launch request could not be read.", "Startlisten kunne ikke læses." },
            { "Select at least one character.", "Vælg mindst én char." },
            { "Select no more than 100 characters at a time.", "Vælg højst 100 chars ad gangen." },
            { "The timeout must be between 10 and 600 seconds.", "Ventetiden skal være mellem 10 og 600 sekunder." },
            { "ID {0} was not detected in game. Check the game window, account details and character ID. Characters started after it continue; existing game windows stay open.", "ID {0} blev ikke fundet i spillet. Kontrollér spilvinduet, kontooplysninger og char-ID. Chars startet efter den fortsætter; åbne spilvinduer forbliver åbne." },
            { "The start spacing must be between 0 and 10000 milliseconds.", "Afstanden mellem opstarter skal være mellem 0 og 10000 millisekunder." },
            { "Enter a username and password for every selected account.", "Indtast kontonavn og adgangskode til alle valgte konti." },
            { "Each character must have a unique, positive character ID.", "Hver char skal have et unikt, positivt char-ID." },
            { "The character name contains invalid control characters.", "Char-navnet indeholder ugyldige kontroltegn." },
            { "Character {0}", "Char {0}" },
            { "Target: {0}:{1} | {2} character(s).", "Mål: {0}:{1} | {2} char(s)." },
            { "Validation OK. No game clients were started.", "Validering OK. Ingen spilklienter er startet." },
            { "A login queue is already running. Wait for it to finish.", "Der kører allerede en startkø. Vent til den er færdig." },
            { "Accounts start in parallel; characters on the same account log in one at a time.", "Konti starter parallelt; chars på samme konto logger ind én ad gangen." },
            { "{0} (ID {1}): already running; skipped.", "{0} (ID {1}): allerede åben; springes over." },
            { "Starting {0} (ID {1})...", "Starter {0} (ID {1})..." },
            { "Waiting for the game window for ID {0}...", "Venter på spilvinduet for ID {0}..." },
            { "This account's queue stopped: ID {0} was not detected in game. Check the game window, account details and character ID. Other accounts continue; existing game windows stay open.", "Kontoens kø er stoppet: ID {0} blev ikke registreret i spillet. Kontrollér spilvinduet, kontooplysningerne og charens ID. Andre konti fortsætter; åbne spilvinduer bliver stående." },
            { "ID {0}: detected as {1} in the game window.", "ID {0}: registreret som {1} i spilvinduet." },
            { "All login queues finished. Game clients remain open.", "Alle startkøer er færdige. Spilklienterne kører videre." },
            { "Login queues finished with an error on at least one account. Game clients remain open.", "Startkøerne sluttede med fejl på mindst én konto. Spilklienterne kører videre." },
            { "Could not start ID {0}. The remaining characters on this account were stopped; other accounts continue.", "ID {0} kunne ikke startes. Resten af kontoens kø er stoppet; andre konti fortsætter." },
            { "The active game client could not be saved to the session registry.", "Den aktive spilklient kunne ikke gemmes i oversigten." },
            { "Missing game path.", "Spilmappen mangler." },
            { "Local application data is unavailable.", "Mappen til lokale programdata er ikke tilgængelig." },
            { "LoginHandler.dll could not be loaded into the game client. Check the game window; the login queue has stopped.", "LoginHandler.dll kunne ikke indlæses i spilklienten. Kontrollér spilvinduet; startkøen er stoppet." }
        };
    }
}
