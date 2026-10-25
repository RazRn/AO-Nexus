using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal sealed class HealthEndpoint
    {
        public bool Enabled { get; set; }
        public string Host { get; set; } = "";
        public int Port { get; set; }
        internal HealthEndpoint Copy() => new HealthEndpoint { Enabled = Enabled, Host = Host, Port = Port };
        internal bool Valid => !string.IsNullOrWhiteSpace(Host) && Host.Length <= 253 && Host == Host.Trim()
            && Uri.CheckHostName(Host) != UriHostNameType.Unknown && Port >= 1 && Port <= 65535;
        internal string Key => Enabled + "/" + Host + "/" + Port;
    }

    internal sealed class HealthEndpoints
    {
        public HealthEndpoint Login { get; set; } = new HealthEndpoint();
        public HealthEndpoint Zone { get; set; } = new HealthEndpoint();
        public HealthEndpoint Chat { get; set; } = new HealthEndpoint();
        internal HealthEndpoints Copy() => new HealthEndpoints { Login = Login?.Copy() ?? new HealthEndpoint(), Zone = Zone?.Copy() ?? new HealthEndpoint(), Chat = Chat?.Copy() ?? new HealthEndpoint() };
        internal string Key => (Login?.Key ?? "") + "|" + (Zone?.Key ?? "") + "|" + (Chat?.Key ?? "");
        internal static HealthEndpoints Defaults(LoginServerProfile server)
        {
            var result = new HealthEndpoints { Login = new HealthEndpoint { Enabled = true, Host = server.Server, Port = server.Port } };
            string kind = ServerHealth.Kind(server);
            if (kind == "rebirth")
            {
                result.Zone = new HealthEndpoint { Enabled = true, Host = server.Server, Port = 7501 };
                result.Chat = new HealthEndpoint { Enabled = true, Host = server.Server, Port = 7012 };
            }
            else if (kind == "live") result.Chat = new HealthEndpoint { Enabled = true, Host = "chat.d1.funcom.com", Port = server.Port == 7506 ? 7106 : 7105 };
            return result;
        }
        internal static HealthEndpoints Resolve(LoginServerProfile server, IDictionary<string, HealthEndpoints> overrides)
            => server.Id != null && overrides != null && overrides.TryGetValue(server.Id, out var value) && value != null ? value.Copy() : Defaults(server);
        internal static Dictionary<string, HealthEndpoints> Clone(IDictionary<string, HealthEndpoints> values)
            => (values ?? new Dictionary<string, HealthEndpoints>()).Where(pair => pair.Value != null).ToDictionary(pair => pair.Key, pair => pair.Value.Copy());
    }

    internal sealed class HealthEndpointsDialog : EditorDialog
    {
        private readonly DataGridView grid;
        private readonly List<LoginServerProfile> profiles;
        public Dictionary<string, HealthEndpoints> Endpoints { get; private set; }
        internal HealthEndpointsDialog(IEnumerable<LoginServerProfile> profiles, IDictionary<string, HealthEndpoints> values)
            : base(L("Health check endpoints", "Adresser til statustjek"), 600)
        {
            this.profiles = profiles.ToList();
            ClientSize = new Size(940, 620);
            Fields.AutoScroll = false; Fields.RowCount = 3;
            Fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var note = new Label { AutoSize = true, Dock = DockStyle.Fill, ForeColor = Theme.Muted, Margin = new Padding(0, 0, 0, 12), Text = L(
                "Connection checks only; account login settings are separate. Enter an IP address or hostname and port. Unverified services start disabled. Public reports use their existing verified sources.",
                "Kun forbindelsestjek; kontoens loginindstillinger er separate. Angiv IP-adresse eller værtsnavn og port. Ikke-verificerede tjenester er slået fra. Offentlige rapporter bruger de eksisterende verificerede kilder.") };
            note.MaximumSize = new Size(850, 0); Fields.Controls.Add(note, 0, 0);
            Fields.SizeChanged += (sender, e) => note.MaximumSize = new Size(Math.Max(100, Fields.ClientSize.Width), 0);
            grid = new DataGridView { Name = "healthEndpointsGrid", Dock = DockStyle.Fill, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, BackgroundColor = Theme.Surface,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, ShowCellToolTips = false };
            grid.Columns.Add("Server", L("Server", "Server")); grid.Columns[0].ReadOnly = true; grid.Columns[0].FillWeight = 150;
            grid.Columns.Add("Service", L("Connection", "Forbindelse")); grid.Columns[1].ReadOnly = true;
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = L("Enabled", "Aktiv"), FillWeight = 50 });
            grid.Columns.Add("Host", L("IP address / hostname", "IP-adresse / værtsnavn")); grid.Columns[3].FillWeight = 150;
            grid.Columns.Add("Port", "Port"); grid.Columns[4].FillWeight = 50;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            grid.Columns[0].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            foreach (var profile in this.profiles)
            {
                var endpoints = HealthEndpoints.Resolve(profile, values);
                Add(profile, L("Login", "Login"), endpoints.Login);
                Add(profile, L("World / zone", "Verden / zone"), endpoints.Zone);
                Add(profile, L("Chat", "Chat"), endpoints.Chat);
            }
            Fields.Controls.Add(SlimScrolling.Wrap(grid), 0, 1);
            var reset = Theme.Button(L("Restore selected server defaults", "Gendan valgt servers standarder"));
            reset.AutoSize = true; reset.Margin = new Padding(0, 12, 0, 0);
            reset.Click += (sender, e) =>
            {
                if (grid.CurrentRow == null) return;
                grid.CancelEdit();
                string id = (string)grid.CurrentRow.Tag;
                var defaults = HealthEndpoints.Defaults(this.profiles.First(profile => profile.Id == id));
                var entries = new[] { defaults.Login, defaults.Zone, defaults.Chat }; int index = 0;
                foreach (DataGridViewRow row in grid.Rows) if ((string)row.Tag == id)
                { var endpoint = entries[index++]; row.Cells[2].Value = endpoint.Enabled; row.Cells[3].Value = endpoint.Host; row.Cells[4].Value = endpoint.Port == 0 ? "" : endpoint.Port.ToString(); }
            };
            Fields.Controls.Add(reset, 0, 2);
            SaveButton.Click += (sender, e) => { if (TrySave()) DialogResult = DialogResult.OK; };
        }
        private void Add(LoginServerProfile profile, string service, HealthEndpoint endpoint)
        {
            int row = grid.Rows.Add(profile.Name, service, endpoint.Enabled, endpoint.Host, endpoint.Port == 0 ? "" : endpoint.Port.ToString());
            grid.Rows[row].Tag = profile.Id;
        }
        private bool TrySave()
        {
            if (!grid.EndEdit()) return false;
            var result = new Dictionary<string, HealthEndpoints>(); int index = 0;
            foreach (var profile in profiles)
            {
                var entries = new List<HealthEndpoint>();
                for (int i = 0; i < 3; i++)
                {
                    var row = grid.Rows[index++]; string text = Convert.ToString(row.Cells[4].Value).Trim();
                    int port; bool numeric = int.TryParse(text, out port);
                    var endpoint = new HealthEndpoint { Enabled = Convert.ToBoolean(row.Cells[2].Value), Host = Convert.ToString(row.Cells[3].Value).Trim(), Port = port };
                    if ((text.Length > 0 && (!numeric || port < 1 || port > 65535)) || (endpoint.Host.Length > 0 && (endpoint.Host.Length > 253 || Uri.CheckHostName(endpoint.Host) == UriHostNameType.Unknown)) || (endpoint.Enabled && !endpoint.Valid))
                    {
                        grid.CurrentCell = row.Cells[3];
                        ErrorLabel.Text = L("Enter a valid IP address or hostname and a port from 1 to 65535 for enabled checks.", "Angiv en gyldig IP-adresse eller et værtsnavn og en port fra 1 til 65535 for aktive tjek."); return false;
                    }
                    entries.Add(endpoint);
                }
                var value = new HealthEndpoints { Login = entries[0], Zone = entries[1], Chat = entries[2] };
                if (value.Key != HealthEndpoints.Defaults(profile).Key) result[profile.Id] = value;
            }
            Endpoints = result; return true;
        }
    }
}
