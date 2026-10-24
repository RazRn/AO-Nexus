using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using static AOLauncher.Localization.Texts;

namespace AOLauncher
{
    internal sealed class PrefsPickerDialog : EditorDialog
    {
        private readonly DataGridView folders;
        private readonly TextBox folderDetails;
        private readonly string username;
        public PrefsCandidate SelectedCandidate { get; private set; }

        public PrefsPickerDialog(IEnumerable<PrefsCandidate> candidates, string requestedUsername) : base(T("Choose a Prefs folder"), 570)
        {
            username = requestedUsername;
            ClientSize = new Size(820, 570);
            AddNote(T("Choose an account folder below. Matches for your game folder and username appear first. Check the account name before choosing."), 55);
            folders = new DataGridView
            {
                Dock = DockStyle.Top, Height = 250, ReadOnly = true, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Theme.Surface,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells
            };
            folders.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            folders.Columns.Add("Installation", T("Installation"));
            folders.Columns.Add("Account", T("Account"));
            folders.Columns.Add("Characters", T("Chars"));
            folders.Columns.Add("Status", T("Installation status"));
            folders.Columns[0].FillWeight = 100;
            folders.Columns[1].FillWeight = 85;
            folders.Columns[2].FillWeight = 35;
            folders.Columns[3].FillWeight = 135;
            foreach (DataGridViewColumn column in folders.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            foreach (var candidate in candidates ?? Enumerable.Empty<PrefsCandidate>())
            {
                int index = folders.Rows.Add(candidate.InstallationName, candidate.AccountName, candidate.CharacterCount, StatusText(candidate));
                folders.Rows[index].Tag = candidate;
            }
            Fields.Controls.Add(SlimScrolling.Wrap(folders));
            folderDetails = new NexusTextBox { Dock = DockStyle.Top, Height = 100, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Background, BorderStyle = BorderStyle.None, Margin = new Padding(0, 10, 0, 0) };
            Fields.Controls.Add(SlimScrolling.Wrap(folderDetails));
            folders.SelectionChanged += (sender, e) => ShowSelection();
            folders.CellDoubleClick += (sender, e) => { if (e.RowIndex >= 0) AcceptSelection(); };
            SaveButton.Text = T("Use folder");
            SaveButton.Click += (sender, e) => AcceptSelection();
            ClearSelection();
            Shown += (sender, e) => ClearSelection();
        }

        private static string StatusText(PrefsCandidate candidate)
        {
            if (!string.IsNullOrWhiteSpace(candidate.GamePath))
            {
                if (!candidate.GameExists) return T("Game installation missing");
                return candidate.MatchesGamePath ? T("Matches selected game folder") : T("Installed");
            }
            return candidate.MatchesGameFolderName ? T("Same folder name — unverified") : T("Installation not verified");
        }

        private void ClearSelection()
        {
            folders.ClearSelection();
            folders.CurrentCell = null;
            ShowSelection();
        }

        private PrefsCandidate CurrentCandidate => folders.SelectedRows.Count == 1 ? folders.SelectedRows[0].Tag as PrefsCandidate : null;

        private void ShowSelection()
        {
            var candidate = CurrentCandidate;
            SaveButton.Enabled = candidate != null;
            if (candidate == null)
            {
                folderDetails.Text = folders.Rows.Count == 0
                    ? T("No account folders with character IDs were found. Close this window and use Browse to choose a folder manually.")
                    : T("Select a row to see the full folder paths. Nothing is imported until you choose a folder.");
                return;
            }
            folderDetails.Text = F("Prefs folder: {0}\r\nGame folder: {1}", candidate.PrefsPath,
                string.IsNullOrWhiteSpace(candidate.GamePath) ? T("Not verified") : candidate.GamePath);
            if (!string.IsNullOrWhiteSpace(username) && !string.Equals(username.Trim(), candidate.AccountName, StringComparison.OrdinalIgnoreCase))
                folderDetails.Text += "\r\n" + F("This folder belongs to “{0}”; the entered login username is “{1}”.", candidate.AccountName, username);
        }

        private void AcceptSelection()
        {
            if (CurrentCandidate == null) return;
            SelectedCandidate = CurrentCandidate;
            DialogResult = DialogResult.OK;
        }
    }
}
