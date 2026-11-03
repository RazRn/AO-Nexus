using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using NexusPlayTools;
using AOLauncher.Localization;
using NexusTeams;
using NexusTelemetry;
using System.Diagnostics;
using System.IO;

namespace AOLauncher
{
    internal sealed partial class TeamWindow
    {
        readonly FlowLayoutPanel formationBar=new FlowLayoutPanel {Name="formationControls",WrapContents=true,Padding=Padding.Empty,Margin=Padding.Empty};
        readonly NexusComboBox formationLeader=new NexusComboBox {Name="formationLeader",Width=145,DropDownStyle=ComboBoxStyle.DropDownList};
        readonly NumericUpDown formationSpacing=new NexusNumericUpDown {Name="formationSpacing",Minimum=1,Maximum=10,DecimalPlaces=1,Increment=.5m,Width=60,AutoSize=false,Height=28};
        readonly NexusComboBox formationShape=new NexusComboBox {Name="formationShape",Width=145,DropDownStyle=ComboBoxStyle.DropDownList};
        readonly Label formationDistanceLabel=FormationLabel("Formation.Spacing");
        readonly NexusComboBox syncRefreshRate=new NexusComboBox {Name="syncRefreshRate",Width=185,DropDownStyle=ComboBoxStyle.DropDownList};
        readonly NexusCheckBox autoRegroup=new NexusCheckBox {Name="autoRegroup",AutoSize=true,Text=Texts.Get("Regroup.Option")};
        readonly Button formationStart=FormationButton("formationStart","Formation.Start",60);
        readonly Button formationGather=FormationButton("formationGather","Formation.Gather",65);
        readonly Button formationStop=FormationButton("formationStop","Formation.Stop",55);
        readonly Label formationStatus=new Label {Name="formationStatus",AutoSize=true,MaximumSize=new Size(230,28),AutoEllipsis=true,ForeColor=Theme.Muted};
        readonly Dictionary<int,FormationReport> formationReports=new Dictionary<int,FormationReport>();
        readonly ToolTip formationTips=new ToolTip {AutoPopDelay=25000,InitialDelay=400,ReshowDelay=100,ShowAlways=true};
        List<ClientObservation> formationClients=new List<ClientObservation>();
        string formationSession,formationSignature,formationAction;
        internal Action<PresetCamping.Client,int[]> BeforeLeaderChange;
        DateTime nextFormationPoll,formationStarted;
        bool formationPolling,formationBusy,formationSelecting,regroupPermissionsWritten;
        int formationGeneration;
        bool logoutMovementPaused;
        DateTime nextSyncDiagnostic;
        sealed class FormationChoice
        {
            internal ClientObservation Row;
            public override string ToString()=>Row.Name;
        }
        static Button FormationButton(string name,string key,int width)=>new NexusButton {Name=name,Text=Texts.Get(key),Width=width,Height=28,FlatStyle=FlatStyle.Flat,BackColor=Theme.SurfaceRaised,ForeColor=Theme.Foreground};
        static Label FormationLabel(string key)=>new Label {Text=Texts.Get(key),AutoSize=true,ForeColor=Theme.Muted};
        static TableLayoutPanel FormationGroup(params Control[] controls)
        {
            // Each group can wrap as a unit, but shares the same row height and
            // vertical centre. Native editors and labels need different heights.
            var group=new TableLayoutPanel {AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,
                ColumnCount=controls.Length,RowCount=1,Padding=Padding.Empty,Margin=new Padding(0,0,12,4)};
            group.RowStyles.Add(new RowStyle(SizeType.Absolute,32));
            for(int i=0;i<controls.Length;i++) {
                group.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                controls[i].Anchor=AnchorStyles.Left;
                controls[i].Margin=new Padding(0,0,i==controls.Length-1?0:6,0);
                group.Controls.Add(controls[i],i,0);
            }
            return group;
        }
        void InitializeFormation()
        {
            formationTips.SetToolTip(syncRefreshRate,Texts.Get("Formation.SyncTooltip"));
            formationTips.SetToolTip(autoRegroup,Texts.Get("Regroup.Help"));
            formationTips.SetToolTip(formationLeader,Texts.Get("Formation.LeaderTooltip"));
            formationTips.SetToolTip(formationShape,Texts.Get("Formation.ShapeTooltip"));
            formationTips.SetToolTip(formationSpacing,Texts.Get("Formation.DistanceTooltip"));
            formationTips.SetToolTip(formationStart,Texts.Get("Formation.StartTooltip"));
            formationTips.SetToolTip(formationGather,Texts.Get("Formation.GatherTooltip"));
            formationTips.SetToolTip(formationStop,Texts.Get("Formation.StopTooltip"));
            foreach(var control in new Control[]{autoRegroup,syncRefreshRate,formationLeader,formationShape,formationSpacing,formationStart,formationGather,formationStop})
                formationTips.SetToolTip(control,TeamPresentation.TooltipText(formationTips.GetToolTip(control)));
            FormClosed+=(s,e)=>formationTips.Dispose();
            formationShape.Items.AddRange(new object[]{Texts.Get("Formation.FollowChoice"),Texts.Get("Formation.CircleChoice"),Texts.Get("Formation.FunnyChoice")});
            formationShape.SelectedIndex=Preferences.FormationMode=="funny"?2:Preferences.FormationMode=="circle"?1:0;
            formationDistanceLabel.Text=Texts.Get(formationShape.SelectedIndex>0?"Formation.Radius":"Formation.Spacing");
            formationSpacing.Value=Math.Max(1,Math.Min(10,formationShape.SelectedIndex>0?Preferences.FormationRadius:Preferences.FormationSpacing));
            formationSpacing.BackColor=Theme.Surface;formationSpacing.ForeColor=Theme.Foreground;
            formationLeader.MatchHeight(formationStart);
            formationShape.MatchHeight(formationStart);
            syncRefreshRate.MatchHeight(formationStart);
            formationBar.Controls.Add(FormationGroup(FormationLabel("Formation.Leader"),formationLeader));
            formationBar.Controls.Add(FormationGroup(FormationLabel("Formation.Shape"),formationShape));
            formationBar.Controls.Add(FormationGroup(formationDistanceLabel,formationSpacing,
                new Label {Text="m",AutoSize=true,ForeColor=Theme.Muted}));
            formationBar.Controls.Add(FormationGroup(formationStart,formationGather,formationStop));
            syncRefreshRate.Items.AddRange(new object[]{Texts.Get("Formation.SyncEventOnly"),Texts.Get("Formation.Sync5Hz"),Texts.Get("Formation.Sync10Hz")});
            syncRefreshRate.SelectedIndex=Preferences.SyncRefreshRate==10?2:Preferences.SyncRefreshRate==5?1:0;
            syncRefreshRate.SelectedIndexChanged+=(s,e)=>{Preferences.SyncRefreshRate=syncRefreshRate.SelectedIndex==2?10:syncRefreshRate.SelectedIndex==1?5:0;SavePanelPreferences();};
            formationBar.Controls.Add(FormationGroup(syncRefreshRate,formationStatus));
            autoRegroup.Checked=Preferences.AutoRegroup;
            autoRegroup.CheckedChanged+=(s,e)=>{Preferences.AutoRegroup=autoRegroup.Checked;SavePanelPreferences();nextFormationPoll=DateTime.MinValue;};
            formationBar.Controls.Add(FormationGroup(autoRegroup));
            sections.SetHeaderTools("team",formationBar);
            formationStart.Click+=async(s,e)=>await StartFormation(formationShape.SelectedIndex==2?"funny":formationShape.SelectedIndex==1?"circle":"follow");
            formationGather.Click+=async(s,e)=>await StartFormation("gather");
            formationStop.Click+=async(s,e)=>{autoRegroup.Checked=false;await StopFormation(Texts.Get("Formation.Stopped"));};
            formationLeader.SelectedIndexChanged+=(s,e)=>{
                if(formationSelecting)return;
                Preferences.FormationLeader=(formationLeader.SelectedItem as FormationChoice)?.Row is ClientObservation row?TeamCardOrdering.Key(row):null;
                SavePanelPreferences();
            };
            formationLeader.SelectionChangeCommitted+=async(s,e)=>await ChangeTeamLeader();
            formationSpacing.ValueChanged+=(s,e)=>{if(formationShape.SelectedIndex>0)Preferences.FormationRadius=formationSpacing.Value;else Preferences.FormationSpacing=formationSpacing.Value;SavePanelPreferences();};
            formationShape.SelectedIndexChanged+=(s,e)=>{
                bool circle=formationShape.SelectedIndex>0;Preferences.FormationMode=formationShape.SelectedIndex==2?"funny":circle?"circle":"follow";
                formationDistanceLabel.Text=Texts.Get(circle?"Formation.Radius":"Formation.Spacing");
                formationSpacing.Value=Math.Max(1,Math.Min(10,circle?Preferences.FormationRadius:Preferences.FormationSpacing));SavePanelPreferences();
            };
            FormClosing+=async(s,e)=>await StopFormation(Texts.Get("Formation.Stopped"));
        }
        internal string FormationStatusFor(ClientObservation row)
        {
            var movement=row.Pulse?.Movement;
            if(MovementTracking.Fresh(row,Stopwatch.GetTimestamp()) && movement?.GuardStatus?.StartsWith("Regroup",StringComparison.Ordinal)==true)
                return RegroupStatus(movement.GuardStatus);
            if(!configuration().ExperimentalTeamAutomation || !formationReports.TryGetValue(row.Pid,out var state)
                || state.StartTicks!=row.StartTicks || !state.Fresh(DateTime.UtcNow))return null;
            return state.Active || (state.Status!="Stopped" && formationClients.Any(r=>r.Pid==row.Pid))?state.Status:null;
        }
        async void UpdateFormation(List<ClientObservation> rows)
        {
            bool enabled=configuration().ExperimentalTeamAutomation;
            formationBar.Visible=enabled;
            if(!enabled){if(formationSession!=null)await StopFormation(Texts.Get("Formation.Stopped"));return;}
            var live=rows.Where(r=>!r.ExitedUtc.HasValue && r.Client!=null).ToList();
            string signature=string.Join("|",live.Select(r=>r.Pid+":"+r.StartTicks));
            if(signature!=formationSignature) {
                formationSignature=signature;formationSelecting=true;
                string selected=(formationLeader.SelectedItem as FormationChoice)?.Row is ClientObservation chosen?TeamCardOrdering.Key(chosen):Preferences.FormationLeader;
                formationLeader.Items.Clear();
                foreach(var row in live)formationLeader.Items.Add(new FormationChoice {Row=row});
                var choice=formationLeader.Items.Cast<FormationChoice>().FirstOrDefault(c=>TeamCardOrdering.Key(c.Row)==selected)
                    ??formationLeader.Items.Cast<FormationChoice>().FirstOrDefault(c=>c.Row.Pulse?.IsTeamLeader==true)
                    ??formationLeader.Items.Cast<FormationChoice>().FirstOrDefault();
                formationLeader.SelectedItem=choice;formationSelecting=false;
            }
            bool recovering=live.Any(r=>MovementTracking.Fresh(r,Stopwatch.GetTimestamp())
                && (r.Pulse.Movement.GuardStatus=="Regrouping" || r.Pulse.Movement.GuardStatus?.StartsWith("Regroup blocked:",StringComparison.Ordinal)==true));
            formationStart.Enabled=formationGather.Enabled=!logoutMovementPaused && !recovering && !formationBusy && formationSession==null && live.Count>=2;
            formationLeader.Enabled=!logoutMovementPaused && !formationBusy;
            formationSpacing.Enabled=formationShape.Enabled=!formationBusy && formationSession==null;
            formationStop.Enabled=Preferences.AutoRegroup || formationSession!=null || formationBusy || formationReports.Values.Any(s=>s.Active);
            if(logoutMovementPaused || formationPolling || DateTime.UtcNow<nextFormationPoll)return;
            formationPolling=true;nextFormationPoll=DateTime.UtcNow.AddSeconds(1);
            try {
                string session=formationSession;
                int refreshRate=Preferences.SyncRefreshRate;
                var results=await Task.WhenAll(live.Select(async row=> {
                    return await Task.Run(()=> {
                        if(PresetCamping.IsSameProcess(row.Client))ShortcutClient.FormationCommand(row.Pid,new FormationRequest {Action="sync-rate",Rate=refreshRate});
                        if(session!=null && formationClients.Any(r=>r.Pid==row.Pid && r.StartTicks==row.StartTicks) && PresetCamping.IsSameProcess(row.Client))
                            ShortcutClient.FormationCommand(row.Pid,new FormationRequest {Action="keep",Session=session});
                        return Tuple.Create(row,ReadFormation(row));
                    });
                }));
                if(IsDisposed)return;
                formationReports.Clear();
                foreach(var result in results)if(result.Item2!=null)formationReports[result.Item1.Pid]=result.Item2;
                var permissions=BuildRegroupPermissions(results,!logoutMovementPaused && Preferences.AutoRegroup && formationSession==null && !formationBusy);
                if(Preferences.AutoRegroup || regroupPermissionsWritten) {
                    await Task.Run(()=>WriteRegroupPermissions(permissions));
                    regroupPermissionsWritten=Preferences.AutoRegroup;
                }
                if(IsDisposed)return;
                if(!logoutMovementPaused && Preferences.AutoRegroup && formationSession==null && !formationBusy)
                    SetStatusText(formationStatus,live.Any(r=>r.Pulse?.Movement?.AutoRegroupSupported!=true)
                        ?Texts.Get("Regroup.UpdateMonitor"):Texts.Get("Regroup.Armed"));
                if(DateTime.UtcNow>=nextSyncDiagnostic) {
                    nextSyncDiagnostic=DateTime.UtcNow.AddSeconds(5);
                    DevelopmentRecorder.Write("sync-refresh",new {requestedRate=refreshRate,clients=results.Where(r=>r.Item2?.SyncRefresh!=null)
                        .Select(r=>new {pid=r.Item1.Pid,name=r.Item1.Name,stats=r.Item2.SyncRefresh}).ToArray()});
                }
                if(session!=null && session==formationSession && !formationBusy && DateTime.UtcNow-formationStarted>TimeSpan.FromSeconds(2)) {
                    var failure=formationClients.FirstOrDefault(r=>!formationReports.TryGetValue(r.Pid,out var s) || s.Session!=session || !s.Active || !s.Fresh(DateTime.UtcNow));
                    if(failure!=null) {
                        string reason=failure.Name+": "+(formationReports.TryGetValue(failure.Pid,out var s)?s.Status:Texts.Get("Formation.Unavailable"));
                        await StopFormation(reason);
                    } else SetStatusText(formationStatus,Texts.Get("Formation.Active"));
                }
                foreach(var card in cards.Values)card.Invalidate();
            } catch(Exception ex){DevelopmentRecorder.Write("formation-poll-failed",new {error=ex.Message});}
            finally{formationPolling=false;}
        }
        static string RegroupStatus(string status)
        {
            if(status=="Regrouping")return Texts.Get("Regroup.Active");
            if(status=="Regrouped")return Texts.Get("Regroup.Complete");
            if(status.StartsWith("Regroup blocked:",StringComparison.Ordinal))return Texts.Get("Regroup.Blocked");
            return Texts.Get("Regroup.Stopped");
        }

        internal static AutoRegroupPermission[] BuildRegroupPermissions(Tuple<ClientObservation,FormationReport>[] clients,bool enabled)
        {
            long now=Stopwatch.GetTimestamp();
            return clients.Select(item=> {
                var row=item.Item1;var state=item.Item2;
                var permission=new AutoRegroupPermission {Pid=row.Pid,StartTicks=row.StartTicks,IssuedQpc=now};
                if(!enabled || state==null || state.Pid!=row.Pid || state.StartTicks!=row.StartTicks || !state.Ready || state.Active || !state.Fresh(DateTime.UtcNow)
                    || !MovementTracking.Fresh(row,now) || row.Pulse.Zoning || row.Pulse.Movement.AutoRegroupSupported!=true
                    || state.Character!=row.Pulse.Movement.CharacterId || state.Playfield!=row.Pulse.Movement.Playfield
                    || state.Channel!=row.Pulse.Movement.Channel)return permission;
                var leaders=clients.Where(other=>other.Item2!=null && other.Item2.Pid==other.Item1.Pid && other.Item2.StartTicks==other.Item1.StartTicks && other.Item2.Ready && !other.Item2.Active
                    && other.Item2.Fresh(DateTime.UtcNow) && !string.IsNullOrEmpty(state.Scope) && other.Item2.Scope==state.Scope
                    && other.Item2.Playfield==state.Playfield && other.Item2.Channel==state.Channel
                    && state.Members!=null && other.Item2.Members!=null && state.Members.OrderBy(id=>id).SequenceEqual(other.Item2.Members.OrderBy(id=>id))
                    && MovementTracking.Fresh(other.Item1,now) && other.Item1.Pulse.IsTeamLeader==true && !other.Item1.Pulse.Zoning
                    && other.Item2.Character==other.Item1.Pulse.Movement.CharacterId
                    && other.Item2.Playfield==other.Item1.Pulse.Movement.Playfield && other.Item2.Channel==other.Item1.Pulse.Movement.Channel
                    && other.Item1.Pulse.TeamStateQpc>0 && now>=other.Item1.Pulse.TeamStateQpc
                    && now-other.Item1.Pulse.TeamStateQpc<2*Stopwatch.Frequency).ToArray();
                if(leaders.Length!=1 || leaders[0].Item1.Pid==row.Pid)return permission;
                var leader=leaders[0];
                permission.Character=state.Character;permission.Leader=leader.Item2.Character;
                permission.LeaderPid=leader.Item1.Pid;permission.LeaderStartTicks=leader.Item1.StartTicks;
                permission.LeaderUpdateQpc=leader.Item1.Pulse.LastUpdateQpc;
                permission.Area=state.Playfield;permission.Channel=state.Channel;permission.Members=state.Members;
                permission.Enabled=true;
                if(!permission.Fresh(now,row.Pid,row.StartTicks))permission.Enabled=false;
                return permission;
            }).ToArray();
        }

        static void WriteRegroupPermissions(AutoRegroupPermission[] permissions)
        {
            string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AONexus","Regroup");
            Directory.CreateDirectory(folder);
            foreach(var permission in permissions) {
                string file=Path.Combine(folder,permission.Pid+"-"+permission.StartTicks+".json");
                if(!AutoRegroupPermission.Write(file,permission))
                    Trace.TraceWarning("Regroup permission update timed out for process {0}",permission.Pid);
            }
        }

        static FormationReport ReadFormation(ClientObservation row)
        {
            if(!PresetCamping.IsSameProcess(row.Client))return null;
            string reply=ShortcutClient.FormationStatus(row.Pid);
            if(reply==null || !reply.StartsWith("OK ",StringComparison.Ordinal))return null;
            try {
                var state=new JavaScriptSerializer().Deserialize<FormationReport>(reply.Substring(3));
                return state!=null && state.Version==1 && state.Pid==row.Pid && state.StartTicks==row.StartTicks && state.Fresh(DateTime.UtcNow)?state:null;
            }catch(ArgumentException){return null;}
        }
        static TeamView ReadLeaderView(ClientObservation row)
        {
            if(!PresetCamping.IsSameProcess(row.Client))return null;
            try {
                string json=ShortcutClient.TeamView(row.Pid);
                if(json==null || !json.StartsWith("{",StringComparison.Ordinal))return null;
                var view=new JavaScriptSerializer().Deserialize<TeamView>(json);long now=DateTime.UtcNow.Ticks;
                return view!=null && view.Ready && view.Start==row.StartTicks && view.Character==row.Client.CharacterId
                    && view.Utc<=now && now-view.Utc<TimeSpan.TicksPerSecond*3 && view.Members!=null
                    && view.Members.Length>=2 && view.Members.Length<=6 && view.Members.All(id=>id>0)
                    && view.Members.Distinct().Count()==view.Members.Length && view.Members.Contains(view.Character)
                    && (view.Leader==0 || view.Members.Contains(view.Leader))?view:null;
            }catch(ArgumentException){return null;}
        }
        async Task ChangeTeamLeader()
        {
            if(logoutMovementPaused || formationBusy || formationSelecting || !configuration().ExperimentalTeamAutomation)return;
            var selected=(formationLeader.SelectedItem as FormationChoice)?.Row;if(selected==null)return;
            string resume=formationSession==null?null:formationAction;
            formationBusy=true;
            try {
                await StopFormation(Texts.Get("Formation.ChangingLeader"));
                int generation=formationGeneration;
                var target=await Task.Run(()=>ReadLeaderView(selected));
                if(target==null)throw new InvalidOperationException(Texts.Get("Formation.TeamNotReady"));
                if(target.Leader!=target.Character) {
                    // Rebirth followers can report a complete roster but no leader
                    // ID. Ask the matching teammates for a fresh self-leader claim.
                    var candidates=read().Where(r=>!r.ExitedUtc.HasValue && r.Client!=null
                        && target.Members.Contains(r.Client.CharacterId) && r.Client.Server==selected.Client.Server && r.Client.Port==selected.Client.Port).ToArray();
                    var views=await Task.WhenAll(candidates.Select(r=>Task.Run(()=>Tuple.Create(r,ReadLeaderView(r)))));
                    var leaders=views.Where(v=>v.Item2!=null && v.Item2.Leader==v.Item2.Character
                        && v.Item2.Members.OrderBy(x=>x).SequenceEqual(target.Members.OrderBy(x=>x))).ToArray();
                    if(leaders.Length!=1)throw new InvalidOperationException(Texts.Get("Formation.LeaderUnavailable"));
                    var current=leaders[0].Item1;var prior=leaders[0].Item2;
                    if(!prior.CanChooseLeader)throw new InvalidOperationException(Texts.Get("Formation.ModeUpdate"));
                    await Task.Run(()=>BeforeLeaderChange?.Invoke(current.Client,prior.Members));
                    if(IsDisposed || generation!=formationGeneration)return;
                    string command="TEAMLEADER "+Guid.NewGuid().ToString("N")+" "+current.StartTicks+" "+prior.Character+" "+target.Character+" "+DateTime.UtcNow.AddSeconds(10).Ticks+" "+string.Join(",",prior.Members);
                    string reply=await Task.Run(()=>PresetCamping.IsSameProcess(current.Client)?ShortcutClient.TeamPlan(current.Pid,command):null);
                    if(reply!="OK queued")throw new InvalidOperationException(Texts.Get("Formation.Unavailable"));
                    bool confirmed=false;var deadline=DateTime.UtcNow.AddSeconds(8);
                    while(DateTime.UtcNow<deadline && !IsDisposed && generation==formationGeneration) {
                        await Task.Delay(250);
                        var observed=await Task.Run(()=>ReadLeaderView(selected));
                        if(observed!=null && observed.Leader==target.Character
                            && observed.Members.OrderBy(x=>x).SequenceEqual(target.Members.OrderBy(x=>x))){confirmed=true;break;}
                    }
                    if(IsDisposed || generation!=formationGeneration)return;
                    if(!confirmed)throw new InvalidOperationException(Texts.Get("Formation.LeaderNotConfirmed"));
                }
                if(IsDisposed || generation!=formationGeneration)return;
                DevelopmentRecorder.Write("team-leader-changed",new {character=target.Character,name=selected.Name});
                SetStatusText(formationStatus,string.Format(Texts.Get("Formation.LeaderChanged"),selected.Name));
                formationBusy=false;
                if(resume!=null)await StartFormation(resume);
            }catch(Exception ex){if(!IsDisposed)SetStatusText(formationStatus,ex.Message);DevelopmentRecorder.Write("team-leader-change-failed",new {error=ex.Message});}
            finally{formationBusy=false;}
        }
        async Task StartFormation(string action)
        {
            if(logoutMovementPaused || formationBusy || formationSession!=null || !configuration().ExperimentalTeamAutomation)return;
            var leader=(formationLeader.SelectedItem as FormationChoice)?.Row;if(leader==null)return;
            formationBusy=true;int generation=++formationGeneration;
            SetStatusText(formationStatus,Texts.Get("Formation.Checking"));
            try {
                var rows=read().Where(r=>!r.ExitedUtc.HasValue && r.Client!=null).ToArray();
                var states=await Task.WhenAll(rows.Select(r=>Task.Run(()=>Tuple.Create(r,ReadFormation(r)))));
                if(generation!=formationGeneration || IsDisposed)return;
                var lead=states.FirstOrDefault(r=>r.Item1.Pid==leader.Pid && r.Item1.StartTicks==leader.StartTicks)?.Item2;
                if(lead==null || !lead.Ready)throw new InvalidOperationException(Texts.Get("Formation.Unavailable"));
                var participants=states.Where(r=>r.Item2!=null && r.Item2.Scope==lead.Scope && lead.Members.Contains(r.Item2.Character)).ToArray();
                if(participants.Any(r=>!r.Item2.PaceSupported) || (action=="funny" && participants.Any(r=>!r.Item2.FunnySupported)))throw new InvalidOperationException(Texts.Get("Formation.ModeUpdate"));
                if(action=="circle" && participants.Any(r=>!r.Item2.CircleSupported))throw new InvalidOperationException(Texts.Get("Formation.CircleUpdate"));
                if(participants.Length!=lead.Members.Length || participants.Any(r=>!r.Item2.Ready || r.Item2.Playfield!=lead.Playfield
                    || r.Item2.Channel!=lead.Channel || !r.Item2.Members.OrderBy(x=>x).SequenceEqual(lead.Members.OrderBy(x=>x))))
                    throw new InvalidOperationException(Texts.Get("Formation.TeamNotReady"));
                formationClients=participants.Select(r=>r.Item1).ToList();formationSession=Guid.NewGuid().ToString("N");formationAction=action;formationStarted=DateTime.UtcNow;
                float spacing=action=="gather"?(float)Math.Max(1,Math.Min(2,Preferences.FormationSpacing)):(float)formationSpacing.Value;
                var order=TeamCardOrdering.Sort(formationClients,configuration().TeamCardOrder).Where(r=>r.Pid!=leader.Pid).ToList();
                string session=formationSession;
                var replies=await Task.WhenAll(participants.Select(async r=> {
                    var command=new FormationRequest {Action=action,Session=session,Leader=lead.Character,Slot=r.Item1.Pid==leader.Pid?0:order.FindIndex(x=>x.Pid==r.Item1.Pid)+1,
                        Spacing=spacing,Channel=lead.Channel,Playfield=lead.Playfield,Members=lead.Members};
                    return await Task.Run(()=>PresetCamping.IsSameProcess(r.Item1.Client)?ShortcutClient.FormationCommand(r.Item1.Pid,command):null);
                }));
                if(generation!=formationGeneration || IsDisposed){await StopFormation(Texts.Get("Formation.Stopped"));return;}
                if(replies.Any(r=>r!="OK"))throw new InvalidOperationException(Texts.Get("Formation.Unavailable"));
                SetStatusText(formationStatus,Texts.Get("Formation.Starting"));
                DevelopmentRecorder.Write("formation-start",new {session,action,leader=lead.Character,spacing});
            } catch(Exception ex){await StopFormation(ex.Message);}
            finally{formationBusy=false;}
        }
        // Called only after confirmation and process/credential validation. Keep the
        // preference, but do not let our movement cancel the game's camping countdown.
        internal async Task PrepareMovementForLogout()
        {
            logoutMovementPaused=true;
            ++formationGeneration;
            formationSession=null;
            var deadline=Stopwatch.StartNew();
            while(formationPolling || formationBusy) {
                if(deadline.Elapsed>TimeSpan.FromSeconds(5))
                    throw new InvalidOperationException(Texts.Get("Formation.LogoutBusy"));
                await Task.Delay(50);
            }
            // Drain any preceding permission writer/keep-alive before revoking grants.
            var targets=formationClients.ToArray();
            var grants=formationReports.Values.Select(state=>new AutoRegroupPermission {
                Pid=state.Pid,StartTicks=state.StartTicks,IssuedQpc=Stopwatch.GetTimestamp()
            }).ToArray();
            await Task.Run(()=>WriteRegroupPermissions(grants));
            await StopFormation(Texts.Get("Formation.Stopped"));
            deadline.Restart();
            while(targets.Length>0) {
                var reports=await Task.WhenAll(targets.Select(row=>Task.Run(()=>ReadFormation(row))));
                if(reports.Select((report,index)=>!PresetCamping.IsSameProcess(targets[index].Client)
                    || (report!=null && !report.Active)).All(stopped=>stopped))break;
                if(deadline.Elapsed>TimeSpan.FromSeconds(3))
                    throw new InvalidOperationException(Texts.Get("Formation.LogoutBusy"));
                await Task.Delay(100);
            }
            // A permission already read by the monitor expires within three seconds.
            // Only wait for this lease when automatic regroup was actually enabled.
            if(Preferences.AutoRegroup && grants.Length>0)await Task.Delay(3100);
        }
        internal void FinishMovementLogout()
        {
            logoutMovementPaused=false;
            nextFormationPoll=DateTime.MinValue;
        }
        async Task StopFormation(string reason)
        {
            ++formationGeneration;formationSession=null;
            var targets=formationClients.ToArray();
            if(!IsDisposed)SetStatusText(formationStatus,reason);
            await Task.WhenAll(targets.Select(row=>Task.Run(()=> {
                if(PresetCamping.IsSameProcess(row.Client))ShortcutClient.FormationCommand(row.Pid,new FormationRequest {Action="stop"});
            })));
            DevelopmentRecorder.Write("formation-stop",new {reason});
        }
    }
}
