using System.Drawing;
using System.Windows.Forms;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal sealed class PrivacyDialog : EditorDialog
    {
        internal PrivacyDialog() : base(L("Privacy & security", "Privatliv og sikkerhed"), 620)
        {
            ClientSize = new Size(670, 620);
            Section(L("Passwords", "Adgangskoder"),
                L("Saving is optional for each account. Saved passwords use Windows DPAPI encryption for your Windows user and are tied to that account and login endpoint. Uncheck Remember password under Edit account to delete that account's copy, or use Settings → Accounts → Forget passwords for all accounts. Update saved password changes only Nexus's copy.",
                "Lagring er valgfri pr. konto. Gemte adgangskoder krypteres med Windows DPAPI for din Windows-bruger og knyttes til konto og loginadresse. Fjern Husk adgangskode under Rediger konto for at slette kopien, eller brug Indstillinger → Konti → Glem adgangskoder for alle konti. Opdater gemt adgangskode ændrer kun kopien i Nexus."));
            Section(L("Local protection has limits", "Lokal beskyttelse har grænser"),
                L("Passwords must be available in memory during login and are passed to the bundled backend through a private input pipe, not command-line arguments. DPAPI does not protect against malicious software running as your Windows user or an administrator. Game authentication uses the game's existing protocol; Nexus does not add TLS to it. Only use servers and game installations you trust.",
                "Adgangskoder findes i hukommelsen ved login og sendes til den medfølgende loginmotor via et privat inputrør, ikke kommandolinjen. DPAPI beskytter ikke mod ondsindet software under din Windows-bruger eller en administrator. Login bruger spillets eksisterende protokol; Nexus tilføjer ikke TLS. Brug kun servere og spilinstallationer, du stoler på."));
            Section(L("Network connections", "Netværksforbindelser"),
                L("Login connects to the address and port configured for each account. Optional Server news uses HTTPS to anarchy-online.com, project-rk.com and ao-rebirth.com for your selected sources, refreshing every 15 minutes while visible. Server health checks only the currently displayed server: a TCP connection to known login/zone/chat ports plus an official HTTPS status report where available, every 60 seconds while visible. Probes do not send account credentials. Hide or disable these panels in Settings → Sidebar to stop their automatic requests.",
                "Login forbinder til kontoens valgte adresse og port. Valgfrie Servernyheder bruger HTTPS til anarchy-online.com, project-rk.com og ao-rebirth.com for dine valgte kilder, hvert 15. minut mens panelet vises. Serverstatus tjekker kun den viste server: TCP til kendte login-/zone-/chatporte og officiel HTTPS-status, hvis tilgængelig, hvert 60. sekund mens panelet vises. Tjek sender ingen kontooplysninger. Skjul eller slå panelerne fra under Indstillinger → Sidepanel for at stoppe automatiske forespørgsler."));
            Section(L("Updates and links", "Opdateringer og links"),
                L("Optional startup checks and manual checks contact GitHub for RazRn/AO-Nexus releases. Downloads require your confirmation and use GitHub's HTTPS asset hosts. Nexus checks SHA-256 values, package contents and executable version before replacing itself. These checks rely on the GitHub release account; they are not independent publisher signatures. Website and article links open your browser. Remote services can see your IP address and normal request metadata. Nexus has no analytics or automatic crash-report upload.",
                "Valgfrie opstartstjek og manuelle tjek kontakter GitHub for RazRn/AO-Nexus-udgivelser. Download kræver din bekræftelse og bruger GitHubs HTTPS-filservere. Nexus kontrollerer SHA-256, pakkens indhold og programversion før udskiftning. Kontrollen stoler på GitHub-udgiverkontoen; den er ikke en uafhængig digital signatur. Links åbnes i din browser. Eksterne tjenester kan se din IP-adresse og almindelige forespørgselsdata. Nexus har ingen analyse-telemetri eller automatisk upload af fejlrapporter."));
            Section(L("Files and cleanup", "Filer og oprydning"),
                L("Profiles, encrypted credentials, news cache, bundled runtime and update history are stored under %LOCALAPPDATA%\\AONexus. Account names, paths and team presets in AONexus.json are not encrypted. The activity log is session-only. Each log defaults to 5 MB, adjustable under Settings → Updates. Completed update staging files are removed after restart; abandoned stages are retried after 24 hours. One previous EXE is kept for recovery. Cached runtime files may still be used by open game clients and are not removed by update cleanup. Accounts, credentials and game files are preserved.",
                "Profiler, krypterede adgangskoder, nyhedscache, loginmotor og opdateringshistorik ligger under %LOCALAPPDATA%\\AONexus. Kontonavne, stier og hold i AONexus.json er ikke krypterede. Aktivitetsloggen gælder kun denne session. Hver log er som standard højst 5 MB; ændres under Indstillinger → Opdateringer. Færdige opdateringsfiler fjernes efter genstart; efterladte mapper forsøges fjernet efter 24 timer. Én tidligere EXE beholdes til gendannelse. Loginmotorens cache kan bruges af åbne spilvinduer og fjernes ikke ved opdateringsoprydning. Konti, adgangskoder og spilfiler bevares."));
            SaveButton.Visible = false; CancelAction.Text = L("Close", "Luk");
            AcceptButton = CancelAction; ActiveControl = CancelAction;
            Shown += (sender, e) => { Fields.AutoScrollPosition = Point.Empty; CancelAction.Focus(); };
        }
        private void Section(string heading, string text)
        {
            Fields.Controls.Add(new Label { Text = heading, Font = new Font(Font, FontStyle.Bold), AutoSize = true,
                Dock = DockStyle.Top, ForeColor = Theme.Accent, Margin = new Padding(0, 14, 0, 4) });
            AddWrappedNote(text, "privacyNote");
        }
    }
}
