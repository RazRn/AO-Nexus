using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Forms;
using static AOLauncher.Localization.Texts;

namespace AOLauncher
{
    internal static class Theme
    {
        public static readonly Color Foreground = Color.FromArgb(224, 237, 241);
        public static readonly Color Navy = Foreground;
        public static readonly Color Accent = Color.FromArgb(72, 198, 220);
        public static readonly Color Background = Color.FromArgb(11, 19, 29);
        public static readonly Color Surface = Color.FromArgb(16, 29, 42);
        public static readonly Color SurfaceRaised = Color.FromArgb(21, 38, 52);
        public static readonly Color Border = Color.FromArgb(43, 66, 82);
        public static readonly Color Selection = Color.FromArgb(29, 83, 104);
        public static readonly Color Muted = Color.FromArgb(141, 164, 178);
        public static readonly Color Amber = Color.FromArgb(224, 167, 73);
        public static readonly Color Warning = Color.FromArgb(255, 143, 125);

        public static Button Button(string text, bool primary = false)
        {
            var button = new NexusButton
            {
                Text = text, AutoSize = true, MinimumSize = new Size(95, 34),
                Padding = new Padding(10, 2, 10, 2), FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : SurfaceRaised,
                ForeColor = primary ? Background : Foreground,
                Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 0),
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderColor = primary ? Accent : Border;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(105, 216, 232) : Color.FromArgb(31, 58, 74);
            button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(45, 169, 194) : Color.FromArgb(40, 75, 91);
            return button;
        }

        internal static void Apply(Control control)
        {
            if (control is TextBoxBase text)
            {
                text.BackColor = Surface;
                text.ForeColor = Foreground;
                if (!(text is NexusTextBox) && !(text is DataGridViewTextBoxEditingControl))
                    text.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (control is ComboBox combo)
            {
                StyleCombo(combo);
            }
            else if (control is NumericUpDown number)
            {
                number.BackColor = Surface;
                number.ForeColor = Foreground;
                if (!(number is NexusNumericUpDown)) number.BorderStyle = BorderStyle.FixedSingle;
                else return; // Its native child editor and spinner share their own theme.
            }
            else if (control is DataGridView grid)
            {
                grid.BackgroundColor = Surface;
                grid.GridColor = Border;
                grid.BorderStyle = BorderStyle.None;
                grid.EnableHeadersVisualStyles = false;
                grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceRaised;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Foreground;
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceRaised;
                grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Foreground;
                grid.DefaultCellStyle.BackColor = Surface;
                grid.DefaultCellStyle.ForeColor = Foreground;
                grid.DefaultCellStyle.SelectionBackColor = Selection;
                grid.DefaultCellStyle.SelectionForeColor = Foreground;
                grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(18, 33, 47);
                grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
                grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
                grid.EditingControlShowing -= StyleGridEditor;
                grid.EditingControlShowing += StyleGridEditor;
                grid.ShowCellToolTips = false;
            }
            else if (control is TabControl tabs)
            {
                if (!(tabs is NexusTabControl))
                {
                    tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
                    tabs.DrawItem -= DrawTab;
                    tabs.DrawItem += DrawTab;
                }
            }
            else if (control is LinkLabel link)
            {
                link.LinkColor = Accent;
                link.ActiveLinkColor = Foreground;
                link.VisitedLinkColor = Accent;
            }
            else if (control is Label && control.ForeColor == Color.Firebrick) control.ForeColor = Warning;
            foreach (Control child in control.Controls) Apply(child);
        }

        internal static void StyleCombo(ComboBox combo)
        {
            combo.BackColor = SurfaceRaised;
            combo.ForeColor = Foreground;
            if (combo is NexusComboBox) return;
            combo.FlatStyle = FlatStyle.Flat;
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.ItemHeight = Math.Max(combo.Font.Height + 4, 20);
            combo.FontChanged -= UpdateComboHeight;
            combo.FontChanged += UpdateComboHeight;
            combo.DrawItem -= DrawComboItem;
            combo.DrawItem += DrawComboItem;
        }

        private static void UpdateComboHeight(object sender, EventArgs e)
        {
            var combo = (ComboBox)sender;
            combo.ItemHeight = Math.Max(combo.Font.Height + 4, 20);
        }

        private static void StyleGridEditor(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            e.CellStyle.BackColor = SurfaceRaised;
            e.CellStyle.ForeColor = Foreground;
            e.Control.BackColor = SurfaceRaised;
            e.Control.ForeColor = Foreground;
            if (e.Control is TextBoxBase text) text.BorderStyle = BorderStyle.None;
        }

        private static void DrawComboItem(object sender, DrawItemEventArgs e)
        {
            var combo = (ComboBox)sender;
            bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            Color background = selected ? Selection : SurfaceRaised;
            using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.Bounds);
            string value = e.Index >= 0 && e.Index < combo.Items.Count ? combo.GetItemText(combo.Items[e.Index]) : combo.Text;
            var bounds = new Rectangle(e.Bounds.Left + 5, e.Bounds.Top, Math.Max(1, e.Bounds.Width - 8), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, value, combo.Font, bounds, combo.Enabled ? Foreground : Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        }

        private static void DrawTab(object sender, DrawItemEventArgs e)
        {
            var tabs = (TabControl)sender;
            var rectangle = e.Bounds;
            using (var brush = new SolidBrush(e.Index == tabs.SelectedIndex ? SurfaceRaised : Background))
                e.Graphics.FillRectangle(brush, rectangle);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, rectangle,
                e.Index == tabs.SelectedIndex ? Accent : Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (e.Index == tabs.SelectedIndex)
                using (var pen = new Pen(Accent, 2)) e.Graphics.DrawLine(pen, rectangle.Left + 4, rectangle.Bottom - 2, rectangle.Right - 4, rectangle.Bottom - 2);
        }
    }

    internal class EditorDialog : Form
    {
        protected readonly TableLayoutPanel Fields;
        protected readonly TableLayoutPanel DialogLayout;
        protected readonly Control FieldsViewport;
        protected readonly Label ErrorLabel;
        protected readonly Button SaveButton;
        protected readonly Button CancelAction;

        protected EditorDialog(string title, int height = 320)
        {
            Text = title;
            AppArtwork.ApplyIcon(this);
            Font = new Font("Segoe UI", 10F);
            BackColor = Theme.Background;
            ForeColor = Theme.Navy;
            ClientSize = new Size(510, height);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 3 };
            DialogLayout = layout;
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true, BackColor = Theme.Background };
            Fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ErrorLabel = new Label { Dock = DockStyle.Fill, ForeColor = Color.Firebrick, TextAlign = ContentAlignment.MiddleLeft };
            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = Padding.Empty, WrapContents = false
            };
            SaveButton = Theme.Button(T("Save"), true);
            CancelAction = Theme.Button(T("Cancel"));
            CancelAction.DialogResult = DialogResult.Cancel;
            actions.Controls.Add(SaveButton);
            actions.Controls.Add(CancelAction);
            FieldsViewport = SlimScrolling.Wrap(Fields);
            layout.Controls.Add(FieldsViewport, 0, 0);
            layout.Controls.Add(ErrorLabel, 0, 1);
            layout.Controls.Add(actions, 0, 2);
            Controls.Add(layout);
            AcceptButton = SaveButton;
            CancelButton = CancelAction;
        }

        protected override void OnLoad(EventArgs e)
        {
            Theme.Apply(this);
            base.OnLoad(e);
        }

        protected TextBox AddTextField(string label, string value, bool password = false, TableLayoutPanel container = null)
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 69, Margin = new Padding(0) };
            var caption = new Label { Text = label, Dock = DockStyle.Top, Height = 24 };
            var field = new NexusTextBox { Text = value ?? "", Dock = DockStyle.Bottom, UseSystemPasswordChar = password, MaxLength = 512 };
            panel.Padding = new Padding(0, 0, 0, 14);
            panel.Controls.Add(field);
            panel.Controls.Add(caption);
            (container ?? Fields).Controls.Add(panel);
            return field;
        }

        protected void AddNote(string text, int height = 55)
        {
            Fields.Controls.Add(new Label { Text = text, Dock = DockStyle.Top, Height = height, ForeColor = Theme.Muted, Margin = new Padding(0, 6, 0, 0) });
        }

        protected Label AddWrappedNote(string text, string name)
        {
            var note = new Label
            {
                Name = name, Text = text, Dock = DockStyle.Top, AutoSize = true,
                MaximumSize = new Size(420, 0), ForeColor = Theme.Muted,
                Margin = new Padding(0, 6, 0, 8)
            };
            Fields.Controls.Add(note);
            Fields.ClientSizeChanged += (sender, e) => note.MaximumSize = new Size(
                Math.Max(100, Fields.ClientSize.Width - note.Margin.Horizontal - SystemInformation.VerticalScrollBarWidth), 0);
            return note;
        }
    }

    internal sealed class WindowActionDialog : EditorDialog
    {
        private readonly CheckBox remember;
        internal bool DontShowAgain => remember.Checked;
        internal WindowActionDialog(bool minimize) : base(T(minimize ? "Minimize to tray?" : "Quit AO Nexus?"), 330)
        {
            AddWrappedNote(T(minimize
                ? "Nexus will keep running in the Windows system tray and disappear from the taskbar and Alt+Tab. Click its tray icon to return. Login activity continues."
                : "This closes Nexus and stops pending launcher activity. Your game windows stay open. Preset shortcuts will stop working until you open Nexus again."), "windowActionGuide");
            remember = new NexusCheckBox { Name = "dontShowAgain", Text = T("Don't show this again"), AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 14, 0, 8) };
            Fields.Controls.Add(remember);
            AddWrappedNote(T("You can turn confirmations back on in Settings."), "windowActionSettingsGuide");
            SaveButton.Text = T(minimize ? "Minimize to tray" : "Quit Nexus");
            SaveButton.Click += (sender, e) => DialogResult = DialogResult.OK;
            AcceptButton = CancelAction;
            ActiveControl = CancelAction;
            Shown += (sender, e) => { CancelAction.Focus(); Fields.AutoScrollPosition = Point.Empty; };
        }
    }

    internal sealed class LaunchConfirmationDialog : EditorDialog
    {
        public LaunchConfirmationDialog(string summary) : base(T("Confirm login"), 250)
        {
            Fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Fields.Controls.Add(new Label {
                Text = summary, Dock = DockStyle.Fill, Margin = Padding.Empty,
                TextAlign = ContentAlignment.TopLeft
            });
            DialogLayout.RowStyles[1].Height = 0;
            ErrorLabel.Visible = false;
            SaveButton.Text = T("Log in");
            SaveButton.DialogResult = DialogResult.OK;
            AcceptButton = CancelAction;
            ActiveControl = CancelAction;
        }
    }

    internal sealed class SelectedTargetsDialog : EditorDialog
    {
        public SelectedTargetsDialog(string details) : base(T("Selected servers and game folders"), 440)
        {
            ClientSize = new Size(760, 440);
            Fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Fields.Controls.Add(SlimScrolling.Wrap(new NexusTextBox {
                Text = details, Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                WordWrap = false, ScrollBars = ScrollBars.Both, BackColor = Theme.Surface,
                Margin = Padding.Empty
            }));
            DialogLayout.RowStyles[1].Height = 0;
            ErrorLabel.Visible = false;
            CancelAction.Visible = false;
            SaveButton.Text = T("Close");
            SaveButton.DialogResult = DialogResult.OK;
            CancelButton = SaveButton;
            ActiveControl = SaveButton;
        }
    }

    internal sealed class AccountDialog : EditorDialog
    {
        private readonly TabControl accountTabs;
        private readonly TextBox pathInput;
        private readonly TextBox serverInput;
        private readonly TextBox portInput;
        private readonly ComboBox serverProfile;
        private readonly TextBox labelInput;
        private readonly TextBox usernameInput;
        private readonly TextBox prefsInput;
        private readonly Label importStatus;
        private readonly Label targetStatus;
        private readonly CheckBox inheritCharacterSlots;
        private readonly CheckBox rememberPasswordInput;
        private readonly NumericUpDown characterSlotsInput;
        private readonly Button useDetectedTarget;
        private readonly IReadOnlyList<AccountConfig> knownAccounts;
        private readonly IReadOnlyList<LoginServerProfile> knownServers;
        private PrefsTargetSuggestion targetSuggestion;
        private bool applyingTarget;
        private bool gameManuallyChosen;
        private bool serverManuallyChosen;
        private bool serverConfirmed;
        private bool scanning;
        public string AccountLabel => string.IsNullOrWhiteSpace(labelInput.Text) ? Username : labelInput.Text.Trim();
        public string Username => usernameInput.Text.Trim();
        public string PrefsPath { get; private set; }
        public string GamePath => pathInput.Text.Trim().Trim('"');
        public string Server => IPAddress.TryParse(serverInput.Text.Trim(), out var address) ? address.ToString() : serverInput.Text.Trim();
        public int Port { get; private set; }
        public string ServerProfileId => (serverProfile.SelectedItem as LoginServerProfile)?.Id;
        public int? CharacterSlots => inheritCharacterSlots.Checked ? (int?)null : (int)characterSlotsInput.Value;
        public bool RememberPassword => rememberPasswordInput.Checked;
        public IReadOnlyList<int> ImportedCharacterIds { get; private set; } = new int[0];

        public AccountDialog(AccountConfig account = null) : this(account, new LauncherSettings()) { }

        public AccountDialog(AccountConfig account, LauncherSettings defaults) : base(account == null ? T("Add account") : T("Edit account"), 620)
        {
            knownAccounts = defaults.Accounts.ToArray();
            knownServers = defaults.LoginServers.ToArray();
            gameManuallyChosen = account != null;
            serverManuallyChosen = account != null;
            serverConfirmed = account != null;
            var parent = DialogLayout;
            parent.Controls.Remove(FieldsViewport);
            accountTabs = new NexusTabControl { Dock = DockStyle.Fill, Margin = Padding.Empty };
            var accountPage = new TabPage(T("Account and Prefs")) { BackColor = Theme.Background, Padding = new Padding(8) };
            var gamePage = new TabPage(T("Game and server")) { BackColor = Theme.Background, Padding = new Padding(8) };
            accountPage.Controls.Add(FieldsViewport);
            var gameFields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true, BackColor = Theme.Background };
            gameFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            gamePage.Controls.Add(SlimScrolling.Wrap(gameFields));
            accountTabs.TabPages.Add(accountPage);
            accountTabs.TabPages.Add(gamePage);
            parent.Controls.Add(accountTabs, 0, 0);

            serverProfile = new NexusComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 12) };
            serverProfile.Items.Add(T("Choose server preset"));
            serverProfile.Items.Add(T("Custom server"));
            foreach (var profile in defaults.LoginServers) serverProfile.Items.Add(profile);
            gameFields.Controls.Add(new Label { Text = T("Server preset"), Dock = DockStyle.Top, Height = 24 });
            gameFields.Controls.Add(serverProfile);
            pathInput = AddTextField(T("Game folder (containing AnarchyOnline.exe)"), account?.GamePath ?? string.Empty, false, gameFields);
            var gameBrowse = Theme.Button(T("Browse…"));
            gameBrowse.Click += (sender, e) =>
            {
                using (var picker = new FolderBrowserDialog { Description = T("Choose this account's game folder"), SelectedPath = GamePath, ShowNewFolderButton = false })
                    if (picker.ShowDialog(this) == DialogResult.OK) pathInput.Text = picker.SelectedPath;
            };
            gameFields.Controls.Add(gameBrowse);
            serverInput = AddTextField(T("Login server (IPv4 address)"), account?.Server ?? string.Empty, false, gameFields);
            portInput = AddTextField(T("Port"), account != null && account.Port != 0 ? account.Port.ToString() : string.Empty, false, gameFields);
            targetStatus = new Label { Dock = DockStyle.Top, AutoSize = true, MinimumSize = new Size(0, 60), MaximumSize = new Size(440, 0), Padding = new Padding(0, 2, 0, 3), ForeColor = Theme.Muted };
            gameFields.SizeChanged += (sender, e) => targetStatus.MaximumSize = new Size(
                Math.Max(100, gameFields.ClientSize.Width - targetStatus.Margin.Horizontal - SystemInformation.VerticalScrollBarWidth), 0);
            gameFields.Controls.Add(targetStatus);
            useDetectedTarget = Theme.Button(T("Use detected settings"));
            useDetectedTarget.Enabled = false;
            useDetectedTarget.Click += (sender, e) => ApplyDetectedTarget(true);
            gameFields.Controls.Add(useDetectedTarget);
            var selectedProfile = defaults.LoginServers.FirstOrDefault(profile => account != null && profile.Id == account.ServerProfileId);
            serverProfile.SelectedItem = (object)selectedProfile ?? serverProfile.Items[account == null ? 0 : 1];
            serverProfile.SelectionChangeCommitted += (sender, e) =>
            {
                serverManuallyChosen = true;
                serverConfirmed = serverProfile.SelectedIndex > 0;
                var profile = serverProfile.SelectedItem as LoginServerProfile;
                applyingTarget = true;
                try
                {
                    if (profile != null)
                    {
                        serverInput.Text = profile.Server;
                        portInput.Text = profile.Port.ToString();
                        serverProfile.SelectedItem = profile;
                    }
                    else if (!serverConfirmed) { serverInput.Text = ""; portInput.Text = ""; }
                }
                finally { applyingTarget = false; }
                RefreshTargetHint();
            };
            EventHandler manualServerEdit = (sender, e) =>
            {
                if (applyingTarget) return;
                serverManuallyChosen = true;
                serverConfirmed = true;
                serverProfile.SelectedIndex = 1;
                RefreshTargetHint();
            };
            serverInput.TextChanged += manualServerEdit;
            portInput.TextChanged += manualServerEdit;
            pathInput.TextChanged += (sender, e) => { if (!applyingTarget) gameManuallyChosen = true; };
            RefreshTargetHint();
            labelInput = AddTextField(T("Display name"), account?.Label);
            usernameInput = AddTextField(T("Login username"), account?.Username);
            prefsInput = AddTextField(T("Prefs folder (optional)"), account?.PrefsPath);
            var importActions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0), WrapContents = true };
            var browse = Theme.Button(T("Browse…"));
            var find = Theme.Button(T("Find characters"));
            var discover = Theme.Button(T("Find Prefs folders…"));
            discover.Click += async (sender, e) => await ChoosePrefsFolderAsync();
            browse.Click += async (sender, e) =>
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string defaultPrefsRoot = Path.Combine(localAppData, "Funcom", "Anarchy Online");
                using (var picker = new FolderBrowserDialog
                {
                    Description = T("Choose the account folder under Prefs"),
                    SelectedPath = Directory.Exists(prefsInput.Text) ? prefsInput.Text
                        : Directory.Exists(defaultPrefsRoot) ? defaultPrefsRoot : localAppData,
                    ShowNewFolderButton = false
                })
                    if (picker.ShowDialog(this) == DialogResult.OK)
                    {
                        prefsInput.Text = picker.SelectedPath;
                        await ScanPrefsAsync(false);
                    }
            };
            find.Click += async (sender, e) => await ScanPrefsAsync(false);
            importActions.Controls.Add(discover);
            importActions.Controls.Add(browse);
            importActions.Controls.Add(find);
            Fields.Controls.Add(importActions);
            importStatus = new Label
            {
                Text = T("Use Find Prefs folders or Browse. New characters are unchecked; names are detected at login."),
                Dock = DockStyle.Top, Height = 58, ForeColor = Theme.Muted, Margin = new Padding(0, 6, 0, 0)
            };
            Fields.Controls.Add(importStatus);
            prefsInput.TextChanged += (sender, e) =>
            {
                ImportedCharacterIds = new int[0];
                PrefsPath = null;
                targetSuggestion = null;
                ApplyDetectedTarget(false);
                importStatus.Text = T("Choose “Find characters” to preview the IDs. Save checks the folder again.");
            };
            rememberPasswordInput = new NexusCheckBox
            {
                Name = "rememberPassword", Text = T("Remember password for this account"),
                AutoSize = true, Dock = DockStyle.Top, Checked = account?.RememberPassword ?? false,
                Margin = new Padding(0, 8, 0, 4)
            };
            Fields.Controls.Add(rememberPasswordInput);
            AddWrappedNote(T("Remembered passwords are encrypted for your Windows user. If unchecked, you enter it once per login action; all selected chars on this account use it.") + "\n\n"
                + T("Uncheck and save this account to remove its saved password."), "accountPasswordGuide");
            inheritCharacterSlots = new NexusCheckBox { Text = T("Use server character-slot limit"), AutoSize = true, Dock = DockStyle.Top,
                Checked = account == null || !account.CharacterSlots.HasValue, Margin = new Padding(0, 8, 0, 4) };
            characterSlotsInput = new NexusNumericUpDown { Minimum = 0, Maximum = 1000, Width = 100, Value = account?.CharacterSlots ?? 0,
                Enabled = !inheritCharacterSlots.Checked, Margin = new Padding(0, 0, 0, 4) };
            var slotsRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
            slotsRow.Controls.Add(new Label { Text = T("Character slots"), AutoSize = true, MinimumSize = new Size(150, 24), Margin = new Padding(0, 4, 12, 0) });
            slotsRow.Controls.Add(characterSlotsInput);
            Fields.Controls.Add(inheritCharacterSlots);
            Fields.Controls.Add(slotsRow);
            var slotsNote = new Label { Text = T("Total characters allowed on this account, based on your saved character list. Override for extra slots. 0 = no launcher limit / unknown."),
                Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = Theme.Muted, Margin = new Padding(0, 4, 0, 8) };
            Fields.Controls.Add(slotsNote);
            Fields.SizeChanged += (sender, e) => slotsNote.MaximumSize = new Size(
                Math.Max(100, Fields.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - slotsNote.Margin.Horizontal), 0);
            inheritCharacterSlots.CheckedChanged += (sender, e) => RefreshCharacterSlotInput();
            RefreshCharacterSlotInput();
            SaveButton.Click += async (sender, e) =>
            {
                if (scanning) return;
                if (!string.IsNullOrWhiteSpace(prefsInput.Text))
                {
                    string reviewedGamePath = GamePath;
                    string reviewedServer = Server;
                    string reviewedPort = portInput.Text.Trim();
                    string reviewedProfile = ServerProfileId;
                    if (!await ScanPrefsAsync(true)) return;
                    if (!string.Equals(reviewedGamePath, GamePath, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(reviewedServer, Server, StringComparison.OrdinalIgnoreCase) ||
                        reviewedPort != portInput.Text.Trim() || reviewedProfile != ServerProfileId)
                    {
                        ErrorLabel.Text = T("Detected settings changed. Review Game and server, then save again.");
                        accountTabs.SelectedTab = gamePage;
                        return;
                    }
                }
                else { ImportedCharacterIds = new int[0]; PrefsPath = null; }
                if (IsDisposed) return;
                if (!ValidateUsername()) return;
                try
                {
                    if (!serverConfirmed) throw new ArgumentException(T("Choose a server preset or enter a custom server before saving."));
                    if (!int.TryParse(portInput.Text.Trim(), out int port)) throw new ArgumentException(T("Port must be between 1 and 65535."));
                    Port = port;
                    AccountTargets.Validate(new AccountConfig { GamePath = GamePath, Server = Server, Port = Port, CharacterSlots = CharacterSlots }, true);
                    DialogResult = DialogResult.OK;
                }
                catch (ArgumentException ex) { ErrorLabel.Text = ex.Message; accountTabs.SelectedTab = gamePage; }
            };
        }

        private void ApplyDetectedTarget(bool explicitlyRequested)
        {
            applyingTarget = true;
            try
            {
                if (!gameManuallyChosen || explicitlyRequested && !string.IsNullOrWhiteSpace(targetSuggestion?.GamePath))
                {
                    pathInput.Text = targetSuggestion?.GamePath ?? string.Empty;
                    if (explicitlyRequested) gameManuallyChosen = true;
                }
                var profile = knownServers.FirstOrDefault(item => targetSuggestion != null &&
                    item.Id == targetSuggestion.ServerProfileId && item.Server == targetSuggestion.Server && item.Port == targetSuggestion.Port);
                if (!serverManuallyChosen || explicitlyRequested && profile != null)
                {
                    serverInput.Text = profile?.Server ?? string.Empty;
                    portInput.Text = profile == null ? string.Empty : profile.Port.ToString();
                    serverProfile.SelectedItem = profile == null ? serverProfile.Items[0] : (object)profile;
                    serverConfirmed = profile != null;
                    if (explicitlyRequested) serverManuallyChosen = true;
                }
            }
            finally { applyingTarget = false; }
            RefreshTargetHint();
        }

        private void RefreshTargetHint()
        {
            RefreshCharacterSlotInput();
            if (targetStatus == null || useDetectedTarget == null) return;
            useDetectedTarget.Enabled = targetSuggestion != null &&
                (!string.IsNullOrWhiteSpace(targetSuggestion.GamePath) || !string.IsNullOrWhiteSpace(targetSuggestion.ServerProfileId));
            if (targetSuggestion == null)
                targetStatus.Text = T("Choose Prefs to detect the last selected server and game folder, or set them manually.");
            else if (targetSuggestion.FromSavedAccount)
            {
                targetStatus.Text = T("Server matched to the saved setup for this Prefs account. Check the settings before saving.");
                if (string.IsNullOrWhiteSpace(targetSuggestion.ServerProfileId))
                    targetStatus.Text += "\r\n" + T("No matching preset was found. Choose a preset or enter a custom server.");
            }
            else if (!string.IsNullOrWhiteSpace(targetSuggestion.DimensionName))
            {
                targetStatus.Text = F("Last server selected in AO: {0}. Check the server before saving.", targetSuggestion.DimensionName);
                if (string.IsNullOrWhiteSpace(targetSuggestion.ServerProfileId))
                    targetStatus.Text += "\r\n" + T("No matching preset was found. Choose a preset or enter a custom server.");
            }
            else targetStatus.Text = T("The server could not be detected. Choose a preset or enter a custom server.");
        }

        private void RefreshCharacterSlotInput()
        {
            if (inheritCharacterSlots == null || characterSlotsInput == null) return;
            characterSlotsInput.Enabled = !inheritCharacterSlots.Checked;
            if (!inheritCharacterSlots.Checked) return;
            int.TryParse(portInput.Text.Trim(), out int port);
            int limit = AccountTargets.CharacterSlotLimit(new LauncherSettings { LoginServers = knownServers.ToList() },
                new AccountConfig { Server = Server, Port = port });
            characterSlotsInput.Value = Math.Max(0, Math.Min(1000, limit));
        }

        private async Task ChoosePrefsFolderAsync()
        {
            if (scanning) return;
            string gamePath = GamePath;
            string username = Username;
            scanning = true;
            accountTabs.Enabled = false;
            SaveButton.Enabled = false;
            ErrorLabel.Text = "";
            string previousStatus = importStatus.Text;
            importStatus.Text = T("Looking for Prefs folders…");
            IReadOnlyList<PrefsCandidate> candidates = null;
            try
            {
                candidates = await Task.Run(() => PrefsDiscovery.Find(PrefsDiscovery.DefaultRoot, gamePath, username));
            }
            catch (Exception)
            {
                if (!IsDisposed) ErrorLabel.Text = T("Could not discover Prefs folders. Use Browse to choose one manually.");
            }
            finally
            {
                scanning = false;
                if (!IsDisposed)
                {
                    accountTabs.Enabled = true;
                    SaveButton.Enabled = true;
                    importStatus.Text = previousStatus;
                }
            }
            if (IsDisposed || candidates == null) return;
            using (var picker = new PrefsPickerDialog(candidates, username))
            {
                if (picker.ShowDialog(this) != DialogResult.OK || IsDisposed) return;
                prefsInput.Text = picker.SelectedCandidate.PrefsPath;
            }
            await ScanPrefsAsync(false);
        }

        private bool ValidateUsername()
        {
            if (string.IsNullOrWhiteSpace(Username)) { ErrorLabel.Text = T("Enter the username you use to log in."); return false; }
            if (Username.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0) { ErrorLabel.Text = T("The username contains invalid characters."); return false; }
            return true;
        }

        private async Task<bool> ScanPrefsAsync(bool saving)
        {
            if (scanning) return false;
            if (string.IsNullOrWhiteSpace(prefsInput.Text))
            { ErrorLabel.Text = T("Choose or paste a Prefs folder first."); return false; }
            string input = prefsInput.Text;
            string username = Username;
            string gamePath = GamePath;
            scanning = true;
            accountTabs.Enabled = false;
            SaveButton.Enabled = false;
            SaveButton.Text = saving ? T("Checking…") : T("Save");
            ErrorLabel.Text = "";
            importStatus.Text = T("Finding character IDs…");
            try
            {
                var scan = await Task.Run(() =>
                {
                    var imported = PrefsImporter.Scan(input, username);
                    var suggested = PrefsTargetResolver.Resolve(imported.AccountPath, gamePath, knownAccounts, knownServers);
                    return Tuple.Create(imported, suggested);
                });
                if (IsDisposed) return false;
                var result = scan.Item1;
                if (string.IsNullOrWhiteSpace(usernameInput.Text)) usernameInput.Text = result.SuggestedUsername;
                if (string.IsNullOrWhiteSpace(labelInput.Text)) labelInput.Text = result.SuggestedUsername;
                prefsInput.Text = result.AccountPath;
                PrefsPath = result.AccountPath;
                ImportedCharacterIds = result.CharacterIds;
                targetSuggestion = scan.Item2;
                ApplyDetectedTarget(false);
                importStatus.Text = F("Found: {0} character IDs. New entries are unchecked. Existing characters are preserved.", ImportedCharacterIds.Count);
                return true;
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                {
                    ImportedCharacterIds = new int[0];
                    PrefsPath = null;
                    importStatus.Text = T("No characters were imported.");
                    ErrorLabel.Text = ex is UnauthorizedAccessException
                        ? T("Could not read the Prefs folder. Check the path and access permissions.")
                        : ex is ArgumentException || ex is PathTooLongException
                            ? T("The Prefs path is invalid. Copy the full path from File Explorer.")
                            : ex.Message;
                }
                return false;
            }
            finally
            {
                scanning = false;
                if (!IsDisposed) { accountTabs.Enabled = true; SaveButton.Enabled = true; SaveButton.Text = T("Save"); }
            }
        }
    }

    internal sealed class PresetNameDialog : EditorDialog
    {
        private readonly TextBox nameInput;
        private readonly HotkeyCapture startHotkeyField, campHotkeyField;
        public string PresetName { get; private set; }
        public HotkeyBinding StartHotkey => startHotkeyField.Binding;
        public HotkeyBinding LogoutHotkey => campHotkeyField.Binding;
        internal Func<HotkeyBinding, HotkeyBinding, string> ValidateShortcuts;

        public PresetNameDialog(string title, string initialName, IEnumerable<string> reservedNames)
            : this(title, initialName, reservedNames, null, null, 0, false) { }

        public PresetNameDialog(string title, string initialName, IEnumerable<string> reservedNames,
            HotkeyBinding start, HotkeyBinding logout, int selectedCount, bool editing) : base(title, 410)
        {
            nameInput = AddTextField(T("Preset name"), initialName);
            nameInput.MaxLength = 80;
            var reserved = new HashSet<string>(reservedNames ?? new string[0], StringComparer.OrdinalIgnoreCase);
            startHotkeyField = AddShortcut(T("Start shortcut"), "presetStartHotkey", start);
            campHotkeyField = AddShortcut(T("Logout shortcut"), "presetCampHotkey", logout);
            foreach (var field in new[] { startHotkeyField, campHotkeyField })
            {
                field.Commit = value => CheckShortcuts(ReferenceEquals(field, startHotkeyField) ? value : StartHotkey,
                    ReferenceEquals(field, campHotkeyField) ? value : LogoutHotkey);
                field.Error = error => ErrorLabel.Text = error;
                field.CaptureChanged = () => { if (!field.Capturing) ErrorLabel.Text = ""; };
            }
            AddWrappedNote(T("Optional shortcuts · Ctrl/Alt + key · Delete clears · Esc cancels recording. Changes apply only when you save. Logout uses /quit."), "presetShortcutGuide");
            if (!editing) AddWrappedNote(F("This preset will save the current selection of {0} characters.", selectedCount), "presetSelectionGuide");
            SaveButton.Click += (sender, e) =>
            {
                try
                {
                    string validName = PresetService.ValidateName(nameInput.Text);
                    if (reserved.Contains(validName)) { ErrorLabel.Text = T("A preset with this name already exists."); return; }
                    string error = CheckShortcuts(StartHotkey, LogoutHotkey);
                    if (error != null) { ErrorLabel.Text = error; return; }
                    PresetName = validName;
                    DialogResult = DialogResult.OK;
                }
                catch (ArgumentException ex) { ErrorLabel.Text = ex.Message; }
            };
            Shown += (sender, e) => { nameInput.Focus(); nameInput.SelectAll(); };
        }

        private string CheckShortcuts(HotkeyBinding start, HotkeyBinding logout)
        {
            if (start != null && start.SameAs(logout)) return T("Start and logout need different shortcuts.");
            return ValidateShortcuts?.Invoke(start, logout);
        }

        private HotkeyCapture AddShortcut(string label, string name, HotkeyBinding initial)
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 66, Margin = Padding.Empty, Padding = new Padding(0, 0, 0, 10) };
            var field = new HotkeyCapture { Name = name, AccessibleName = label, Dock = DockStyle.Bottom, Height = 32,
                Binding = initial == null ? null : new HotkeyBinding { Key = initial.Key, Modifiers = initial.Modifiers } };
            field.RefreshText();
            panel.Controls.Add(field);
            panel.Controls.Add(new Label { Text = label, Dock = DockStyle.Top, Height = 24 });
            Fields.Controls.Add(panel);
            return field;
        }
    }

    internal sealed class CharacterDialog : EditorDialog
    {
        private readonly TextBox nameInput;
        private readonly TextBox idInput;
        public string CharacterName => NamePending ? F("Character {0}", CharacterId) : nameInput.Text.Trim();
        public bool NamePending => string.IsNullOrWhiteSpace(nameInput.Text);
        public int CharacterId { get; private set; }

        public CharacterDialog(string accountLabel, CharacterConfig character = null) : base(character == null ? F("Add character — {0}", accountLabel) : T("Edit character"), 370)
        {
            CharacterId = character?.CharacterId ?? 0;
            nameInput = AddTextField(T("Display name (optional)"), character?.NamePending == true ? "" : character?.Name);
            idInput = AddTextField(T("Character ID (the number after 50000:)"), character?.CharacterId.ToString());
            AddNote(T("Log in normally, press Shift+F9, and find the character ID. For 50000:123456789, enter 123456789.\r\nThe ID is used to log in. The actual name is detected when the game window is ready."), 85);
            SaveButton.Click += (sender, e) =>
            {
                int id;
                if (CharacterName.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0) { ErrorLabel.Text = T("The character name contains invalid characters."); return; }
                if (!int.TryParse(idInput.Text.Trim(), out id) || id <= 0) { ErrorLabel.Text = T("Character ID must be a positive whole number."); return; }
                CharacterId = id;
                DialogResult = DialogResult.OK;
            };
        }
    }

    internal sealed class PasswordDialog : EditorDialog
    {
        private readonly TextBox passwordInput;
        private readonly CheckBox rememberPasswordInput;
        public string Password => passwordInput.Text;
        public bool RememberPassword => rememberPasswordInput.Checked;

        public PasswordDialog(AccountConfig account) : base(F("Password — {0}", account.Label), 540)
        {
            AddNote(F("Login for {0}", account.Username), 35);
            passwordInput = AddTextField(T("Password"), "", true);
            rememberPasswordInput = new NexusCheckBox
            {
                Name = "rememberPassword", Text = T("Remember password for this account"),
                AutoSize = true, Dock = DockStyle.Top, Checked = account.RememberPassword,
                Margin = new Padding(0, 4, 0, 4)
            };
            Fields.Controls.Add(rememberPasswordInput);
            AddWrappedNote(T("AO Nexus uses this password to log this account in to its selected game server.") + "\n\n"
                + T("Remembered passwords are encrypted for your Windows user. If unchecked, you enter it once per login action; all selected chars on this account use it.") + "\n\n"
                + T("Change this under Edit account. Uncheck and save to remove this account's saved password, or use Settings → Forget passwords to clear them all."), "passwordGuide");
            SaveButton.Text = T("Continue");
            SaveButton.Click += (sender, e) =>
            {
                if (passwordInput.Text.Length == 0) { ErrorLabel.Text = T("Enter the password for this account."); return; }
                DialogResult = DialogResult.OK;
            };
            Shown += (sender, e) => passwordInput.Focus();
        }
    }

    internal sealed class SavedPasswordDialog : EditorDialog
    {
        private readonly TextBox passwordInput;
        public Func<string, string> SavePasswordAction { private get; set; }

        public SavedPasswordDialog(AccountConfig account) : base(F("Update saved password — {0}", account.Label), 520)
        {
            AddWrappedNote(F("Login for {0}", account.Username) + "\n"
                + F("Login server: {0}", AccountTargets.Endpoint(account)), "savedPasswordAccount");
            passwordInput = AddTextField(T("Game account password"), "", true);
            passwordInput.Name = "gameAccountPassword";
            AddWrappedNote(T("Enter the password you already use to log in to this game account. This updates only the copy saved in AO Nexus, for all chars on this account. It does not change your password on the game server.") + "\n\n"
                + T("The password is saved encrypted for your Windows user. To remove it, uncheck Remember password for this account under Edit account, or use Settings → Forget passwords to remove all saved passwords."), "savedPasswordGuide");
            if (!account.RememberPassword)
                AddWrappedNote(T("Saving here also enables Remember password for this account. Future logins will use the saved password."), "savedPasswordRememberNotice");
            ErrorLabel.ForeColor = Theme.Warning;
            ErrorLabel.AutoSize = true;
            ErrorLabel.MinimumSize = new Size(0, 48);
            ErrorLabel.MaximumSize = new Size(450, 0);
            DialogLayout.RowStyles[1].SizeType = SizeType.AutoSize;
            DialogLayout.ClientSizeChanged += (sender, e) => ErrorLabel.MaximumSize = new Size(
                Math.Max(100, DialogLayout.ClientSize.Width - DialogLayout.Padding.Horizontal - ErrorLabel.Margin.Horizontal), 0);
            SaveButton.Text = T("Save password");
            SaveButton.Click += (sender, e) =>
            {
                if (passwordInput.Text.Length == 0)
                {
                    ErrorLabel.Text = T("Enter the password for this account.");
                    passwordInput.Focus();
                    return;
                }

                string error;
                SaveButton.Enabled = false;
                try
                {
                    error = SavePasswordAction == null ? T("The password could not be saved securely. Try again.")
                        : SavePasswordAction(passwordInput.Text);
                }
                catch
                {
                    // Storage exceptions must never disclose a password or other sensitive details.
                    error = T("The password could not be saved securely. Try again.");
                }
                finally { SaveButton.Enabled = true; }
                if (error != null)
                {
                    ErrorLabel.Text = string.IsNullOrWhiteSpace(error)
                        ? T("The password could not be saved securely. Try again.") : error;
                    return;
                }

                ClearPasswordFields();
                DialogResult = DialogResult.OK;
            };
            Shown += (sender, e) =>
            {
                passwordInput.Focus();
                Fields.AutoScrollPosition = Point.Empty;
            };
        }

        private void ClearPasswordFields()
        {
            if (passwordInput != null && !passwordInput.IsDisposed) passwordInput.Clear();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ClearPasswordFields();
            SavePasswordAction = null;
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ClearPasswordFields();
                SavePasswordAction = null;
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class SettingsDialog : EditorDialog
    {
        private readonly ComboBox languageInput;
        private readonly CheckBox startWithWindowsInput;
        private readonly CheckBox checkUpdatesInput;
        private readonly CheckBox confirmTrayInput, confirmQuitInput;
        public bool SkipTrayConfirmation => !confirmTrayInput.Checked;
        public bool SkipQuitConfirmation => !confirmQuitInput.Checked;
        public Action CheckUpdatesAction { private get; set; }
        public bool CheckForUpdatesOnStartup => checkUpdatesInput.Checked;
        public Func<bool, string> SaveStartupAction { private get; set; }
        public bool StartWithWindows { get => startWithWindowsInput.Checked; set => startWithWindowsInput.Checked = value; }
        public Func<bool> ForgetPasswordsAction { private get; set; }
        public string Language => languageInput.SelectedIndex == 1 ? "da" : languageInput.SelectedIndex == 2 ? "en" : "auto";
        public List<LoginServerProfile> LoginServers { get; private set; }

        public SettingsDialog(LauncherSettings settings) : base(T("Settings"), 650)
        {
            // Keep notes within the viewport as text scales. An implicit AutoSize
            // column can grow to its text width and repeatedly toggle both bars.
            LoginServers = settings.LoginServers.Select(item => new LoginServerProfile { Id = item.Id, Name = item.Name, Server = item.Server, Port = item.Port, MaxAccounts = item.MaxAccounts, MaxCharactersPerAccount = item.MaxCharactersPerAccount, CharacterSlots = item.CharacterSlots }).ToList();
            var languagePanel = new Panel { Dock = DockStyle.Top, Height = 69, Margin = Padding.Empty, Padding = new Padding(0, 0, 0, 14) };
            languagePanel.Controls.Add(new Label { Text = T("Language"), Dock = DockStyle.Top, Height = 24 });
            languageInput = new NexusComboBox { Dock = DockStyle.Bottom, DropDownStyle = ComboBoxStyle.DropDownList };
            languageInput.Items.AddRange(new object[] { T("Automatic (system language)"), "Dansk", "English" });
            languageInput.SelectedIndex = settings.Language == "da" ? 1 : settings.Language == "en" ? 2 : 0;
            languagePanel.Controls.Add(languageInput);
            Fields.Controls.Add(languagePanel);
            startWithWindowsInput = new NexusCheckBox
            {
                Name = "startWithWindows", Text = T("Start with Windows"),
                AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 4)
            };
            Fields.Controls.Add(startWithWindowsInput);
            AddWrappedNote(T("Start in the system tray when you sign in to Windows. No game accounts are logged in automatically. Turn this off here to remove automatic startup."), "windowsStartupGuide");
            confirmTrayInput = new NexusCheckBox { Text = T("Confirm before minimizing to tray"), Name = "confirmTray", AutoSize = true, Dock = DockStyle.Top,
                Checked = !settings.SkipTrayConfirmation, Margin = new Padding(0, 4, 0, 4) };
            confirmQuitInput = new NexusCheckBox { Text = T("Confirm before quitting Nexus"), Name = "confirmQuit", AutoSize = true, Dock = DockStyle.Top,
                Checked = !settings.SkipQuitConfirmation, Margin = new Padding(0, 4, 0, 4) };
            Fields.Controls.Add(confirmTrayInput); Fields.Controls.Add(confirmQuitInput);
            AddNote(T("Set the game folder and login server under each account: Edit → Game and server."), 65);
            var serversButton = Theme.Button(T("Manage login servers…"));
            serversButton.Click += (sender, e) =>
            {
                using (var dialog = new LoginServersDialog(LoginServers))
                    if (dialog.ShowDialog(this) == DialogResult.OK) LoginServers = dialog.LoginServers;
            };
            Fields.Controls.Add(serversButton);
            AddNote(T("Saved passwords"), 32);
            AddNote(T("Clear saved passwords for all AO Nexus accounts immediately. You will be asked again at the next login."), 58);
            var forgetPasswords = Theme.Button(T("Forget passwords"));
            forgetPasswords.Name = "forgetSavedPasswords";
            var passwordStatus = new Label { AutoSize = true, Dock = DockStyle.Top, ForeColor = Theme.Muted, Margin = new Padding(0, 8, 0, 0), Name = "passwordActionStatus" };
            forgetPasswords.Click += (sender, e) =>
            {
                if (ForgetPasswordsAction == null) return;
                bool cleared = ForgetPasswordsAction();
                passwordStatus.Text = cleared ? T("Saved passwords cleared. Accounts and presets are unchanged.") : T("Passwords could not be cleared. Try again.");
                passwordStatus.ForeColor = cleared ? Theme.Accent : Theme.Warning;
                Fields.PerformLayout();
                Fields.ScrollControlIntoView(passwordStatus);
            };
            Fields.Controls.Add(forgetPasswords);
            Fields.Controls.Add(passwordStatus);
            checkUpdatesInput = new NexusCheckBox { Name = "checkUpdatesOnStartup", Text = T("Check for updates when Nexus starts"),
                Checked = settings.CheckForUpdatesOnStartup, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 16, 0, 8) };
            Fields.Controls.Add(checkUpdatesInput);
            var checkUpdates = Theme.Button(T("Check for updates…"));
            checkUpdates.Name = "checkForUpdates";
            checkUpdates.Click += (sender, e) => CheckUpdatesAction?.Invoke();
            Fields.Controls.Add(checkUpdates);
            AddWrappedNote(T("Checks GitHub for stable releases. Downloads and installation always require your confirmation."), "automaticUpdatesGuide");
            Fields.ClientSizeChanged += (sender, e) => passwordStatus.MaximumSize = new Size(Math.Max(100, Fields.ClientSize.Width - 20), 0);
            SaveButton.Click += (sender, e) =>
            {
                string startupError = SaveStartupAction?.Invoke(StartWithWindows);
                if (!string.IsNullOrEmpty(startupError)) { ErrorLabel.Text = startupError; return; }
                DialogResult = DialogResult.OK;
            };
        }
    }

    internal sealed class LoginServersDialog : EditorDialog
    {
        private readonly DataGridView grid;
        public List<LoginServerProfile> LoginServers { get; private set; }

        public LoginServersDialog(IEnumerable<LoginServerProfile> profiles) : base(T("Login servers"), 560)
        {
            ClientSize = new Size(1100, 600);
            Fields.AutoScroll = false;
            Fields.RowCount = 4;
            Fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var scopeNote = new Label { Text = T("Changes apply to all accounts using that server. Removed servers become custom settings on those accounts."), AutoSize = true, Dock = DockStyle.Fill, ForeColor = Theme.Muted, Margin = new Padding(0, 0, 0, 12) };
            var limitNote = new Label { Text = T("Online limits control how many accounts and chars may be logged in at once (0 = unlimited). Char slots are the default total capacity per account (0 = no launcher limit / unknown), based on saved characters. Account overrides apply individually."), AutoSize = true, Dock = DockStyle.Fill, ForeColor = Theme.Muted, Margin = new Padding(0, 0, 0, 12) };
            Fields.Controls.Add(scopeNote, 0, 0);
            Fields.Controls.Add(limitNote, 0, 1);
            scopeNote.MaximumSize = new Size(Math.Max(100, Fields.ClientSize.Width), 0);
            limitNote.MaximumSize = scopeNote.MaximumSize;
            Fields.SizeChanged += (sender, e) =>
            {
                int width = Math.Max(100, Fields.ClientSize.Width);
                scopeNote.MaximumSize = new Size(width, 0);
                limitNote.MaximumSize = new Size(width, 0);
            };
            grid = new DataGridView { Dock = DockStyle.Fill, MinimumSize = new Size(0, 150), AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, BackgroundColor = Theme.Surface, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
            grid.Columns.Add("Name", T("Server name"));
            grid.Columns.Add("Server", T("IPv4 address"));
            grid.Columns.Add("Port", T("Port"));
            grid.Columns.Add("MaxAccounts", T("Max. accounts online"));
            grid.Columns.Add("MaxCharactersPerAccount", T("Max. chars online/account"));
            grid.Columns.Add("CharacterSlots", T("Default char slots/account"));
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.Columns[0].FillWeight = 160;
            grid.Columns[0].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            grid.Columns[1].FillWeight = 100;
            grid.Columns[2].FillWeight = 50;
            grid.Columns[3].FillWeight = 80;
            grid.Columns[4].FillWeight = 100;
            grid.Columns[5].FillWeight = 90;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            grid.Columns[3].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            grid.Columns[4].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            grid.Columns[5].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            foreach (var profile in profiles)
            {
                int row = grid.Rows.Add(profile.Name, profile.Server, profile.Port.ToString(),
                    (profile.MaxAccounts ?? ServerAccountPolicy.DefaultLimit(profile.Server, profile.Port)).ToString(),
                    (profile.MaxCharactersPerAccount ?? ServerAccountPolicy.DefaultCharacterLimit(profile.Server, profile.Port)).ToString(),
                    (profile.CharacterSlots ?? AccountTargets.DefaultCharacterSlots(profile.Server, profile.Port)).ToString());
                grid.Rows[row].Tag = profile.Id;
            }
            Fields.Controls.Add(SlimScrolling.Wrap(grid), 0, 2);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 12, 0, 0), WrapContents = false };
            var add = Theme.Button(T("Add login server"));
            add.Click += (sender, e) =>
            {
                int row = grid.Rows.Add("", "", "7500", "0", "0", "0");
                grid.Rows[row].Tag = Guid.NewGuid().ToString("D");
                grid.CurrentCell = grid.Rows[row].Cells[0];
                grid.BeginEdit(true);
            };
            var remove = Theme.Button(T("Remove server"));
            remove.Click += (sender, e) => { if (grid.CurrentRow != null) grid.Rows.Remove(grid.CurrentRow); };
            actions.Controls.Add(add);
            actions.Controls.Add(remove);
            Fields.Controls.Add(actions, 0, 3);
            SaveButton.Click += (sender, e) =>
            {
                grid.EndEdit();
                var result = new List<LoginServerProfile>();
                try
                {
                    foreach (DataGridViewRow row in grid.Rows)
                    {
                        if (!int.TryParse(Convert.ToString(row.Cells[2].Value), out int port)) throw new ArgumentException(T("Port must be between 1 and 65535."));
                        if (!int.TryParse(Convert.ToString(row.Cells[3].Value), out int maxAccounts) || maxAccounts < 0 || maxAccounts > 100)
                            throw new ArgumentException(T("Max. accounts must be a whole number from 0 to 100. Use 0 for unlimited."));
                        if (!int.TryParse(Convert.ToString(row.Cells[4].Value), out int maxCharacters) || maxCharacters < 0 || maxCharacters > 100)
                            throw new ArgumentException(T("Chars per account must be a whole number from 0 to 100. Use 0 for unlimited."));
                        if (!int.TryParse(Convert.ToString(row.Cells[5].Value), out int characterSlots) || characterSlots < 0 || characterSlots > 1000)
                            throw new ArgumentException(T("Character slots must be a whole number from 0 to 1000. Use 0 for no launcher limit."));
                        string server = Convert.ToString(row.Cells[1].Value).Trim();
                        result.Add(new LoginServerProfile { Id = (string)row.Tag, Name = Convert.ToString(row.Cells[0].Value).Trim(), Server = IPAddress.TryParse(server, out var address) ? address.ToString() : server, Port = port, MaxAccounts = maxAccounts, MaxCharactersPerAccount = maxCharacters, CharacterSlots = characterSlots });
                    }
                    AccountTargets.ValidateProfiles(result);
                    LoginServers = result;
                    DialogResult = DialogResult.OK;
                }
                catch (ArgumentException ex) { ErrorLabel.Text = ex.Message; }
            };
        }
    }
}
