using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal sealed partial class MainForm
    {
        TeamWindow teamWindow;
        void ShowTeamWindow()
        {
            if (teamWindow == null || teamWindow.IsDisposed) {
                teamWindow = new TeamWindow(() => clientObservations.ToList(), () => settings,
                    async () => { ShowLauncher(); await ShowSection("plugins"); }, LogoutTeamClients,
                    () => !shortcutActionBusy && PresetShortcutContextAvailable(), RelogTeamClients, RecoverTeamClient,()=>backgroundClients?.Status,SaveSettings,
                    ()=>panelHealthHistory.ToList());
                teamWindow.BeforeLeaderChange=(client,members)=>teamRecovery.BeforeLeaderChange(settings,client,members);
                // An owned window stays above its owner even when TopMost is false.
                teamWindow.Show();
            } else { if(teamWindow.WindowState==FormWindowState.Minimized) teamWindow.WindowState=FormWindowState.Normal; teamWindow.Activate(); }
        }
    }

    internal sealed class ClientResponseGraph : Control
    {
        readonly List<Tuple<DateTime,double?>> local=new List<Tuple<DateTime,double?>>();
        List<HealthResult> services=new List<HealthResult>();
        DateTime nextSample;
        internal ClientResponseGraph() {
            Dock=DockStyle.Top;Height=92;Name="controlPanelResponseGraph";
            DoubleBuffered=true;BackColor=Theme.Background;
        }
        internal void Sample(List<ClientObservation> rows,List<HealthSnapshot> history)
        {
            var now=DateTime.UtcNow;
            if(now<nextSample)return;nextSample=now.AddSeconds(1);
            var values=rows.Where(r=>!r.ExitedUtc.HasValue && r.Sample!=null && (now-r.Sample.Utc).TotalSeconds<3
                && r.Sample.ReplyAgeMs<2500 && r.Sample.ReplyMs.HasValue).Select(r=>r.Sample.ReplyMs.Value).ToArray();
            local.Add(Tuple.Create(now,values.Length==0?(double?)null:values.Max()));
            local.RemoveAll(p=>p.Item1<now.AddMinutes(-5));
            var endpoints=new HashSet<string>(rows.Where(r=>!r.ExitedUtc.HasValue && r.Client!=null)
                .Select(r=>r.Client.Server+":"+r.Client.Port),StringComparer.OrdinalIgnoreCase);
            services=(history??new List<HealthSnapshot>()).Where(s=>endpoints.Count==0 || endpoints.Contains(ServerHealth.EndpointKey(s.Server)))
                .Select(s=>s.Results.Where(r=>r.Key!="report").ToArray()).Where(r=>r.Length>0)
                .Select(r=>new HealthResult {CheckedUtc=r.Max(v=>v.CheckedUtc),
                    State=r.Any(v=>v.State==HealthState.Unreachable)?HealthState.Unreachable:HealthState.Reachable,
                    Milliseconds=r.Where(v=>v.Milliseconds.HasValue).Select(v=>v.Milliseconds).DefaultIfEmpty(null).Max()})
                .Where(r=>r.CheckedUtc>=now.AddMinutes(-5)).ToList();
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);var now=DateTime.UtcNow;
            var left=new Rectangle(18,27,Math.Max(80,Width/2-36),46);
            var right=new Rectangle(Width/2+12,27,Math.Max(80,Width/2-30),46);
            TextRenderer.DrawText(e.Graphics,AOLauncher.Localization.Texts.Get("Graph.Local"),Font,new Point(left.X,5),Theme.Muted);
            TextRenderer.DrawText(e.Graphics,AOLauncher.Localization.Texts.Get("Graph.Services"),Font,new Point(right.X,5),Theme.Muted);
            var servicePoints=services.GroupBy(r=>r.CheckedUtc).OrderBy(g=>g.Key)
                .Select(g=>Tuple.Create(g.Key,g.Any(r=>r.State==HealthState.Unreachable)?(double?)null:g.Where(r=>r.Milliseconds.HasValue).Select(r=>(double?)r.Milliseconds.Value).Max())).ToList();
            Plot(e.Graphics,left,local,now,Theme.Accent,3);
            Plot(e.Graphics,right,servicePoints,now,Theme.Amber,150);
        }
        void Plot(Graphics g,Rectangle rect,List<Tuple<DateTime,double?>> points,DateTime now,Color color,int freshness)
        {
            using(var pen=new Pen(Theme.Border))g.DrawRectangle(pen,rect);
            double ceiling=Math.Max(50,points.Where(p=>p.Item2.HasValue).Select(p=>p.Item2.Value).DefaultIfEmpty(0).Max()*1.15);
            PointF? previous=null;DateTime previousTime=DateTime.MinValue;
            using(var pen=new Pen(color,1.5f))using(var brush=new SolidBrush(color)) {
                foreach(var p in points) {
                    if(!p.Item2.HasValue){previous=null;continue;}
                    var current=new PointF(rect.Right-(float)((now-p.Item1).TotalSeconds/300*rect.Width),rect.Bottom-(float)(p.Item2.Value/ceiling*rect.Height));
                    if(previous.HasValue && (p.Item1-previousTime).TotalSeconds<=freshness)g.DrawLine(pen,previous.Value,current);
                    g.FillEllipse(brush,current.X-1.5f,current.Y-1.5f,3,3);previous=current;previousTime=p.Item1;
                }
            }
            var last=points.LastOrDefault();
            string value=last?.Item2!=null && (now-last.Item1).TotalSeconds<=freshness?last.Item2.Value.ToString("0")+" ms":"—";
            TextRenderer.DrawText(g,value,Font,new Point(rect.X+4,rect.Y+3),color,BackColor);
        }
    }

    internal static class TeamPresentation
    {
        internal static string TooltipText(string text)
        {
            var lines=new List<string>();
            foreach(var paragraph in (text??"").Replace("\r","").Split('\n')) {
                string remaining=paragraph;
                while(remaining.Length>76) {
                    int split=remaining.LastIndexOf(' ',76);
                    if(split<=0)split=76;
                    lines.Add(remaining.Substring(0,split));remaining=remaining.Substring(split).TrimStart();
                }
                lines.Add(remaining);
            }
            return string.Join("\n",lines);
        }
        internal static bool Leader(ClientObservation row,long now) => Fresh(row,now) && row.Pulse.IsTeamLeader==true
            && row.Pulse.TeamStateQpc>0 && row.Pulse.TeamStateQpc<=row.Pulse.PublishedQpc && ClientMonitor.Age(now,row.Pulse.TeamStateQpc)<2500;
        internal static bool Fresh(ClientObservation row, long now)
        {
            var p=row?.Pulse;
            return row!=null && !row.ExitedUtc.HasValue && p!=null && p.Active && !p.Zoning && row.Sample?.Zoning!=true
                && p.Pid==row.Pid && p.StartTicks==row.StartTicks && p.VitalsQpc<=p.PublishedQpc
                && ClientMonitor.Age(now,p.PublishedQpc)<2500 && ClientMonitor.Age(now,p.VitalsQpc)<2500;
        }
        internal static double? Ratio(int current, int maximum) => maximum<=0 || maximum>100000000 || current<0 || current>maximum
            ? (double?)null : (double)current/maximum;
        internal static bool RecentCast(ClientObservation row,long now) => Fresh(row,now) && row.Pulse.CastNanoId>0
            && row.Pulse.CastAttemptQpc<=row.Pulse.PublishedQpc && ClientMonitor.Age(now,row.Pulse.CastAttemptQpc)<5000;
        internal static bool Attacking(ClientObservation row,long now)=>Fresh(row,now) && row.Pulse.Attacking;
        internal static int? AggroCount(ClientObservation row,long now)=>Fresh(row,now)
            && row.Pulse.AggroQpc>0 && row.Pulse.AggroQpc<=row.Pulse.PublishedQpc
            && ClientMonitor.Age(now,row.Pulse.AggroQpc)<2500 && row.Pulse.AggroCount>=0
            ? (int?)row.Pulse.AggroCount : null;
        internal static string AggroNames(ClientObservation row)
        {
            var names=(row?.Pulse?.AggroNames??new string[0]).Take(3).Select(n=> {
                string name=new string((n??"").Where(c=>!char.IsControl(c)).Take(96).ToArray()).Trim();
                return name.Length>0?name:AOLauncher.Localization.Texts.Get("Text_Unknown_target_DCD863AEFB73");
            }).ToArray();
            int more=Math.Max(0,(row?.Pulse?.AggroCount??0)-names.Length);
            return string.Join(" · ",names)+(more>0?" · "+string.Format(AOLauncher.Localization.Texts.Get("Aggro.More"),more):"");
        }
        internal static string Target(ClientObservation row) {
            string name=new string((row?.Pulse?.FightingTarget??"").Where(c=>!char.IsControl(c)).ToArray()).Trim();
            return name.Length>0?name:AOLauncher.Localization.Texts.Get("Text_Unknown_target_DCD863AEFB73");
        }
        internal static string NanoAttempt(ClientPulse pulse) {
            string name=new string((pulse?.CastNanoName??"").Where(c=>!char.IsControl(c)).Take(96).ToArray()).Trim();
            return AOLauncher.Localization.Texts.Get("Text_Nano_attempt_3556753B9511")+(name.Length>0 ? name : AOLauncher.Localization.Texts.Get("Text_Unknown_nano_6122EE363302")+" (#"+(pulse?.CastNanoId??0)+")");
        }
        internal static string Status(ClientObservation row)
        {
            if(row.ExitedUtc.HasValue) return AOLauncher.Localization.Texts.Get("Text_CLIENT_CLOSED_B15B59C3129F");
            if(row.Sample==null || (DateTime.UtcNow-row.Sample.Utc).TotalSeconds>3) return AOLauncher.Localization.Texts.Get("Text_NO_FRESH_MEASUREMENT_AE281AEDA9C1");
            if(row.Sample.WindowResponding==false && row.Sample.WindowUnresponsiveSamples>=3)return AOLauncher.Localization.Texts.Get("Text_CLIENT_FROZEN_2706781CADEF");
            if(row.Sample.Zoning==true && row.Sample.PulseAgeMs<3000) return AOLauncher.Localization.Texts.Get("Text_ZONING_236F3469107F");
            if(row.Condition==ClientCondition.Healthy)return AOLauncher.Localization.Texts.Get("Text_Normal_response_time_1D46D676C9DD");
            if(row.Condition==ClientCondition.Delayed)return AOLauncher.Localization.Texts.Get("Text_Delayed_response_time_161A365E5174");
            if(row.Condition==ClientCondition.NoResponse)return AOLauncher.Localization.Texts.Get("Text_Bad_response_time_6160495066EF");
            return ClientMonitor.ConditionText(row.Condition).ToUpperInvariant();
        }
        internal static Color StatusColor(ClientObservation row)
        {
            if(row.ExitedUtc.HasValue || row.Sample==null || (DateTime.UtcNow-row.Sample.Utc).TotalSeconds>3) return Theme.Muted;
            if(row.Sample.WindowResponding==false && row.Sample.WindowUnresponsiveSamples>=3)return Color.FromArgb(244,105,105);
            if(row.Sample.Zoning==true) return Theme.Accent;
            return row.Condition==ClientCondition.Healthy ? Color.FromArgb(96,220,161) :
                row.Condition==ClientCondition.Delayed ? Color.FromArgb(255,206,84) :
                row.Condition==ClientCondition.NoResponse ? Color.FromArgb(244,105,105) : Theme.Muted;
        }
    }

    internal static class TeamCardOrdering
    {
        internal static string Key(ClientObservation row) => row.Client!=null && !string.IsNullOrEmpty(row.Client.AccountIdentity) && row.Client.CharacterId>0
            ? row.Client.AccountIdentity+":"+row.Client.CharacterId : "session:"+row.Pid+":"+row.StartTicks;
        internal static List<ClientObservation> Sort(IEnumerable<ClientObservation> rows,IEnumerable<string> saved)
        {
            var ranks=(saved??Enumerable.Empty<string>()).Where(k=>!string.IsNullOrEmpty(k)).Distinct().Select((key,index)=>new {key,index}).ToDictionary(x=>x.key,x=>x.index);
            return rows.OrderBy(r=>ranks.TryGetValue(Key(r),out var rank)?rank:int.MaxValue)
                .ThenBy(r=>r.Name,StringComparer.OrdinalIgnoreCase).ThenBy(Key,StringComparer.Ordinal).ToList();
        }
        internal static List<string> Move(IEnumerable<string> saved,IEnumerable<ClientObservation> rows,string source,string target,bool after)
        {
            var order=(saved??Enumerable.Empty<string>()).Concat(Sort(rows,saved).Select(Key)).Where(k=>!string.IsNullOrEmpty(k)).Distinct().ToList();
            if(source==target || !order.Contains(source) || !order.Contains(target))return order;
            order.Remove(source);order.Insert(order.IndexOf(target)+(after?1:0),source);return order;
        }
    }

    internal sealed partial class TeamWindow : Form
    {
        readonly Func<List<ClientObservation>> read;
        readonly Func<LauncherSettings> configuration;
        readonly Func<List<PresetCamping.Client>,Task> logout;
        readonly Func<List<PresetCamping.Client>,Task> relog;
        readonly Func<PresetCamping.Client,bool,Task> recover;
        readonly Func<string> backgroundStatus;
        readonly Func<List<HealthSnapshot>> serviceHistory;
        readonly ClientResponseGraph responseGraph=new ClientResponseGraph();
        readonly Action saveCardOrder;
        bool draggingCard;
        ClientObservation movementDriver;
        internal MovementAssessment MovementState(ClientObservation row)=>MovementTracking.Assess(row,movementDriver,Stopwatch.GetTimestamp());
        internal async Task Regroup(ClientObservation row)
        {
            var driver=movementDriver;
            if(!MovementTracking.Assess(row,driver,Stopwatch.GetTimestamp()).CanRegroup) {
                NexusMessageBox.Show(this,AOLauncher.Localization.Texts.Get("Movement.RegroupUnavailable"),"Regroup",MessageBoxButtons.OK,MessageBoxIcon.Information);return;
            }
            if(NexusMessageBox.Show(this,string.Format(AOLauncher.Localization.Texts.Get("Movement.RegroupConfirm"),row.Name,driver.Name),
                "Regroup",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
            if(!PresetCamping.IsSameProcess(row.Client) || !PresetCamping.IsSameProcess(driver.Client)
                || !MovementTracking.Assess(row,driver,Stopwatch.GetTimestamp()).CanRegroup)return;
            string command="nexusregroup "+driver.Pulse.Movement.CharacterId+" "+driver.Pulse.Movement.Channel;
            try {
                var reply=await Task.Run(()=>ShortcutClient.Run(row.Pid,command));
                DevelopmentRecorder.Action("regroup",row.Client,reply.Ok?"Command queued; game-thread validation pending":reply.Message);
                if(!IsDisposed)NexusMessageBox.Show(this,reply.Ok?AOLauncher.Localization.Texts.Get("Movement.RegroupQueued"):reply.Message,
                    "Regroup",MessageBoxButtons.OK,reply.Ok?MessageBoxIcon.Information:MessageBoxIcon.Warning);
            } catch(Exception ex) {
                DevelopmentRecorder.Action("regroup",row.Client,"Failed: "+ex);
                if(!IsDisposed)NexusMessageBox.Show(this,AOLauncher.Localization.Texts.Get("Movement.RegroupUnavailable"),
                    "Regroup",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            }
        }
        int viewedPid, foregroundPid;
        long viewedStartTicks;
        internal bool IsSelected(ClientObservation row)=>row!=null && !row.ExitedUtc.HasValue && row.Pid==viewedPid && row.StartTicks==viewedStartTicks;
        internal bool IsViewing(ClientObservation row)=>IsSelected(row) && row.Pid==foregroundPid;
        internal void UpdateViewedClient(IEnumerable<ClientObservation> rows,int activePid) {
            foregroundPid=activePid;
            var active=rows.FirstOrDefault(r=>r.Pid==activePid && !r.ExitedUtc.HasValue);
            // Opening the panel should prefer the actual leader, not whichever
            // follower happened to take focus last during startup. Explicit game
            // selection still wins and is retained when returning to the panel.
            if(active==null && viewedPid==0)active=rows.FirstOrDefault(r=>TeamPresentation.Leader(r,Stopwatch.GetTimestamp()));
            if(active==null && viewedPid==0)active=rows.Where(r=>!r.ExitedUtc.HasValue && r.Sample?.Foreground==true
                && (DateTime.UtcNow-r.Sample.Utc).TotalSeconds<2).OrderByDescending(r=>r.Sample.Utc).FirstOrDefault();
            if(active!=null){viewedPid=active.Pid;viewedStartTicks=active.StartTicks;}
            else if(!rows.Any(IsSelected)){viewedPid=0;viewedStartTicks=0;}
        }
        readonly Label backgroundInfo=new Label {Name="backgroundClientStatus",Dock=DockStyle.Top,Height=38,Padding=new Padding(18,2,18,2),ForeColor=Theme.Muted};
        readonly Func<bool> canLogout;
        readonly Button logoutAll=new NexusButton { Name="teamLogoutAll", Text=AOLauncher.Localization.Texts.Get("Text_Log_out_all_BA1E74E8761F"), Width=112, Height=28, Margin=new Padding(8,0,0,0), FlatStyle=FlatStyle.Flat, ForeColor=Theme.Foreground, BackColor=Theme.SurfaceRaised };
        readonly Button relogAll=new NexusButton { Name="teamRelogAll", Text=AOLauncher.Localization.Texts.Get("Text_Relog_all_7AF70ACCE169"), Width=112, Height=28, Margin=new Padding(8,0,0,0), FlatStyle=FlatStyle.Flat, ForeColor=Theme.Foreground, BackColor=Theme.SurfaceRaised };
        List<ClientObservation> shownRows=new List<ClientObservation>();
        bool logoutBusy;
        readonly TeamCombatStats combat=new TeamCombatStats();
        readonly ObservedCombatStats observedCombat=new ObservedCombatStats();
        readonly ObservedCombatStats npcCombat=new ObservedCombatStats(true);
        readonly NexusTabControl damageTabs=new NexusTabControl {Name="teamDamageTabs",Dock=DockStyle.Fill};
        TabPage allDamagePage, npcDamagePage;
        void ApplyExperimentalTabs(bool enabled)
        {
            if (enabled == damageTabs.TabPages.Contains(allDamagePage)) return;
            bool previous = restoringPreferences;
            restoringPreferences = true;
            try {
                if (enabled) {
                    damageTabs.TabPages.Add(allDamagePage); damageTabs.TabPages.Add(npcDamagePage);
                    damageTabs.SelectedIndex = Math.Max(0, Math.Min(2, Preferences.Tab));
                } else {
                    damageTabs.SelectedIndex = 0;
                    damageTabs.TabPages.Remove(allDamagePage); damageTabs.TabPages.Remove(npcDamagePage);
                }
            } finally { restoringPreferences = previous; }
        }
        bool AllDamage=>damageTabs.SelectedIndex>0;
        bool NpcDamage=>damageTabs.SelectedIndex==2;
        ObservedCombatStats SelectedCombat=>NpcDamage?npcCombat:observedCombat;
        readonly ControlPanelSections sections;
        readonly TeamStatsTable statistics;
        readonly NexusComboBox meterSource=new NexusComboBox {Name="combatMeterSource",DropDownStyle=ComboBoxStyle.DropDownList,Width=220};
        readonly Label meterTotals=new Label {Name="combatTeamTotals",ForeColor=Theme.Muted,AutoSize=false};
        readonly Button resetTeamStats=new NexusButton {Name="resetTeamStats",Text=AOLauncher.Localization.Texts.Get("Text_Reset_team_totals_026A28FA0AAC"),Size=new Size(150,28),ForeColor=Theme.Foreground,BackColor=Theme.SurfaceRaised,FlatStyle=FlatStyle.Flat};
        string meterSources;
        bool changingMeterSource;
        sealed class MeterSourceChoice {internal ClientObservation Row;internal string SavedKey, SavedName;public override string ToString()=>Row!=null?Row.Name:SavedKey!=null?SavedName+AOLauncher.Localization.Texts.Get("Text_offline_0617901C3B09"):AOLauncher.Localization.Texts.Get("Text_Automatic_meter_source_F66C290A14C1");}
        readonly FlowLayoutPanel wall=new FlowLayoutPanel { Dock=DockStyle.Fill, AutoScroll=true, Padding=new Padding(12), BackColor=Theme.Background };
        readonly Label summary=new Label { Name="teamSummary", Dock=DockStyle.Top, Height=38, ForeColor=Theme.Muted, Padding=new Padding(18,8,0,0) };
        readonly Label memoryWarning=new Label {Name="teamMemoryWarning",Dock=DockStyle.Top,Height=30,Padding=new Padding(18,2,18,2),ForeColor=Theme.Amber,AutoEllipsis=true,Visible=false};
        readonly Label empty=new Label { Dock=DockStyle.Fill, TextAlign=ContentAlignment.MiddleCenter, ForeColor=Theme.Muted };
        readonly LinkLabel monitorHelp=new LinkLabel { Name="teamMonitoringHelp", Dock=DockStyle.Top, Height=48, Padding=new Padding(18,3,18,3), LinkColor=Theme.Accent, Visible=false };
        readonly NexusComboBox teams=new NexusComboBox { Name="teamFilter",DropDownStyle=ComboBoxStyle.DropDownList, Width=220 };
        readonly CheckBox previews=new NexusCheckBox { Name="teamLivePreviews", Text=AOLauncher.Localization.Texts.Get("Text_Live_previews_7C216164B3E8"), Checked=true, AutoSize=false, Size=new Size(150,28), Margin=new Padding(16,0,8,0) };
        readonly Timer timer=new Timer { Interval=500 };
        readonly Dictionary<string,TeamCard> cards=new Dictionary<string,TeamCard>();
        string teamSignature;
        internal TeamWindow(Func<List<ClientObservation>> observations, Func<LauncherSettings> settings, Action openPlugins=null,
            Func<List<PresetCamping.Client>,Task> logoutClients=null, Func<bool> logoutAvailable=null, Func<List<PresetCamping.Client>,Task> relogClients=null,
            Func<PresetCamping.Client,bool,Task> recoverClient=null,Func<string> readBackgroundStatus=null,Action saveOrder=null,Func<List<HealthSnapshot>> healthHistory=null)
        {
            read=observations; configuration=settings; logout=logoutClients; canLogout=logoutAvailable;relog=relogClients;recover=recoverClient;
            saveCardOrder=saveOrder;
            serviceHistory=healthHistory;
            backgroundStatus=readBackgroundStatus;backgroundInfo.Visible=readBackgroundStatus!=null;
            statistics=new TeamStatsTable(StatsFor,ResetStats);
            statistics.ResetObserved=row=>{RefreshWall();SelectedCombat.ResetRow(row);RefreshWall();};
            Text=AOLauncher.Localization.Texts.Get("Text_AO_NEXUS_Control_Panel_67297D7B75C2"); Name="teamWindow";
            AppArtwork.ApplyIcon(this);
            BackColor=Theme.Background; ForeColor=Theme.Foreground; Font=new Font("Segoe UI",10);
            Size=new Size(1160,Math.Min(980,Screen.PrimaryScreen.WorkingArea.Height-40)); MinimumSize=new Size(760,560); StartPosition=FormStartPosition.CenterScreen;
            var title=new Label { Text=AOLauncher.Localization.Texts.Get("Text_CONTROL_PANEL_B3774C79CF88"), Dock=DockStyle.Top, Height=58, Padding=new Padding(18,15,0,0), Font=new Font("Segoe UI Semibold",19), ForeColor=Theme.Accent };
            var toolbar=new FlowLayoutPanel { Dock=DockStyle.Top, Height=46, AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink, Padding=new Padding(18,6,0,6), WrapContents=true, BackColor=Theme.Surface };
            var onTop=new NexusCheckBox { Name="teamAlwaysOnTop",Text=AOLauncher.Localization.Texts.Get("Text_Always_on_top_AE65B792A5B3"),AutoSize=false,Size=new Size(160,28),Margin=new Padding(12,0,8,0) };
            onTop.CheckedChanged+=(s,e)=>TopMost=onTop.Checked;
            toolbar.Controls.Add(teams); toolbar.Controls.Add(previews); toolbar.Controls.Add(onTop);
            toolbar.Controls.Add(logoutAll);
            toolbar.Controls.Add(relogAll);relogAll.Click+=async(s,e)=>await LogOut(shownRows.ToList(),true);
            var combatPanel=new Panel {Name="teamTotalsPanel",Dock=DockStyle.Bottom,Height=44,BackColor=Theme.Surface};
            statistics.AddToolbarControl(resetTeamStats);statistics.AddToolbarControl(meterSource);
            var exportScripts=new NexusButton {Name="exportCombatScripts",Text=AOLauncher.Localization.Texts.Get("Text_Export_scripts_9A1341122E39"),Size=new Size(140,28),ForeColor=Theme.Foreground,BackColor=Theme.SurfaceRaised,FlatStyle=FlatStyle.Flat};
            statistics.AddToolbarControl(exportScripts);exportScripts.Click+=(s,e)=>ExportCombatScripts();
            meterTotals.Dock=DockStyle.Fill;meterTotals.Padding=new Padding(18,4,10,0);
            combatPanel.Controls.Add(meterTotals);
            resetTeamStats.Click+=(s,e)=>{RefreshWall();if(AllDamage)SelectedCombat.ResetAll();else combat.ResetTeam(shownRows);RefreshWall();};
            meterSource.SelectedIndexChanged+=(s,e)=>{if(!changingMeterSource && !restoringPreferences){
                var choice=meterSource.SelectedItem as MeterSourceChoice;
                Preferences.ObserverKey=choice?.Row!=null?ObserverKey(choice.Row):choice?.SavedKey;
                Preferences.ObserverName=choice?.Row?.Name??choice?.SavedName;
                ApplyMeterSource(choice);SavePanelPreferences();RefreshWall();}};
            logoutAll.AccessibleDescription=AOLauncher.Localization.Texts.Get("Text_Log_out_all_running_clients_in_720BA8038490");
            logoutAll.Click+=async(s,e)=>await LogOut(shownRows.ToList());
            var footer=new Label { Dock=DockStyle.Bottom, Height=38, Padding=new Padding(18,7,10,0), ForeColor=Theme.Muted,
                Text=AOLauncher.Localization.Texts.Get("Text_Click_a_card_to_switch_client_D7E8C429563B") };
            monitorHelp.LinkClicked+=(s,e)=>openPlugins?.Invoke();
            damageTabs.TabPages.Add(new TabPage(AOLauncher.Localization.Texts.Get("Text_Team_959D60936804")) {Name="teamDamagePage",BackColor=Theme.Background});
            allDamagePage = new TabPage(AOLauncher.Localization.Texts.Get("Text_All_35CB769E8E4B")) {Name="allDamagePage",BackColor=Theme.Background};
            npcDamagePage = new TabPage("NPC") {Name="npcDamagePage",BackColor=Theme.Background};
            if (settings().ExperimentalNearbyCombat) { damageTabs.TabPages.Add(allDamagePage); damageTabs.TabPages.Add(npcDamagePage); }
            damageTabs.TabPages[0].Controls.Add(statistics);damageTabs.TabPages[0].Controls.Add(combatPanel);
            damageTabs.SelectedIndexChanged+=(s,e)=>{
                damageTabs.SelectedTab.Controls.Add(statistics);damageTabs.SelectedTab.Controls.Add(combatPanel);RefreshWall();
            };
            sections=new ControlPanelSections(()=>{Preferences.Sections=sections.Snapshot();SavePanelPreferences();});
            var teamContent=new Panel();teamContent.Controls.Add(SlimScrolling.Wrap(wall));teamContent.Controls.Add(empty);
            sections.AddSection("damage",AOLauncher.Localization.Texts.Get("Text_Damage_Dumper_E12E21112806"),damageTabs,300);
            sections.AddSection("team",AOLauncher.Localization.Texts.Get("Text_Team_959D60936804"),teamContent,480);
            InitializeFormation();
            sections.AddSection("timers",AOLauncher.Localization.Texts.Get("Text_Timers_AF56D071FADE"),new TimersPanel(settings(),scope=>{Preferences.TimerScope=scope;SavePanelPreferences();},Preferences.TimerScope),450);
            sections.Restore(Preferences.Sections,Preferences.Splitter);
            sections.ViewChanged+=()=>{Arrange();RefreshThumbnails();};
            footer.Text=AOLauncher.Localization.Texts.Get("Text_Drag_card_headers_to_reorder_Ctrl_F47B80180345");
            Controls.Add(SlimScrolling.Wrap(sections));Controls.Add(memoryWarning);Controls.Add(responseGraph);Controls.Add(backgroundInfo); Controls.Add(monitorHelp); Controls.Add(summary); Controls.Add(toolbar); Controls.Add(title); Controls.Add(footer);
            empty.BringToFront(); empty.Visible=false;
            teams.SelectedIndexChanged+=(s,e)=>RefreshWall(); previews.CheckedChanged+=(s,e)=>RefreshWall();
            wall.SizeChanged+=(s,e)=>Arrange(); wall.Scroll+=(s,e)=>RefreshThumbnails();
            wall.AllowDrop=true;
            wall.DragOver+=(s,e)=>HoverCardDrop(e,null);
            wall.DragDrop+=(s,e)=>DropCard(e,null);
            LocationChanged+=(s,e)=>RefreshThumbnails(); Resize+=(s,e)=>RefreshThumbnails();
            timer.Tick+=(s,e)=>RefreshWall();
            InitializePreferences(onTop);
            Shown+=(s,e)=> { RestorePanelLayout(); RefreshWall(); timer.Start(); };
        }
        internal void RefreshWall()
        {
            if(IsDisposed || draggingCard || restoringPreferences || changingTeams) return;
            if(backgroundStatus!=null)SetStatusText(backgroundInfo,backgroundStatus()??AOLauncher.Localization.Texts.Get("Text_Auto_minimize_unavailable_914E2DFA77C9"));
            var settings=configuration();
            ApplyExperimentalTabs(settings.ExperimentalNearbyCombat);
            string signature=string.Join("|",settings.Presets.Select(p=>p.Id+":"+p.Name));
            if(signature!=teamSignature) {
                teamSignature=signature; string selected=Preferences.TeamId;
                changingTeams=true;
                teams.BeginUpdate(); teams.Items.Clear(); teams.Items.Add(AOLauncher.Localization.Texts.Get("Text_All_running_characters_071F33554214"));
                foreach(var p in settings.Presets) teams.Items.Add(p);
                teams.SelectedIndex=0;
                for(int i=1;i<teams.Items.Count;i++) if(((LaunchPreset)teams.Items[i]).Id==selected) teams.SelectedIndex=i;
                teams.EndUpdate();changingTeams=false;
            }
            var allRows=read();long now=Stopwatch.GetTimestamp();
            responseGraph.Sample(allRows,serviceHistory?.Invoke());
            TeamCard.Native.GetWindowThreadProcessId(TeamCard.Native.GetForegroundWindow(),out uint activePid);
            UpdateViewedClient(allRows,(int)activePid);
            movementDriver=allRows.FirstOrDefault(IsSelected);
            statistics.UsePreferences(Preferences,damageTabs.SelectedIndex,SavePanelPreferences);
            string sourceSignature=string.Join("|",allRows.Where(r=>!r.ExitedUtc.HasValue).Select(r=>r.Pid+":"+r.StartTicks+":"+r.Name));
            if(sourceSignature!=meterSources) {
                meterSources=sourceSignature;
                changingMeterSource=true;
                try {
                    meterSource.Items.Clear();meterSource.Items.Add(new MeterSourceChoice());
                    foreach(var candidate in allRows.Where(r=>!r.ExitedUtc.HasValue)) meterSource.Items.Add(new MeterSourceChoice {Row=candidate});
                    var preferred=meterSource.Items.Cast<MeterSourceChoice>().FirstOrDefault(c=>c.Row!=null && ObserverKey(c.Row)==Preferences.ObserverKey);
                    if(preferred==null && !string.IsNullOrEmpty(Preferences.ObserverKey)) {
                        preferred=new MeterSourceChoice {SavedKey=Preferences.ObserverKey,SavedName=Preferences.ObserverName??AOLauncher.Localization.Texts.Get("Text_Selected_character_25F4F29F4B3B")};
                        meterSource.Items.Add(preferred);
                    }
                    meterSource.SelectedItem=preferred??meterSource.Items[0];
                    ApplyMeterSource(meterSource.SelectedItem as MeterSourceChoice);
                } finally {changingMeterSource=false;}
            }
            combat.Update(allRows,now);
            if (settings.ExperimentalNearbyCombat) { observedCombat.Update(allRows,now);npcCombat.Update(allRows,now); }
            var preset=teams.SelectedItem as LaunchPreset;
            var rows=allRows.Where(r=>!r.ExitedUtc.HasValue)
                .Where(r=>preset==null || preset.Characters.Any(c=>r.Client!=null && c.CharacterId==r.Client.CharacterId && r.Client.AccountIdentity=="id:"+c.AccountId))
                .ToList();
            rows=TeamCardOrdering.Sort(rows,settings.TeamCardOrder);
            shownRows=rows;
            UpdateFormation(allRows);
            var totals=combat.Total(rows);
            resetTeamStats.Enabled=AllDamage?SelectedCombat.Ready:combat.Ready;
            resetTeamStats.Text=NpcDamage?AOLauncher.Localization.Texts.Get("Text_Reset_NPC_totals_854B8DEA0619"):AllDamage?AOLauncher.Localization.Texts.Get("Text_Reset_all_totals_26D203A7CA15"):AOLauncher.Localization.Texts.Get("Text_Reset_team_totals_026A28FA0AAC");
            meterSource.Visible=AllDamage || (!combat.Native && combat.Ready);
            bool measured=totals!=null && totals.Observed>0;
            string meterNote=!measured ? (combat.Native ?
                AOLauncher.Localization.Texts.Get("Text_Waiting_for_fresh_statistics_from_the_39D3D8759553") :
                AOLauncher.Localization.Texts.Get("Text_Reload_Nexus_Health_Monitor_from_Plugins_9E95A2D59A01")) :
                string.Format(AOLauncher.Localization.Texts.Get("Text_0_1_clients_measured_session_averages_705509FD137C"),totals.Observed,totals.Requested)
                +(totals.Native && totals.ExperienceEvents==0?AOLauncher.Localization.Texts.Get("Text_no_XP_awards_observed_yet_11C7E723A05C"):"");
            if(AllDamage) {
                var source=SelectedCombat.Source;
                meterTotals.Text=SelectedCombat.Ready
                    ? string.Format(NpcDamage?AOLauncher.Localization.Texts.Get("Text_Observed_by_0_NPC_damage_dealt_064EF6BD246B"):AOLauncher.Localization.Texts.Get("Text_Observed_by_0_nearby_events_only_9F1EF7A19A16"),source.Name)
                        +(SelectedCombat.Snapshot.LimitReached?AOLauncher.Localization.Texts.Get("Text_128_entry_limit_reached_partial_totals_2745092F71F5"):"")
                    : NpcDamage?(!allRows.Any(r=>!r.ExitedUtc.HasValue)?AOLauncher.Localization.Texts.Get("Text_Log_in_a_character_to_observe_29365E13D63F"):
                        allRows.Any(r=>r.Pulse?.NpcCombatMeter!=null)?AOLauncher.Localization.Texts.Get("Text_Waiting_for_fresh_NPC_observations_from_5FDF349155FB"):
                        AOLauncher.Localization.Texts.Get("Text_NPC_tracking_needs_the_updated_Health_EDF540E4CB3D"))
                    : source?.Pulse?.ObservedCombatMeter==null?AOLauncher.Localization.Texts.Get("Text_All_needs_the_updated_Nexus_Health_89B0F8DE7238")
                        : string.Format(AOLauncher.Localization.Texts.Get("Text_Waiting_for_fresh_observations_from_0_ED80D755384D"),source.Name);
                statistics.UpdateObservedRows(SelectedCombat.Rows(),SelectedCombat.Total());
            } else {meterTotals.Text=meterNote;statistics.UpdateRows(rows,totals);}
            logoutAll.Enabled=rows.Any(CanLogOut);
            relogAll.Enabled=rows.Any(CanRelog);
            var wanted=new HashSet<string>(rows.Select(r=>r.Pid+":"+r.StartTicks));
            foreach(var key in cards.Keys.Where(k=>!wanted.Contains(k)).ToArray()) { wall.Controls.Remove(cards[key]); cards[key].Dispose(); cards.Remove(key); }
            wall.SuspendLayout();
            foreach(var row in rows) {
                string key=row.Pid+":"+row.StartTicks;
                if(!cards.TryGetValue(key,out var card)) { card=new TeamCard(this);cards.Add(key,card);wall.Controls.Add(card); }
                card.Update(row,previews.Checked);
                int position=rows.IndexOf(row);
                if(wall.Controls.GetChildIndex(card)!=position)wall.Controls.SetChildIndex(card,position);
            }
            wall.ResumeLayout();
            string summaryText=rows.Count+AOLauncher.Localization.Texts.Get("Text_characters_83C7167F53FE")+rows.Count(r=>TeamPresentation.StatusColor(r)==Color.FromArgb(96,220,161))+
                AOLauncher.Localization.Texts.Get("Text_with_normal_response_time_local_client_6C188A8CFA5D");
            SetStatusText(summary,summaryText);
            var lowMemory=rows.Where(r=>ClientAddressSpace.Fresh(r.Sample,now) && ClientAddressSpace.Warning(r.Sample.FreeClientBytes,r.Sample.LargestFreeClientBytes)>0).ToList();
            SetStatusText(memoryWarning,AOLauncher.Localization.Texts.Get("Text_Low_client_memory_368F71A32633")+string.Join(", ",lowMemory.Select(r=>r.Name))+AOLauncher.Localization.Texts.Get("Text_Consider_relogging_before_zoning_4C9E4FCC2136"));
            memoryWarning.ForeColor=lowMemory.Any(r=>ClientAddressSpace.Warning(r.Sample.FreeClientBytes,r.Sample.LargestFreeClientBytes)==2)?Color.FromArgb(255,100,100):Theme.Amber;
            memoryWarning.Visible=lowMemory.Count>0;
            // The monitor deliberately clears vitals during zoning. That is not a
            // missing plugin and must not make the setup link flash or shift cards.
            bool missingMeasurements=rows.Any(r=>!r.ExitedUtc.HasValue && r.Pulse?.Zoning!=true && r.Sample?.Zoning!=true
                && (r.Pulse==null || !r.Pulse.Active || r.Pulse.VitalsQpc<=0));
            monitorHelp.Visible=missingMeasurements;
            if(missingMeasurements) monitorHelp.Text=AOLauncher.Localization.Texts.Get("Text_Health_nano_and_activity_need_Nexus_70D45A8FD229");
            empty.Text=settings.MonitorClientHealth ? AOLauncher.Localization.Texts.Get("Text_No_characters_in_this_view_Log_52B08D56F8EE") :
                AOLauncher.Localization.Texts.Get("Text_Enable_client_health_monitoring_in_Settings_19A9A883F36C");
            empty.Visible=rows.Count==0; SlimScrolling.SetVisible(wall,rows.Count!=0); Arrange();
        }
        internal bool CanLogOut(ClientObservation observation) => logout!=null && !logoutBusy && (canLogout==null || canLogout())
            && observation!=null && !observation.ExitedUtc.HasValue && observation.Client!=null
            && observation.Pid==observation.Client.ProcessId && observation.StartTicks==observation.Client.StartTimeUtcTicks && observation.Client.CanCamp;
        static void SetStatusText(Label label,string text) {if(label.Text!=text)label.Text=text;}
        internal CombatStats StatsFor(ClientObservation row)=>combat.Card(row);
        internal void MoveCard(ClientObservation source,ClientObservation target,bool after)
        {
            if(source==null || target==null || !shownRows.Contains(source) || !shownRows.Contains(target))return;
            var settings=configuration();
            settings.TeamCardOrder=TeamCardOrdering.Move(settings.TeamCardOrder,read(),TeamCardOrdering.Key(source),TeamCardOrdering.Key(target),after)
                .Where(k=>!k.StartsWith("session:",StringComparison.Ordinal)).ToList();
            saveCardOrder?.Invoke();RefreshWall();
        }
        internal void MoveCardBy(ClientObservation source,int direction)
        {
            int index=shownRows.IndexOf(source),target=index+direction;
            if(index>=0 && target>=0 && target<shownRows.Count)MoveCard(source,shownRows[target],direction>0);
        }
        internal void ResetCardOrder() {configuration().TeamCardOrder=new List<string>();saveCardOrder?.Invoke();RefreshWall();}
        internal void BeginCardDrag(TeamCard card)
        {
            draggingCard=true;
            try {card.DoDragDrop(card,DragDropEffects.Move);}
            finally {draggingCard=false;foreach(var c in cards.Values)c.DropSide=0;RefreshWall();}
        }
        TeamCard DraggedCard(DragEventArgs e) => e.Data.GetDataPresent(typeof(TeamCard)) ? e.Data.GetData(typeof(TeamCard)) as TeamCard : null;
        internal void HoverCardDrop(DragEventArgs e,TeamCard target)
        {
            var source=DraggedCard(e);
            e.Effect=source!=null && cards.Values.Contains(source)?DragDropEffects.Move:DragDropEffects.None;
            foreach(var c in cards.Values)c.DropSide=0;
            if(e.Effect==DragDropEffects.Move && target!=null && target!=source)
                target.DropSide=target.PointToClient(new Point(e.X,e.Y)).X>=target.Width/2?1:-1;
        }
        internal void DropCard(DragEventArgs e,TeamCard target)
        {
            var source=DraggedCard(e);
            if(source==null || !cards.Values.Contains(source))return;
            bool after=target==null || target.PointToClient(new Point(e.X,e.Y)).X>=target.Width/2;
            if(target==null)target=wall.Controls.OfType<TeamCard>().LastOrDefault();
            if(target!=null)MoveCard(source.Observation,target.Observation,after);
        }
        internal bool CanRelog(ClientObservation row)=>relog!=null && CanLogOut(row);
        internal bool CanRecover(ClientObservation row)=>recover!=null && !logoutBusy && (canLogout==null || canLogout()) && row?.Client!=null
            && !row.ExitedUtc.HasValue && row.Pid==row.Client.ProcessId && row.StartTicks==row.Client.StartTimeUtcTicks;
        internal async Task Recover(ClientObservation row,bool restart) {
            if(!CanRecover(row))return;
            var client=row.Client;logoutBusy=true;RefreshWall();
            try {await recover(client,restart);}
            catch(Exception error){if(!IsDisposed)NexusMessageBox.Show(this,error.Message,AOLauncher.Localization.Texts.Get("Text_Recovery_failed_CBD54B9693AC"),MessageBoxButtons.OK,MessageBoxIcon.Warning);}
            finally {logoutBusy=false;if(!IsDisposed)RefreshWall();}
        }
        internal void ResetStats(ClientObservation row){RefreshWall();combat.ResetCard(row);RefreshWall();}
        internal async Task LogOut(IEnumerable<ClientObservation> selected,bool restart=false) {
            var targets=selected.Where(r=>restart?CanRelog(r):CanLogOut(r)).Select(r=>r.Client).GroupBy(c=>c.ProcessId+":"+c.StartTimeUtcTicks).Select(g=>g.First()).ToList();
            if(targets.Count==0) return;
            logoutBusy=true;RefreshWall();
            try { await (restart?relog(targets):logout(targets)); }
            catch(Exception ex) { if(!IsDisposed) NexusMessageBox.Show(this,ex.Message,restart?AOLauncher.Localization.Texts.Get("Text_Could_not_relog_EB4AC0BB81E3"):AOLauncher.Localization.Texts.Get("Text_Could_not_log_out_111E95B11E71"),MessageBoxButtons.OK,MessageBoxIcon.Warning); }
            finally { logoutBusy=false;if(!IsDisposed) RefreshWall(); }
        }
        void Arrange()
        {
            int columns=Math.Max(1,(wall.ClientSize.Width-26)/350);
            int width=Math.Max(320,(wall.ClientSize.Width-30-SystemInformation.VerticalScrollBarWidth)/columns-12);
            foreach(var card in cards.Values) card.Arrange(width);
            RefreshThumbnails();
        }
        void RefreshThumbnails() { foreach(var card in cards.Values) card.UpdateThumbnail(); }
        internal Rectangle Viewport => Rectangle.Intersect(RectangleToClient(wall.Parent.RectangleToScreen(wall.Parent.ClientRectangle)),RectangleToClient(sections.RectangleToScreen(sections.ClientRectangle)));
        protected override void Dispose(bool disposing) { if(disposing) { DisposePreferences(); timer.Stop();timer.Dispose();foreach(var card in cards.Values) card.Dispose();cards.Clear(); } base.Dispose(disposing); }
    }

    internal sealed class TeamCard : Control
    {
        readonly TeamWindow owner;
        ClientObservation row;
        internal ClientObservation Observation=>row;
        Point dragStart;
        bool headerPressed,dragged;
        int dropSide;
        internal int DropSide {get=>dropSide;set {if(dropSide!=value){dropSide=value;Invalidate();}}}
        IntPtr thumbnail, source;
        bool preview;
        bool updatingThumbnail;
        bool previewMinimized;
        string lastMovementCode;
        Size previewSourceSize=new Size(16,9);
        readonly Font titleFont=new Font("Segoe UI Semibold",13);
        readonly Font smallFont=new Font("Segoe UI",9);
        readonly Font combatFont=new Font("Segoe UI Semibold",11);
        readonly ToolTip detailsTip=new ToolTip();
        static readonly Color combatColor=Color.FromArgb(255,174,76);
        readonly Button logoutButton=new NexusButton { Name="clientLogout", Text=AOLauncher.Localization.Texts.Get("Text_Log_out_698471977A3C"), FlatStyle=FlatStyle.Flat, BackColor=Theme.SurfaceRaised, ForeColor=Theme.Foreground, Size=new Size(88,26), TabStop=true };
        readonly Button relogButton=new NexusButton { Name="clientRelog", Text=AOLauncher.Localization.Texts.Get("Text_Relog_18C6E265BF38"), FlatStyle=FlatStyle.Flat, BackColor=Theme.SurfaceRaised, ForeColor=Theme.Foreground, Size=new Size(70,26), TabStop=true };
        readonly Button recoverButton=new NexusButton {Name="clientRecover",Text=AOLauncher.Localization.Texts.Get("Text_Recover_AFE4609A8FBF"),FlatStyle=FlatStyle.Flat,BackColor=Theme.SurfaceRaised,ForeColor=Theme.Amber,Size=new Size(98,26)};
        readonly ContextMenuStrip recoveryMenu=new ContextMenuStrip();
        readonly ContextMenuStrip orderMenu=new ContextMenuStrip {BackColor=Theme.SurfaceRaised,ForeColor=Theme.Foreground};
        internal TeamCard(TeamWindow window) {
            owner=window; DoubleBuffered=true; BackColor=Theme.Surface; ForeColor=Theme.Foreground;
            Margin=new Padding(6); Cursor=Cursors.Hand; TabStop=true; AccessibleRole=AccessibleRole.PushButton;
            AllowDrop=true;
            DragOver+=(s,e)=>owner.HoverCardDrop(e,this);
            DragLeave+=(s,e)=>DropSide=0;
            DragDrop+=(s,e)=>owner.DropCard(e,this);
            orderMenu.Items.Add(AOLauncher.Localization.Texts.Get("Text_Move_earlier_96BB68B902F8"),null,(s,e)=>owner.MoveCardBy(row,-1));
            orderMenu.Items.Add(AOLauncher.Localization.Texts.Get("Text_Move_later_B2C781335F56"),null,(s,e)=>owner.MoveCardBy(row,1));
            orderMenu.Items.Add(new ToolStripSeparator());
            orderMenu.Items.Add(AOLauncher.Localization.Texts.Get("Text_Reset_card_order_2CE0435E4ED9"),null,(s,e)=>owner.ResetCardOrder());
            ContextMenuStrip=orderMenu;
            AccessibleDescription=AOLauncher.Localization.Texts.Get("Text_Drag_the_header_to_reorder_Ctrl_1C4308965285");
            Controls.Add(logoutButton);
            Controls.Add(relogButton);relogButton.Click+=async(s,e)=>await owner.LogOut(new[]{row},true);
            Controls.Add(recoverButton);
            recoveryMenu.Items.Add(AOLauncher.Localization.Texts.Get("Text_Force_close_BE59B080FE0B"),null,async(s,e)=>await owner.Recover(row,false));
            recoveryMenu.Items.Add(AOLauncher.Localization.Texts.Get("Text_Force_restart_A533A391EDD6"),null,async(s,e)=>await owner.Recover(row,true));
            recoveryMenu.Items.Add(new ToolStripSeparator());
            recoveryMenu.Items.Add(AOLauncher.Localization.Texts.Get("Movement.Regroup"),null,async(s,e)=>await owner.Regroup(row));
            orderMenu.Items.Add(AOLauncher.Localization.Texts.Get("Movement.Regroup"),null,async(s,e)=>await owner.Regroup(row));
            recoverButton.Click+=(s,e)=>recoveryMenu.Show(recoverButton,new Point(0,recoverButton.Height));
            logoutButton.Click+=async(s,e)=>await owner.LogOut(new[]{row});
            MouseClick+=(s,e)=>{if(e.Button==MouseButtons.Left && !dragged)ActivateClient();};
            KeyDown+=(s,e)=> {
                if(e.Control && (e.KeyCode==Keys.Left || e.KeyCode==Keys.Right)) {e.Handled=e.SuppressKeyPress=true;owner.MoveCardBy(row,e.KeyCode==Keys.Left?-1:1);}
                else if(e.KeyCode==Keys.Enter) {e.Handled=true;ActivateClient();}
            };
            LocationChanged+=(s,e)=>UpdateThumbnail(); SizeChanged+=(s,e)=>UpdateThumbnail();
        }
        internal void Update(ClientObservation observation,bool showPreview) {
            row=observation;preview=showPreview;AccessibleName=row.Name+", "+TeamPresentation.Status(row);
            bool attacking=TeamPresentation.Attacking(row,Stopwatch.GetTimestamp());
            if(owner.IsSelected(row))AccessibleName+=owner.IsViewing(row)?AOLauncher.Localization.Texts.Get("Text_Viewing_9163009B6F6C"):AOLauncher.Localization.Texts.Get("Text_Selected_020F1F4BD9F8");
            if(attacking)AccessibleName+=AOLauncher.Localization.Texts.Get("Text_Attacking_EEE46E41F757")+TeamPresentation.Target(row);
            string tip=attacking?AOLauncher.Localization.Texts.Get("Text_Attacking_8A69BEFC09A8")+TeamPresentation.Target(row):"";
            var aggro=TeamPresentation.AggroCount(row,Stopwatch.GetTimestamp());
            tip+=(tip.Length>0?"\n":"")+AOLauncher.Localization.Texts.Get("Aggro.Tooltip");
            if(aggro>0)tip+="\n"+TeamPresentation.AggroNames(row);
            AccessibleName+=", "+(aggro.HasValue?string.Format(AOLauncher.Localization.Texts.Get("Aggro.Count"),aggro.Value):AOLauncher.Localization.Texts.Get("Aggro.Unavailable"));
            var movement=owner.MovementState(row);
            tip+=(tip.Length>0?"\n":"")+movement.Text;
            if(movement.Limit.HasValue)tip+="\n"+string.Format(AOLauncher.Localization.Texts.Get("Movement.RangeDetail"),movement.Limit.Value);
            if(lastMovementCode!=movement.Code) {
                lastMovementCode=movement.Code;
                DevelopmentRecorder.Write("movement-state",new {row.Pid,row.StartTicks,movement.Code,movement.Distance,movement.Limit,
                    Minimized=row.Sample?.Minimized,UpdateGap=row.Sample?.ObservedUpdateGapMs});
            }
            if(preview && previewMinimized) tip+="\n"+AOLauncher.Localization.Texts.Get("Preview.MinimizedHelp");
            tip=TeamPresentation.TooltipText(tip);
            if(detailsTip.GetToolTip(this)!=tip)detailsTip.SetToolTip(this,tip);
            if(TeamPresentation.Leader(row,Stopwatch.GetTimestamp()))AccessibleName+=AOLauncher.Localization.Texts.Get("Text_Team_leader_719C73A3AB21");
            logoutButton.Enabled=owner.CanLogOut(row);logoutButton.AccessibleName=AOLauncher.Localization.Texts.Get("Text_Log_out_790577005915")+row.Name;
            relogButton.Enabled=owner.CanRelog(row);relogButton.AccessibleName=AOLauncher.Localization.Texts.Get("Text_Relog_AC51CE4C262E")+row.Name;
            recoverButton.Enabled=owner.CanRecover(row);recoverButton.AccessibleName=AOLauncher.Localization.Texts.Get("Text_Recover_10E4D454A40E")+row.Name;
            Invalidate();UpdateThumbnail();
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);dragged=false;dragStart=e.Location;
            headerPressed=e.Button==MouseButtons.Left && e.Y<34;
            if(e.Button==MouseButtons.Left)Focus();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if(!headerPressed || e.Button!=MouseButtons.Left)return;
            var threshold=new Rectangle(dragStart.X-SystemInformation.DragSize.Width/2,dragStart.Y-SystemInformation.DragSize.Height/2,SystemInformation.DragSize.Width,SystemInformation.DragSize.Height);
            if(threshold.Contains(e.Location))return;
            headerPressed=false;dragged=true;owner.BeginCardDrag(this);
        }
        protected override void OnMouseUp(MouseEventArgs e) {headerPressed=false;base.OnMouseUp(e);}
        internal Rectangle PreviewRect => new Rectangle(12,57,Math.Max(1,Width-24),
            Math.Max(1,(int)Math.Round(Math.Max(1,Width-24)*9.0/16)));
        internal Rectangle PreviewImageRect {
            get {
                var frame=PreviewRect;
                double scale=Math.Min((double)frame.Width/previewSourceSize.Width,(double)frame.Height/previewSourceSize.Height);
                int width=Math.Max(1,Math.Min(frame.Width,(int)Math.Round(previewSourceSize.Width*scale)));
                int height=Math.Max(1,Math.Min(frame.Height,(int)Math.Round(previewSourceSize.Height*scale)));
                return new Rectangle(frame.X+(frame.Width-width)/2,frame.Y+(frame.Height-height)/2,width,height);
            }
        }
        internal void Arrange(int width) {
            Width=width;
            Height=preview?PreviewRect.Bottom+316:363;
            logoutButton.Location=new Point(width-100,Height-34);relogButton.Location=new Point(width-178,Height-34);
            recoverButton.Location=new Point(12,Height-34);
        }
        internal void SetPreviewSourceSize(Size size) {
            if(size.Width<=0 || size.Height<=0 || size==previewSourceSize) return;
            previewSourceSize=size;
            // A restored client can reveal a different aspect ratio. Fit its image
            // inside the reserved frame without moving the card or its controls.
            Invalidate();
        }
        internal void UpdateThumbnail() {
            if(updatingThumbnail || row==null || !IsHandleCreated || !owner.IsHandleCreated) return;
            updatingThumbnail=true;
            try {
            IntPtr window=IntPtr.Zero;
            if(preview && !row.ExitedUtc.HasValue && row.Client!=null) try {
                using(var process=Process.GetProcessById(row.Pid)) if(!process.HasExited && process.StartTime.ToUniversalTime().Ticks==row.StartTicks) window=process.MainWindowHandle;
            } catch { }
            if(window!=source) { ReleaseThumbnail();source=window;if(window!=IntPtr.Zero && Native.DwmRegisterThumbnail(owner.Handle,window,out thumbnail)!=0)thumbnail=IntPtr.Zero; }
            bool minimized=source!=IntPtr.Zero && Native.IsIconic(source);
            if(previewMinimized!=minimized) {previewMinimized=minimized;Invalidate();}
            if(thumbnail==IntPtr.Zero) return;
            // ClientOnly excludes the title bar and borders, so measure the same
            // client rectangle rather than DWM's full source-window dimensions.
            if(!minimized && Native.GetClientRect(source,out var client))
                SetPreviewSourceSize(new Size(client.Right-client.Left,client.Bottom-client.Top));
            Rectangle destination=owner.RectangleToClient(RectangleToScreen(PreviewImageRect));
            // DWM draws on the form, so never let a scrolled tile overlap toolbars.
            // Keep DWM's available image when minimized. Never restore a client or
            // force rendering just to update its preview; the image may pause.
            bool visible=preview && Visible && owner.WindowState!=FormWindowState.Minimized && owner.Viewport.Contains(destination);
            var properties=new Native.Properties { Flags=1|4|8|16,Destination=new Native.Rect(destination),Opacity=255,Visible=visible,ClientOnly=true };
            if(Native.DwmUpdateThumbnailProperties(thumbnail,ref properties)!=0) {ReleaseThumbnail();source=IntPtr.Zero;}
            } finally { updatingThumbnail=false; }
        }
        void ActivateClient() {
            if(row?.Client==null || row.ExitedUtc.HasValue || !PresetCamping.IsSameProcess(row.Client)) return;
            try { using(var p=Process.GetProcessById(row.Pid)) {var h=p.MainWindowHandle;if(Native.IsIconic(h))Native.ShowWindowAsync(h,9);Native.SetForegroundWindow(h);owner.RefreshWall();} } catch { }
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);if(row==null)return;var g=e.Graphics;
            long now=Stopwatch.GetTimestamp();bool fresh=TeamPresentation.Fresh(row,now);
            bool selected=owner.IsSelected(row),attacking=TeamPresentation.Attacking(row,now);
            Color status=TeamPresentation.StatusColor(row);
            if(selected)using(var brush=new SolidBrush(Theme.Selection))g.FillRectangle(brush,4,4,Width-8,49);
            using(var pen=new Pen(selected?Theme.Accent:Theme.Border,selected?4:1))
                g.DrawRectangle(pen,2,2,Width-5,Height-5);
            string profession=row.Pulse?.Profession?.Replace("-", "").Replace(" ", "");
            string shortProfession=profession!=null && PluginHealth.ProfessionKeys.TryGetValue(profession,out var key)?key:"";
            if(shortProfession=="Soldier")shortProfession="Sold";
            string heading=row.Name+(shortProfession.Length>0?" - "+shortProfession:"");
            bool leader=TeamPresentation.Leader(row,Stopwatch.GetTimestamp());
            string leaderText=AOLauncher.Localization.Texts.Get("Text_LEADER_7AA87E6662BB");
            int badgeWidth=leader?TextRenderer.MeasureText(g,leaderText,smallFont,Size.Empty,TextFormatFlags.NoPadding).Width+12:0;
            Draw(g,heading,new Rectangle(13,9,Math.Max(0,Width-26-(leader?badgeWidth+8:0)),25),Theme.Foreground,titleFont);
            if(leader) {
                var badge=new Rectangle(Width-13-badgeWidth,12,badgeWidth,21);
                using(var brush=new SolidBrush(Theme.SurfaceRaised))g.FillRectangle(brush,badge);
                TextRenderer.DrawText(g,leaderText,smallFont,badge,Theme.Amber,
                    TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);
            }
            if(dropSide!=0)using(var pen=new Pen(Theme.Accent,4))g.DrawLine(pen,dropSide<0?3:Width-4,3,dropSide<0?3:Width-4,Height-4);
            string statusText=TeamPresentation.Status(row)+(preview && previewMinimized?AOLauncher.Localization.Texts.Get("Text_Preview_may_pause_980151EA24B2"):"");
            Draw(g,statusText,new Rectangle(13,34,Width-26,18),status,smallFont);
            if(preview) {using(var b=new SolidBrush(Theme.Background))g.FillRectangle(b,PreviewRect);}
            var p=row.Pulse;int y=preview?PreviewRect.Bottom+10:57;
            Bar(g,AOLauncher.Localization.Texts.Get("Text_HEALTH_8586076A9679"),fresh?TeamPresentation.Ratio(p.Health,p.MaxHealth):null,fresh?p.Health+" / "+p.MaxHealth:"—",y,Color.FromArgb(230,30,30));
            Bar(g,"NANO",fresh?TeamPresentation.Ratio(p.Nano,p.MaxNano):null,fresh?p.Nano+" / "+p.MaxNano:"—",y+29,Color.FromArgb(0,170,230));
            var combatRect=new Rectangle(13,y+61,Width-26,48);
            using(var brush=new SolidBrush(attacking?Color.FromArgb(66,45,26):Theme.Background))g.FillRectangle(brush,combatRect);
            if(attacking) {
                using(var pen=new Pen(combatColor,2))g.DrawRectangle(pen,combatRect);
                Draw(g,AOLauncher.Localization.Texts.Get("Text_ATTACKING_5A56AE07BA21"),new Rectangle(21,y+63,Width-42,18),combatColor,smallFont);
                Draw(g,TeamPresentation.Target(row),new Rectangle(21,y+81,Width-42,25),Theme.Foreground,combatFont);
            } else Draw(g,!fresh?AOLauncher.Localization.Texts.Get("Text_Activity_unavailable_D23BA8B3D10C"):AOLauncher.Localization.Texts.Get("Text_IDLE_not_attacking_B2BE76B05156"),new Rectangle(21,y+61,Width-42,48),Theme.Muted,smallFont);
            var aggro=TeamPresentation.AggroCount(row,now);
            var aggroRect=new Rectangle(13,y+115,Width-26,50);
            using(var brush=new SolidBrush(aggro>0?Color.FromArgb(65,27,28):Theme.Background))g.FillRectangle(brush,aggroRect);
            string aggroTitle=!aggro.HasValue?AOLauncher.Localization.Texts.Get("Aggro.Unavailable")
                :aggro.Value==0?AOLauncher.Localization.Texts.Get("Aggro.None")
                :string.Format(AOLauncher.Localization.Texts.Get("Aggro.Count"),aggro.Value);
            Draw(g,aggroTitle,new Rectangle(21,y+116,Width-42,aggro>0?19:48),aggro>0?Theme.Warning:Theme.Muted,smallFont);
            if(aggro>0)Draw(g,TeamPresentation.AggroNames(row),new Rectangle(21,y+135,Width-42,28),Theme.Foreground,smallFont);
            y+=56;
            string response=row.Sample?.ReplyMs.HasValue==true?AOLauncher.Localization.Texts.Get("Text_Local_reply_6F2D9C0D54DE")+row.Sample.ReplyMs.Value.ToString("0.0")+" ms":"";
            Draw(g,response,new Rectangle(13,y+115,Width-26,18),Theme.Muted,smallFont);
            var sample=row.Sample;
            bool memoryFresh=!row.ExitedUtc.HasValue && ClientAddressSpace.Fresh(sample,now);
            int memoryWarning=memoryFresh?ClientAddressSpace.Warning(sample.FreeClientBytes,sample.LargestFreeClientBytes):0;
            Color memoryColor=memoryWarning==2?Color.FromArgb(255,100,100):memoryWarning==1?Theme.Amber:Theme.Muted;
            string cpu=sample?.CpuPercent.HasValue==true && (DateTime.UtcNow-sample.Utc).TotalSeconds<5?sample.CpuPercent.Value.ToString("0.0")+"%":"—";
            string memory=memoryFresh?(sample.FreeClientBytes.Value/(1024*1024))+AOLauncher.Localization.Texts.Get("Text_MB_free_67613B739280"):"—";
            Draw(g,AOLauncher.Localization.Texts.Get("Text_Client_CPU_A700BD379050")+cpu+" · "+AOLauncher.Localization.Texts.Get("Text_Mem_1944D875C909")+memory,new Rectangle(13,y+137,Width-26,18),memoryColor,smallFont);
            var movement=owner.MovementState(row);
            bool syncStopped=MovementTracking.Fresh(row,now) && p.Movement.GuardStatus=="Stopped";
            bool regroupBlocked=MovementTracking.Fresh(row,now) && p.Movement.GuardStatus?.StartsWith("Regroup blocked:",StringComparison.Ordinal)==true;
            string formation=owner.FormationStatusFor(row);
            Draw(g,formation??(syncStopped?AOLauncher.Localization.Texts.Get("Movement.GuardStopped"):movement.Text),new Rectangle(13,y+180,Width-26,18),regroupBlocked?Theme.Warning:formation!=null?Theme.Amber:syncStopped || movement.Severity==2?Theme.Warning:movement.Severity==1?Theme.Amber:Theme.Muted,smallFont);
            string zone=p?.Zoning==true?AOLauncher.Localization.Texts.Get("Movement.ZoneLoading")
                :MovementTracking.Fresh(row,now)?(string.IsNullOrWhiteSpace(p.Movement.ZoneName)?"—":p.Movement.ZoneName):"—";
            Draw(g,AOLauncher.Localization.Texts.Get("Movement.Zone")+zone,new Rectangle(13,y+158,Width-26,18),Theme.Muted,smallFont);
        }
        void Bar(Graphics g,string label,double? ratio,string value,int y,Color color) {
            var rect=new Rectangle(13,y,Width-26,23);using(var b=new SolidBrush(Theme.Background))g.FillRectangle(b,rect);
            if(ratio.HasValue)using(var b=new SolidBrush(color))g.FillRectangle(b,rect.X,rect.Y,(int)(rect.Width*ratio.Value),rect.Height);
            Draw(g,label+"   "+value,new Rectangle(rect.X+7,rect.Y,rect.Width-14,rect.Height),Theme.Foreground,smallFont);
        }
        static void Draw(Graphics g,string text,Rectangle rect,Color color,Font font) => TextRenderer.DrawText(g,text??"",font,rect,color,TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
        void ReleaseThumbnail(){if(thumbnail!=IntPtr.Zero){Native.DwmUnregisterThumbnail(thumbnail);thumbnail=IntPtr.Zero;}}
        protected override void Dispose(bool disposing){ReleaseThumbnail();if(disposing){recoveryMenu.Dispose();orderMenu.Dispose();detailsTip.Dispose();titleFont.Dispose();smallFont.Dispose();combatFont.Dispose();}base.Dispose(disposing);}
        internal static class Native {
            [StructLayout(LayoutKind.Sequential)] internal struct Rect {internal int Left,Top,Right,Bottom;internal Rect(Rectangle r){Left=r.Left;Top=r.Top;Right=r.Right;Bottom=r.Bottom;}}
            [StructLayout(LayoutKind.Sequential)] internal struct Properties {internal uint Flags;internal Rect Destination,Source;internal byte Opacity;[MarshalAs(UnmanagedType.Bool)]internal bool Visible;[MarshalAs(UnmanagedType.Bool)]internal bool ClientOnly;}
            [DllImport("dwmapi.dll")] internal static extern int DwmRegisterThumbnail(IntPtr destination,IntPtr source,out IntPtr thumbnail);
            [DllImport("dwmapi.dll")] internal static extern int DwmQueryThumbnailSourceSize(IntPtr thumbnail,out Size size);
            [DllImport("dwmapi.dll")] internal static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail,ref Properties properties);
            [DllImport("dwmapi.dll")] internal static extern int DwmUnregisterThumbnail(IntPtr thumbnail);
            [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
            [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr window,out Rect rectangle);
            [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(IntPtr window,int command);
            [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
            [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
            [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
        }
    }
}
