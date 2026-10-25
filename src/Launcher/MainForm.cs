using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using AOLauncher.Localization;
using static AOLauncher.Localization.Texts;

namespace AOLauncher
{
    internal sealed partial class MainForm : Form
    {
        private readonly bool smoke;
        private LauncherSettings settings;
        private readonly Dictionary<string, string> passwords = new Dictionary<string, string>();
        private readonly TreeView tree = new NexusTreeView();
        private readonly Label selectionLabel = new Label();
        private readonly Label detailTitle = new Label();
        private readonly Label detailText = new Label();
        private readonly Label detailHelp = new Label();
        private readonly LinkLabel pathLabel = new LinkLabel();
        private string statusText;
        private LogViewer logViewer;
        private LinkLabel updateAvailableLink;
        private UpdateRelease availableUpdate;
        private readonly CancellationTokenSource updateCancellation = new CancellationTokenSource();
        private string StatusText
        {
            get => statusText;
            set
            {
                if (statusText == value) return;
                statusText = value;
                int separator = value.IndexOf('·');
                AppendLog(separator >= 0 ? value.Substring(separator + 1).Trim() : value);
            }
        }
        private readonly TextBox log = new TextBox();
        private int activityLogBytes;
        private StringBuilder activityLog = new StringBuilder();
        private int missingStartupCharacters;
        private readonly ComboBox presetPicker = new NexusComboBox();
        private readonly List<Control> editingControls = new List<Control>();
        private Button startButton;
        private Button addCharacterButton;
        private Button editButton;
        private Button removeButton;
        private Panel detailsBody;
        private Button createCharacterButton;
        private Button updatePasswordButton;
        private Button stopButton;
        private Button updatePresetButton;
        private Button editPresetButton;
        private Button deletePresetButton;
        private LinkLabel expandAccountsLink;
        private LinkLabel collapseAccountsLink;
        private BackendRunner backendRunner;
        private readonly HashSet<BackendRunner> creationMonitors = new HashSet<BackendRunner>();
        private CancellationTokenSource preparationCancellation;
        private bool refreshing;
        private bool refreshingPresets;
        private bool running;
        private bool stopRequested;
        private bool closing;
        private bool storageBlocked;
        private bool saveErrorShown;
        private string startupError;
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private ToolStripMenuItem trayOpenItem;
        private ToolStripMenuItem trayExitItem;
        private FormWindowState restoreWindowState = FormWindowState.Normal;
        private bool hiddenInTray;
        private bool restoringFromTray;
        private bool confirmingWindowAction;
        internal Func<bool, Tuple<bool, bool>> WindowActionPrompt = null;

        public MainForm(bool smokeMode)
        {
            smoke = smokeMode;
            settings = new LauncherSettings();
            string startupSettingsPath = null;
            if (smoke)
            {
                settings.Accounts.Add(new AccountConfig
                {
                    Label = "The Rubi-Ka expedition", Username = "example-account", GamePath = @"C:\Games\AO-Rebirth", ServerProfileId = "rebirth",
                    Characters = new List<CharacterConfig>
                    {
                        new CharacterConfig { Name = "Aster · Doctor", CharacterId = 123456789 },
                        new CharacterConfig { Name = "Orion · Enforcer", CharacterId = 123456790 },
                        new CharacterConfig { Name = "Vesper · Meta-Physicist", CharacterId = 123456792 }
                    }
                });
                settings.Accounts.Add(new AccountConfig
                {
                    Label = "Weekend wanderers", Username = "second-example-account", GamePath = @"C:\Games\Anarchy-Online", ServerProfileId = "live", Collapsed = true,
                    Characters = new List<CharacterConfig> { new CharacterConfig { Name = "Solstice · Trader", CharacterId = 123456791, Selected = false } }
                });
                settings.Accounts.Add(new AccountConfig
                {
                    Label = "Ithaca explorers", Username = "third-example-account", GamePath = @"C:\Games\Project-Rubi-Ka", ServerProfileId = "prk", Collapsed = true,
                    Characters = new List<CharacterConfig> { new CharacterConfig { Name = "Lyra · Engineer", CharacterId = 123456793, Selected = false } }
                });
                var preset = new LaunchPreset { Name = "Rubi-Ka expedition" };
                foreach (var character in settings.Accounts[0].Characters)
                    preset.Characters.Add(new PresetCharacter { AccountId = settings.Accounts[0].Id, CharacterId = character.CharacterId });
                settings.Presets.Add(preset);
                settings.SelectedPresetId = preset.Id;
            }
            else
            {
                startupSettingsPath = SettingsStore.FilePath;
                try
                {
                    startupSettingsPath = SettingsStore.ReadFilePath;
                    settings = SettingsStore.Read();
                }
                catch (Exception ex)
                {
                    storageBlocked = true;
                    startupError = ex.Message;
                }
            }

            foreach (var account in settings.Accounts) AccountTargets.Initialize(settings, account);
            if (!smoke) missingStartupCharacters = PresetService.ApplyStartupSelection(settings).MissingCount;
            settings.MaxLogSizeMb = LogRetention.NormalizeMb(settings.MaxLogSizeMb);
            if (!smoke) UpdateJournal.Configure(settings.MaxLogSizeMb);
            if (smoke) settings.Language = "en";
            Texts.SetLanguage(settings.Language);
            GuiStrings.Register();
            NexusStrings.Register();
            if (startupError != null) startupError = F("Could not read settings: {0}\r\n\r\nThe existing file is preserved. Changes will not be saved in this session.\r\n{1}", startupError, startupSettingsPath);
            Text = smoke ? T("AO Nexus — preview") : "AO Nexus";
            AppArtwork.ApplyIcon(this);
            Font = new Font("Segoe UI", 10F);
            ForeColor = Theme.Navy;
            BackColor = Theme.Background;
            ClientSize = new Size(1280, 900);
            MinimumSize = new Size(1100, 850);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            BuildLayout();
            RefreshPresetPicker();
            RefreshTree();
            InitializeTray();
            Shown += (sender, e) =>
            {
                if (!smoke) _ = UpdateCleanup.AfterStartup(Application.ExecutablePath);
                RegisterPresetHotkeys();
                BeginInvoke((Action)HideCreationCheckboxes);
                if (startupError != null) NexusMessageBox.Show(this, startupError, T("Could not open settings"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (smoke) AppendLog(T("UI preview uses sample data. It does not log in or change your settings."));
                else AppendLog(T("Ready. Add your accounts and characters, check the ones you want, then choose “Log in selected”."));
                if (missingStartupCharacters > 0) AppendLog(F("{0} saved characters no longer exist in the list and were skipped.", missingStartupCharacters));
                if (!smoke && LauncherBootstrap.ImportedThisSession) AppendLog(T("Copied your AO Launcher profile. AO Nexus now saves changes separately."));
                if (!smoke && UpdateJournal.ConsumeInstalledNotice(AppUpdates.UpdateRoot))
                {
                    AppendLog(F("AO Nexus was updated to version {0}.", AppUpdates.CurrentVersion.ToString(3)));
                    BeginInvoke((Action)(() =>
                    {
                        if (closing || IsDisposed) return;
                        var whatsNew = new WhatsNewDialog();
                        whatsNew.FormClosed += (closedSender, closedArgs) => whatsNew.Dispose();
                        whatsNew.Show(this);
                    }));
                }
                if (!smoke && !storageBlocked && !settings.HasChosenUpdatePreference)
                {
                    var answer = NexusMessageBox.Show(this, T("Check GitHub for updates automatically when Nexus starts?\r\n\r\nYou can change this in Settings at any time. Updates are only downloaded and installed after you choose Install and restart."),
                        T("Automatic update checks"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                    settings.CheckForUpdatesOnStartup = answer == DialogResult.Yes;
                    settings.HasChosenUpdatePreference = true;
                    SaveSettings();
                }
                if (!smoke && settings.CheckForUpdatesOnStartup) CheckStartupUpdate();
            };
            FormClosing += OnFormClosing;
        }

        private NewsPanel newsPanel;
        private ServerSidebar serverSidebar;
        private TableLayoutPanel sidebarLayout;

        private void ConfigureSidebar()
        {
            serverSidebar.Configure(settings);
            bool shown = settings.ShowServerNews || settings.ShowServerHealth;
            sidebarLayout.ColumnStyles[1].Width = shown ? NexusChrome.Scale(this, 358) : 0;
            sidebarLayout.Controls[0].Margin = new Padding(0, 0, shown ? NexusChrome.Scale(this, 18) : 0, 0);
        }

        private void InitializeTray()
        {
            trayMenu = new ContextMenuStrip
            {
                BackColor = Theme.SurfaceRaised, ForeColor = Theme.Foreground, Font = Font,
                ShowImageMargin = false, Padding = new Padding(4),
                Renderer = new NewsPanel.SourceMenuRenderer()
            };
            trayOpenItem = new ToolStripMenuItem { Padding = new Padding(5) };
            trayExitItem = new ToolStripMenuItem { Padding = new Padding(5) };
            trayOpenItem.Click += (sender, e) => RestoreFromTray();
            // Let the native menu finish dismissing before Close disposes it.
            trayExitItem.Click += (sender, e) => BeginInvoke((Action)Close);
            trayMenu.Items.AddRange(new ToolStripItem[] { trayOpenItem, new ToolStripSeparator(), trayExitItem });
            RefreshTrayLanguage();
            trayIcon = new NotifyIcon { Icon = Icon, Text = "AO Nexus", ContextMenuStrip = trayMenu };
            trayIcon.MouseClick += (sender, e) => { if (e.Button == MouseButtons.Left) RestoreFromTray(); };
        }

        private void RefreshTrayLanguage()
        {
            if (trayOpenItem == null) return;
            trayOpenItem.Text = T("Open AO Nexus");
            trayExitItem.Text = T("Exit AO Nexus");
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (trayIcon == null || closing || IsDisposed || restoringFromTray) return;
            if (WindowState == FormWindowState.Minimized)
            {
                if (hiddenInTray || confirmingWindowAction) return;
                confirmingWindowAction = true;
                BeginInvoke((Action)CompleteMinimizeRequest);
            }
            else if (!hiddenInTray) restoreWindowState = WindowState;
        }

        private void RequestMinimize()
        {
            if (hiddenInTray || confirmingWindowAction || closing) return;
            confirmingWindowAction = true;
            CompleteMinimizeRequest();
        }

        private void CompleteMinimizeRequest()
        {
            try
            {
                if (IsDisposed || closing) return;
                restoringFromTray = true;
                if (WindowState == FormWindowState.Minimized) WindowState = restoreWindowState;
                restoringFromTray = false;
                if (ConfirmWindowAction(true)) MinimizeToTray();
            }
            finally { restoringFromTray = false; confirmingWindowAction = false; }
        }

        internal void MinimizeToTray()
        {
            hiddenInTray = true;
            trayIcon.Visible = true;
            Hide();
        }

        private bool ConfirmWindowAction(bool minimize)
        {
            if ((smoke && WindowActionPrompt == null) || (minimize ? settings.SkipTrayConfirmation : settings.SkipQuitConfirmation)) return true;
            bool accepted, remember;
            if (WindowActionPrompt != null)
            {
                var answer = WindowActionPrompt(minimize); accepted = answer.Item1; remember = answer.Item2;
            }
            else
            {
                using (var dialog = new WindowActionDialog(minimize))
                { accepted = dialog.ShowDialog(this) == DialogResult.OK; remember = dialog.DontShowAgain; }
            }
            if (accepted && remember)
            {
                if (minimize) settings.SkipTrayConfirmation = true; else settings.SkipQuitConfirmation = true;
                SaveSettings();
            }
            return accepted;
        }

        private void RestoreFromTray()
        {
            if (closing || IsDisposed || !hiddenInTray) return;
            restoringFromTray = true;
            try
            {
                Show();
                WindowState = restoreWindowState;
                Activate();
                hiddenInTray = false;
                trayIcon.Visible = false;
            }
            finally { restoringFromTray = false; }
        }

        internal void ShowLauncher()
        {
            if (closing || IsDisposed) return;
            if (hiddenInTray) RestoreFromTray();
            else
            {
                Show();
                if (WindowState == FormWindowState.Minimized) WindowState = restoreWindowState;
                Activate();
            }
            // Bring an open account/settings dialog forward with its owner.
            foreach (var owned in OwnedForms.Where(form => form.Visible)) owned.Activate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (trayIcon != null)
                {
                    trayIcon.Visible = false;
                    trayIcon.Dispose();
                    trayIcon = null;
                }
                trayMenu?.Dispose();
                trayMenu = null;
            }
            base.Dispose(disposing);
        }

        private void BuildLayout()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty, Padding = Padding.Empty };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 125));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            Controls.Add(layout);

            var header = new NexusHero { Dock = DockStyle.Fill, Margin = Padding.Empty };
            var headerMark = AppArtwork.CreateHeaderMark();
            headerMark.Location = new Point(27, 38);
            headerMark.Size = new Size(66, 66);
            header.Controls.Add(headerMark);
            header.Controls.Add(new Label { Text = T("YOUR GATEWAY TO RUBI-KA"), Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), ForeColor = Theme.Accent, BackColor = Color.Transparent, Location = new Point(109, 22), Size = new Size(550, 20) });
            header.Controls.Add(new Label { Text = "AO NEXUS", Font = new Font("Segoe UI", 33F, FontStyle.Bold), ForeColor = Theme.Foreground, BackColor = Color.Transparent, Location = new Point(103, 37), Size = new Size(550, 60) });
            var credit = new Label { Text = T("Based on AOQuickLauncher by never-knows-best & contributors · Enhanced by Luckyspawn"), UseMnemonic = false, Font = new Font("Segoe UI", 8F), ForeColor = Color.FromArgb(159, 179, 191), BackColor = Color.Transparent, Location = new Point(110, 99), Size = new Size(710, 19) };
            header.Controls.Add(credit);
            var settingsButton = Theme.Button(T("Settings"));
            settingsButton.AutoSize = false;
            settingsButton.Size = new Size(124, 36);
            settingsButton.Dock = DockStyle.Top;
            settingsButton.Click += OpenSettings;
            var headerActions = new Panel { Dock = DockStyle.Right, Width = 148, Padding = new Padding(0, 22, 24, 7), BackColor = Color.Transparent };
            string versionText = "v" + typeof(MainForm).Assembly.GetName().Version.ToString(3);
            var versionLabel = new Label
            {
                Name = "appVersion", Text = versionText, AccessibleName = "AO Nexus " + versionText,
                Dock = DockStyle.Bottom, Height = 19, TextAlign = ContentAlignment.TopRight,
                Font = new Font("Segoe UI", 8F), ForeColor = Theme.Muted, BackColor = Color.Transparent
            };
            headerActions.Controls.Add(versionLabel);
            updateAvailableLink = new LinkLabel { Text = T("Update available…"), Dock = DockStyle.Bottom, Height = 24, Visible = false,
                LinkColor = Theme.Accent, VisitedLinkColor = Theme.Accent, ActiveLinkColor = Theme.Amber, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleRight };
            updateAvailableLink.LinkClicked += (sender, e) => OpenUpdates(this);
            headerActions.Controls.Add(updateAvailableLink);
            headerActions.Controls.Add(settingsButton);
            header.Controls.Add(headerActions);
            editingControls.Add(settingsButton);
            layout.Controls.Add(header, 0, 0);

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(24, 20, 24, 0) };
            sidebarLayout = body;
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 358));
            layout.Controls.Add(body, 0, 1);
            var workspace = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0, 0, 18, 0) };
            workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 102));
            body.Controls.Add(workspace, 0, 0);

            var toolbar = new FlowLayoutPanel { Name = "accountActions", Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = Padding.Empty, WrapContents = true, Margin = Padding.Empty };
            AddAction(toolbar, T("Add account"), AddAccount);
            addCharacterButton = AddAction(toolbar, T("Add character"), AddCharacter);
            editButton = AddAction(toolbar, T("Edit"), EditSelected);
            removeButton = AddAction(toolbar, T("Remove"), RemoveSelected);
            AddAction(toolbar, T("Select all"), (sender, e) => SetAll(true));
            AddAction(toolbar, T("Clear selection"), (sender, e) => SetAll(false));
            foreach (Button button in toolbar.Controls)
            {
                button.Font = new Font("Segoe UI", 9F);
                button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                button.MinimumSize = new Size(0, 34);
                button.Padding = new Padding(8, 3, 8, 3);
                button.Margin = new Padding(0, 0, 5, 6);
            }

            var presetFrame = new NexusPanel { Name = "presetSection", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12), Padding = new Padding(12) };
            var presets = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Theme.Surface, ColumnCount = 5, RowCount = 2 };
            presets.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int index = 0; index < 4; index++) presets.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            presets.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            presets.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            var presetCaption = Caption(T("Team presets"));
            presets.Controls.Add(presetCaption, 0, 0);
            presets.SetColumnSpan(presetCaption, 5);
            presetPicker.DropDownStyle = ComboBoxStyle.DropDownList;
            presetPicker.FlatStyle = FlatStyle.Flat;
            presetPicker.BackColor = Theme.SurfaceRaised;
            presetPicker.ForeColor = Theme.Foreground;
            Theme.StyleCombo(presetPicker);
            presetPicker.Dock = DockStyle.Fill;
            presetPicker.Margin = new Padding(0, 1, 8, 0);
            presetPicker.SelectionChangeCommitted += ApplySelectedPreset;
            presets.Controls.Add(presetPicker, 0, 1);
            editingControls.Add(presetPicker);
            var savePresetButton = Theme.Button(T("Save new…"));
            savePresetButton.Click += SaveNewPreset;
            updatePresetButton = Theme.Button(T("Update"));
            updatePresetButton.Click += UpdateCurrentPreset;
            editPresetButton = Theme.Button(T("Edit…"));
            editPresetButton.Click += EditCurrentPreset;
            deletePresetButton = Theme.Button(T("Delete"));
            deletePresetButton.Click += DeleteCurrentPreset;
            var presetButtons = new[] { savePresetButton, updatePresetButton, editPresetButton, deletePresetButton };
            for (int index = 0; index < presetButtons.Length; index++)
            {
                presetButtons[index].MinimumSize = new Size(0, 30);
                presetButtons[index].Font = new Font("Segoe UI", 9F);
                presetButtons[index].Padding = new Padding(7, 2, 7, 2);
                presetButtons[index].Margin = new Padding(0, 1, index == presetButtons.Length - 1 ? 0 : 5, 0);
                presets.Controls.Add(presetButtons[index], index + 1, 1);
                editingControls.Add(presetButtons[index]);
            }
            ((NexusComboBox)presetPicker).MatchHeight(savePresetButton);
            presetFrame.Controls.Add(presets);
            workspace.Controls.Add(presetFrame, 0, 0);

            var content = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 2, RowCount = 1 };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var leftFrame = new NexusPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(12), Name = "accountsByServer" };
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty, BackColor = Theme.Surface };
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            leftFrame.Controls.Add(left);
            var accountHeader = new TableLayoutPanel { Name = "accountsHeader", Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            accountHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            accountHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            accountHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            accountHeader.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            var accountCaption = Caption(T("ACCOUNTS BY SERVER"));
            accountCaption.Dock = DockStyle.Fill;
            accountCaption.TextAlign = ContentAlignment.MiddleLeft;
            accountCaption.Margin = Padding.Empty;
            accountHeader.Controls.Add(accountCaption, 0, 0);
            expandAccountsLink = new LinkLabel { Text = T("Expand all"), Name = "expandAccounts" };
            collapseAccountsLink = new LinkLabel { Text = T("Collapse all"), Name = "collapseAccounts" };
            expandAccountsLink.LinkClicked += (sender, e) => SetAccountsCollapsed(false);
            collapseAccountsLink.LinkClicked += (sender, e) => SetAccountsCollapsed(true);
            var accountLinks = new[] { expandAccountsLink, collapseAccountsLink };
            for (int index = 0; index < accountLinks.Length; index++)
            {
                var link = accountLinks[index];
                link.Font = new Font("Segoe UI", 8.5F);
                link.AutoSize = true;
                link.Dock = DockStyle.Fill;
                link.TextAlign = ContentAlignment.MiddleLeft;
                link.LinkBehavior = LinkBehavior.HoverUnderline;
                link.LinkColor = link.VisitedLinkColor = Theme.Accent;
                link.ActiveLinkColor = Theme.Amber;
                link.DisabledLinkColor = Theme.Muted;
                link.TabStop = true;
                link.Margin = new Padding(12, 0, 0, 0);
                accountHeader.Controls.Add(link, index + 1, 0);
                editingControls.Add(link);
            }
            left.Controls.Add(accountHeader, 0, 0);
            left.Controls.Add(toolbar, 0, 1);
            tree.Dock = DockStyle.Fill;
            tree.BorderStyle = BorderStyle.None;
            tree.BackColor = Theme.Surface;
            tree.CheckBoxes = true;
            tree.HideSelection = false;
            tree.FullRowSelect = true;
            tree.ItemHeight = 30;
            tree.Indent = 17;
            tree.ShowLines = false;
            tree.ShowRootLines = true;
            tree.ShowPlusMinus = true;
            tree.ShowNodeToolTips = false;
            tree.ForeColor = Theme.Foreground;
            tree.LineColor = Theme.Muted;
            tree.Font = new Font("Segoe UI", 9F);
            tree.AfterCheck += OnAfterCheck;
            tree.BeforeCheck += (sender, e) => { if (e.Node.Tag is CreationAction) e.Cancel = true; };
            tree.AfterSelect += (sender, e) => UpdateSelection();
            tree.AfterCollapse += (sender, e) => RememberAccountExpansion(e.Node, true);
            tree.AfterExpand += (sender, e) => RememberAccountExpansion(e.Node, false);
            tree.NodeMouseDoubleClick += (sender, e) =>
            {
                if (e.Node?.Tag is CreationAction) CreateCharacter(sender, e);
                else if (e.Node?.Tag is CharacterConfig) EditSelected(sender, e);
            };
            tree.KeyDown += (sender, e) =>
            {
                if (e.KeyCode == Keys.Enter && tree.SelectedNode?.Tag is CreationAction)
                { e.Handled = true; e.SuppressKeyPress = true; CreateCharacter(sender, e); }
            };
            tree.HandleCreated += (sender, e) => HideCreationCheckboxes();
            left.Controls.Add(SlimScrolling.Wrap(tree), 0, 2);
            editingControls.Add(tree);
            content.Controls.Add(leftFrame, 0, 0);

            var rightFrame = new NexusPanel { Dock = DockStyle.Fill, Margin = new Padding(12, 0, 0, 0), Padding = new Padding(12), Name = "selectionDetails" };
            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty, BackColor = Theme.Surface };
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.Controls.Add(Caption(T("DETAILS")), 0, 0);
            detailTitle.Dock = DockStyle.None;
            detailTitle.AutoSize = true;
            detailTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            detailTitle.ForeColor = Theme.Foreground;
            detailTitle.Margin = new Padding(0, 0, 0, 10);
            detailTitle.TextAlign = ContentAlignment.TopLeft;
            detailText.Dock = DockStyle.None;
            detailText.AutoSize = true;
            detailText.ForeColor = Theme.Muted;
            detailText.Font = new Font("Segoe UI", 9F);
            detailText.Margin = Padding.Empty;
            detailHelp.Dock = DockStyle.None;
            detailHelp.AutoSize = true;
            detailHelp.ForeColor = Theme.Muted;
            detailHelp.Font = new Font("Segoe UI", 9F);
            detailHelp.Margin = new Padding(0, 10, 0, 8);
            detailsBody = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty, Name = "selectionDetailsBody", BackColor = Theme.Surface };
            var detailsFlow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
            detailsFlow.Controls.Add(detailTitle);
            detailsFlow.Controls.Add(detailText);
            detailsFlow.Controls.Add(detailHelp);
            detailsBody.Controls.Add(detailsFlow);
            detailsBody.ClientSizeChanged += (sender, e) =>
            {
                int width = Math.Max(80, detailsBody.ClientSize.Width - 2);
                detailTitle.MaximumSize = new Size(width, 0);
                detailText.MaximumSize = new Size(width, 0);
                detailHelp.MaximumSize = new Size(width, 0);
            };
            var detailsViewport = SlimScrolling.Wrap(detailsBody);
            right.Controls.Add(detailsViewport, 0, 1);
            createCharacterButton = Theme.Button(T("Create new char"), true);
            createCharacterButton.Dock = DockStyle.Fill;
            createCharacterButton.MinimumSize = new Size(130, 30);
            createCharacterButton.Margin = new Padding(0, 12, 0, 0);
            createCharacterButton.Click += CreateCharacter;
            right.Controls.Add(createCharacterButton, 0, 2);
            editingControls.Add(createCharacterButton);
            updatePasswordButton = Theme.Button(T("Update saved password"));
            updatePasswordButton.Name = "updateSavedPassword";
            updatePasswordButton.Dock = DockStyle.Fill;
            updatePasswordButton.Margin = new Padding(0, 12, 0, 0);
            updatePasswordButton.Click += UpdateSavedPassword;
            right.Controls.Add(updatePasswordButton, 0, 3);
            editingControls.Add(updatePasswordButton);
            rightFrame.Controls.Add(right);
            content.Controls.Add(rightFrame, 1, 0);
            workspace.Controls.Add(content, 0, 1);

            var logPanel = new NexusPanel { Name = "terminalLog", Dock = DockStyle.Fill, Padding = new Padding(10), Margin = new Padding(0, 8, 0, 0), BackColor = LogViewer.TerminalBackground };
            log.Dock = DockStyle.Fill;
            log.Multiline = true;
            log.ReadOnly = true;
            log.ScrollBars = ScrollBars.Vertical;
            log.BackColor = LogViewer.TerminalBackground;
            log.ForeColor = LogViewer.TerminalForeground;
            log.BorderStyle = BorderStyle.None;
            log.Font = new Font("Consolas", 9F);
            log.AccessibleName = T("Activity log");
            log.Margin = Padding.Empty;
            var logLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            logLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            logLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var openLog = new LinkLabel { Text = T("Open log…"), AutoSize = true, LinkColor = Theme.Accent, ActiveLinkColor = Theme.Amber,
                VisitedLinkColor = Theme.Accent, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(10, 0, 0, 0) };
            openLog.LinkClicked += (sender, e) => OpenLog();
            logLayout.Controls.Add(SlimScrolling.Wrap(log), 0, 0);
            logLayout.Controls.Add(openLog, 1, 0);
            logPanel.Controls.Add(logLayout);
            workspace.Controls.Add(logPanel, 0, 2);

            serverSidebar = new ServerSidebar(smoke);
            newsPanel = serverSidebar.News;
            newsPanel.SelectionPreferencesChanged += (sender, e) =>
            {
                settings.NewsSourceIds = newsPanel.SelectedSourceIds.ToList();
                SaveSettings();
            };
            body.Controls.Add(serverSidebar, 1, 0);
            ConfigureSidebar();

            var footerFrame = new NexusPanel { Dock = DockStyle.Fill, Margin = new Padding(24, 10, 24, 18), Padding = new Padding(14, 10, 10, 10), BackColor = Theme.SurfaceRaised };
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 214));
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            selectionLabel.Dock = DockStyle.Fill;
            selectionLabel.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            selectionLabel.ForeColor = Theme.Foreground;
            selectionLabel.AutoEllipsis = true;
            selectionLabel.Margin = Padding.Empty;
            pathLabel.Dock = DockStyle.Fill;
            pathLabel.ForeColor = Theme.Muted;
            pathLabel.AutoEllipsis = true;
            pathLabel.Font = new Font("Segoe UI", 8.5F);
            pathLabel.LinkColor = Theme.Accent;
            pathLabel.ActiveLinkColor = Theme.Foreground;
            pathLabel.VisitedLinkColor = Theme.Accent;
            pathLabel.LinkBehavior = LinkBehavior.HoverUnderline;
            pathLabel.Margin = Padding.Empty;
            pathLabel.LinkClicked += (sender, e) =>
            {
                string details = BuildSelectedTargetDetails();
                if (string.IsNullOrEmpty(details)) return;
                using (var dialog = new SelectedTargetsDialog(details)) dialog.ShowDialog(this);
            };
            footer.Controls.Add(selectionLabel, 0, 0);
            footer.Controls.Add(pathLabel, 0, 1);
            stopButton = Theme.Button(T("Stop login"));
            stopButton.Dock = DockStyle.Fill;
            stopButton.Enabled = false;
            stopButton.Font = new Font("Segoe UI", 9F);
            stopButton.Margin = new Padding(0, 0, 8, 0);
            stopButton.Click += (sender, e) => StopLogin();
            footer.Controls.Add(stopButton, 1, 0);
            footer.SetRowSpan(stopButton, 2);
            startButton = Theme.Button(T("Log in selected  →"), true);
            startButton.BackColor = Theme.Amber;
            startButton.ForeColor = Theme.Background;
            startButton.FlatAppearance.BorderColor = Theme.Amber;
            startButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(240, 190, 103);
            startButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(199, 140, 47);
            startButton.Dock = DockStyle.Fill;
            startButton.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            startButton.Margin = Padding.Empty;
            startButton.Click += StartSelected;
            footer.Controls.Add(startButton, 2, 0);
            footer.SetRowSpan(startButton, 2);
            footerFrame.Controls.Add(footer);
            layout.Controls.Add(footerFrame, 0, 2);
        }

        private static Label Caption(string text) => new Label {
            Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), ForeColor = Theme.Muted,
            AutoEllipsis = true, Margin = Padding.Empty
        };
        private Button AddAction(Control parent, string text, EventHandler handler)
        {
            var button = Theme.Button(text);
            button.Click += handler;
            parent.Controls.Add(button);
            editingControls.Add(button);
            return button;
        }

        private AccountConfig CurrentAccount => tree.SelectedNode?.Tag as AccountConfig ?? tree.SelectedNode?.Parent?.Tag as AccountConfig;
        private CharacterConfig CurrentCharacter => tree.SelectedNode?.Tag as CharacterConfig;
        private LaunchPreset CurrentPreset => settings.Presets.FirstOrDefault(preset => string.Equals(preset.Id, settings.SelectedPresetId, StringComparison.OrdinalIgnoreCase));

        private sealed class CreationAction
        {
            internal AccountConfig Account { get; set; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TreeItemState
        {
            internal uint Mask;
            internal IntPtr Item;
            internal uint State;
            internal uint StateMask;
            internal IntPtr Text;
            internal int TextLength, Image, SelectedImage, Children;
            internal IntPtr Parameter;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr parameter, ref TreeItemState item);

        private void HideCreationCheckboxes()
        {
            if (!tree.IsHandleCreated) return;
            foreach (var node in AllNodes(tree.Nodes).Where(node => node.Tag is CreationAction))
            {
                var state = new TreeItemState { Mask = 0x0008, Item = node.Handle, StateMask = 0xF000 };
                SendMessage(tree.Handle, 0x113F, IntPtr.Zero, ref state); // TVM_SETITEMW: no checkbox for action rows.
            }
        }

        private sealed class ServerGroup
        {
            internal string Endpoint { get; set; }
            internal string Name { get; set; }
            internal string DisplayName { get; set; }
            internal int MaxAccounts { get; set; }
            internal int MaxCharactersPerAccount { get; set; }
            internal int DefaultCharacterSlots { get; set; }
            internal List<AccountConfig> Accounts { get; set; }
        }

        private List<ServerGroup> BuildServerGroups()
        {
            var groups = settings.Accounts.GroupBy(AccountTargets.Endpoint, StringComparer.OrdinalIgnoreCase)
                .Select(group => new ServerGroup {
                    Endpoint = group.Key, Name = ServerName(group.Key), MaxAccounts = AccountTargets.AccountLimit(settings, group.Key),
                    MaxCharactersPerAccount = AccountTargets.CharacterLimit(settings, group.Key),
                    DefaultCharacterSlots = AccountTargets.ServerCharacterSlotLimit(settings, group.Key),
                    Accounts = group.OrderBy(account => account.Label, StringComparer.CurrentCultureIgnoreCase)
                        .ThenBy(account => account.Username, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(account => account.Id, StringComparer.Ordinal).ToList()
                }).ToList();
            foreach (var group in groups)
                group.DisplayName = groups.Count(other => string.Equals(other.Name, group.Name, StringComparison.CurrentCultureIgnoreCase)) > 1
                    ? group.Name + " — " + group.Endpoint : group.Name;
            return groups.OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(group => group.Endpoint, StringComparer.Ordinal).ToList();
        }

        private string ServerName(string endpoint)
        {
            var profiles = settings.LoginServers.Where(profile =>
                AccountTargets.Endpoint(new AccountConfig { Server = profile.Server, Port = profile.Port }) == endpoint).ToArray();
            var names = profiles.Select(profile => profile.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (names.Length == 1) return names[0];
            var linkedNames = profiles.Where(profile => settings.Accounts.Any(account =>
                AccountTargets.Endpoint(account) == endpoint && account.ServerProfileId == profile.Id))
                .Select(profile => profile.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return linkedNames.Length == 1 ? linkedNames[0] : T("Custom server") + " — " + endpoint;
        }

        private static bool SameTreeItem(object left, object right) => ReferenceEquals(left, right) ||
            (left is CreationAction x && right is CreationAction y && ReferenceEquals(x.Account, y.Account)) ||
            (left is ServerGroup a && right is ServerGroup b && string.Equals(a.Endpoint, b.Endpoint, StringComparison.OrdinalIgnoreCase));

        private static IEnumerable<TreeNode> AllNodes(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                yield return node;
                foreach (var child in AllNodes(node.Nodes)) yield return child;
            }
        }

        private bool ServerCollapsed(string endpoint) => settings.CollapsedServers.Contains(endpoint, StringComparer.OrdinalIgnoreCase);

        private void SetServerCollapsed(string endpoint, bool collapsed)
        {
            settings.CollapsedServers.RemoveAll(value => string.Equals(value, endpoint, StringComparison.OrdinalIgnoreCase));
            if (collapsed) settings.CollapsedServers.Add(endpoint);
        }

        private static string ServerCaption(ServerGroup group)
        {
            int accounts = SelectedServerAccounts(group);
            return F("{0}  ·  {1}/{2} accounts selected", group.DisplayName, accounts,
                group.MaxAccounts == 0 ? "∞" : group.MaxAccounts.ToString());
        }

        private static int SelectedServerAccounts(ServerGroup group) => group.Accounts
            .Where(account => account.Characters.Any(character => character.Selected))
            .Select(account => account.Username.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        private static bool ServerLimitExceeded(ServerGroup group) =>
            (group.MaxAccounts > 0 && SelectedServerAccounts(group) > group.MaxAccounts) ||
            (group.MaxCharactersPerAccount > 0 && group.Accounts.GroupBy(account => account.Username.Trim(), StringComparer.OrdinalIgnoreCase)
                .Any(accounts => accounts.Sum(account => account.Characters.Count(character => character.Selected)) > group.MaxCharactersPerAccount));

        private static string FormatLimit(int limit) => limit == 0 ? T("Unlimited") : limit.ToString();

        private static string ServerLimitText(ServerGroup group) =>
            F("Max. accounts online: {0}", FormatLimit(group.MaxAccounts)) + "\r\n"
            + F("Max. chars online/account: {0}", FormatLimit(group.MaxCharactersPerAccount)) + "\r\n"
            + F("Default char slots/account: {0}", group.DefaultCharacterSlots == 0
                ? T("No launcher limit / unknown") : group.DefaultCharacterSlots.ToString())
            + (group.Accounts.Any(account => account.CharacterSlots.HasValue)
                ? "\r\n" + T("Account slot overrides apply individually.") : string.Empty);

        private static string CharacterSlotSource(AccountConfig account) => account.CharacterSlots.HasValue
            ? T("Slot setting: account override") : T("Slot setting: server default");

        private sealed class PresetChoice
        {
            internal LaunchPreset Preset { get; }
            private readonly bool startupDefault;
            internal PresetChoice(LaunchPreset preset, bool startupDefault = false) { Preset = preset; this.startupDefault = startupDefault; }
            public override string ToString() => Preset == null ? T("No preset selected") : Preset.Name + (startupDefault ? " · " + T("Startup default") : "");
        }

        private void RefreshPresetPicker()
        {
            refreshingPresets = true;
            try
            {
                presetPicker.Items.Clear();
                presetPicker.Items.Add(new PresetChoice(null));
                presetPicker.SelectedIndex = 0;
                foreach (var preset in settings.Presets)
                {
                    var choice = new PresetChoice(preset, string.Equals(preset.Id, settings.DefaultPresetId, StringComparison.OrdinalIgnoreCase));
                    presetPicker.Items.Add(choice);
                    if (string.Equals(preset.Id, settings.SelectedPresetId, StringComparison.OrdinalIgnoreCase))
                        presetPicker.SelectedItem = choice;
                }
            }
            finally { refreshingPresets = false; }
            UpdatePresetStatus();
        }

        private void UpdatePresetStatus()
        {
            var preset = CurrentPreset;
            updatePresetButton.Enabled = preset != null && !running && !PresetService.Matches(settings, preset);
            editPresetButton.Enabled = preset != null && !running;
            deletePresetButton.Enabled = preset != null && !running;
        }

        private void ApplySelectedPreset(object sender, EventArgs e)
        {
            if (refreshingPresets || running) return;
            var choice = presetPicker.SelectedItem as PresetChoice;
            if (choice?.Preset == null)
            {
                settings.SelectedPresetId = null;
                SaveSettings();
                UpdatePresetStatus();
                return;
            }
            try
            {
                var result = PresetService.Apply(settings, choice.Preset);
                SaveSettings();
                RefreshTree();
                AppendLog(F("Preset “{0}” selected: {1} characters checked.", choice.Preset.Name, result.SelectedCount));
                if (result.MissingCount > 0)
                    AppendLog(F("{0} saved characters no longer exist in the list and were skipped.", result.MissingCount));
            }
            catch (ArgumentException ex) { ShowInfo(ex.Message); RefreshPresetPicker(); }
            catch (InvalidOperationException ex) { ShowInfo(ex.Message); RefreshPresetPicker(); }
        }

        private void SaveNewPreset(object sender, EventArgs e)
        {
            EditPreset(null);
        }

        private void UpdateCurrentPreset(object sender, EventArgs e)
        {
            var preset = CurrentPreset;
            if (running || shortcutActionBusy || storageBlocked || preset == null) return;
            ChangePreset(() => PresetService.Update(settings, preset));
        }

        private void EditCurrentPreset(object sender, EventArgs e)
        {
            var preset = CurrentPreset;
            if (running || preset == null) return;
            EditPreset(preset);
        }

        private void EditPreset(LaunchPreset preset)
        {
            if (running || shortcutActionBusy || storageBlocked) return;
            editingPreset = true;
            UnregisterPresetHotkeys();
            try
            {
                using (var dialog = new PresetNameDialog(preset == null ? T("Create team preset") : T("Edit team preset"),
                    preset?.Name ?? "", settings.Presets.Where(item => !ReferenceEquals(item, preset)).Select(item => item.Name),
                    preset?.StartHotkey, preset?.CampHotkey, settings.Accounts.Sum(account => account.Characters.Count(character => character.Selected)), preset != null))
                {
                    dialog.UseAsStartupDefault = preset != null && string.Equals(preset.Id, settings.DefaultPresetId, StringComparison.OrdinalIgnoreCase);
                    dialog.ValidateShortcuts = (start, logout) => ValidatePresetHotkeys(preset, start, logout);
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    ChangePreset(() =>
                    {
                        var target = preset ?? PresetService.Create(settings, dialog.PresetName);
                        if (preset != null)
                        {
                            PresetService.Rename(settings, target, dialog.PresetName);
                        }
                        target.StartHotkey = dialog.StartHotkey;
                        target.CampHotkey = dialog.LogoutHotkey;
                        PresetService.SetStartupDefault(settings, target, dialog.UseAsStartupDefault);
                    });
                }
            }
            finally { editingPreset = false; RegisterPresetHotkeys(); }
        }

        private void DeleteCurrentPreset(object sender, EventArgs e)
        {
            var preset = CurrentPreset;
            if (running || preset == null) return;
            if (NexusMessageBox.Show(this, F("Delete the saved preset “{0}”?", preset.Name), T("Delete preset"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            ChangePreset(() => PresetService.Delete(settings, preset));
        }

        private void ChangePreset(Action change)
        {
            try
            {
                change();
                SaveSettings();
                RegisterPresetHotkeys();
                RefreshPresetPicker();
                UpdateSelection();
            }
            catch (ArgumentException ex) { ShowInfo(ex.Message); }
            catch (InvalidOperationException ex) { ShowInfo(ex.Message); }
        }

        private void RefreshTree(object selected = null)
        {
            bool revealSelected = selected != null;
            bool expansionChanged = false;
            selected = selected ?? tree.SelectedNode?.Tag;
            object previousTop = tree.TopNode?.Tag;
            refreshing = true;
            tree.BeginUpdate();
            try
            {
                tree.Nodes.Clear();
                TreeNode selectedNode = null;
                foreach (var group in BuildServerGroups())
                {
                    var groupCharacters = group.Accounts.SelectMany(account => account.Characters).ToArray();
                    var serverNode = new TreeNode {
                        Tag = group, Checked = groupCharacters.Length > 0 && groupCharacters.All(character => character.Selected),
                        ForeColor = ServerLimitExceeded(group) ? Theme.Warning : Theme.Accent, BackColor = Theme.SurfaceRaised,
                        Text = ServerCaption(group)
                    };
                    tree.Nodes.Add(serverNode);
                    if (SameTreeItem(selected, group)) selectedNode = serverNode;
                    foreach (var account in group.Accounts)
                    {
                        var node = new TreeNode(AccountCaption(account)) {
                            Tag = account, Checked = account.Characters.Count > 0 && account.Characters.All(c => c.Selected)
                        };
                        serverNode.Nodes.Add(node);
                        bool selectedAccount = ReferenceEquals(selected, account) || account.Characters.Any(character => ReferenceEquals(selected, character));
                        if (revealSelected && selectedAccount && ServerCollapsed(group.Endpoint))
                        {
                            SetServerCollapsed(group.Endpoint, false);
                            expansionChanged = true;
                        }
                        if (ReferenceEquals(selected, account)) selectedNode = node;
                        foreach (var character in account.Characters)
                        {
                            var child = new TreeNode(character.NamePending ? F("Character {0} (name detected at login)", character.CharacterId) : character.Name) {
                                Tag = character, Checked = character.Selected
                            };
                            node.Nodes.Add(child);
                            if (ReferenceEquals(selected, character))
                            {
                                if (revealSelected && account.Collapsed)
                                {
                                    account.Collapsed = false;
                                    expansionChanged = true;
                                }
                                selectedNode = account.Collapsed ? node : child;
                            }
                        }
                        if (AccountTargets.CanCreateCharacter(settings, account))
                        {
                            var action = new CreationAction { Account = account };
                            var actionNode = new TreeNode("+ " + T("Create new char")) {
                                Tag = action, ForeColor = Theme.Accent
                            };
                            node.Nodes.Add(actionNode);
                            if (SameTreeItem(selected, action)) selectedNode = account.Collapsed ? node : actionNode;
                        }
                        if (!account.Collapsed) node.Expand();
                    }
                    if (!ServerCollapsed(group.Endpoint)) serverNode.Expand();
                }
                // Restore to the nearest visible ancestor without reopening a collapsed group.
                for (var ancestor = selectedNode?.Parent; ancestor != null; ancestor = ancestor.Parent)
                    if (!ancestor.IsExpanded) selectedNode = ancestor;
                tree.SelectedNode = selectedNode ?? (tree.Nodes.Count > 0 ? tree.Nodes[0] : null);
                foreach (var node in AllNodes(tree.Nodes))
                {
                    if (!SameTreeItem(previousTop, node.Tag)) continue;
                    var top = node;
                    for (var ancestor = node.Parent; ancestor != null; ancestor = ancestor.Parent)
                        if (!ancestor.IsExpanded) top = ancestor;
                    tree.TopNode = top;
                    break;
                }
                if (revealSelected) tree.SelectedNode?.EnsureVisible();
            }
            finally { tree.EndUpdate(); refreshing = false; }
            HideCreationCheckboxes();
            if (expansionChanged) SaveSettings();
            UpdateSelection();
        }

        private void RememberAccountExpansion(TreeNode node, bool collapsed)
        {
            if (refreshing || running) return;
            if (node.Tag is AccountConfig account) account.Collapsed = collapsed;
            else if (node.Tag is ServerGroup group) SetServerCollapsed(group.Endpoint, collapsed);
            else return;
            if (collapsed)
                for (var ancestor = tree.SelectedNode?.Parent; ancestor != null; ancestor = ancestor.Parent)
                    if (ReferenceEquals(ancestor, node)) { tree.SelectedNode = node; break; }
            SaveSettings();
        }

        private void SetAccountsCollapsed(bool collapsed)
        {
            if (running) return;
            refreshing = true;
            tree.BeginUpdate();
            try
            {
                if (collapsed)
                    while (tree.SelectedNode?.Parent != null) tree.SelectedNode = tree.SelectedNode.Parent;
                foreach (var account in settings.Accounts) account.Collapsed = collapsed;
                foreach (TreeNode serverNode in tree.Nodes)
                {
                    SetServerCollapsed(((ServerGroup)serverNode.Tag).Endpoint, collapsed);
                    foreach (TreeNode accountNode in serverNode.Nodes)
                    {
                        if (collapsed) accountNode.Collapse(true);
                        else accountNode.Expand();
                    }
                    if (collapsed) serverNode.Collapse(true);
                    else serverNode.Expand();
                }
            }
            finally { tree.EndUpdate(); refreshing = false; }
            SaveSettings();
            UpdateSelection();
        }

        private static string AccountCaption(AccountConfig account)
        {
            return F("{0}  ({1}/{2} selected)", account.Label, account.Characters.Count(c => c.Selected), account.Characters.Count);
        }

        private void OnAfterCheck(object sender, TreeViewEventArgs e)
        {
            if (refreshing || running) return;
            refreshing = true;
            try
            {
                var account = e.Node.Tag as AccountConfig;
                var character = e.Node.Tag as CharacterConfig;
                var group = e.Node.Tag as ServerGroup;
                if (group != null)
                {
                    foreach (var item in group.Accounts.SelectMany(value => value.Characters)) item.Selected = e.Node.Checked;
                }
                else if (account != null)
                {
                    foreach (var item in account.Characters) item.Selected = e.Node.Checked;
                }
                else if (character != null) character.Selected = e.Node.Checked;
                foreach (var node in AllNodes(tree.Nodes))
                {
                    if (node.Tag is CharacterConfig item) node.Checked = item.Selected;
                    else if (node.Tag is AccountConfig owner)
                    {
                        node.Checked = owner.Characters.Count > 0 && owner.Characters.All(value => value.Selected);
                        node.Text = AccountCaption(owner);
                    }
                    else if (node.Tag is ServerGroup server)
                    {
                        var characters = server.Accounts.SelectMany(value => value.Characters).ToArray();
                        node.Checked = characters.Length > 0 && characters.All(value => value.Selected);
                        node.Text = ServerCaption(server);
                        node.ForeColor = ServerLimitExceeded(server) ? Theme.Warning : Theme.Accent;
                    }
                }
            }
            finally { refreshing = false; }
            SaveSettings();
            UpdateSelection();
        }

        private void UpdateSelection()
        {
            var selectedAccounts = settings.Accounts.Where(a => a.Characters.Any(c => c.Selected)).ToArray();
            int count = selectedAccounts.Sum(a => a.Characters.Count(c => c.Selected));
            int accounts = selectedAccounts.Length;
            selectionLabel.Text = F("{0} {1} selected across {2} {3}", count, count == 1 ? T("character") : T("characters"), accounts, accounts == 1 ? T("account") : T("accounts"));
            UpdateTargetSummary(selectedAccounts);
            startButton.Enabled = count > 0 && !running && !smoke;
            addCharacterButton.Enabled = CurrentAccount != null && !running;
            editButton.Enabled = CurrentAccount != null && !running;
            removeButton.Enabled = CurrentAccount != null && !(tree.SelectedNode?.Tag is CreationAction) && !running;
            expandAccountsLink.Enabled = collapseAccountsLink.Enabled = settings.Accounts.Count > 0 && !running;
            var account = CurrentAccount;
            var character = CurrentCharacter;
            bool creation = tree.SelectedNode?.Tag is CreationAction;
            updatePasswordButton.Visible = tree.SelectedNode?.Tag is AccountConfig;
            updatePasswordButton.Enabled = updatePasswordButton.Visible && !running && !smoke && !storageBlocked;
            createCharacterButton.Visible = creation && account != null && AccountTargets.CanCreateCharacter(settings, account);
            createCharacterButton.Enabled = createCharacterButton.Visible && !running && !smoke;
            detailHelp.Visible = creation;
            detailHelp.Text = creation
                ? T("Keep AO Nexus open while you create the char. Once it enters the game, its ID and name are added here automatically. It starts unchecked and is not added to your saved teams.")
                : T("Select an account or char to see its details. Double-click a char to edit it.");
            if (creation)
            {
                detailTitle.Text = T("Create new char");
                detailText.Text = F("Account: {0}\r\n{1}", account.Label, CharacterSlotSummary(settings, account));
            }
            else if (character != null)
            {
                detailTitle.Text = character.NamePending ? F("Character {0}", character.CharacterId) : character.Name;
                detailText.Text = F("Account: {0}\r\nCharacter ID: {1}\r\n{2}", account.Label, character.CharacterId, character.Selected ? T("Selected for the next login") : T("Not selected for the next login"));
            }
            else if (account != null)
            {
                detailTitle.Text = account.Label;
                detailText.Text = F("Username: {0}\r\n{1}\r\n{2}", account.Username, CharacterSlotSummary(settings, account), !account.RememberPassword ? T("Ask for password at each login") : passwords.ContainsKey(account.Id) ? T("Password ready for login") : T("Saved passwords are used at login"));
                detailText.Text += "\r\n" + CharacterSlotSource(account);
            }
            else if (tree.SelectedNode?.Tag is ServerGroup group)
            {
                var characters = group.Accounts.SelectMany(value => value.Characters).ToArray();
                detailTitle.Text = group.Name;
                detailText.Text = F("Login address: {0}\r\nSelected for login: {1} accounts · {2}/{3} chars",
                    group.Endpoint, SelectedServerAccounts(group), characters.Count(value => value.Selected), characters.Length)
                    + "\r\n\r\n" + ServerLimitText(group);
                if (ServerLimitExceeded(group)) detailText.Text += "\r\n" + T("Selection exceeds the server limits.");
            }
            else
            {
                detailTitle.Text = T("Build your team");
                detailText.Text = T("Start with “Add account”. Save several characters per account and choose your team using the checkboxes.");
            }
            if (account != null) detailText.Text += "\r\n" + F("Server: {0}", ServerName(AccountTargets.Endpoint(account)));
            UpdatePresetStatus();
        }

        internal static string CharacterSlotSummary(LauncherSettings value, AccountConfig account)
        {
            int limit = AccountTargets.CharacterSlotLimit(value, account);
            int count = AccountTargets.KnownCharacterCount(value, account);
            return limit == 0 ? F("{0} chars listed · slot limit unknown", count) : F("{0}/{1} char slots listed", count, limit);
        }

        private void UpdateTargetSummary(IReadOnlyList<AccountConfig> accounts)
        {
            pathLabel.Enabled = accounts.Count > 0;
            if (accounts.Count == 0)
            {
                pathLabel.Text = T("Select characters to see servers and game folders.");
                return;
            }
            int servers = accounts.Select(AccountTargets.Endpoint).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var folders = accounts.Select(account => NormalizeSummaryFolder(account.GamePath)).ToArray();
            int folderCount = folders.Where(folder => folder != null).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            int missing = folders.Count(folder => folder == null);
            pathLabel.Text = F("{0} {1} · {2} {3}", servers, servers == 1 ? T("login server") : T("login servers"),
                folderCount, folderCount == 1 ? T("game folder") : T("game folders"));
            if (missing > 0) pathLabel.Text += " · " + F("{0} missing", missing);
            pathLabel.Text += " · " + T("Details…");
        }

        private string BuildSelectedTargetDetails()
        {
            return string.Join("\r\n\r\n", settings.Accounts.Where(account => account.Characters.Any(character => character.Selected)).Select(account =>
            {
                string endpoint = AccountTargets.Endpoint(account);
                var profile = settings.LoginServers.FirstOrDefault(item => item.Id == account.ServerProfileId &&
                    AccountTargets.Endpoint(new AccountConfig { Server = item.Server, Port = item.Port }) == endpoint);
                string server = profile == null ? endpoint : profile.Name + " (" + endpoint + ")";
                int count = account.Characters.Count(character => character.Selected);
                return F("{0} — {1} {2}\r\nServer: {3}\r\nGame folder: {4}", account.Label, count,
                    count == 1 ? T("character") : T("characters"), server,
                    string.IsNullOrWhiteSpace(account.GamePath) ? T("Not set") : account.GamePath);
            }));
        }

        private static string NormalizeSummaryFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string value = path.Trim().Trim('"');
            if (value.Length == 0) return null;
            try
            {
                string fullPath = Path.GetFullPath(value);
                return fullPath.Length == Path.GetPathRoot(fullPath).Length ? fullPath : fullPath.TrimEnd('\\', '/');
            }
            catch (ArgumentException) { return value; }
            catch (NotSupportedException) { return value; }
            catch (IOException) { return value; }
            catch (System.Security.SecurityException) { return value; }
        }

        private void SaveSettings()
        {
            if (smoke || storageBlocked) return;
            try { SettingsStore.Save(settings); saveErrorShown = false; }
            catch (Exception ex)
            {
                AppendLog(F("Could not save settings: {0}", ex.Message));
                if (!saveErrorShown)
                {
                    saveErrorShown = true;
                    NexusMessageBox.Show(this, F("Your changes are in memory but could not be saved.\r\n{0}", ex.Message), T("Could not save"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void AddAccount(object sender, EventArgs e)
        {
            using (var dialog = new AccountDialog(null, settings))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var account = new AccountConfig { Label = dialog.AccountLabel, Username = dialog.Username, PrefsPath = dialog.PrefsPath, GamePath = dialog.GamePath, Server = dialog.Server, Port = dialog.Port, ServerProfileId = dialog.ServerProfileId, CharacterSlots = dialog.CharacterSlots, RememberPassword = dialog.RememberPassword };
                if (settings.Accounts.Any(a => AccountTargets.SameServer(a, account) && string.Equals(a.Username, dialog.Username, StringComparison.OrdinalIgnoreCase)))
                { ShowInfo(T("This account already exists. Add more characters under the existing account.")); return; }
                string importError;
                if (!TryMergeImportedCharacters(settings, account, dialog.ImportedCharacterIds, out importError))
                { ShowInfo(importError); return; }
                settings.Accounts.Add(account);
                SaveSettings();
                RefreshTree(account);
            }
        }

        private void AddCharacter(object sender, EventArgs e)
        {
            var account = CurrentAccount;
            if (account == null) return;
            using (var dialog = new CharacterDialog(account.Label))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (settings.Accounts.Where(a => AccountTargets.SameServer(a, account)).SelectMany(a => a.Characters).Any(c => c.CharacterId == dialog.CharacterId))
                { ShowInfo(T("A character with this Character ID is already listed.")); return; }
                var character = new CharacterConfig { Name = dialog.CharacterName, CharacterId = dialog.CharacterId, NamePending = dialog.NamePending };
                account.Characters.Add(character);
                SaveSettings();
                RefreshTree(character);
            }
        }

        private void EditSelected(object sender, EventArgs e)
        {
            if (running) return;
            var account = CurrentAccount;
            var character = CurrentCharacter;
            if (account == null) return;
            if (character != null)
            {
                using (var dialog = new CharacterDialog(account.Label, character))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    if (settings.Accounts.Where(a => AccountTargets.SameServer(a, account)).SelectMany(a => a.Characters).Any(c => !ReferenceEquals(c, character) && c.CharacterId == dialog.CharacterId))
                    { ShowInfo(T("Another character already has this Character ID.")); return; }
                    character.Name = dialog.CharacterName;
                    character.CharacterId = dialog.CharacterId;
                    character.NamePending = dialog.NamePending;
                }
            }
            else
            {
                using (var dialog = new AccountDialog(account, settings))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    var candidate = new AccountConfig { Id = account.Id, Label = dialog.AccountLabel, Username = dialog.Username, PrefsPath = dialog.PrefsPath, GamePath = dialog.GamePath, Server = dialog.Server, Port = dialog.Port, ServerProfileId = dialog.ServerProfileId, CharacterSlots = dialog.CharacterSlots, RememberPassword = dialog.RememberPassword, Characters = account.Characters.ToList() };
                    if (settings.Accounts.Any(a => a.Id != account.Id && AccountTargets.SameServer(a, candidate) && string.Equals(a.Username, dialog.Username, StringComparison.OrdinalIgnoreCase)))
                    { ShowInfo(T("Another account already has this username.")); return; }
                    string importError;
                    if (!TryMergeImportedCharacters(settings, candidate, dialog.ImportedCharacterIds, out importError))
                    { ShowInfo(importError); return; }
                    if (!candidate.RememberPassword || account.Username != dialog.Username || !AccountTargets.SameServer(account, candidate)) { passwords.Remove(account.Id); DeleteSavedPassword(account); }
                    account.Label = dialog.AccountLabel;
                    account.Username = dialog.Username;
                    account.PrefsPath = dialog.PrefsPath;
                    account.GamePath = candidate.GamePath;
                    account.Server = candidate.Server;
                    account.Port = candidate.Port;
                    account.ServerProfileId = candidate.ServerProfileId;
                    account.CharacterSlots = candidate.CharacterSlots;
                    account.RememberPassword = candidate.RememberPassword;
                    account.Characters = candidate.Characters;
                }
            }
            SaveSettings();
            RefreshTree(character != null ? (object)character : account);
        }

        internal static bool TryMergeImportedCharacters(LauncherSettings value, AccountConfig account, IEnumerable<int> importedIds, out string error)
        {
            error = null;
            int[] ids = (importedIds ?? Enumerable.Empty<int>()).Distinct().ToArray();
            if (ids.Any(id => id <= 0)) { error = T("The import contains an invalid character ID. No changes were saved."); return false; }
            foreach (int id in ids.Concat(account.Characters.Select(character => character.CharacterId)).Distinct())
            {
                var owner = value.Accounts.FirstOrDefault(other => other.Id != account.Id && AccountTargets.SameServer(other, account) && other.Characters.Any(character => character.CharacterId == id));
                if (owner != null)
                {
                    error = F("Character ID {0} is already listed under account “{1}”. Choose the correct Prefs folder. No changes were saved.", id, owner.Label);
                    return false;
                }
            }
            foreach (int id in ids)
            {
                if (!account.Characters.Any(character => character.CharacterId == id))
                    account.Characters.Add(new CharacterConfig { CharacterId = id, Name = F("Character {0}", id), NamePending = true, Selected = false });
            }
            return true;
        }

        private void RemoveSelected(object sender, EventArgs e)
        {
            if (tree.SelectedNode?.Tag is CreationAction) return;
            var account = CurrentAccount;
            var character = CurrentCharacter;
            if (account == null) return;
            string message = character != null ? F("Remove “{0}” from the list?", character.Name) : F("Remove “{0}” and its characters from the list?", account.Label);
            if (NexusMessageBox.Show(this, message + T("\r\nThis changes the launcher list only."), T("Remove from list"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            if (character != null) account.Characters.Remove(character);
            else { DeleteSavedPassword(account); settings.Accounts.Remove(account); passwords.Remove(account.Id); }
            SaveSettings();
            RefreshTree();
        }

        private void SetAll(bool selected)
        {
            foreach (var character in settings.Accounts.SelectMany(a => a.Characters)) character.Selected = selected;
            SaveSettings();
            RefreshTree();
        }

        private void OpenSettings(object sender, EventArgs e)
        {
            using (var dialog = new SettingsDialog(settings))
            {
                try { dialog.StartWithWindows = !smoke && WindowsStartup.IsEnabled; }
                catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException)
                { ShowInfo(T("Windows startup settings could not be read.") + "\r\n" + ex.Message); return; }
                dialog.SaveStartupAction = enabled =>
                {
                    if (smoke) return null;
                    try { WindowsStartup.SetEnabled(enabled, Application.ExecutablePath); return null; }
                    catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException)
                    { return T("Windows startup settings could not be saved.") + "\r\n" + ex.Message; }
                };
                dialog.ForgetPasswordsAction = () => ForgetPasswords(null, EventArgs.Empty);
                dialog.CheckUpdatesAction = () => { if (smoke) return; OpenUpdates(dialog); };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var previousTargets = settings.Accounts.ToDictionary(account => account.Id, AccountTargets.Endpoint);
                try { AccountTargets.ChangeProfiles(settings, dialog.LoginServers); }
                catch (ArgumentException ex) { ShowInfo(ex.Message); return; }
                foreach (var account in settings.Accounts)
                {
                    if (previousTargets[account.Id] != AccountTargets.Endpoint(account)) { passwords.Remove(account.Id); DeleteSavedPassword(account); }
                }
                settings.Language = dialog.Language;
                settings.CheckForUpdatesOnStartup = dialog.CheckForUpdatesOnStartup;
                settings.HasChosenUpdatePreference = true;
                settings.SkipTrayConfirmation = dialog.SkipTrayConfirmation;
                settings.SkipQuitConfirmation = dialog.SkipQuitConfirmation;
                settings.MaxLogSizeMb = dialog.MaxLogSizeMb;
                if (!smoke) UpdateJournal.Configure(settings.MaxLogSizeMb);
                string retained = LogRetention.Tail(activityLog.ToString(), settings.MaxLogSizeMb * 1024 * 1024);
                activityLog = new StringBuilder(retained);
                activityLogBytes = Encoding.UTF8.GetByteCount(retained);
                log.Text = LogRetention.Tail(retained, 16000);
                if (logViewer != null && !logViewer.IsDisposed) logViewer.SetLogText(retained);
                settings.ShowServerNews = dialog.ShowServerNews;
                settings.ShowServerHealth = dialog.ShowServerHealth;
                settings.NewsSourceIds = dialog.NewsSourceIds;
                settings.HealthServerIds = dialog.HealthServerIds;
                settings.HealthCheckEndpoints = dialog.HealthCheckEndpoints;
                SaveSettings();
                ApplyLanguage();
                UpdateSelection();
            }
        }

        private void ApplyLanguage()
        {
            Texts.SetLanguage(settings.Language);
            GuiStrings.Register();
            NexusStrings.Register();
            SuspendLayout();
            GuiStrings.Retranslate(this);
            NexusStrings.Retranslate(this);
            if (serverSidebar != null) ConfigureSidebar();
            if (logViewer != null && !logViewer.IsDisposed) logViewer.RefreshLanguage();
            RefreshTrayLanguage();
            RefreshPresetPicker();
            RefreshTree(tree.SelectedNode?.Tag);
            ResumeLayout(true);
        }

        private async void StartSelected(object sender, EventArgs e)
        {
            if (shortcutActionBusy) return;
            await StartSelectedCore();
        }

        private async Task StartSelectedCore()
        {
            if (running || smoke) return;
            var accounts = settings.Accounts.Where(a => a.Characters.Any(c => c.Selected)).ToList();
            if (accounts.Count == 0) return;
            if (!ConfirmSelectedLaunch()) return;
            foreach (var account in accounts)
                if (!EnsureGameFolder(account)) return;
            try { ValidateSelectedAccounts(settings); }
            catch (ArgumentException ex) { ShowInfo(ex.Message); return; }
            if (!EnsurePasswords(accounts, out var credentials)) return;
            LaunchRequest request;
            try { request = CreateLaunchRequest(settings, credentials); }
            finally { credentials.Clear(); }
            await RunLaunchRequestAsync(request, accounts, false);
        }

        private async void CreateCharacter(object sender, EventArgs e)
        {
            var account = CurrentAccount;
            if (running || smoke || account == null) return;
            if (!AccountTargets.CanCreateCharacter(settings, account))
            { ShowInfo(T("All configured character slots are listed. Check the account's character-slot setting or remove deleted characters from the launcher.")); return; }
            string question = F("Open character creation for {0} on {1}?\r\n\r\nYou will choose the new char's name and appearance in the game.",
                account.Label, AccountTargets.ServerName(settings, account));
            if (NexusMessageBox.Show(this, question, T("Create new char"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            if (!EnsureGameFolder(account)) return;
            var accounts = new List<AccountConfig> { account };
            if (!EnsurePasswords(accounts, out var credentials)) return;
            try
            {
                LaunchRequest request;
                try { request = CreateCharacterRequest(settings, account, credentials[account.Id]); }
                finally { credentials.Clear(); }
                await RunLaunchRequestAsync(request, accounts, true);
            }
            catch (ArgumentException ex) { ShowInfo(ex.Message); }
        }

        private bool EnsurePasswords(IEnumerable<AccountConfig> accounts, out Dictionary<string, string> credentials)
        {
            bool ready = AccountPasswords.TryPrepare(accounts, passwords, account =>
            {
                using (var dialog = new PasswordDialog(account))
                    return dialog.ShowDialog(this) == DialogResult.OK
                        ? new PasswordEntry { Password = dialog.Password, RememberPassword = dialog.RememberPassword }
                        : null;
            }, CredentialStore.Read, CredentialStore.Save, CredentialStore.Delete,
                account => SaveSettings(), AppendLog, out credentials);
            UpdateSelection();
            return ready;
        }

        private async Task RunLaunchRequestAsync(LaunchRequest request, List<AccountConfig> accounts, bool creation)
        {
            SaveSettings();
            stopRequested = false;
            var runner = new BackendRunner();
            var gameLog = new GameLogFilter();
            var preparation = new CancellationTokenSource();
            backendRunner = runner;
            if (creation) creationMonitors.Add(runner);
            preparationCancellation = preparation;
            SetRunning(true);
            try
            {
                using (runner)
                {
                    StatusText = T("STATUS · Preparing launcher");
                    AppendLog(T("Preparing launcher components…"));
                    string backendPath = await Task.Run(() => EmbeddedBackend.EnsureAvailable(preparation.Token));
                    if (closing) return;
                    if (stopRequested)
                    {
                        StatusText = T("STATUS · Login stopped");
                        AppendLog(T("Login stopped. Game windows that have already started remain open."));
                        return;
                    }
                    StatusText = T(creation ? "STATUS · Opening character creation" : "STATUS · Accounts in parallel; one character at a time per account");
                    AppendLog(creation ? F("Opening character creation for {0} on {1}.", accounts[0].Label, AccountTargets.ServerName(settings, accounts[0]))
                        : F("Starting {0} selected characters. Accounts start in parallel, one character at a time per account.", request.Characters.Count));
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = backendPath,
                        Arguments = "--launch-stdin",
                        WorkingDirectory = Path.GetDirectoryName(backendPath),
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = new UTF8Encoding(false),
                        StandardErrorEncoding = new UTF8Encoding(false)
                    };
                    // Credentials travel only through this child process's private standard-input pipe.
                    int exitCode = await runner.RunAsync(startInfo, new JavaScriptSerializer().Serialize(request),
                        line => { string visible = gameLog.Filter(line); if (visible != null) HandleOperationLine(visible, request, runner, creation); },
                        line => { string visible = gameLog.Filter(line); if (visible != null) AppendLog(visible); });
                    if (closing) return;
                    // A creation client is monitored independently after its window opens.
                    // Finishing that observer must not reset a newer foreground login.
                    if (creation && !ReferenceEquals(backendRunner, runner))
                    {
                        if (exitCode != 0)
                        {
                            if (!running) StatusText = T("STATUS · Automatic char detection ended");
                            AppendLog(T("Automatic char detection ended. You can import the char from the account's Prefs folder or add its ID manually."));
                        }
                        return;
                    }
                    if (stopRequested)
                    {
                        StatusText = T("STATUS · Login stopped");
                        AppendLog(T("Login stopped. Game windows that have already started remain open."));
                        return;
                    }
                    StatusText = exitCode == 0 ? T(creation ? "STATUS · Continue character creation in game" : "STATUS · Login complete") : T("STATUS · Login finished with errors — see status");
                    AppendLog(exitCode == 0 ? T(creation ? "Character creation finished. The detected char is available in the account list." : "Done. Game windows remain open. You can choose more characters or close the launcher.") : F("Login finished with errors (code {0}). An error stops the remaining logins for that account. Existing game windows remain open.", exitCode));
                    if (exitCode != 0)
                    {
                        foreach (var account in accounts) passwords.Remove(account.Id);
                        if (accounts.Any(account => account.RememberPassword))
                            AppendLog(T("Saved passwords were kept. If a password is incorrect, select its account and choose “Update saved password” before trying again."));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (closing) return;
                if (ReferenceEquals(backendRunner, runner)) StatusText = T("STATUS · Login stopped");
                AppendLog(T("Login stopped. Game windows that have already started remain open."));
            }
            catch (Exception ex)
            {
                if (closing) return;
                if (ReferenceEquals(backendRunner, runner)) StatusText = T("STATUS · Could not start login");
                AppendLog(F("Could not complete login: {0}", ex.Message));
            }
            finally
            {
                foreach (var character in request.Characters) character.Password = null;
                bool ownedForeground = ReferenceEquals(backendRunner, runner);
                if (ownedForeground) backendRunner = null;
                creationMonitors.Remove(runner);
                if (ReferenceEquals(preparationCancellation, preparation)) preparationCancellation = null;
                preparation.Dispose();
                if (!closing && ownedForeground) SetRunning(false);
            }
        }

        internal static string BuildLaunchConfirmation(LauncherSettings value)
        {
            var accounts = value.Accounts.Where(account => account.Characters.Any(character => character.Selected)).ToArray();
            int servers = accounts.Select(AccountTargets.Endpoint).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            int characters = accounts.Sum(account => account.Characters.Count(character => character.Selected));
            return F("Log in {0} {1} on {2} {3}?\r\n\r\n{4} {5} selected.",
                accounts.Length, accounts.Length == 1 ? T("account") : T("accounts"),
                servers, servers == 1 ? T("login server") : T("login servers"),
                characters, characters == 1 ? T("character") : T("characters"));
        }

        private bool ConfirmSelectedLaunch()
        {
            using (var dialog = new LaunchConfirmationDialog(BuildLaunchConfirmation(settings)))
                return dialog.ShowDialog(this) == DialogResult.OK;
        }

        private bool EnsureGameFolder(AccountConfig account)
        {
            while (!AccountTargets.GameExists(account.GamePath))
            {
                if (NexusMessageBox.Show(this, F("The game folder for “{0}” is missing or does not contain AnarchyOnline.exe.\r\n\r\n{1}\r\n\r\nChoose another folder? If the game was removed, cancel and reinstall the correct client, then try again.", account.Label, account.GamePath),
                    T("Game folder missing"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return false;
                using (var picker = new FolderBrowserDialog { Description = F("Choose the game folder for {0}", account.Label), ShowNewFolderButton = false })
                {
                    if (picker.ShowDialog(this) != DialogResult.OK) return false;
                    if (!AccountTargets.GameExists(picker.SelectedPath))
                    { ShowInfo(T("Choose the game folder containing AnarchyOnline.exe.")); continue; }
                    account.GamePath = picker.SelectedPath;
                    SaveSettings();
                    UpdateSelection();
                }
            }
            return true;
        }

        internal static void ValidateSelectedAccounts(LauncherSettings value)
        {
            var accounts = value.Accounts.Where(account => account.Characters.Any(character => character.Selected)).ToList();
            foreach (var account in accounts)
            {
                try { AccountTargets.Validate(account, true); }
                catch (ArgumentException ex) { throw new ArgumentException(F("Account “{0}”: {1}", account.Label, ex.Message)); }
            }
            if (accounts.SelectMany(account => account.Characters.Where(character => character.Selected).Select(character => AccountTargets.Endpoint(account) + "/" + character.CharacterId)).GroupBy(key => key).Any(group => group.Count() > 1))
                throw new ArgumentException(T("The selected characters contain a duplicate Character ID. Correct the entries before logging in."));
            AccountTargets.ValidateSelectedAccountLimits(value);
        }

        internal static LaunchRequest CreateLaunchRequest(LauncherSettings value, IDictionary<string, string> credentials)
        {
            var request = new LaunchRequest { Language = Texts.CurrentLanguage };
            foreach (var account in value.Accounts)
                foreach (var character in account.Characters.Where(item => item.Selected))
                    request.Characters.Add(new LaunchCharacter { AccountId = account.Id, Account = account.Username, Password = credentials[account.Id], GamePath = account.GamePath, Server = account.Server, ServerName = AccountTargets.ServerName(value, account), Port = account.Port, MaxAccounts = AccountTargets.AccountLimit(value, AccountTargets.Endpoint(account)), MaxCharactersPerAccount = AccountTargets.CharacterLimit(value, AccountTargets.Endpoint(account)), CharacterId = character.CharacterId, CharacterName = character.NamePending ? F("Character {0}", character.CharacterId) : character.Name });
            return request;
        }

        internal static LaunchRequest CreateCharacterRequest(LauncherSettings value, AccountConfig account, string password)
        {
            if (account == null || !value.Accounts.Contains(account)) throw new ArgumentException(T("The account settings are unavailable."));
            AccountTargets.Validate(account, true);
            if (!AccountTargets.CanCreateCharacter(value, account))
                throw new ArgumentException(T("All configured character slots are listed. Check the account's character-slot setting or remove deleted characters from the launcher."));
            return new LaunchRequest { Language = Texts.CurrentLanguage, Characters = new List<LaunchCharacter> {
                new LaunchCharacter {
                    CreateCharacter = true, CharacterId = 0, CharacterName = T("Create new char"),
                    AccountId = account.Id, Account = account.Username, Password = password,
                    GamePath = account.GamePath, Server = account.Server, Port = account.Port,
                    ServerName = AccountTargets.ServerName(value, account),
                    MaxAccounts = AccountTargets.AccountLimit(value, AccountTargets.Endpoint(account)),
                    MaxCharactersPerAccount = AccountTargets.CharacterLimit(value, AccountTargets.Endpoint(account))
                }
            }};
        }

        private void SetRunning(bool value)
        {
            running = value;
            foreach (var control in editingControls) control.Enabled = !value;
            startButton.Text = value ? T("Logging in…") : T("Log in selected  →");
            stopButton.Enabled = value && !stopRequested;
            if (value) StatusText = T("STATUS · Accounts in parallel; one character at a time per account");
            UpdateSelection();
        }

        private void StopLogin()
        {
            if (!running) return;
            stopRequested = true;
            preparationCancellation?.Cancel();
            backendRunner?.Stop();
            stopButton.Enabled = false;
            StatusText = T("STATUS · Stopping login");
            AppendLog(T("Stopping new logins. Game windows remain open."));
        }

        private void UpdateSavedPassword(object sender, EventArgs e)
        {
            if (running || smoke || storageBlocked || !(tree.SelectedNode?.Tag is AccountConfig account)) return;
            using (var dialog = new SavedPasswordDialog(account))
            {
                dialog.SavePasswordAction = password =>
                {
                    string error;
                    if (!AccountPasswords.TryUpdateSaved(account, password, passwords, CredentialStore.Save,
                        changed => { SettingsStore.Save(settings); saveErrorShown = false; }, CredentialStore.Delete, out error)) return error;
                    return null;
                };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
            }
            AppendLog(F("Saved password updated for {0}.", account.Label));
            UpdateSelection();
        }

        private bool ForgetPasswords(object sender, EventArgs e)
        {
            passwords.Clear();
            if (!smoke)
            {
                try { CredentialStore.ForgetAll(); }
                catch { AppendLog(T("Saved passwords could not be deleted. Try “Forget passwords” again.")); UpdateSelection(); return false; }
            }
            AppendLog(smoke ? T("UI preview: no saved passwords were changed.") : T("Passwords were forgotten and removed from encrypted storage. You will be asked again at the next login."));
            UpdateSelection();
            return true;
        }

        private void DeleteSavedPassword(AccountConfig account)
        {
            if (smoke) return;
            try { CredentialStore.Delete(account); }
            catch { AppendLog(T("The old password could not be deleted. Use “Forget passwords” to clear saved passwords.")); }
        }

        private void HandleOperationLine(string line, LaunchRequest request, BackendRunner runner, bool creation)
        {
            if (closing || IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((Action)(() => HandleOperationLine(line, request, runner, creation))); }
                catch (InvalidOperationException) { }
                return;
            }
            const string prefix = "AOQL_CREATION_STARTED ";
            if (line != null && line.StartsWith("AOQL_CREATION_STARTED", StringComparison.Ordinal))
            {
                if (!creation || stopRequested || !ReferenceEquals(backendRunner, runner) || request?.Characters?.Count != 1 ||
                    !request.Characters[0].CreateCharacter || !line.StartsWith(prefix, StringComparison.Ordinal) || line.Length > 2048) return;
                CreationStartedEvent started;
                try { started = new JavaScriptSerializer { MaxJsonLength = 2048, RecursionLimit = 8 }.Deserialize<CreationStartedEvent>(line.Substring(prefix.Length)); }
                catch (Exception) { return; } // JavaScriptSerializer also wraps numeric conversion failures in Exception.
                if (started == null || started.ProcessId <= 0 || started.StartTimeUtcTicks <= 0 ||
                    string.IsNullOrWhiteSpace(started.AccountId) || !string.Equals(started.AccountId, request.Characters[0].AccountId, StringComparison.OrdinalIgnoreCase)) return;
                foreach (var character in request.Characters) character.Password = null;
                backendRunner = null;
                preparationCancellation = null;
                SetRunning(false);
                StatusText = T("STATUS · Waiting for the new char in game");
                AppendLog(T("Keep AO Nexus open. The new char will be added automatically after entering the game. You can continue using the launcher meanwhile."));
                return;
            }
            HandleBackendLine(line, request);
        }

        private void HandleBackendLine(string line, LaunchRequest request)
        {
            if (closing || IsDisposed || Disposing || line == null) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((Action)(() => HandleBackendLine(line, request))); }
                catch (InvalidOperationException) { }
                return;
            }
            if (!line.StartsWith("AOQL_CHARACTER", StringComparison.Ordinal))
            {
                AppendLog(line);
                return;
            }
            CharacterConfig updated;
            if (!TryApplyCharacterEvent(line, request, settings, out updated))
            {
                if (!running && request.Characters.Any(character => character.CreateCharacter))
                    StatusText = T("STATUS · Automatic char detection ended");
                AppendLog(T(request.Characters.Any(character => character.CreateCharacter)
                    ? "The detected char could not be matched to its original account. Check whether the account or server settings changed; you can add the char manually."
                    : "An invalid name notification was ignored. The saved list was not changed."));
                return;
            }
            SaveSettings();
            RefreshTree();
            if (request.Characters.Count == 1 && request.Characters[0].CreateCharacter)
            {
                if (!running) StatusText = T("STATUS · Char detected");
                AppendLog(F("{0} (ID {1}) is now in the account list. Your saved teams are unchanged.", updated.Name, updated.CharacterId));
            }
            else AppendLog(F("Character {0} logged in as {1}. The name has been updated in the list.", updated.CharacterId, updated.Name));
        }

        internal static bool TryApplyCharacterEvent(string line, LaunchRequest request, LauncherSettings value, out CharacterConfig updated)
        {
            const string prefix = "AOQL_CHARACTER ";
            updated = null;
            if (line == null || line.Length > 2048 || !line.StartsWith(prefix, StringComparison.Ordinal) || request?.Characters == null || value?.Accounts == null)
                return false;
            CharacterReadyEvent notification;
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = 2048, RecursionLimit = 8 };
                notification = serializer.Deserialize<CharacterReadyEvent>(line.Substring(prefix.Length));
            }
            catch (Exception) { return false; } // Includes wrapped numeric conversion failures from the bounded JSON parser.
            if (notification == null || notification.CharacterId <= 0 || string.IsNullOrWhiteSpace(notification.CharacterName) || notification.CharacterName.Any(char.IsControl)) return false;
            string actualName = notification.CharacterName.Trim();
            if (actualName.Length > 64 || actualName.Any(char.IsControl)) return false;
            if (notification.Created)
                return TryApplyCreatedCharacter(notification, actualName, request, value, out updated);
            var matches = request.Characters.Where(character => character.CharacterId == notification.CharacterId && string.Equals(character.AccountId, notification.AccountId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count != 1) return false;
            var requestedCharacter = matches[0];
            var accounts = value.Accounts.Where(item => string.Equals(item.Username, requestedCharacter.Account, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(requestedCharacter.AccountId) || (string.Equals(item.Id, requestedCharacter.AccountId, StringComparison.OrdinalIgnoreCase) &&
                AccountTargets.Endpoint(item) == AccountTargets.Endpoint(new AccountConfig { Server = requestedCharacter.Server, Port = requestedCharacter.Port })))).ToList();
            if (accounts.Count != 1) return false;
            var account = accounts[0];
            var existing = account?.Characters?.FirstOrDefault(character => character.CharacterId == notification.CharacterId);
            if (existing == null) return false;
            existing.Name = actualName;
            existing.NamePending = false;
            updated = existing;
            return true;
        }

        private static bool TryApplyCreatedCharacter(CharacterReadyEvent notification, string actualName,
            LaunchRequest request, LauncherSettings value, out CharacterConfig updated)
        {
            updated = null;
            if (request.Characters.Count != 1 || string.IsNullOrWhiteSpace(notification.AccountId)) return false;
            var launch = request.Characters[0];
            if (launch == null || !launch.CreateCharacter || launch.CharacterId != 0 || string.IsNullOrWhiteSpace(launch.Account) ||
                string.IsNullOrWhiteSpace(launch.Server) || launch.Port <= 0 || launch.Port > 65535 ||
                !string.Equals(launch.AccountId, notification.AccountId, StringComparison.OrdinalIgnoreCase)) return false;
            var matches = value.Accounts.Where(account => string.Equals(account.Id, notification.AccountId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1) return false;
            var owner = matches[0];
            if (owner.Characters == null || !string.Equals(owner.Username?.Trim(), launch.Account?.Trim(), StringComparison.OrdinalIgnoreCase) ||
                AccountTargets.Endpoint(owner) != AccountTargets.Endpoint(new AccountConfig { Server = launch.Server, Port = launch.Port }) ||
                !SameCreationGamePath(owner.GamePath, launch.GamePath)) return false;
            if (value.Accounts.Any(other => other.Id != owner.Id && AccountTargets.SameServer(other, owner) &&
                other.Characters.Any(character => character.CharacterId == notification.CharacterId))) return false;
            var existing = owner.Characters.FirstOrDefault(character => character.CharacterId == notification.CharacterId);
            if (existing == null)
            {
                existing = new CharacterConfig { CharacterId = notification.CharacterId, Name = actualName, NamePending = false, Selected = false };
                owner.Characters.Add(existing);
            }
            else if (existing.NamePending || string.IsNullOrWhiteSpace(existing.Name))
            {
                existing.Name = actualName;
                existing.NamePending = false;
            }
            updated = existing;
            return true;
        }

        private static bool SameCreationGamePath(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right) || !Path.IsPathRooted(left) || !Path.IsPathRooted(right)) return false;
            try { return string.Equals(Path.GetFullPath(left).TrimEnd('\\', '/'), Path.GetFullPath(right).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) { return false; }
        }

        private async void CheckStartupUpdate()
        {
            try
            {
                UpdateJournal.Record("Startup update check.");
                AppendLog(T("Checking for updates…"));
                var release = await AppUpdates.CheckAsync(updateCancellation.Token);
                if (closing || IsDisposed) return;
                string message = release.Version <= AppUpdates.CurrentVersion ? T("You are using the latest version.") : F("Version {0} is available.", release.Version.ToString(3));
                AppendLog(message); UpdateJournal.Record(message);
                if (release.Version <= AppUpdates.CurrentVersion) return;
                availableUpdate = release; updateAvailableLink.Visible = true;
            }
            catch (Exception ex) { if (!closing && !IsDisposed) { string message = F("Could not check for updates: {0}", ex.Message); AppendLog(message); UpdateJournal.Record(message); } }
        }

        private void OpenUpdates(IWin32Window owner)
        {
            using (var dialog = new UpdateDialog(() => !running && !shortcutActionBusy && creationMonitors.Count == 0 && !closing, availableUpdate))
            { dialog.LogAction = AppendLog; dialog.ShowDialog(owner); }
        }

        private void OpenLog()
        {
            if (logViewer == null || logViewer.IsDisposed)
            {
                logViewer = new LogViewer();
                logViewer.SetLogText(activityLog.ToString());
                logViewer.Show(this);
            }
            else { if (logViewer.WindowState == FormWindowState.Minimized) logViewer.WindowState = FormWindowState.Normal; logViewer.Activate(); }
        }

        private void AppendLog(string message)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((Action)(() => AppendLog(message))); }
                catch (InvalidOperationException) { }
                return;
            }
            string entry = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine;
            int limit = LogRetention.NormalizeMb(settings.MaxLogSizeMb) * 1024 * 1024;
            int entryBytes = Encoding.UTF8.GetByteCount(entry);
            if (activityLogBytes + entryBytes > limit)
            {
                string retained = LogRetention.Tail(activityLog.ToString() + entry, limit * 9 / 10);
                activityLog = new StringBuilder(retained);
                activityLogBytes = Encoding.UTF8.GetByteCount(retained);
            }
            else { activityLog.Append(entry); activityLogBytes += entryBytes; }
            // The dashboard is a short preview; the searchable window receives the full retained history.
            string preview = LogRetention.Tail(entry, 16000);
            if (log.TextLength + preview.Length > 16000) log.Text = LogRetention.Tail(log.Text + preview, 12000);
            else log.AppendText(preview);
            log.SelectionStart = log.TextLength;
            log.ScrollToCaret();
            if (logViewer != null && !logViewer.IsDisposed) logViewer.SetLogText(activityLog.ToString());
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!closing && e.CloseReason == CloseReason.UserClosing)
            {
                if (confirmingWindowAction) { e.Cancel = true; return; }
                confirmingWindowAction = true;
                try
                {
                    if (hiddenInTray) RestoreFromTray();
                    if (!ConfirmWindowAction(false)) { e.Cancel = true; return; }
                }
                finally { confirmingWindowAction = false; }
            }
            closing = true;
            updateCancellation.Cancel();
            stopRequested = true;
            preparationCancellation?.Cancel();
            backendRunner?.Stop();
            foreach (var monitor in creationMonitors.ToArray()) monitor.Stop();
            passwords.Clear();
        }

        private void ShowInfo(string message)
        {
            NexusMessageBox.Show(this, message, "AO Nexus", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
