using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal sealed partial class TeamStatsTable : UserControl
    {
        sealed class StatsGrid : DataGridView {internal StatsGrid(){DoubleBuffered=true;}}
        sealed class Metric {internal string Name;internal Func<CombatStats,long> Total;internal Func<CombatStats,double?> Rate;internal bool Experience;}
        sealed class PetRow {internal object Parent;internal PetCombatStats Pet;}
        readonly HashSet<string> collapsed=new HashSet<string>();
        readonly Metric[] playerMetrics={
            new Metric{Name="DMG",Total=s=>s.Damage,Rate=s=>s.Dps},
            new Metric{Name="Nano",Total=s=>s.NanoDamage,Rate=s=>s.NanoDps},
            new Metric{Name="Pets",Total=s=>s.PetDamage,Rate=s=>s.PetDps},
            new Metric{Name="Healing",Total=s=>s.Healing,Rate=s=>s.Hps},
            new Metric{Name="XP",Total=s=>s.Xp,Rate=s=>s.XpHour/3600,Experience=true},
            new Metric{Name="AXP",Total=s=>s.Axp,Rate=s=>s.AxpHour/3600,Experience=true},
            new Metric{Name="SK",Total=s=>s.Sk,Rate=s=>s.SkHour/3600,Experience=true}};
        readonly Metric[] npcMetrics={
            new Metric{Name="DMG",Total=s=>s.Damage,Rate=s=>s.Dps},
            new Metric{Name="Taken",Total=s=>s.DamageTaken,Rate=s=>s.TakenDps},
            new Metric{Name="Nano",Total=s=>s.NanoDamage,Rate=s=>s.NanoDps},
            new Metric{Name="Healing",Total=s=>s.Healing,Rate=s=>s.Hps}};
        bool npcMode;
        Metric[] metrics=>npcMode?npcMetrics:playerMetrics;
        string MetricLabel(Metric metric)=>npcMode && metric.Name=="DMG"?AOLauncher.Localization.Texts.Get("Text_DMG_dealt_F30CA17445B8"):npcMode && metric.Name=="Taken"?AOLauncher.Localization.Texts.Get("Text_DMG_taken_88091B9244FF"):metric.Name;
        readonly DataGridView grid=MakeGrid("teamStatistics"), totalGrid=MakeGrid("teamStatisticsTotal");
        readonly NexusComboBox view=new NexusComboBox {Name="statisticsMetric",DropDownStyle=ComboBoxStyle.DropDownList,Width=180};
        readonly Button reset=new NexusButton {Name="resetCardStats",Text=AOLauncher.Localization.Texts.Get("Text_Reset_selected_4536598DD042"),Width=150,Height=28};
        readonly FlowLayoutPanel toolbar=new FlowLayoutPanel {Name="teamStatisticsToolbar",Dock=DockStyle.Top,Height=44,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(12,7,0,6),WrapContents=true,BackColor=Theme.Surface};
        readonly Func<ClientObservation,CombatStats> read;
        readonly Action<ClientObservation> resetRow;
        readonly List<Func<CombatStats,object>> values=new List<Func<CombatStats,object>>();
        readonly List<bool> averages=new List<bool>();
        List<object> rows=new List<object>();CombatStats total;
        internal Action<ObservedCombatRow> ResetObserved;
        bool updating;
        internal TeamStatsTable(Func<ClientObservation,CombatStats> readStats,Action<ClientObservation> resetStats) {
            read=readStats;resetRow=resetStats;Dock=DockStyle.Fill;BackColor=Theme.Background;
            PopulateViews();
            toolbar.Controls.Add(view);toolbar.Controls.Add(reset);
            totalGrid.Dock=DockStyle.Bottom;totalGrid.Height=34;totalGrid.ColumnHeadersVisible=false;totalGrid.ScrollBars=ScrollBars.None;
            totalGrid.DefaultCellStyle.BackColor=Theme.SurfaceRaised;totalGrid.DefaultCellStyle.ForeColor=Theme.Accent;
            totalGrid.CellFormatting+=(s,e)=>{
                int index=e.ColumnIndex-2;
                if(index>=0 && index<averages.Count && averages[index] && e.Value is double value){e.Value=value.ToString("N1")+AOLauncher.Localization.Texts.Get("Text_avg_4BF7FE4F1DF0");e.FormattingApplied=true;}
            };
            Controls.Add(grid);Controls.Add(totalGrid);Controls.Add(toolbar);
            view.SelectedIndexChanged+=(s,e)=>{if(view.SelectedIndex<0 || restoringTable)return;ChangeMetric();};
            grid.SelectionChanged+=(s,e)=>{if(!updating)UpdateReset();};
            grid.CurrentCellChanged+=(s,e)=>{if(!updating)UpdateReset();};
            grid.SortCompare+=CompareGroups;
            grid.CellMouseClick+=(s,e)=>{if(e.RowIndex>=0 && e.ColumnIndex==0 && e.X<28 && e.Button==MouseButtons.Left)TogglePets(grid.Rows[e.RowIndex].Tag);};
            grid.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Left || e.KeyCode==Keys.Right) {
                var row=grid.CurrentRow?.Tag;if(row==null || row is PetRow)return;
                if(e.KeyCode==Keys.Left)collapsed.Add(CollapseKey(row));else collapsed.Remove(CollapseKey(row));SaveCollapsed();UpdateEntries(rows,total);e.Handled=true;
            }};
            grid.Scroll+=(s,e)=>{if(e.ScrollOrientation==ScrollOrientation.HorizontalScroll)totalGrid.HorizontalScrollingOffset=grid.HorizontalScrollingOffset;};
            grid.ColumnWidthChanged+=(s,e)=>{if(e.Column.Index<totalGrid.Columns.Count)totalGrid.Columns[e.Column.Index].Width=e.Column.Width;SaveTableLayout();};
            grid.Sorted+=(s,e)=>SaveTableLayout();
            reset.Click+=(s,e)=>{var selected=grid.CurrentRow?.Tag;if(selected is ClientObservation client)resetRow(client);else if(selected is ObservedCombatRow observed)ResetObserved?.Invoke(observed);};
            view.SelectedIndex=0;
        }
        void TogglePets(object row) {
            if(row==null || row is PetRow || npcMode || ReadStats(row)?.Pets.Count==0)return;
            if(!collapsed.Add(CollapseKey(row)))collapsed.Remove(CollapseKey(row));SaveCollapsed();UpdateEntries(rows,total);
        }
        void CompareGroups(object sender,DataGridViewSortCompareEventArgs e) {
            var left=grid.Rows[e.RowIndex1];var right=grid.Rows[e.RowIndex2];
            object lp=(left.Tag as PetRow)?.Parent??left.Tag,rp=(right.Tag as PetRow)?.Parent??right.Tag;
            if(lp==null || rp==null)return;
            int comparison;
            if(Key(lp)==Key(rp)) {
                comparison=left.Tag is PetRow?(right.Tag is PetRow?string.Compare(((PetRow)left.Tag).Pet.Name,((PetRow)right.Tag).Pet.Name,StringComparison.CurrentCulture):1):-1;
                if(comparison==0)comparison=string.Compare(Key(left.Tag),Key(right.Tag),StringComparison.Ordinal);
                // DataGridView reverses the comparison for descending sorting;
                // children must remain after their owner in either direction.
                if(grid.SortOrder==SortOrder.Descending)comparison=-comparison;
            } else {
                var a=grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>!(r.Tag is PetRow) && Key(r.Tag)==Key(lp));
                var b=grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>!(r.Tag is PetRow) && Key(r.Tag)==Key(rp));
                var av=e.Column.Index==0?RowName(lp):a?.Cells[e.Column.Index].Value;
                var bv=e.Column.Index==0?RowName(rp):b?.Cells[e.Column.Index].Value;
                comparison=System.Collections.Comparer.DefaultInvariant.Compare(av,bv);
                if(comparison==0)comparison=string.Compare(Key(lp),Key(rp),StringComparison.Ordinal);
            }
            e.SortResult=comparison;e.Handled=true;
        }
        static string RowName(object row)=>row is ClientObservation client?client.Name:(row as ObservedCombatRow)?.Name;
        void PopulateViews() {
            view.Items.Clear();view.Items.Add(npcMode?AOLauncher.Localization.Texts.Get("Text_Overview_dealt_taken_CA3440621F00"):AOLauncher.Localization.Texts.Get("Text_Overview_DMG_m_0BDA631B398F"));
            foreach(var metric in metrics)view.Items.Add(MetricLabel(metric)+" · /s /m /h");view.Items.Add(AOLauncher.Localization.Texts.Get("Text_All_totals_and_rates_BA9E777B6E54"));
        }
        internal void SetNpcMode(bool value) {
            if(npcMode==value)return;
            npcMode=value;view.Width=value?240:180;rows=new List<object>();total=null;PopulateViews();view.SelectedIndex=0;
        }
        internal void AddToolbarControl(Control control) { toolbar.Controls.Add(control); }
        static DataGridView MakeGrid(string name) {
            var result=new StatsGrid {Name=name,Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,
                AllowUserToResizeRows=false,RowHeadersVisible=false,MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor=Theme.Background,BorderStyle=BorderStyle.None,GridColor=Theme.Border,EnableHeadersVisualStyles=false,
                AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None,ColumnHeadersHeight=34,ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing};
            result.DefaultCellStyle=new DataGridViewCellStyle {BackColor=Theme.Surface,ForeColor=Theme.Foreground,SelectionBackColor=Theme.SurfaceRaised,SelectionForeColor=Theme.Accent,NullValue="—",Padding=new Padding(5,0,5,0)};
            result.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle {BackColor=Theme.SurfaceRaised,ForeColor=Theme.Foreground,Alignment=DataGridViewContentAlignment.MiddleLeft};
            result.RowTemplate.Height=32;return result;
        }
        void AddColumn(string name,string label,int width,Type type,string format=null) {
            var column=new DataGridViewTextBoxColumn {Name=name,HeaderText=label,Width=width,ValueType=type,SortMode=DataGridViewColumnSortMode.Automatic};
            if(format!=null){column.DefaultCellStyle.Format=format;column.DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleRight;}
            grid.Columns.Add(column);var clone=(DataGridViewColumn)column.Clone();clone.SortMode=DataGridViewColumnSortMode.NotSortable;totalGrid.Columns.Add(clone);
        }
        void ConfigureColumns() {
            grid.Rows.Clear();totalGrid.Rows.Clear();grid.Columns.Clear();totalGrid.Columns.Clear();values.Clear();averages.Clear();
            AddColumn("character",npcMode?"NPC":AOLauncher.Localization.Texts.Get("Text_Character_pet_42ACD03E2B8F"),npcMode?210:240,typeof(string));AddColumn("profession",npcMode?"ID":AOLauncher.Localization.Texts.Get("Text_Prof_803B69B2063D"),npcMode?90:65,typeof(string));
            grid.Columns[0].Frozen=true;grid.Columns[1].Frozen=true;totalGrid.Columns[0].Frozen=true;totalGrid.Columns[1].Frozen=true;
            bool overview=view.SelectedIndex==0,all=view.SelectedIndex==metrics.Length+1;
            var chosen=overview||all?metrics:new[]{metrics[view.SelectedIndex-1]};
            foreach(var metric in chosen) {
                var m=metric;AddColumn(m.Name,MetricLabel(m),110,typeof(long),"N0");values.Add(s=>m.Experience&&!s.Native?null:(object)m.Total(s));averages.Add(false);
                if(overview && npcMode && (m.Name=="DMG" || m.Name=="Taken")) {
                    AddColumn(m.Name+"m",m.Name=="DMG"?AOLauncher.Localization.Texts.Get("Text_Dealt_m_FCD59786972E"):AOLauncher.Localization.Texts.Get("Text_Taken_m_E7599F7976B6"),110,typeof(double),"N1");values.Add(s=>(object)(m.Rate(s)*60));averages.Add(false);
                }
                if(overview && !npcMode && m.Name=="Pets") {AddColumn("DMGm","DMG/m",110,typeof(double),"N1");values.Add(s=>(object)s.Dpm);averages.Add(true);}
                if(!overview)foreach(int seconds in new[]{1,60,3600}) {
                    int period=seconds;string unit=seconds==1?"s":seconds==60?"m":"h";
                    AddColumn(m.Name+unit,MetricLabel(m)+"/"+unit,110,typeof(double),"N1");values.Add(s=>m.Experience&&!s.Native?null:(object)(m.Rate(s)*period));averages.Add(!npcMode && m.Name=="DMG" && period==60);
                }
            }
            totalGrid.Rows.Add();grid.Sort(grid.Columns[0],ListSortDirection.Ascending);
        }
        static string Key(object row)=>row is PetRow pet?Key(pet.Parent)+"/pet/"+pet.Pet.Name.ToUpperInvariant():row is ClientObservation client?client.Pid+":"+client.StartTicks:((ObservedCombatRow)row).Key;
        internal static IEnumerable<PetCombatStats> PetRows(CombatStats owner) {
            return (owner?.Pets??new List<PetCombatStats>()).Where(p=>p.Stats!=null)
                .GroupBy(p=>string.IsNullOrWhiteSpace(p.Name)?"Pet":p.Name.Trim(),StringComparer.OrdinalIgnoreCase)
                .Select(g=>new PetCombatStats {Name=g.Key,Stats=new CombatStats {Seconds=owner.Seconds,
                    Damage=g.Sum(p=>p.Stats.Damage),PetDamage=g.Sum(p=>p.Stats.PetDamage),NanoDamage=g.Sum(p=>p.Stats.NanoDamage),Healing=g.Sum(p=>p.Stats.Healing)}})
                .OrderBy(p=>p.Name,StringComparer.CurrentCultureIgnoreCase);
        }
        CombatStats ReadStats(object row)=>row is PetRow pet?pet.Pet.Stats:row is ClientObservation client?read(client):(row as ObservedCombatRow)?.Stats;
        internal void UpdateRows(List<ClientObservation> observations,CombatStats totals)=>UpdateEntries(observations.Cast<object>().ToList(),totals);
        internal void UpdateObservedRows(List<ObservedCombatRow> observations,CombatStats totals)=>UpdateEntries(observations.Cast<object>().ToList(),totals);
        void UpdateEntries(List<object> observations,CombatStats totals) {
            rows=observations;total=totals;if(grid.Columns.Count==0)return;
            updating=true;
            try {
                var selected=grid.CurrentRow?.Tag;
                var entries=new List<object>();
                foreach(var parent in rows) {
                    entries.Add(parent);
                    if(!npcMode && !collapsed.Contains(CollapseKey(parent)))foreach(var pet in PetRows(ReadStats(parent)))
                        entries.Add(new PetRow {Parent=parent,Pet=pet});
                }
                var wanted=new HashSet<string>(entries.Select(Key));
                foreach(var stale in grid.Rows.Cast<DataGridViewRow>().Where(r=>!wanted.Contains(Key(r.Tag))).ToList())grid.Rows.Remove(stale);
                bool changed=false;
                foreach(var observation in entries) {
                    var row=grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>Key(r.Tag)==Key(observation));
                    if(row==null){row=grid.Rows[grid.Rows.Add()];row.Tag=observation;changed=true;}else row.Tag=observation;
                    var client=observation as ClientObservation;var observed=observation as ObservedCombatRow;
                    var petRow=observation as PetRow;
                    string profession=(client?.Pulse?.Profession??observed?.Profession)?.Replace("-","").Replace(" ","");
                    string shortName=profession=="Pet"?"Pet":profession!=null && PluginHealth.ProfessionKeys.TryGetValue(profession,out var key)?key:"—";
                    var stats=ReadStats(observation);
                    bool hasPets=!npcMode && petRow==null && stats?.Pets.Count>0;
                    string label=petRow!=null?"    ↳ "+petRow.Pet.Name:(hasPets?(collapsed.Contains(CollapseKey(observation))?"▸ ":"▾ "):"")+(client?.Name??observed?.Name);
                    changed|=Set(row,0,label);changed|=Set(row,1,petRow!=null?"Pet":npcMode?observed?.CharacterId.ToString():shortName=="Soldier"?"Sold":shortName);
                    row.DefaultCellStyle.ForeColor=petRow!=null?Theme.Muted:Theme.Foreground;
                    for(int i=0;i<values.Count;i++)changed|=Set(row,i+2,stats==null?null:values[i](stats));
                    row.Cells[0].ToolTipText=petRow!=null?petRow.Pet.Name+AOLauncher.Localization.Texts.Get("Text_Included_in_the_owner_s_total_72144836D833"):hasPets?AOLauncher.Localization.Texts.Get("Text_Click_the_arrow_to_expand_collapse_E329B5F4F089"):client!=null?TeamPresentation.Status(client):npcMode?AOLauncher.Localization.Texts.Get("Text_Damage_dealt_by_and_taken_by_1F760169D798"):AOLauncher.Localization.Texts.Get("Text_Combat_seen_by_the_selected_observer_3E958F364989");
                }
                if(changed && grid.SortedColumn!=null)grid.Sort(grid.SortedColumn,grid.SortOrder==SortOrder.Descending?ListSortDirection.Descending:ListSortDirection.Ascending);
                if(selected!=null){var current=grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>Key(r.Tag)==Key(selected));if(current!=null && grid.CurrentRow!=current)grid.CurrentCell=current.Cells[0];}
                var footer=totalGrid.Rows[0];Set(footer,0,npcMode?"TOTAL":AOLauncher.Localization.Texts.Get("Text_TOTAL_AVG_C9BF6C77F06C"));Set(footer,1,null);
                for(int i=0;i<values.Count;i++) {
                    object value=total==null||total.Observed==0?null:values[i](total);
                    if(averages[i] && value is double rate)value=rate/total.Observed;
                    Set(footer,i+2,value);
                    footer.Cells[i+2].ToolTipText=averages[i]?AOLauncher.Localization.Texts.Get("Text_Average_DMG_per_minute_per_measured_DBEE085C68CF"):AOLauncher.Localization.Texts.Get("Text_Combined_total_98FF038243D4");
                }
            } finally {updating=false;UpdateReset();}
        }
        static bool Set(DataGridViewRow row,int column,object value){if(Equals(row.Cells[column].Value,value))return false;row.Cells[column].Value=value;return true;}
        void UpdateReset(){reset.Enabled=grid.CurrentRow?.Tag!=null && !(grid.CurrentRow.Tag is PetRow) && ReadStats(grid.CurrentRow.Tag)!=null;}
    }
}
