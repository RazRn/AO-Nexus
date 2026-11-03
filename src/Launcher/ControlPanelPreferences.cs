using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AOLauncher
{
    // User choices only. Session counters, process IDs and pending actions are not preferences.
    internal sealed class ControlPanelPreferences
    {
        public bool LivePreviews { get; set; } = true;
        public bool AlwaysOnTop { get; set; }
        public string TeamId { get; set; }
        public string FormationLeader { get; set; }
        public decimal FormationSpacing { get; set; } = 2;
        public string FormationMode { get; set; } = "follow";
        public decimal FormationRadius { get; set; } = 3;
        public int SyncRefreshRate { get; set; }
        public bool AutoRegroup { get; set; }
        public int Tab { get; set; }
        public string ObserverKey { get; set; }
        public string ObserverName { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Maximized { get; set; }
        public int Splitter { get; set; } // Legacy divider position, used for migration.
        public List<ControlPanelSectionPreference> Sections { get; set; } = new List<ControlPanelSectionPreference>();
        public string TimerScope { get; set; }
        public string ScriptDestination { get; set; }
        public string ScriptReport { get; set; }
        public Dictionary<string,ControlPanelTablePreferences> Tables { get; set; } = new Dictionary<string,ControlPanelTablePreferences>();
        internal ControlPanelTablePreferences Table(int tab)
        {
            if(Tables==null)Tables=new Dictionary<string,ControlPanelTablePreferences>();
            string key=tab==2?"npc":tab==1?"all":"team";
            if(!Tables.TryGetValue(key,out var result) || result==null)Tables[key]=result=new ControlPanelTablePreferences();
            if(result.Layouts==null)result.Layouts=new Dictionary<string,ControlPanelColumnPreferences>();
            if(result.CollapsedOwners==null)result.CollapsedOwners=new List<string>();
            return result;
        }
    }
    internal sealed class ControlPanelTablePreferences
    {
        public int Metric { get; set; }
        public List<string> CollapsedOwners { get; set; } = new List<string>();
        public Dictionary<string,ControlPanelColumnPreferences> Layouts { get; set; } = new Dictionary<string,ControlPanelColumnPreferences>();
        internal ControlPanelColumnPreferences Layout(int metric)
        {
            string key=metric.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if(!Layouts.TryGetValue(key,out var result) || result==null)Layouts[key]=result=new ControlPanelColumnPreferences();
            if(result.Widths==null)result.Widths=new Dictionary<string,int>();
            return result;
        }
    }
    internal sealed class ControlPanelColumnPreferences
    {
        public string SortColumn { get; set; } = "character";
        public bool Descending { get; set; }
        public Dictionary<string,int> Widths { get; set; } = new Dictionary<string,int>();
    }

    internal sealed partial class TeamWindow
    {
        bool restoringPreferences=true, changingTeams, panelShown, preferencesDisposed;
        readonly Timer preferenceSaveTimer=new Timer {Interval=400};
        bool preferencesDirty;
        string appliedMeterSource;
        ControlPanelPreferences Preferences => configuration().ControlPanel ?? (configuration().ControlPanel=new ControlPanelPreferences());
        internal static string ObserverKey(ClientObservation row) => row?.Client!=null && row.Client.CharacterId>0
            ? row.Client.Server+":"+row.Client.Port+"/"+row.Client.AccountIdentity+":"+row.Client.CharacterId : null;

        void InitializePreferences(CheckBox onTop)
        {
            var p=Preferences;
            previews.Checked=p.LivePreviews;onTop.Checked=p.AlwaysOnTop;TopMost=p.AlwaysOnTop;
            if(p.Width>0 && p.Height>0) {
                var saved=new Rectangle(p.Left,p.Top,p.Width,p.Height);
                var area=Screen.FromRectangle(saved).WorkingArea;
                Bounds=ConstrainPanelBounds(saved,area);StartPosition=FormStartPosition.Manual;
            }
            damageTabs.SelectedIndex=Math.Max(0,Math.Min(damageTabs.TabCount-1,p.Tab));
            statistics.UsePreferences(p,damageTabs.SelectedIndex,SavePanelPreferences);
            previews.CheckedChanged+=(s,e)=>{if(!restoringPreferences){Preferences.LivePreviews=previews.Checked;SavePanelPreferences();}};
            onTop.CheckedChanged+=(s,e)=>{if(!restoringPreferences){Preferences.AlwaysOnTop=onTop.Checked;SavePanelPreferences();}};
            teams.SelectedIndexChanged+=(s,e)=>{if(!restoringPreferences && !changingTeams){Preferences.TeamId=(teams.SelectedItem as LaunchPreset)?.Id;SavePanelPreferences();}};
            damageTabs.SelectedIndexChanged+=(s,e)=>{if(!restoringPreferences){Preferences.Tab=damageTabs.SelectedIndex;SavePanelPreferences();}};
            LocationChanged+=(s,e)=>CapturePanelBounds();
            SizeChanged+=(s,e)=>CapturePanelBounds();
            FormClosing+=(s,e)=>{CapturePanelBounds();FlushPanelPreferences();};
            preferenceSaveTimer.Tick+=(s,e)=>FlushPanelPreferences();
            restoringPreferences=false;
        }
        internal static Rectangle ConstrainPanelBounds(Rectangle saved,Rectangle area)
        {
            int width=Math.Min(area.Width,Math.Max(760,saved.Width));
            int height=Math.Min(area.Height,Math.Max(560,saved.Height));
            return new Rectangle(Math.Max(area.Left,Math.Min(saved.Left,area.Right-width)),
                Math.Max(area.Top,Math.Min(saved.Top,area.Bottom-height)),width,height);
        }
        void RestorePanelLayout()
        {
            restoringPreferences=true;
            try {
                if(Preferences.Maximized)WindowState=FormWindowState.Maximized;
            } finally {restoringPreferences=false;panelShown=true;}
        }
        void CapturePanelBounds()
        {
            if(!panelShown || restoringPreferences || preferencesDisposed || WindowState==FormWindowState.Minimized)return;
            var p=Preferences;
            bool changed=p.Maximized!=(WindowState==FormWindowState.Maximized);
            if(WindowState==FormWindowState.Normal) {
                changed|=p.Left!=Left || p.Top!=Top || p.Width!=Width || p.Height!=Height;
                p.Left=Left;p.Top=Top;p.Width=Width;p.Height=Height;
            }
            p.Maximized=WindowState==FormWindowState.Maximized;if(changed)SavePanelPreferences();
        }
        void SavePanelPreferences()
        {
            if(restoringPreferences || preferencesDisposed)return;
            preferencesDirty=true;preferenceSaveTimer.Stop();preferenceSaveTimer.Start();
        }
        void FlushPanelPreferences()
        {
            preferenceSaveTimer.Stop();
            if(!preferencesDirty)return;
            preferencesDirty=false;saveCardOrder?.Invoke();
        }
        void DisposePreferences()
        {
            if(preferencesDisposed)return;
            CapturePanelBounds();FlushPanelPreferences();preferencesDisposed=true;preferenceSaveTimer.Dispose();
        }
        void ApplyMeterSource(MeterSourceChoice choice)
        {
            // Keep an explicit offline choice pinned, rather than silently using somebody else.
            var source=choice?.Row;
            string token=source!=null?source.Pid+":"+source.StartTicks:choice?.SavedKey!=null?"offline:"+choice.SavedKey:"automatic";
            if(appliedMeterSource==token)return;
            appliedMeterSource=token;
            if(source==null && choice?.SavedKey!=null)source=new ClientObservation {Pid=-1,StartTicks=-1,Name=choice.SavedName};
            combat.SelectSource(source);observedCombat.SelectSource(source);npcCombat.SelectSource(source);
        }
    }

    internal sealed partial class TeamStatsTable
    {
        bool restoringTable;
        ControlPanelTablePreferences tablePreferences;
        Action tablePreferencesChanged;
        internal void UsePreferences(ControlPanelPreferences preferences,int tab,Action changed)
        {
            var next=preferences.Table(tab);
            if(ReferenceEquals(next,tablePreferences))return;
            tablePreferences=next;tablePreferencesChanged=changed;
            restoringTable=true;
            try {
                npcMode=tab==2;view.Width=npcMode?240:180;rows=new List<object>();total=null;
                collapsed.Clear();foreach(string key in next.CollapsedOwners.Where(k=>k!=null))collapsed.Add(key);
                PopulateViews();view.SelectedIndex=Math.Max(0,Math.Min(view.Items.Count-1,next.Metric));
                ConfigureColumns();RestoreTableLayout();
            } finally {restoringTable=false;}
        }
        void ChangeMetric()
        {
            restoringTable=true;
            try {
                if(tablePreferences!=null)tablePreferences.Metric=view.SelectedIndex;
                ConfigureColumns();RestoreTableLayout();UpdateEntries(rows,total);
            } finally {restoringTable=false;}
            tablePreferencesChanged?.Invoke();
        }
        void RestoreTableLayout()
        {
            if(tablePreferences==null)return;
            var p=tablePreferences.Layout(view.SelectedIndex);
            foreach(DataGridViewColumn col in grid.Columns)
                if(p.Widths.TryGetValue(col.Name,out int width))col.Width=Math.Max(40,Math.Min(1200,width));
            var sort=grid.Columns.Contains(p.SortColumn??"")?grid.Columns[p.SortColumn]:grid.Columns[0];
            grid.Sort(sort,p.Descending?ListSortDirection.Descending:ListSortDirection.Ascending);
        }
        void SaveTableLayout()
        {
            if(restoringTable || updating || tablePreferences==null)return;
            var p=tablePreferences.Layout(view.SelectedIndex);
            p.SortColumn=grid.SortedColumn?.Name??"character";p.Descending=grid.SortOrder==SortOrder.Descending;
            p.Widths=grid.Columns.Cast<DataGridViewColumn>().ToDictionary(c=>c.Name,c=>c.Width);
            tablePreferencesChanged?.Invoke();
        }
        static string CollapseKey(object row) => row is ClientObservation client?TeamWindow.ObserverKey(client)??client.Name:
            (row as ObservedCombatRow)?.Name?.ToUpperInvariant()??"";
        void SaveCollapsed()
        {
            if(tablePreferences==null)return;
            tablePreferences.CollapsedOwners=collapsed.ToList();tablePreferencesChanged?.Invoke();
        }
    }
}
