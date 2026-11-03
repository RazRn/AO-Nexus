using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal sealed partial class TeamWindow
    {
        static string ReportProfession(string value)
        {
            string clean=(value??"").Replace("-", "").Replace(" ", "");
            return PluginHealth.ProfessionKeys.TryGetValue(clean,out var key)?key:clean;
        }
        internal List<CombatScriptReport> CaptureCombatReports()
        {
            RefreshWall();
            var scopes=new List<CombatReportScope> {
                new CombatReportScope {Key="team",Context=(teams.SelectedItem as LaunchPreset)?.Name??"All running characters",
                    Rows=shownRows.Select(r=>new CombatReportRow {Name=r.Name,Profession=ReportProfession(r.Pulse?.Profession),Stats=combat.Card(r)}).ToList(),Total=combat.Total(shownRows)}
            };
            if (configuration().ExperimentalNearbyCombat)
            foreach(var pair in new[]{new {Key="all",Meter=observedCombat},new {Key="npc",Meter=npcCombat}})
                scopes.Add(new CombatReportScope {Key=pair.Key,
                    Rows=pair.Meter.Rows().Select(r=>new CombatReportRow {Name=r.Name,Profession=ReportProfession(r.Profession),Stats=r.Stats}).ToList(),Total=pair.Meter.Total()});
            return CombatScripts.Build(scopes,DateTime.Now);
        }
        void ExportCombatScripts()
        {
            try {using(var dialog=new CombatScriptsDialog(configuration(),CaptureCombatReports,shownRows.Select(r=>r.Client?.GamePath),SavePanelPreferences))dialog.ShowDialog(this);}
            catch(Exception error) {MessageBox.Show(this,error.Message,AOLauncher.Localization.Texts.Get("Text_Combat_scripts_8BEC3979E2D9"),MessageBoxButtons.OK,MessageBoxIcon.Error);}
        }
    }
    internal sealed class CombatScriptsDialog : Form
    {
        sealed class Destination {internal string Root; public override string ToString()=>GuiScripts.ScriptsRoot(Root);}
        readonly ComboBox destination=new NexusComboBox {Name="combatScriptsDestination",Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};
        readonly ListBox reports=new ListBox {Name="combatScriptReports",Dock=DockStyle.Fill,HorizontalScrollbar=true,IntegralHeight=false,BackColor=Theme.Surface,ForeColor=Theme.Foreground};
        readonly TextBox preview=new NexusTextBox {Name="combatScriptPreview",Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical};
        readonly Label status=new Label {Name="combatScriptsStatus",Dock=DockStyle.Fill,ForeColor=Theme.Muted};
        readonly Func<List<CombatScriptReport>> capture;
        List<CombatScriptReport> snapshot;
        internal CombatScriptsDialog(LauncherSettings settings,Func<List<CombatScriptReport>> capture,IEnumerable<string> activeGamePaths=null,Action savePreferences=null)
        {
            this.capture=capture;Text=AOLauncher.Localization.Texts.Get("Text_AO_NEXUS_Combat_scripts_DA2BA8858046");Name="combatScriptsDialog";
            AppArtwork.ApplyIcon(this);BackColor=Theme.Background;ForeColor=Theme.Foreground;Font=new Font("Segoe UI",10);
            Size=new Size(960,690);MinimumSize=new Size(820,540);StartPosition=FormStartPosition.CenterParent;
            var layout=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(14),ColumnCount=1,RowCount=5};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,72));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,78));
            layout.Controls.Add(new Label {Dock=DockStyle.Fill,Text=AOLauncher.Localization.Texts.Get("Text_Share_coloured_clickable_combat_reports_in_73FBDCC1E383")},0,0);
            layout.Controls.Add(destination,0,1);
            // A test installation can differ from the account's saved GUI source.
            // Discover actual game folders first; leave the full destination visible.
            foreach(string game in (activeGamePaths??Enumerable.Empty<string>()).Where(p=>!string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
                foreach(var candidate in PrefsDiscovery.Find(PrefsDiscovery.DefaultRoot,game,null).Where(c=>c.MatchesGamePath || c.MatchesGameFolderName))AddDestination(candidate.PrefsPath);
            foreach(var account in settings.Accounts)try {
                AddDestination(account.PrefsPath);
            }catch(Exception error)when(error is IOException || error is ArgumentException || error is UnauthorizedAccessException){}
            var split=new SplitContainer {Dock=DockStyle.Fill,Size=new Size(910,400),SplitterDistance=350,Panel1MinSize=240,Panel2MinSize=300};
            split.Panel1.Controls.Add(reports);split.Panel2.Controls.Add(preview);layout.Controls.Add(split,0,2);
            var actions=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=true,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink};
            Add(actions,AOLauncher.Localization.Texts.Get("Text_Refresh_snapshot_CB2A42120BB8"),RefreshSnapshot);
            Add(actions,AOLauncher.Localization.Texts.Get("Text_Save_scripts_FCCE708BDAC6"),Save);
            Add(actions,AOLauncher.Localization.Texts.Get("Text_Copy_post_command_E1AF37A11BB3"),()=>Copy(false));
            Add(actions,AOLauncher.Localization.Texts.Get("Text_Copy_private_preview_BF06DC928977"),()=>Copy(true));
            Add(actions,AOLauncher.Localization.Texts.Get("Text_Choose_Prefs_931A7090864C"),()=> {
                using(var choose=new FolderBrowserDialog {Description="Choose this installation's Prefs folder"})if(choose.ShowDialog(this)==DialogResult.OK)try {
                    var item=new Destination {Root=GuiSkins.ResolvePrefsRoot(choose.SelectedPath)};destination.Items.Add(item);destination.SelectedItem=item;
                }catch(Exception error){status.Text=error.Message;}
            });
            layout.Controls.Add(actions,0,3);layout.Controls.Add(status,0,4);Controls.Add(layout);
            var preferences=settings.ControlPanel??(settings.ControlPanel=new ControlPanelPreferences());
            bool restoring=true;
            reports.SelectedIndexChanged+=(s,e)=>{ShowPreview();if(!restoring && reports.SelectedItem is CombatScriptReport report){preferences.ScriptReport=report.Command;savePreferences?.Invoke();}};
            destination.SelectedIndexChanged+=(s,e)=>{if(!restoring && destination.SelectedItem is Destination target){preferences.ScriptDestination=target.Root;savePreferences?.Invoke();}};
            if(destination.Items.Count>0)destination.SelectedIndex=0;
            RefreshSnapshot();
            if(!string.IsNullOrWhiteSpace(preferences.ScriptDestination)) {
                AddDestination(preferences.ScriptDestination);
                var selected=destination.Items.Cast<Destination>().FirstOrDefault(d=>string.Equals(d.Root,preferences.ScriptDestination,StringComparison.OrdinalIgnoreCase));
                if(selected!=null)destination.SelectedItem=selected;
            }
            var savedReport=snapshot.FirstOrDefault(r=>r.Command==preferences.ScriptReport);
            if(savedReport!=null)reports.SelectedItem=savedReport;
            restoring=false;
        }
        void AddDestination(string path) {
            try {string root=GuiSkins.ResolvePrefsRoot(path);
                if(!destination.Items.Cast<Destination>().Any(d=>string.Equals(d.Root,root,StringComparison.OrdinalIgnoreCase)))destination.Items.Add(new Destination {Root=root});
            }catch(Exception error)when(error is IOException || error is ArgumentException || error is UnauthorizedAccessException){}
        }
        void Add(FlowLayoutPanel panel,string text,Action action) {
            var button=new NexusButton {Text=text,AutoSize=true,Height=30,BackColor=Theme.SurfaceRaised,ForeColor=Theme.Foreground,FlatStyle=FlatStyle.Flat};
            button.Click+=(s,e)=>action();panel.Controls.Add(button);
        }
        void RefreshSnapshot() {
            string selected=(reports.SelectedItem as CombatScriptReport)?.Command;
            snapshot=capture();reports.BeginUpdate();reports.Items.Clear();reports.Items.AddRange(snapshot.ToArray());reports.EndUpdate();
            reports.SelectedItem=snapshot.FirstOrDefault(r=>r.Command==selected)??snapshot.FirstOrDefault();
            status.Text=AOLauncher.Localization.Texts.Get("Text_Snapshot_captured_save_to_update_the_E2900F629534");
        }
        void ShowPreview() {
            var r=reports.SelectedItem as CombatScriptReport;
            preview.Text=r==null?"":"/"+r.Command+"\r\n"+r.Links.Count+" page links in this command.\r\nPrivate: /"+r.Command+"-preview\r\n\r\n"+r.Preview;
        }
        void Save() {
            if(!(destination.SelectedItem is Destination target)){status.Text=AOLauncher.Localization.Texts.Get("Text_Choose_the_game_s_Prefs_folder_3A4476331079");return;}
            try {
                var files=CombatScripts.Files(snapshot);
                GuiScripts.WriteReports(target.Root,files,Path.Combine(GuiLayouts.DefaultRoot,"ScriptBackups"));
                status.Text=AOLauncher.Localization.Texts.Get("Text_Saved_0381EC88C036")+files.Count+AOLauncher.Localization.Texts.Get("Text_scripts_Type_nexus_in_game_Previous_1B7FE8DA54C1");
            }catch(Exception error){status.Text=error.Message;}
        }
        void Copy(bool local) {
            if(reports.SelectedItem is CombatScriptReport r)try {Clipboard.SetText("/"+r.Command+(local?"-preview":""));status.Text=AOLauncher.Localization.Texts.Get("Text_Command_copied_Save_the_scripts_before_24F7C9E08702");}
            catch(System.Runtime.InteropServices.ExternalException){status.Text=AOLauncher.Localization.Texts.Get("Text_Clipboard_is_busy_Try_again_28685FF76470");}
        }
    }
}
