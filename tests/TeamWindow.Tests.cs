using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace AOLauncher {
    static class TeamWindowTests {
        static int checks;
        static void LogoutMovementCoordination() {
            var settings=new LauncherSettings {ExperimentalTeamAutomation=true};
            settings.ControlPanel=new ControlPanelPreferences {AutoRegroup=true,FormationMode="funny"};
            using(var panel=new TeamWindow(()=>new List<ClientObservation>(),()=>settings)) {
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var paused=typeof(TeamWindow).GetField("logoutMovementPaused",flags);
                var polling=typeof(TeamWindow).GetField("formationPolling",flags);
                var session=typeof(TeamWindow).GetField("formationSession",flags);
                Check(!(bool)paused.GetValue(panel),"Unconfirmed logout does not suspend movement");
                polling.SetValue(panel,true);session.SetValue(panel,"fixture-session");
                var prepare=panel.PrepareMovementForLogout();
                Check(!prepare.IsCompleted && (bool)paused.GetValue(panel),"Confirmed logout waits for in-flight formation polling");
                Check(session.GetValue(panel)==null,"Logout invalidates the formation keep-alive before draining polling");
                polling.SetValue(panel,false);
                var deadline=Stopwatch.StartNew();
                while(!prepare.IsCompleted && deadline.ElapsedMilliseconds<2000){Application.DoEvents();Thread.Sleep(10);}
                Check(prepare.IsCompleted,"Idle formation drain completes without a fixed three-second delay");
                prepare.GetAwaiter().GetResult();
                Check(settings.ControlPanel.AutoRegroup && settings.ControlPanel.FormationMode=="funny","Logout preserves saved regroup and formation preferences");
                Check((bool)paused.GetValue(panel),"Movement stays suspended throughout logout");
                panel.FinishMovementLogout();
                Check(!(bool)paused.GetValue(panel) && session.GetValue(panel)==null,"Logout completion releases the pause without restarting the old formation");
                panel.FinishMovementLogout();
                Check(!(bool)paused.GetValue(panel),"Logout cleanup is safe to repeat");
            }
        }
        static void FormationHeader() {
            var settings=new LauncherSettings {ExperimentalTeamAutomation=true};
            settings.ControlPanel=new ControlPanelPreferences {FormationSpacing=2};
            using(var panel=new TeamWindow(()=>new List<ClientObservation>(),()=>settings)) {
                panel.ShowInTaskbar=false;panel.StartPosition=FormStartPosition.Manual;panel.Location=new Point(-18000,-18000);
                panel.Show();Application.DoEvents();panel.RefreshWall();
                var controls=All(panel).ToList();var bar=controls.Single(c=>c.Name=="formationControls");
                var sections=controls.OfType<ControlPanelSections>().Single();
                var stop=controls.Single(c=>c.Name=="formationStop");
                Check(bar.Visible && bar.Parent==controls.Single(c=>c.Name=="sectionHeader-team").Parent,"Formation controls share the Team header");
                var spacing=controls.OfType<NumericUpDown>().Single(c=>c.Name=="formationSpacing");
                Check(spacing.Value==2,"Formation spacing defaults to two metres");spacing.Value=3;
                Check(settings.ControlPanel.FormationSpacing==3,"Spacing preference persists without starting movement");
                var shape=controls.OfType<ComboBox>().Single(c=>c.Name=="formationShape");shape.SelectedIndex=1;
                Check(settings.ControlPanel.FormationMode=="circle" && spacing.Value==3,"Circle selection persists and uses radius");spacing.Value=4;
                shape.SelectedIndex=0;Check(spacing.Value==3 && settings.ControlPanel.FormationRadius==4,"Circle radius is separate from Follow spacing");
                shape.SelectedIndex=1;Check(spacing.Value==4,"Saved circle radius restored");
                shape.SelectedIndex=2;Check(settings.ControlPanel.FormationMode=="funny" && spacing.Value==4,"Funny business persists and uses radius");
                Check(!controls.Single(c=>c.Name=="formationStart").Enabled,"No movement start without running clients");
                sections.Toggle("damage");sections.Toggle("team");
                foreach(int width in new[]{1160,760}) {
                    panel.Width=width;Application.DoEvents();
                    var stopBounds=bar.RectangleToClient(stop.RectangleToScreen(stop.ClientRectangle));
                    Check(bar.ClientRectangle.Contains(stopBounds),"Stop remains within collapsed header at width "+width);
                    Check(controls.Single(c=>c.Name=="sectionHeader-team").Height==30,"Team heading stays one line at width "+width);
                    using(var image=new Bitmap(panel.Width,panel.Height)){panel.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save("logs/team-previews/formation-"+width+".png");}
                }
                settings.ExperimentalTeamAutomation=false;panel.RefreshWall();Check(!bar.Visible,"Experimental flag hides formation controls");
                panel.Close();
            }
        }
        static void OrderedTeamPreset() {
            var settings=new LauncherSettings {ExperimentalTeamAutomation=true,AutoInjectPlugins=true,IncludeShortcuts=true};
            settings.Accounts=Enumerable.Range(1,3).Select(i=>new AccountConfig {Id="a"+i,Username="fixture"+i,Server="127.0.0.1",Port=7500,
                Characters=new List<CharacterConfig>{new CharacterConfig {CharacterId=i,Name="Character "+i,Selected=true}}}).ToList();
            var preset=PresetService.Create(settings,"Fixture");preset.OrderedLogin=true;preset.AutoFormTeam=true;
            preset.Characters=preset.Characters.AsEnumerable().Reverse().ToList();preset.Characters[0].PluginSetId="fixture-plugin";
            var credentials=settings.Accounts.ToDictionary(a=>a.Id,a=>"fixture");
            var request=MainForm.CreateLaunchRequest(settings,credentials);
            Check(request.KeepLeaderActive,"Ordered preset requests leader focus and background followers");
            Check(request.OrderedStarts && request.Characters.Select(c=>c.CharacterId).SequenceEqual(new[]{3,2,1}),"Saved preset order controls login request across accounts");
            var relog=new LaunchRequest {Characters=request.Characters.AsEnumerable().Reverse().ToList()};
            foreach(var account in settings.Accounts)account.Characters[0].Selected=false;
            MainForm.ApplyPresetLaunchOrder(settings,relog,true);
            Check(relog.OrderedStarts && relog.KeepLeaderActive && relog.Characters[0].CharacterId==3,"Full relog retains preset leader focus and order independently of dashboard checkboxes");
            var partial=new LaunchRequest {Characters=relog.Characters.Take(2).ToList()};
            MainForm.ApplyPresetLaunchOrder(settings,partial,true);
            Check(!partial.KeepLeaderActive,"Partial relog does not nominate a replacement preset leader");
            foreach(var account in settings.Accounts)account.Characters[0].Selected=true;
            PresetService.Update(settings,preset);Check(preset.Characters.Select(c=>c.CharacterId).SequenceEqual(new[]{3,2,1}),"Update selection preserves saved team order");
            PresetService.Normalize(settings);Check(preset.Characters[0].PluginSetId=="fixture-plugin","Normalization retains plugin overrides while preserving order");
            Check(AutoTeamStartup.Validate(settings,preset.Characters,true)==null,"Valid same-server team can auto-form");
            settings.Accounts[0].Server="127.0.0.2";Check(AutoTeamStartup.Validate(settings,preset.Characters,true)!=null,"Cross-server automatic teams rejected");
            settings.Accounts[0].Server="127.0.0.1";settings.IncludeShortcuts=false;Check(AutoTeamStartup.Validate(settings,preset.Characters,true)!=null,"Missing required helper setting is explicit");settings.IncludeShortcuts=true;
            var now=DateTime.UtcNow;
            var replies=new Dictionary<int,string>();var sent=new List<Tuple<int,string>>();
            var startup=new AutoTeamStartup(preset) {Now=()=>now,SameProcess=r=>true,ReadStatus=pid=>replies.ContainsKey(pid)?replies[pid]:"WAIT",
                SendPlan=(pid,line)=>{sent.Add(Tuple.Create(pid,line));return "OK queued";}};
            var rows=preset.Characters.Select(m=>new PluginAutoInjector.Row {AccountId=m.AccountId,CharacterId=m.CharacterId,Pid=m.CharacterId,
                StartTicks=m.CharacterId,State="Loaded",ShortcutsRequested=true,CharacterName="Fixture"+m.CharacterId}).ToList();
            startup.Poll(settings,rows);
            Check(sent.Select(s=>s.Item1).SequenceEqual(new[]{2,1,3}),"Followers are armed before leader");
            Check(sent.Last().Item2.Split(' ').Last()=="3","Unacknowledged helpers are not invitation targets");
            foreach(var item in sent)replies[item.Item1]=item.Item2.Split(' ')[1]+" Waiting for leader invitation";
            sent.Clear();now=now.AddSeconds(3);startup.Poll(settings,rows);
            Check(sent.Count==1 && sent[0].Item1==3 && sent[0].Item2.Split(' ').Last()=="3,2,1","Leader receives acknowledged members in stable preset order");
            sent.Clear();now=now.AddSeconds(3);startup.Poll(settings,rows);Check(sent.Count==0,"Stable plan does not resend each poll");
            replies[3]=replies[3].Split(' ')[0]+" Team formed";
            now=now.AddSeconds(3);startup.Poll(settings,rows);
            replies.Clear();now=now.AddSeconds(3);startup.Poll(settings,rows);Check(sent.Count==0,"Confirmed team finishes the launcher startup session");
            using(var dialog=new PresetNameDialog("Fixture","Fixture",new string[0])) {
                dialog.ConfigureTeam(settings,preset);
                dialog.ShowInTaskbar=false;dialog.Show();Application.DoEvents();
                var list=All(dialog).OfType<ListBox>().Single(c=>c.Name=="teamLoginOrder");
                list.SelectedIndex=1;
                All(dialog).OfType<Button>().Single(b=>b.Name=="teamOrderUp").PerformClick();
                Check(dialog.TeamOrder.Select(c=>c.CharacterId).SequenceEqual(new[]{2,3,1}),"Move up changes first character/leader");
                Check(preset.Characters.Select(c=>c.CharacterId).SequenceEqual(new[]{3,2,1}),"Cancelling dialog cannot mutate saved order");
            }
            settings.Accounts[0].Characters[0].Selected=false;
            request=MainForm.CreateLaunchRequest(settings,credentials);
            Check(!request.OrderedStarts && !request.KeepLeaderActive,"Manual selection outside saved preset retains normal parallel launch and window behavior");
        }
        static void FormationOwnsRoster() {
            var settings=new LauncherSettings {ExperimentalTeamAutomation=true,AutoInjectPlugins=true,IncludeShortcuts=true};
            settings.Accounts=Enumerable.Range(1,3).Select(i=>new AccountConfig {Id="a"+i,Server="fixture",Port=7500,
                Characters=new List<CharacterConfig>{new CharacterConfig{CharacterId=i,Selected=true}}}).ToList();
            var preset=PresetService.Create(settings,"Formation fixture");preset.AutoFormTeam=true;
            var rows=Enumerable.Range(1,3).Select(i=>new PluginAutoInjector.Row{AccountId="a"+i,CharacterId=i,Pid=i,StartTicks=i,State="Loaded",ShortcutsRequested=true}).ToList();
            var now=DateTime.UtcNow;var alive=new HashSet<int>(new[]{1,2,3});
            var views=rows.ToDictionary(r=>r.Pid,r=>new NexusTeams.TeamView{Start=r.StartTicks,Character=r.CharacterId,Ready=true,Leader=1,Members=new[]{1,2,3}});
            var sent=new List<string>();var json=new System.Web.Script.Serialization.JavaScriptSerializer();
            var recovery=new TeamRecoveryController {Now=()=>now,IsAlive=r=>alive.Contains(r.Pid),ReadRecoveryStatus=pid=>"WAIT",
                ReadView=pid=>{views[pid].Utc=now.Ticks;return json.Serialize(views[pid]);},SendPlan=(pid,line)=>{sent.Add(line);return "OK queued";}};
            recovery.Poll(settings,rows);alive.Remove(1);views[2].Leader=2;views[3].Leader=2;views[2].Members=new[]{2,3};views[3].Members=new[]{2,3};
            now=now.AddSeconds(3);recovery.Poll(settings,rows);
            Check(sent.Any(c=>c.StartsWith("TEAMRESTORE ")),"Fixture reproduces an armed recovery episode after leader exit");
            // The tracker can discard a conflicting episode while its game plans still exist.
            views[3].Leader=3;now=now.AddSeconds(3);recovery.Poll(settings,rows);views[3].Leader=2;
            var startup=new AutoTeamStartup(preset){Now=()=>now};recovery.BeginStartup(settings,preset,startup);
            sent.Clear();alive.Add(1);rows[0].StartTicks=100;views[1].Start=100;views[1].Leader=0;views[1].Members=new int[0];
            now=now.AddSeconds(3);recovery.Poll(settings,rows);
            Check(sent.Count==3 && sent.All(c=>c.StartsWith("TEAMCANCEL ")),"Fresh preset cancels old recovery plans on surviving and returning clients before formation");
            sent.Clear();now=now.AddSeconds(3);recovery.Poll(settings,rows);
            Check(sent.Count==0,"Partial teams cannot start competing recovery during preset formation");
            foreach(var row in rows){row.StartTicks+=1000;views[row.Pid].Start=row.StartTicks;}
            now=now.AddSeconds(3);recovery.Poll(settings,rows);
            Check(sent.Count==0,"Further process replacements during formation do not resurrect an old team");
            foreach(var v in views.Values){v.Leader=1;v.Members=new[]{1,2,3};}
            startup.Cancel(new List<PluginAutoInjector.Row>());now=now.AddSeconds(3);recovery.Poll(settings,rows);
            alive.Remove(1);views[2].Leader=2;views[3].Leader=2;views[2].Members=new[]{2,3};views[3].Members=new[]{2,3};
            now=now.AddSeconds(3);recovery.Poll(settings,rows);
            Check(sent.Any(c=>c.StartsWith("TEAMRESTORE ") && c.EndsWith(" 1")),"After formation recovery remembers the new real team and restores its original leader");
        }
        static void Check(bool test,string message){if(!test)throw new Exception(message);checks++;}
        static IEnumerable<Control> All(Control c){foreach(Control child in c.Controls){yield return child;foreach(var d in All(child))yield return d;}}
        static void CardOrderAndLeader() {
            long now=Stopwatch.GetTimestamp();
            var rows=Enumerable.Range(1,3).Select(i=>new ClientObservation {Pid=i,StartTicks=i,Name="Character "+i,
                Client=new PresetCamping.Client {AccountIdentity="id:fixture",CharacterId=i,ProcessId=i,StartTimeUtcTicks=i},
                Pulse=new ClientPulse {Pid=i,StartTicks=i,Active=true,PublishedQpc=now,VitalsQpc=now,TeamStateQpc=now,IsTeamLeader=true}}).ToList();
            var keys=rows.Select(TeamCardOrdering.Key).ToList();
            var order=TeamCardOrdering.Move(null,rows,keys[2],keys[0],false);
            Check(TeamCardOrdering.Sort(rows,order).SequenceEqual(new[]{rows[2],rows[0],rows[1]}),"Card can move before the first card");
            order=TeamCardOrdering.Move(order,rows,keys[2],keys[1],true);
            Check(TeamCardOrdering.Sort(rows,order).SequenceEqual(rows),"Card can move after the last card");
            order=TeamCardOrdering.Move(order,new[]{rows[0],rows[2]},keys[2],keys[0],false);
            Check(order.Contains(keys[1]),"Reordering a filtered view preserves hidden characters");
            rows[2].Pid=42;rows[2].StartTicks=99;
            Check(TeamCardOrdering.Key(rows[2])==keys[2],"Card order uses character identity, not relog process IDs");
            var json=new System.Web.Script.Serialization.JavaScriptSerializer();
            var settings=json.Deserialize<LauncherSettings>(json.Serialize(new LauncherSettings {TeamCardOrder=order}));
            Check(settings.TeamCardOrder.SequenceEqual(order),"Card order survives settings serialization");
            int saved=0;
            using(var form=new TeamWindow(()=>rows,()=>settings,saveOrder:()=>saved++)) {
                rows[1].Sample=new ClientSample {Utc=DateTime.UtcNow,Foreground=true};
                rows[1].Pulse.IsTeamLeader=false;rows[2].Pulse.IsTeamLeader=false;
                form.UpdateViewedClient(rows,0);
                Check(form.IsSelected(rows[0]),"Initial panel selection prefers actual leader over a last-started follower");
                form.UpdateViewedClient(rows,rows[1].Pid);form.UpdateViewedClient(rows,0);
                Check(form.IsSelected(rows[1]),"An explicit follower switch remains selected when returning to the panel");
                form.RefreshWall();form.MoveCard(rows[2],rows[1],true);form.RefreshWall();
                var cards=All(form).OfType<TeamCard>().ToList();
                Check(cards.Select(c=>c.Observation).SequenceEqual(rows) && saved==1,"Actual card control order changes and persists once");
                form.ResetCardOrder();Check(settings.TeamCardOrder.Count==0 && saved==2,"Reset clears only card ordering");
            }
            var row=rows[0];
            Check(TeamPresentation.Leader(row,now),"Fresh game team leader gets a badge");
            row.Pulse.IsTeamLeader=null;Check(!TeamPresentation.Leader(row,now),"Older monitors do not invent leadership");
            row.Pulse.IsTeamLeader=true;row.Pulse.TeamStateQpc=now+1;Check(!TeamPresentation.Leader(row,now),"Future team snapshot rejected");
            row.Pulse.TeamStateQpc=now-3*Stopwatch.Frequency;Check(!TeamPresentation.Leader(row,now),"Stale leadership hidden");
            row.Pulse.TeamStateQpc=now;row.Pulse.Zoning=true;Check(!TeamPresentation.Leader(row,now),"Zoning clears visible leader badge");
            row.Pulse.Zoning=false;row.Pulse.IsTeamLeader=false;Check(!TeamPresentation.Leader(row,now),"Leadership transfer removes badge");
        }
        static void IndependentTeamWindow() {
            TeamWindow team;
            using(var launcher=new MainForm(true)) {
                typeof(MainForm).GetMethod("ShowTeamWindow",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(launcher,null);
                team=(TeamWindow)typeof(MainForm).GetField("teamWindow",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(launcher);
                Check(team.Owner==null && !team.TopMost,"Production Team Window has no owner forcing it above Nexus");
                var toggle=All(team).OfType<NexusCheckBox>().Single(c=>c.Name=="teamAlwaysOnTop");
                toggle.Checked=true;Application.DoEvents();Check(team.TopMost,"Always on top enables topmost state");
                toggle.Checked=false;Application.DoEvents();Check(!team.TopMost && team.Owner==null,"Unchecking restores independent normal stacking");
            }
            Check(team.IsDisposed,"Closing Nexus disposes the independent Team Window");
        }
        static void PetBreakdowns(){
            long now=Stopwatch.GetTimestamp();
            var rows=Enumerable.Range(1,2).Select(i=>new ClientObservation {Pid=i,StartTicks=i,Name="Owner "+i,
                Client=new PresetCamping.Client {ProcessId=i,StartTimeUtcTicks=i,CharacterId=i,Server="fixture",Port=1},
                Pulse=new ClientPulse {Pid=i,StartTicks=i,Active=true,PublishedQpc=now,PlayfieldId=42,Profession="Engineer",
                    CombatMeter=new NexusTelemetry.CombatMeterSnapshot {Source="Nexus",Status="Ready",Epoch="local"+i,ObservedQpc=now,ElapsedSeconds=10,
                        Actors=new List<NexusTelemetry.CombatMeterActor>{new NexusTelemetry.CombatMeterActor {CharacterId=i,Damage=i==1?150:200,PetDamage=i==1?100:0}}}}}).ToList();
            var owner=rows[0].Pulse.CombatMeter.Actors[0];
            owner.Pets.Add(new NexusTelemetry.CombatMeterPet {CharacterId=11,Name="Robot",Damage=40,Active=true});
            owner.Pets.Add(new NexusTelemetry.CombatMeterPet {CharacterId=12,Name="Second robot",Damage=60,Active=true});
            var team=new TeamCombatStats();team.Update(rows,now);
            Check(team.Card(rows[0]).Pets.Count==2 && team.Card(rows[0]).Pets[0].Stats.Dpm==240 && team.Total(rows).Damage==350,"Team keeps separate same-name pets, rates and unduplicated totals");
            var observed=new NexusTelemetry.CombatMeterSnapshot {Source="NexusObserved",Status="Ready",Epoch="nearby",ObservedQpc=now,ElapsedSeconds=10,
                Actors=new List<NexusTelemetry.CombatMeterActor>{new NexusTelemetry.CombatMeterActor {CharacterId=1,Name="Owner 1",Damage=50},
                    new NexusTelemetry.CombatMeterActor {CharacterId=2,Name="Owner 2",Damage=200},
                    new NexusTelemetry.CombatMeterActor {CharacterId=11,Name="Robot",UnassignedPet=true,Damage=40,PetDamage=40},
                    new NexusTelemetry.CombatMeterActor {CharacterId=12,Name="Robot",UnassignedPet=true,Damage=60,PetDamage=60}}};
            foreach(var r in rows)r.Pulse.ObservedCombatMeter=observed;
            var all=new ObservedCombatStats();all.Update(rows,now);
            Check(all.Rows().Count==2 && all.Rows()[0].Stats.Pets.Count==2 && all.Rows()[0].Stats.Damage==150 && all.Total().Damage==350 && all.Total().Observed==2,"All uses fresh roster ownership while counting only one observer's damage");
            using(var form=new Form {Width=1150,Height=440,ShowInTaskbar=false})using(var table=new TeamStatsTable(team.Card,team.ResetCard)) {
                form.Controls.Add(table);form.Show();table.UpdateRows(rows,team.Total(rows));Application.DoEvents();
                var grid=All(table).OfType<DataGridView>().Single(g=>g.Name=="teamStatistics");
                Check(grid.Rows.Count==4 && grid.Rows[0].Tag==rows[0] && grid.Rows[3].Tag==rows[1],"Expanded Team shows each pet immediately after its owner");
                grid.Sort(grid.Columns["DMG"],System.ComponentModel.ListSortDirection.Descending);
                Check(grid.Rows[0].Tag==rows[1] && grid.Rows[1].Tag==rows[0] && grid.Rows[2].Cells[0].Value.ToString().Trim()=="↳ Robot","Descending damage sorting keeps named pets under owner without visible IDs");
                grid.Sort(grid.Columns["DMG"],System.ComponentModel.ListSortDirection.Ascending);
                Check(grid.Rows[0].Tag==rows[0] && grid.Rows[3].Tag==rows[1],"Ascending sorting also keeps children after their owner");
                typeof(TeamStatsTable).GetMethod("TogglePets",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(table,new object[]{rows[0]});
                Check(grid.Rows.Count==2,"Owner disclosure collapses pet rows");
                table.UpdateRows(rows,team.Total(rows));Check(grid.Rows.Count==2,"Live refresh preserves collapsed state");
                typeof(TeamStatsTable).GetMethod("TogglePets",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(table,new object[]{rows[0]});
                grid.CurrentCell=grid.Rows[1].Cells[0];Application.DoEvents();
                Check(!All(table).OfType<Button>().Single(b=>b.Name=="resetCardStats").Enabled,"Pet breakdown cannot accidentally reset an owner's totals");
                table.UpdateObservedRows(all.Rows(),all.Total());Check(grid.Rows.Count==4,"All presents the same owner/pet hierarchy");
                Directory.CreateDirectory("logs/team-previews");using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save("logs/team-previews/individual-pets.png");}
            }
            all.ResetRow(all.Rows().First(r=>r.CharacterId==1));
            Check(all.Rows().First(r=>r.CharacterId==1).Stats.Damage==0 && all.Rows().First(r=>r.CharacterId==1).Stats.Pets.All(p=>p.Stats.Damage==0) && all.Total().Damage==350,"Resetting All owner resets its pet breakdown but not grand totals");
            team.ResetCard(rows[0]);owner.Pets[0].Damage+=5;owner.PetDamage+=5;owner.Damage+=5;rows[0].Pulse.CombatMeter.ElapsedSeconds=12;
            Check(team.Card(rows[0]).Pets[0].Stats.Damage==5 && team.Card(rows[0]).Pets[0].Stats.Dpm==150,"Team pet reset baseline is independent and rates use reset elapsed time");
            var cross=new ObservedCombatStats();cross.SelectSource(rows[1]);rows[0].Client.Server="other";cross.Update(rows,now);
            Check(cross.Rows().Count==4 && cross.Rows().Count(r=>r.Name.Contains("owner unknown"))==2,"Pet ownership never crosses servers");
            rows[0].Client.Server="fixture";rows[0].Pulse.PlayfieldId=43;cross.SelectSource(rows[1]);cross.Update(rows,now);
            Check(cross.Rows().Count==4,"Pet ownership never crosses playfields");
            var invalid=TeamCombatStats.Copy(owner);invalid.Pets.Add(TeamCombatStats.Copy(owner).Pets[0]);
            var invalidSnapshot=new NexusTelemetry.CombatMeterSnapshot {Status="Ready",Epoch="bad",Actors=new List<NexusTelemetry.CombatMeterActor>{invalid}};
            Check(!TeamCombatStats.Valid(invalidSnapshot),"Duplicate nested pet IDs are rejected");
            rows[0].Pulse.PlayfieldId=42;
            owner.Pets[0].Name="beeep";owner.Pets[1].Name="beeep";
            var freshTeam=new TeamCombatStats();freshTeam.Update(rows,now);
            var grouped=TeamStatsTable.PetRows(freshTeam.Card(rows[0])).ToList();
            Check(grouped.Count==1 && grouped[0].Name=="beeep" && grouped[0].Stats.Damage==105 && grouped[0].Stats.Dpm==525,"Resummoned pets combine by owner and name with correct rates");
            all.Update(rows,now);
            Check(all.Rows().First(r=>r.CharacterId==1).Stats.Pets.All(p=>p.Name=="beeep"),"All refreshes renamed pets from live owner roster");
            var empty=new NexusTelemetry.CombatMeterSnapshot {Source="NexusObserved",Status="Ready",Epoch="empty",ObservedQpc=now,ElapsedSeconds=10};
            rows[1].Pulse.ObservedCombatMeter=empty;var zero=new ObservedCombatStats();zero.SelectSource(rows[1]);zero.Update(rows,now);
            Check(zero.Rows().Count==1 && TeamStatsTable.PetRows(zero.Rows()[0].Stats).Count()==1 && zero.Total().Damage==0,"All shows known named pets before combat without importing local damage");
            var automatic=new ObservedCombatStats();automatic.Update(rows,now);rows[0].ExitedUtc=DateTime.UtcNow;automatic.Update(rows,now);
            Check(automatic.Source==rows[1] && automatic.Ready,"Automatic All observer recovers after its previous client exits");
        }
        static void ObservedCounters(){
            long now=Stopwatch.GetTimestamp();
            var data=new NexusTelemetry.CombatMeterSnapshot {Source="NexusObserved",Status="Ready",Epoch="nearby1",ObservedQpc=now,ElapsedSeconds=10,
                Actors=new List<NexusTelemetry.CombatMeterActor>{new NexusTelemetry.CombatMeterActor {CharacterId=40,Name="Visitor",Profession="Doctor",Damage=100,NanoDamage=30,PetDamage=20},
                    new NexusTelemetry.CombatMeterActor {CharacterId=41,Name="Unknown pet",UnassignedPet=true,Damage=50,PetDamage=50}}};
            var rows=Enumerable.Range(1,2).Select(i=>new ClientObservation {Pid=i,StartTicks=i,Name="Observer "+i,
                Client=new PresetCamping.Client {ProcessId=i,StartTimeUtcTicks=i,CharacterId=i,Server="fixture",Port=1},
                Pulse=new ClientPulse {Pid=i,StartTicks=i,Active=true,PublishedQpc=now,ObservedCombatMeter=data}}).ToList();
            var meter=new ObservedCombatStats();meter.Update(rows,now);
            Check(meter.Source==rows[0] && meter.Rows().Count==2 && meter.Total().Damage==150 && meter.Total().PetDamage==70,"All includes outside-team actors, aggregates pets and never combines duplicate observer feeds");
            var visitor=meter.Rows().Single(r=>r.CharacterId==40);
            Check(!visitor.Stats.Native && meter.Rows().Single(r=>r.CharacterId==41).Name.Contains("owner unknown"),"Other players have no XP claim and unassigned pets are labelled");
            meter.ResetRow(visitor);Check(meter.Rows().Single(r=>r.CharacterId==40).Stats.Damage==0 && meter.Total().Damage==150,"Observed row resets do not reset all totals");
            data.ElapsedSeconds=20;data.Actors[0].Damage=200;
            Check(meter.Rows().Single(r=>r.CharacterId==40).Stats.Dpm==600,"Observed rates use the selected row's elapsed period");
            meter.ResetAll();data.ElapsedSeconds=25;data.Actors[0].Damage=220;
            Check(meter.Total().Damage==20 && meter.Total().Dpm==240,"All totals have an independent reset period");
            rows[0].Pulse.Zoning=true;meter.Update(rows,now);
            Check(!meter.Ready && meter.Rows().Count==0 && meter.Source==rows[0],"Zoning observer is unavailable rather than silently replaced by another feed");
            meter.SelectSource(rows[1]);meter.Update(rows,now);
            Check(meter.Total().Damage==270 && meter.Rows().Single(r=>r.CharacterId==40).Stats.Damage==220,"Explicit source change clears old baselines instead of merging observer histories");
            meter.ResetRow(visitor);Check(meter.Rows().Single(r=>r.CharacterId==40).Stats.Damage==220,"A stale row from another observer cannot reset the new source");
            meter.Update(rows,now+4*Stopwatch.Frequency);Check(!meter.Ready && meter.Total()==null,"Stale nearby feed hides live counters");
            meter.Update(rows,now);meter.ResetAll();data.Epoch="nearby2";data.ElapsedSeconds=2;data.Actors[0].Damage=5;data.Actors[0].NanoDamage=0;data.Actors[0].PetDamage=0;
            meter.Update(rows,now);Check(meter.Total().Damage==55,"Zoning epoch resets obsolete nearby baselines");
            rows[1].Client.Server="another-world";meter.Update(rows,now);Check(!meter.Ready,"Observer identity includes the server; character ID alone cannot cross worlds");
        }
        static void CombatCounters(){
            long now=Stopwatch.GetTimestamp();
            var data=new NexusTelemetry.CombatMeterSnapshot {Status="Ready",Epoch="session1",ObservedQpc=now,ElapsedSeconds=10,
                Actors=new List<NexusTelemetry.CombatMeterActor>{new NexusTelemetry.CombatMeterActor {CharacterId=1,Damage=100,Healing=50,NanoDamage=40,PetDamage=20},new NexusTelemetry.CombatMeterActor {CharacterId=2,Damage=200,Healing=10,NanoDamage=30,PetDamage=0}}};
            var rows=Enumerable.Range(1,2).Select(i=>new ClientObservation {Pid=i,StartTicks=i,Name="Meter fixture "+i,
                Client=new PresetCamping.Client {ProcessId=i,StartTimeUtcTicks=i,CharacterId=i,Server="fixture",Port=1},
                Pulse=new ClientPulse {Pid=i,StartTicks=i,Active=true,PublishedQpc=now,CombatMeter=data}}).ToList();
            var meter=new TeamCombatStats();meter.Update(rows,now);
            Check(meter.Total(rows).Damage==300 && meter.Total(rows).Dps==30,"One observer counts team damage once, even when both clients publish it");
            Check(meter.Total(rows).NanoDamage==70 && meter.Total(rows).PetDamage==20 && meter.Total(rows).Damage==300,"Nano and pet breakdowns are not added to total damage a second time");
            meter.ResetCard(rows[0]);
            Check(meter.Card(rows[0]).Damage==0 && meter.Card(rows[1]).Damage==200 && meter.Total(rows).Damage==300,"Card reset is independent of other cards and team totals");
            Check(!meter.Card(rows[0]).Dps.HasValue,"Zero-length reporting period is not infinite DPS");
            data.ElapsedSeconds=20;data.Actors[0].Damage=150;data.Actors[1].Damage=250;meter.Update(rows,now);
            Check(meter.Card(rows[0]).Damage==50 && meter.Card(rows[0]).Dps==5 && meter.Total(rows).Damage==400,"Card baseline is a copy and rates use that card's reporting period");
            meter.ResetTeam();
            Check(meter.Total(rows).Damage==0 && meter.Card(rows[0]).Damage==50,"Team reset preserves card reporting periods");
            data.ElapsedSeconds=25;data.Actors[0].Damage=160;data.Actors[1].Damage=260;
            Check(meter.Total(rows).Damage==20 && meter.Total(rows).Dps==4,"Team DPS uses a common team reporting period");
            rows[1].Client.Server="other world";
            Check(meter.Card(rows[1])==null && meter.Total(rows).Observed==1 && meter.Total(rows).Requested==2,"Same character ID on another server cannot receive source statistics");
            rows[1].Client.Server="fixture";
            meter.Update(rows,now+4*Stopwatch.Frequency);
            Check(!meter.Ready && meter.Total(rows)==null,"Stale observer does not silently switch sources or show live totals");
            data.Epoch="session2";meter.Update(rows,now);
            Check(meter.Card(rows[0]).Damage==160 && meter.Total(rows).Damage==420,"Underlying meter reset clears obsolete Nexus baselines");
            data.Actors.Add(data.Actors[0]);
            Check(!TeamCombatStats.Valid(data),"Duplicate character IDs reject the snapshot");data.Actors.RemoveAt(2);
            data.Actors[0].Damage=-1;Check(!TeamCombatStats.Valid(data),"Overflowed or negative counters are unavailable, never negative damage");
        }
        static void LogoutActions(){
            Localization.Texts.SetLanguage("en");
            var settings=new LauncherSettings();
            var rows=new List<ClientObservation>();
            var requests=new List<EventWaitHandle>();
            try {
                for(int i=0;i<3;i++) {
                    var client=new PresetCamping.Client {ProcessId=800001+i,StartTimeUtcTicks=123+i,CharacterId=101+i,AccountIdentity="id:fixture",CharacterName="Logout fixture "+i,CampToken=Guid.NewGuid().ToString("N")};
                    requests.Add(new EventWaitHandle(false,EventResetMode.AutoReset,PresetCamping.EventName(client,"request")));
                    rows.Add(new ClientObservation {Name=client.CharacterName,Pid=client.ProcessId,StartTicks=client.StartTimeUtcTicks,Client=client});
                }
                var preset=new LaunchPreset {Name="Two clients",Characters=new List<PresetCharacter>{new PresetCharacter {AccountId="fixture",CharacterId=101},new PresetCharacter {AccountId="fixture",CharacterId=102}}};
                settings.Presets.Add(preset);
                List<PresetCamping.Client> selected=null;
                int calls=0,relogs=0;bool available=true;
                var pending=new TaskCompletionSource<bool>();
                var relogPending=new TaskCompletionSource<bool>();
                using(var window=new TeamWindow(()=>rows,()=>settings,null, clients=>{calls++;selected=clients;return pending.Task;},()=>available,
                    clients=>{relogs++;selected=clients;return relogPending.Task;})) {
                    window.ShowInTaskbar=false;window.StartPosition=FormStartPosition.Manual;window.Location=new Point(-18000,-18000);window.Show();Application.DoEvents();window.RefreshWall();
                    var buttons=All(window).OfType<Button>().Where(b=>b.Name=="clientLogout").ToArray();
                    Check(buttons.Length==3 && buttons.All(b=>b.Enabled),"Each connected client has a logout button");
                    buttons[0].PerformClick();
                    Check(calls==1 && selected.Count==1 && selected[0].ProcessId==rows[0].Pid,"Individual logout snapshots only its own client");
                    var all=All(window).OfType<Button>().Single(b=>b.Name=="teamLogoutAll");
                    Check(!all.Enabled && buttons.All(b=>!b.Enabled),"Logout buttons disable while a request is pending");
                    all.PerformClick();window.LogOut(rows).GetAwaiter().GetResult();
                    Check(calls==1,"Repeated clicks cannot submit duplicate logout requests");
                    pending.SetResult(true);Application.DoEvents();window.RefreshWall();
                    Check(all.Enabled,"Buttons recover after logout finishes or is cancelled");
                    All(window).OfType<NexusComboBox>().Single(c=>c.Name=="teamFilter").SelectedItem=preset;window.RefreshWall();
                    all.PerformClick();Application.DoEvents();
                    Check(calls==2 && selected.Count==2 && selected.All(c=>c.CharacterId==101 || c.CharacterId==102),"Log out all applies only to the selected team view");
                    var relogAll=All(window).OfType<Button>().Single(b=>b.Name=="teamRelogAll");
                    var relogButtons=All(window).OfType<Button>().Where(b=>b.Name=="clientRelog").ToArray();
                    Check(relogButtons.Length==2 && relogButtons.All(b=>b.Enabled),"Each filtered client has a Relog button");
                    relogButtons[0].PerformClick();
                    Check(relogs==1 && selected.Count==1 && selected[0].ProcessId==rows[0].Pid,"Individual Relog submits only that card's client");
                    Check(!all.Enabled && !relogAll.Enabled && relogButtons.All(b=>!b.Enabled),"Relog blocks duplicate relog and logout actions while pending");
                    relogAll.PerformClick();window.LogOut(rows,true).GetAwaiter().GetResult();Check(relogs==1,"Repeated Relog cannot enqueue duplicate restarts");
                    relogPending.SetResult(true);Application.DoEvents();window.RefreshWall();relogAll.PerformClick();Application.DoEvents();
                    Check(relogs==2 && selected.Count==2 && selected.All(c=>c.CharacterId==101 || c.CharacterId==102),"Relog all snapshots only the selected team");
                    var closingCard=All(window).OfType<TeamCard>().Single(c=>c.Observation==rows[0]);
                    rows[0].ExitedUtc=DateTime.UtcNow;rows[1].Client.CampToken=null;window.RefreshWall();
                    Check(closingCard.IsDisposed && All(window).OfType<TeamCard>().Count()==1,"Closed client card is removed and disposed on the first refresh, without a grace period");
                    Check(!all.Enabled && !relogAll.Enabled,"Exited and unsupported clients cannot be logged out or relogged");
                    All(window).OfType<NexusComboBox>().Single(c=>c.Name=="teamFilter").SelectedIndex=0;available=false;window.RefreshWall();
                    Check(!all.Enabled && All(window).OfType<Button>().Where(b=>b.Name=="clientLogout").All(b=>!b.Enabled),"Launcher busy state disables card and bulk actions");
                    Check(requests.All(e=>!e.WaitOne(0)),"UI fixture never signals a real or synthetic quit event");
                }
            } finally {foreach(var request in requests)request.Dispose();}
        }
        static void RelogTargets(){
            var clients=Enumerable.Range(1,3).Select(i=>new PresetCamping.Client {AccountIdentity="id:fixture",CharacterId=i,ProcessId=100+i,StartTimeUtcTicks=i,Server="fixture",Port=1,GamePath=@"C:\fixture"}).ToList();
            var replacement=new PresetCamping.Client {AccountIdentity="id:fixture",CharacterId=2,ProcessId=999,StartTimeUtcTicks=999,Server="fixture",Port=1,GamePath=@"C:\fixture"};
            Check(MainForm.TeamRelogTargets(clients,c=>false,new PresetCamping.Client[0]).Count==0,"Interrupted logout never restarts a still-running client");
            var restart=MainForm.TeamRelogTargets(clients,c=>c.CharacterId!=3,new[]{replacement});
            Check(restart.Count==1 && restart[0]==clients[0],"Partial logout restarts only confirmed exits, excluding a manually restarted character");
            Check(MainForm.TeamRelogTargets(clients,c=>true,new PresetCamping.Client[0]).SequenceEqual(clients),"Completed team logout preserves the exact selected clients");
            var target=clients[0];
            Check(ClientRecovery.Matches(target,target.ProcessId,target.StartTimeUtcTicks,@"C:\fixture\AnarchyOnline.exe"),"Recovery accepts the exact process lifetime and game executable");
            Check(!ClientRecovery.Matches(target,target.ProcessId,target.StartTimeUtcTicks+1,@"C:\fixture\AnarchyOnline.exe"),"Recovery rejects a reused PID");
            Check(!ClientRecovery.Matches(target,target.ProcessId,target.StartTimeUtcTicks,@"C:\other\AnarchyOnline.exe"),"Recovery rejects another installation");
            Check(!ClientRecovery.Matches(target,target.ProcessId+1,target.StartTimeUtcTicks,@"C:\fixture\AnarchyOnline.exe"),"Recovery cannot target a different client");
            Check(ClientRecovery.WindowResponding(IntPtr.Zero,target.ProcessId)==null,"A missing window is unknown, not frozen");
            var observation=new ClientObservation {Pid=target.ProcessId,StartTicks=target.StartTimeUtcTicks,Client=target,Name="Fixture",Sample=new ClientSample {Utc=DateTime.UtcNow,WindowResponding=false,WindowUnresponsiveSamples=1}};
            Check(TeamPresentation.Status(observation)!="CLIENT FROZEN","A single slow window probe does not announce a frozen client");
            observation.Sample.WindowUnresponsiveSamples=3;
            Check(TeamPresentation.Status(observation)=="CLIENT FROZEN","Repeated failed Windows window probes identify a frozen client even without AOSharp");
            int recovered=0;bool restarted=false;var pending=new TaskCompletionSource<bool>();
            using(var window=new TeamWindow(()=>new List<ClientObservation>{observation},()=>new LauncherSettings(),null,null,()=>true,null,(client,restartRequested)=>{Check(client==target,"Recovery captures only the requested client");recovered++;restarted=restartRequested;return pending.Task;})) {
                window.RefreshWall();Check(window.CanRecover(observation),"Recovery stays available without a working logout channel");
                var operation=window.Recover(observation,true);
                Check(recovered==1 && restarted && !window.CanRecover(observation),"Force restart request locks duplicate actions while pending");
                window.Recover(observation,false).GetAwaiter().GetResult();Check(recovered==1,"Repeated recovery cannot enqueue a second termination");
                pending.SetResult(true);Application.DoEvents();operation.GetAwaiter().GetResult();Check(window.CanRecover(observation),"Recovery controls return after cancellation or completion");
                observation.ExitedUtc=DateTime.UtcNow;Check(!window.CanRecover(observation),"Exited clients cannot be force closed");
            }
        }
        static void Recording(){
            string root=Path.Combine(Path.GetTempPath(),"NexusRecordingFixture-"+Guid.NewGuid().ToString("N"));
            var store=new DevelopmentRecordingStore(root,800);
            try {
                for(int i=0;i<30;i++)store.Append(new {Sequence=i,Payload=new string('x',90)});
                var records=store.Read();Check(records.Count==3 && Directory.GetFiles(root).All(f=>new FileInfo(f).Length<=800),"Recorder rotates within three bounded files");
                var serializer=new System.Web.Script.Serialization.JavaScriptSerializer();
                var sequences=records.Values.SelectMany(text=>text.Split(new[]{'\n'},StringSplitOptions.RemoveEmptyEntries)).Select(line=>serializer.Deserialize<Dictionary<string,object>>(line)).Select(item=>Convert.ToInt32(item["Sequence"])).ToArray();
                Check(sequences.Last()==29 && sequences.First()>0 && sequences.SequenceEqual(sequences.OrderBy(i=>i)),"Recorder retains complete JSON records in time order and drops oldest data");
                var sample=new ClientObservation {Name="PRIVATE-NAME",Client=new PresetCamping.Client {Username="SECRET-LOGIN",CampToken="SECRET-TOKEN"},Pulse=new ClientPulse {FightingTarget="PRIVATE-CHAT",CastNanoName="PRIVATE-NANO"},Sample=new ClientSample {Utc=DateTime.UtcNow,WorkingSetBytes=123,WindowResponding=false}};
                string json=serializer.Serialize(DevelopmentRecorder.Sample(sample));
                Check(!json.Contains("PRIVATE") && !json.Contains("SECRET") && json.Contains("123"),"Recorder excludes names, login details, capability tokens and free game text while retaining measurements");
                store.Append(new {Payload=new string('x',2000)});Check(store.Read().Values.SequenceEqual(records.Values),"Oversized records cannot exceed the storage bound");
            } finally {if(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("NexusRecordingFixture-"))Directory.Delete(root,true);}
        }
        static void NativeCounters(){
            long now=Stopwatch.GetTimestamp();
            var rows=Enumerable.Range(1,2).Select(i=>new ClientObservation {Pid=i,StartTicks=i,Name="Native fixture "+i,
                Client=new PresetCamping.Client {ProcessId=i,StartTimeUtcTicks=i,CharacterId=i,Server="fixture",Port=1},
                Pulse=new ClientPulse {Pid=i,StartTicks=i,Active=true,PublishedQpc=now,CombatMeter=new NexusTelemetry.CombatMeterSnapshot {
                    Source="Nexus",Status="Ready",Epoch="native"+i,ObservedQpc=now,ElapsedSeconds=10*i,Actors=new List<NexusTelemetry.CombatMeterActor> {
                        new NexusTelemetry.CombatMeterActor {CharacterId=i,Damage=100*i,Healing=10*i,NanoDamage=30*i,PetDamage=20*i,Xp=123*i,Axp=10*i,ExperienceEvents=2}}}}}).ToList();
            var meter=new TeamCombatStats();meter.Update(rows,now);
            Check(meter.Native && meter.Total(rows).Damage==300 && meter.Total(rows).Dps==20,"Each client's own counters are summed once; rates respect different session start times");
            Check(meter.Card(rows[0]).PetDps==2 && meter.Total(rows).PetDps==4 && meter.Total(rows).PetDpm==240 && meter.Total(rows).PetDph==14400,
                "Pet rates use each client's period before summing, without double-counting pet damage");
            Check(meter.Total(rows).Xp==369 && meter.Total(rows).Axp==30 && meter.Total(rows).XpHour==88560,"Independent XP/AXP per client and summed hourly rates");
            Check(meter.Total(rows.Concat(new[]{rows[0]})).Damage==300,"Duplicate client rows cannot duplicate team damage");
            meter.ResetCard(rows[0]);Check(meter.Card(rows[0]).Xp==0 && meter.Card(rows[0]).Damage==0 && meter.Total(rows).Xp==369,"Card reset includes XP while preserving team totals");
            meter.ResetTeam(rows);Check(meter.Total(rows).Damage==0 && meter.Total(rows).Xp==0 && meter.Card(rows[1]).Damage==200,"Team reset includes XP independently of cards");
            Check(meter.Total(rows).PetDamage==0 && meter.Total(rows).PetDps==0 && meter.Card(rows[1]).PetDps==2,"Team reset clears pet rates independently of cards");
            rows[0].Pulse.CombatMeter.ElapsedSeconds+=10;rows[0].Pulse.CombatMeter.Actors[0].Damage+=50;rows[0].Pulse.CombatMeter.Actors[0].Xp+=70;
            Check(meter.Total(rows).Damage==50 && meter.Total(rows).Xp==70 && meter.Total(rows).Dps==5,"Post-reset gains and rates use independent team baselines");
            rows[1].Pulse.PublishedQpc=now-4*Stopwatch.Frequency;meter.Update(rows,now);
            Check(meter.Total(rows).Observed==1 && meter.Total(rows).Requested==2 && meter.Card(rows[1])==null,"Stale client creates explicitly partial totals");
            meter.ResetTeam(rows);rows[1].Pulse.PublishedQpc=now;rows[1].Pulse.CombatMeter.Actors[0].Damage=500;meter.Update(rows,now);
            Check(meter.Total(rows).Damage==0,"Client missing during reset starts from its next fresh sample");
            rows[0].Pulse.CombatMeter.Epoch="reloaded";rows[0].Pulse.CombatMeter.Actors[0].Damage=0;rows[0].Pulse.CombatMeter.Actors[0].NanoDamage=0;rows[0].Pulse.CombatMeter.Actors[0].PetDamage=0;
            meter.Update(rows,now);Check(meter.Card(rows[0]).Damage==0,"Monitor reload starts a new counter epoch without negative deltas");
            rows[0].Pulse.CombatMeter.Actors[0].CharacterId=99;meter.Update(rows,now);Check(meter.Card(rows[0])==null,"Native snapshot cannot claim another character's statistics");
        }
        static void MonitoringReasons(){
            Localization.Texts.SetLanguage("en");
            long now=Stopwatch.GetTimestamp(), first=now-20*Stopwatch.Frequency;
            var pulse=new ClientPulse {Active=true,PublishedQpc=now,LastUpdateQpc=now,RecentMaxMs=104.7};
            var ping=new ClientPing {PublishedQpc=now,LastReplyQpc=now,LastMs=15.5};
            string reason;
            Check(ClientMonitor.Assess(pulse,ping,now,first,out reason)==ClientCondition.Delayed && reason.Contains("last 10 seconds"),"104.7 ms log spike is historical, not a current stall");
            pulse.RecentMaxMs=538.6;
            Check(ClientMonitor.Assess(pulse,ping,now,first,out reason)==ClientCondition.Delayed && reason.Contains("current updates and IPC are responding"),"Recovered half-second spike is identified as recent history");
            pulse.RecentMaxMs=0;
            Check(ClientMonitor.Assess(pulse,ping,now,first,out reason)==ClientCondition.Healthy && reason==null,"Spike expiry clears the delayed state");
            pulse.LastUpdateQpc=now-Stopwatch.Frequency;
            Check(ClientMonitor.Assess(pulse,ping,now,first,out reason)==ClientCondition.Delayed && reason.Contains("stalled at snapshot"),"Current one-second stall has a separate reason");
            pulse.LastUpdateQpc=now-2*Stopwatch.Frequency;
            Check(ClientMonitor.Assess(pulse,ping,now,first,out reason)==ClientCondition.NoResponse,"Two-second frozen update remains a no-response alert");
            pulse.LastUpdateQpc=now;pulse.RecentMaxMs=150;ping.LastMs=120;
            Check(ClientMonitor.Assess(pulse,ping,now,first,out reason)==ClientCondition.Delayed && reason=="Slow IPC round trip","Slow IPC takes precedence over a historical game spike");
            Check(ClientMonitor.Assess(pulse,null,now,now,out reason)==ClientCondition.Waiting && reason.Contains("first IPC"),"Startup wait is not described as a failed helper");
            Check(ClientMonitor.Assess(pulse,null,now,first,out reason)==ClientCondition.MeasurementUnavailable && reason.Contains("unavailable"),"Missing helper after grace is still reported");
            string detail=ClientMonitor.ChangeDetail(new ClientSample {Reason=reason,PulseAgeMs=159,ObservedUpdateGapMs=12,RecentMaxGapMs=21.2});
            Check(detail.Contains("helper unavailable") && detail.Contains("RTT unavailable") && detail.Contains("timeouts unavailable") && !detail.Contains("  ms"),"Missing measurements are explicit, not empty numeric fields");
        }
        static void StartupAndMemory() {
            long now=Stopwatch.GetTimestamp();
            var pulse=new ClientPulse {Version=2,Pid=10,StartTicks=20,Instance="fresh",Active=true,Updates=2,PublishedQpc=now,LastUpdateQpc=now,VitalsQpc=now};
            Check(PluginAutoInjector.StartupReady(pulse,10,20,now-1,now),"Startup requires fresh game updates and vitals");
            Check(!PluginAutoInjector.StartupReady(null,10,20,0,now),"Missing startup snapshot stays pending");
            Check(!PluginAutoInjector.StartupReady(pulse,11,20,0,now),"Another process cannot release the startup gate");
            Check(!PluginAutoInjector.StartupReady(pulse,10,21,0,now),"Reused PID cannot release the startup gate");
            Check(!PluginAutoInjector.StartupReady(pulse,10,20,now+1,now),"Earlier monitor instance cannot release a new startup check");
            Check(!PluginAutoInjector.StartupReady(pulse,10,20,0,now+3*Stopwatch.Frequency),"Stale startup snapshots stay pending");
            pulse.LastUpdateQpc=now-3*Stopwatch.Frequency;
            Check(!PluginAutoInjector.StartupReady(pulse,10,20,0,now),"A fresh writer heartbeat cannot hide a stopped game update loop");pulse.LastUpdateQpc=now;
            pulse.VitalsQpc=now-3*Stopwatch.Frequency;
            Check(!PluginAutoInjector.StartupReady(pulse,10,20,0,now),"Old vitals cannot release the startup gate");pulse.VitalsQpc=now;
            pulse.Zoning=true;Check(!PluginAutoInjector.StartupReady(pulse,10,20,0,now),"Zoning cannot start profession plugins");pulse.Zoning=false;
            pulse.Updates=0;Check(!PluginAutoInjector.StartupReady(pulse,10,20,0,now),"Writer-only heartbeat is not game readiness");
            Check(PluginAutoInjector.ReadStartupPulse(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".missing"))==null,"Missing file does not abort the startup/retry poll");
            const long mb=1024*1024;
            Check(ClientAddressSpace.Warning(null,null)==0,"Unknown memory is not treated as exhaustion");
            Check(ClientAddressSpace.Warning(1000*mb,100*mb)==0 && ClientAddressSpace.Warning(511*mb,100*mb)==1 && ClientAddressSpace.Warning(255*mb,100*mb)==2,"Free address-space warning thresholds");
            Check(ClientAddressSpace.Warning(2000*mb,31*mb)==2,"Fragmentation warns even with plenty of total free space");
            Check(ClientAddressSpace.CpuPercent(100,500,Stopwatch.Frequency,4)==10,"Client CPU uses elapsed time and total processor capacity");
            Check(ClientAddressSpace.CpuPercent(null,500,Stopwatch.Frequency,4)==null,"First CPU sample remains unavailable");
            using(var process=Process.GetCurrentProcess()) {
                var memory=ClientAddressSpace.Read(process.Handle);
                Check(!Environment.Is64BitOperatingSystem || memory!=null && memory.Free>0 && memory.Largest<=memory.Free && memory.Total<=4294967296L,"Read-only address map of actual 32-bit fixture process");
            }
        }
        [STAThread] static int Main(){try{return Run();}catch(Exception e){Console.Error.WriteLine(e);return 1;}}

        static void SavedControlPanelChoices() {
            var json=new System.Web.Script.Serialization.JavaScriptSerializer();
            var settings=json.Deserialize<LauncherSettings>("{}"); settings.ExperimentalNearbyCombat=true;
            Check(settings.ControlPanel.LivePreviews && !settings.ControlPanel.AlwaysOnTop,"Older profiles retain sensible Control Panel defaults");
            var character=new ClientObservation {Pid=901,StartTicks=100,Name="Fixture",
                Client=new PresetCamping.Client {AccountIdentity="id:test",CharacterId=12,Server="fixture",Port=7500}};
            var rows=new List<ClientObservation>{character};
            settings.Presets.Add(new LaunchPreset {Id="fixture-team",Name="Fixture team"});
            int saves=0;string saved=null;
            Action persist=()=>{saves++;saved=json.Serialize(settings);};
            using(var panel=new TeamWindow(()=>rows,()=>settings,saveOrder:persist)) {
                panel.ShowInTaskbar=false;panel.StartPosition=FormStartPosition.Manual;panel.Location=new Point(-18000,-18000);
                panel.Show();Application.DoEvents();panel.RefreshWall();
                Check(saves==0,"Opening and refreshing Control Panel does not write defaults to disk");
                var controls=All(panel).ToList();
                controls.OfType<CheckBox>().Single(c=>c.Name=="teamLivePreviews").Checked=false;
                controls.OfType<CheckBox>().Single(c=>c.Name=="teamAlwaysOnTop").Checked=true;
                var tabs=controls.OfType<TabControl>().Single(c=>c.Name=="teamDamageTabs");
                var metric=controls.OfType<ComboBox>().Single(c=>c.Name=="statisticsMetric");
                var grid=controls.OfType<DataGridView>().Single(c=>c.Name=="teamStatistics");
                metric.SelectedIndex=1;grid.Columns["DMG"].Width=187;grid.Sort(grid.Columns["DMGm"],System.ComponentModel.ListSortDirection.Descending);
                tabs.SelectedIndex=1;metric.SelectedIndex=4;
                tabs.SelectedIndex=2;metric.SelectedIndex=2;
                tabs.SelectedIndex=0;
                Check(metric.SelectedIndex==1 && grid.Columns["DMG"].Width==187 && grid.SortedColumn.Name=="DMGm" && grid.SortOrder==SortOrder.Descending,"Each tab restores its metric, resized columns and sort");
                var source=controls.OfType<ComboBox>().Single(c=>c.Name=="combatMeterSource");source.SelectedIndex=1;
                string identity=settings.ControlPanel.ObserverKey;
                rows.Clear();panel.RefreshWall();
                Check(source.SelectedItem.ToString().Contains("offline") && settings.ControlPanel.ObserverKey==identity,"Offline observer preference stays selected instead of switching to automatic");
                character.Pid=902;character.StartTicks=200;rows.Add(character);panel.RefreshWall();
                Check(source.SelectedItem.ToString()=="Fixture" && settings.ControlPanel.ObserverKey==identity,"Observer reattaches after relog using stable character identity");
                controls.OfType<ComboBox>().Single(c=>c.Name=="teamFilter").SelectedIndex=1;
                var sections=controls.OfType<ControlPanelSections>().Single();
                sections.ResizeSection("damage",240);
                sections.Toggle("damage");sections.MoveSection("timers",-2);sections.Toggle("timers");
                tabs.SelectedIndex=2;
                sections.Toggle("team");sections.Toggle("timers");
                Application.DoEvents();
                Check(sections.Controls.Cast<Control>().All(c=>c.Height==34),"Every collapsed section is only a header bar");
                Check(!controls.OfType<TeamCard>().Any(c=>c.Visible) && !grid.Visible,"Collapsed cards and statistics are hidden without deleting their state");
                Check(sections.Controls.Cast<Control>().Max(c=>c.Bottom)-sections.Controls.Cast<Control>().Min(c=>c.Top)==114,"Three collapsed headers have no empty content gaps");
                using(var image=new Bitmap(panel.Width,panel.Height)){panel.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save("logs/team-previews/sections-collapsed.png");}
                sections.Toggle("timers");
                Check(controls.OfType<TimersPanel>().Single().Visible,"Timers expands inline in the Control Panel");
                using(var image=new Bitmap(panel.Width,panel.Height)){panel.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save("logs/team-previews/sections-timers.png");}
                sections.MoveSection("timers",-99);
                Check(sections.Snapshot()[0].Id=="timers","Moving first section upward is a no-op");
                panel.Close();
            }
            Check(saves==1 && saved!=null,"Closing immediately flushes debounced preference changes");
            settings=json.Deserialize<LauncherSettings>(saved);
            Check(!settings.ControlPanel.LivePreviews && settings.ControlPanel.AlwaysOnTop && settings.ControlPanel.Tab==2 && settings.ControlPanel.TeamId=="fixture-team","Checkboxes, tab and filter survive settings serialization");
            Check(settings.ControlPanel.Sections[0].Id=="timers" && !settings.ControlPanel.Sections[0].Collapsed && settings.ControlPanel.Sections[1].Id=="damage" && settings.ControlPanel.Sections[1].Collapsed && settings.ControlPanel.Sections[1].Height==240,"Section order, collapse and resized height survive close");
            using(var panel=new TeamWindow(()=>rows,()=>settings,saveOrder:persist)) {
                panel.RefreshWall();var controls=All(panel).ToList();
                Check(controls.OfType<ControlPanelSections>().Single().Snapshot()[0].Id=="timers" && controls.OfType<ControlPanelSections>().Single().Snapshot()[1].Collapsed,"Reopened panel restores section layout");
                Check(!controls.OfType<CheckBox>().Single(c=>c.Name=="teamLivePreviews").Checked && panel.TopMost,"New Control Panel restores both checkboxes");
                Check(controls.OfType<TabControl>().Single().SelectedIndex==2 && controls.OfType<ComboBox>().Single(c=>c.Name=="statisticsMetric").SelectedIndex==2,"New Control Panel restores NPC metric and active tab");
                Check(controls.OfType<ComboBox>().Single(c=>c.Name=="teamFilter").SelectedItem is LaunchPreset,"New Control Panel restores team filter");
                Check(controls.OfType<ComboBox>().Single(c=>c.Name=="combatMeterSource").SelectedItem.ToString()=="Fixture","New Control Panel restores chosen observer");
                int before=saves;for(int i=0;i<5;i++)panel.RefreshWall();Check(before==saves,"Polling unchanged data causes no settings writes");
            }
            var preferences=new ControlPanelPreferences();
            var owner=new ObservedCombatRow {Key="old-session",Name="Owner",Stats=new CombatStats {Pets=new List<PetCombatStats>{new PetCombatStats{Name="Pet",Stats=new CombatStats{Damage=12}}}}};
            using(var table=new TeamStatsTable(r=>null,r=>{})) {
                table.UsePreferences(preferences,1,()=>{});
                table.UpdateObservedRows(new List<ObservedCombatRow>{owner},new CombatStats());
                typeof(TeamStatsTable).GetMethod("TogglePets",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(table,new object[]{owner});
            }
            preferences=json.Deserialize<ControlPanelPreferences>(json.Serialize(preferences));owner.Key="new-session";
            using(var table=new TeamStatsTable(r=>null,r=>{})) {
                table.UsePreferences(preferences,1,()=>{});table.UpdateObservedRows(new List<ObservedCombatRow>{owner},new CombatStats());
                Check(All(table).OfType<DataGridView>().Single(g=>g.Name=="teamStatistics").Rows.Count==1,"Collapsed pets survive a new session and settings reload");
            }
            Check(TeamWindow.ConstrainPanelBounds(new Rectangle(-18000,-18000,5000,4000),new Rectangle(0,0,1920,1080))==new Rectangle(0,0,1920,1080),"Saved window bounds are constrained to an available display");
            settings.ControlPanel=new ControlPanelPreferences {Tab=90,Tables=null};
            using(var panel=new TeamWindow(()=>new List<ClientObservation>(),()=>settings))panel.RefreshWall();
            Check(true,"Invalid tab and null table preferences fall back safely");
            using(var stack=new ControlPanelSections(()=>{})) {
                stack.AddSection("damage","Damage",new Panel(),300);stack.AddSection("team","Team",new Panel(),480);stack.AddSection("timers","Timers",new Panel(),450);
                stack.Restore(new[]{null,new ControlPanelSectionPreference{Id="unknown"},new ControlPanelSectionPreference{Id="team",Height=int.MaxValue},new ControlPanelSectionPreference{Id="team",Collapsed=true}},0);
                Check(stack.Snapshot().Select(p=>p.Id).SequenceEqual(new[]{"team","damage","timers"}) && stack.Snapshot()[0].Height==1600,"Invalid and duplicate saved sections cannot hide sections or create oversized content");
            }
        }

        static int Run(){
            SavedControlPanelChoices();
            var oldLanguage=AOLauncher.Localization.Texts.CurrentLanguage;
            AOLauncher.Localization.Texts.SetLanguage("en");
            Check(MainForm.StartupStatusText(false,new[]{NexusEngine.StartupProfile.AOVKVerified}).Contains("off"),"Disabled fast loading visible despite compatible files");
            Check(MainForm.StartupStatusText(true,new[]{NexusEngine.StartupProfile.None}).Contains("unsupported"),"Unrecognized renderer cannot be shown as fast-ready");
            Check(MainForm.StartupStatusText(true,new[]{NexusEngine.StartupProfile.AOVKVerified}).Contains("ready"),"Validated profile visible as ready");
            Check(MainForm.StartupStatusText(true,new[]{NexusEngine.StartupProfile.Stock,NexusEngine.StartupProfile.None}).Contains("unsupported"),"Mixed client installations surface fallback");
            var jsonStartup=new System.Web.Script.Serialization.JavaScriptSerializer();
            foreach(bool enabled in new[]{true,false}) {
                var roundTrip=jsonStartup.Deserialize<LauncherSettings>(jsonStartup.Serialize(new LauncherSettings {FastStartup=enabled}));
                Check(roundTrip.FastStartup==enabled,"Fast loading preference round trips without a reset");
                using(var options=new SettingsDialog(roundTrip))Check(options.FastStartup==enabled,"Settings reflects actual fast-loading preference");
            }
            AOLauncher.Localization.Texts.SetLanguage(oldLanguage);
            FormationHeader();LogoutMovementCoordination();OrderedTeamPreset();
            FormationOwnsRoster();
            StartupAndMemory();
            CardOrderAndLeader();
            string monitor=@"C:\verified\NexusHealthPlugin.dll";
            Check(PluginAutoInjector.CanRetryMissingMonitor(new[]{monitor},monitor,true,true,false,30,false),"Missing health-only startup can receive one retry");
            Check(!PluginAutoInjector.CanRetryMissingMonitor(new[]{monitor,@"C:\plugins\Doctor.dll"},monitor,true,true,false,60,false),"Missing monitoring never restarts a profession suite");
            Check(!PluginAutoInjector.CanRetryMissingMonitor(new[]{monitor},monitor,true,true,true,60,false),"Monitoring startup retry is bounded");
            Check(!PluginAutoInjector.CanRetryMissingMonitor(new[]{monitor},monitor,true,true,false,60,true),"Existing stale or inactive snapshot cannot trigger an automatic reload");
            Check(!PluginAutoInjector.CanRetryMissingMonitor(new[]{monitor},monitor,false,true,false,60,false),"Disabling automatic loading prevents monitor retry");
            Check(!PluginAutoInjector.CanRetryMissingMonitor(new[]{monitor},monitor,true,false,false,60,false),"Disabling monitoring prevents retry");
            Check(!PluginAutoInjector.CanRetryMissingMonitor(new[]{monitor},monitor,true,true,false,29,false),"Initial monitoring startup gets a grace period");
            Check(!PluginAutoInjector.CanRetryMissingMonitor(new[]{@"C:\custom\NexusHealthPlugin.dll"},monitor,true,true,false,60,false),"Custom plugin with the same filename is never automatically reloaded");
            IndependentTeamWindow();
            MonitoringReasons();
            CombatCounters();
            NativeCounters();
            ObservedCounters();
            PetBreakdowns();
            Application.EnableVisualStyles();Control.CheckForIllegalCrossThreadCalls=true;
            LogoutActions();
            RelogTargets();
            Recording();
            long now=Stopwatch.GetTimestamp();long start=Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
            var rows=new List<ClientObservation>();
            string[] names={"Wazoo","Sexologist","Fatale","Calm","Overwhelming","Geek"};
            string[] jobs={"Trader","Doctor","Meta-Physicist","Bureaucrat","Enforcer","Engineer"};
            for(int i=0;i<6;i++) rows.Add(new ClientObservation {Name=names[i],Pid=100+i,StartTicks=start,Condition=ClientCondition.Healthy,Measured=true,
                Client=new PresetCamping.Client {AccountIdentity="id:fixture",CharacterId=166+i,ProcessId=100+i,StartTimeUtcTicks=start,Server="fixture",Port=1},
                Sample=new ClientSample {Utc=DateTime.UtcNow,ReplyMs=1.4+i,PulseAgeMs=20,Zoning=false},
                Pulse=new ClientPulse {Pid=100+i,StartTicks=start,Active=true,PublishedQpc=now,VitalsQpc=now,Health=8000-i*900,MaxHealth=9000,Nano=4000-i*500,MaxNano=5000,
                    Profession=jobs[i],Attacking=i%2==0,FightingTarget="ICC Peacekeeper Commander",CastNanoId=12345,CastAttemptQpc=i==1?now:0}});
            var row=rows[0];
            Check(TeamPresentation.Fresh(row,now),"fresh vitals");
            Check(!TeamPresentation.Fresh(row,now+3*Stopwatch.Frequency),"stale data never appears live");
            row.Pulse.VitalsQpc=now+1;Check(!TeamPresentation.Fresh(row,now),"future vitals rejected");row.Pulse.VitalsQpc=now;
            row.Pulse.Pid++;Check(!TeamPresentation.Fresh(row,now),"identity mismatch rejected");row.Pulse.Pid--;
            row.Pulse.Zoning=true;Check(!TeamPresentation.Fresh(row,now),"zoning invalidates bars");row.Pulse.Zoning=false;
            row.Pulse.VitalsQpc=0;Check(!TeamPresentation.Fresh(row,now),"old monitor requires updated telemetry");row.Pulse.VitalsQpc=now;
            Check(TeamPresentation.Ratio(0,100)==0 && TeamPresentation.Ratio(50,100)==.5,"resource bars");
            Check(!TeamPresentation.Ratio(3,0).HasValue && !TeamPresentation.Ratio(-1,100).HasValue && !TeamPresentation.Ratio(101,100).HasValue,"invalid stats unavailable");
            Check(TeamPresentation.RecentCast(rows[1],now),"observed attempt visible");
            rows[1].Pulse.CastNanoName="Synthetic healing nano";
            for(int i=0;i<rows.Count;i++)rows[i].Pulse.CombatMeter=new NexusTelemetry.CombatMeterSnapshot {Source="Nexus",Status="Ready",Epoch="ui-fixture"+i,ObservedQpc=now,ElapsedSeconds=60,
                Actors=new List<NexusTelemetry.CombatMeterActor>{new NexusTelemetry.CombatMeterActor {CharacterId=rows[i].Client.CharacterId,Damage=12000+i*3000,Healing=i==1?18000:0,NanoDamage=3000,PetDamage=i==5?9000:0,Xp=12345+i*100,Axp=2191,ExperienceEvents=3}}};
            Check(TeamPresentation.NanoAttempt(rows[1].Pulse)=="Nano attempt: Synthetic healing nano","Known nano name replaces the numeric ID");
            rows[1].Pulse.CastNanoName=null;
            Check(TeamPresentation.NanoAttempt(rows[1].Pulse)=="Nano attempt: Unknown nano (#12345)","Older monitoring plugins and unknown nanos retain an honest fallback");
            rows[1].Pulse.CastNanoName="Synthetic healing nano";
            Check(!TeamPresentation.RecentCast(rows[1],now+6*Stopwatch.Frequency),"old attempt not casting");
            rows[2].Condition=ClientCondition.Delayed; rows[3].Sample.Zoning=true; rows[4].Condition=ClientCondition.NoResponse; rows[5].Pulse.VitalsQpc=0;
            var settings=new LauncherSettings {ExperimentalNearbyCombat=true,MonitorClientHealth=true};
            settings.Presets.Add(new LaunchPreset {Id="fixture",Name="Tank + healer",Characters=new List<PresetCharacter>{new PresetCharacter{AccountId="fixture",CharacterId=167},new PresetCharacter{AccountId="fixture",CharacterId=170}}});
            Directory.CreateDirectory("logs/team-previews");
            foreach(var language in new[]{"en","da"}) {
                Localization.Texts.SetLanguage(language);
                string backgroundStatus="Auto-minimize: 6 supported · 0 skipped · AO-VK (experimental)";
                using(var form=new TeamWindow(()=>rows,()=>settings,readBackgroundStatus:()=>backgroundStatus)) {
                    form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-18000,-18000);form.Show();Application.DoEvents();form.RefreshWall();
                    Check(All(form).OfType<TeamCard>().Count()==6,"six cards");
                    var backgroundLabel=All(form).OfType<Label>().Single(c=>c.Name=="backgroundClientStatus");
                    Check(backgroundLabel.Visible && backgroundLabel.Text==backgroundStatus,"Team Window reports AO-VK background support");
                    backgroundStatus="Auto-minimize: 0 supported · 6 skipped · renderer and patch do not match";form.RefreshWall();
                    Check(backgroundLabel.Text==backgroundStatus,"Changed skip reasons replace stale support status");
                    backgroundStatus="Auto-minimize: 6 supported · 0 skipped · AO-VK (experimental)";form.RefreshWall();
                    var summaryLabel=All(form).OfType<Label>().Single(c=>c.Name=="teamSummary");
                    var enabledField=typeof(DevelopmentRecorder).GetField("enabled",BindingFlags.Static|BindingFlags.NonPublic);
                    object oldRecording=enabledField.GetValue(null);
                    try {
                        enabledField.SetValue(null,true);form.RefreshWall();
                        int summaryChanges=0,backgroundChanges=0;
                        summaryLabel.TextChanged+=(s,e)=>summaryChanges++;backgroundLabel.TextChanged+=(s,e)=>backgroundChanges++;
                        for(int tick=0;tick<5;tick++)form.RefreshWall();
                        Check(summaryChanges==0 && backgroundChanges==0,"Unchanged diagnostic/status text never alternates during timer refresh");
                    } finally {enabledField.SetValue(null,oldRecording);}
                    rows[0].Sample.FreeClientBytes=200L*1024*1024;rows[0].Sample.LargestFreeClientBytes=50L*1024*1024;rows[0].Sample.MemoryMeasuredQpc=Stopwatch.GetTimestamp();
                    form.RefreshWall();var memoryLabel=All(form).OfType<Label>().Single(c=>c.Name=="teamMemoryWarning");
                    Check(memoryLabel.Visible && memoryLabel.Text.Contains(rows[0].Name),"Low memory warning identifies the affected character");
                    rows[0].Sample.MemoryMeasuredQpc=Stopwatch.GetTimestamp()-13*Stopwatch.Frequency;form.RefreshWall();
                    Check(!memoryLabel.Visible,"Old memory samples cannot keep a live warning visible");
                    var help=All(form).OfType<LinkLabel>().Single(c=>c.Name=="teamMonitoringHelp");
                    Check(help.Visible && help.Text.Contains("Health Monitor"),"Missing vitals explain the monitoring dependency");
                    rows[5].Pulse.Zoning=true;form.RefreshWall();
                    Check(!help.Visible,"Zoning with cleared vitals never shows the missing-plugin link");
                    rows[5].Pulse.Zoning=false;rows[5].Sample.Zoning=true;form.RefreshWall();
                    Check(!help.Visible,"Observer zoning state also suppresses the missing-plugin link");
                    rows[5].Sample.Zoning=false;form.RefreshWall();
                    Check(help.Visible,"A genuinely missing measurement still shows setup help outside zoning");
                    rows[5].Pulse.VitalsQpc=now;form.RefreshWall();
                    Check(!help.Visible,"Setup hint clears when vitals are available, including temporarily unresponsive clients");
                    rows[5].Pulse.VitalsQpc=0;form.RefreshWall();
                    // Refresh fixture timestamps, then exercise both real reset controls.
                    long meterNow=Stopwatch.GetTimestamp();foreach(var measured in rows){measured.Pulse.PublishedQpc=meterNow;measured.Pulse.CombatMeter.ObservedQpc=meterNow;}form.RefreshWall();
                    var grid=All(form).OfType<DataGridView>().Single(g=>g.Name=="teamStatistics");
                    var metric=All(form).OfType<NexusComboBox>().Single(c=>c.Name=="statisticsMetric");
                    Check(grid.Rows.Count==6 && grid.Columns.Contains("DMGm") && !grid.Columns.Contains("DMGs"),"Overview compares every character's totals and damage per minute");
                    var stack=All(form).OfType<ControlPanelSections>().Single();
                    var damageTabs=All(form).OfType<TabControl>().Single(c=>c.Name=="teamDamageTabs");
                    Check(grid.Visible && All(stack).OfType<TeamCard>().Any() && damageTabs.TabCount==3 && damageTabs.SelectedIndex==0,"Team, All and NPC damage tabs keep client cards in their own section");
                    Check(grid.Columns["DMGm"].Index==grid.Columns["Pets"].Index+1,"Overview damage per minute follows Pets");
                    var totals=All(form).OfType<DataGridView>().Single(g=>g.Name=="teamStatisticsTotal");
                    var measuredStats=rows.Select(form.StatsFor).Where(s=>s!=null).ToList();
                    Check(Math.Abs(Convert.ToDouble(totals.Rows[0].Cells["DMGm"].Value)-measuredStats.Average(s=>s.Dpm.Value))<.001,"DMG/m footer averages measured characters rather than adding their rates");
                    Check(Convert.ToInt64(totals.Rows[0].Cells["DMG"].Value)==measuredStats.Sum(s=>s.Damage),"Damage footer remains the combined team total");
                    rows[0].Pulse.ObservedCombatMeter=new NexusTelemetry.CombatMeterSnapshot {Source="NexusObserved",Status="Ready",Epoch="ui-nearby",ObservedQpc=meterNow,ElapsedSeconds=10,
                        Actors=new List<NexusTelemetry.CombatMeterActor>{new NexusTelemetry.CombatMeterActor {CharacterId=900,Name="Outside player",Profession="Doctor",Damage=100,PetDamage=40}}};
                    damageTabs.SelectedIndex=1;form.RefreshWall();
                    Check(grid.Rows.Count==1 && grid.Rows[0].Tag is ObservedCombatRow && (string)grid.Rows[0].Cells[0].Value=="Outside player" && grid.Rows[0].Cells["XP"].Value==null,
                        "All tab displays observed actors without manufacturing local client rows or XP");
                    var sourceChoice=All(form).OfType<NexusComboBox>().Single(c=>c.Name=="combatMeterSource");
                    Check(sourceChoice.Visible && All(form).OfType<TeamCard>().Count()==6,"All observer selection leaves the six local client cards intact");
                    rows[0].Pulse.NpcCombatMeter=new NexusTelemetry.CombatMeterSnapshot {Source="NexusNPC",Status="Ready",Epoch="ui-npc",ObservedQpc=meterNow,ElapsedSeconds=10,
                        Actors=new List<NexusTelemetry.CombatMeterActor>{new NexusTelemetry.CombatMeterActor {CharacterId=901,Name="Training Drone",Damage=100,DamageTaken=600,NanoDamage=25,Healing=10},
                            new NexusTelemetry.CombatMeterActor {CharacterId=902,Name="Training Drone",Damage=50,DamageTaken=200}}};
                    damageTabs.SelectedIndex=2;form.RefreshWall();
                    Check(grid.Rows.Count==2 && grid.Columns.Contains("Taken") && grid.Columns.Contains("Takenm") && !grid.Columns.Contains("XP"),"NPC view compares dealt and taken damage separately without irrelevant XP columns");
                    Check(Convert.ToInt64(totals.Rows[0].Cells["Taken"].Value)==800 && Convert.ToDouble(totals.Rows[0].Cells["Takenm"].Value)==4800,"NPC footer totals damage received and its per-minute rate from one observer");
                    metric.SelectedIndex=2;form.RefreshWall();
                    Check(grid.Columns.Contains("Takens") && grid.Columns.Contains("Takenm") && grid.Columns.Contains("Takenh"),"NPC damage taken exposes per-second, per-minute and per-hour rates");
                    metric.SelectedIndex=0;form.RefreshWall();
                    using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save("logs/team-previews/npc-"+language+".png");}
                    All(form).OfType<Button>().Single(b=>b.Name=="resetTeamStats").PerformClick();
                    Check(Convert.ToInt64(totals.Rows[0].Cells["Taken"].Value)==0,"NPC total reset clears the NPC period");
                    damageTabs.SelectedIndex=1;form.RefreshWall();
                    Check(grid.Rows.Count==1 && Convert.ToInt64(totals.Rows[0].Cells["DMG"].Value)==100,"NPC resets do not change All-player totals");
                    rows[0].Pulse.PublishedQpc=rows[0].Pulse.VitalsQpc=rows[0].Pulse.TeamStateQpc=Stopwatch.GetTimestamp();rows[0].Pulse.IsTeamLeader=true;form.RefreshWall();
                    Check(All(form).OfType<TeamCard>().Single(c=>c.Observation==rows[0]).AccessibleName.Contains(language=="da"?"Holdleder":"Team leader"),"Leader badge is accessible in each language");
                    var leaderCard=All(form).OfType<TeamCard>().Single(c=>c.Observation==rows[0]);
                    using(var image=new Bitmap(leaderCard.Width,leaderCard.Height)){leaderCard.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save("logs/team-previews/leader-"+language+".png");}
                    using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save("logs/team-previews/all-"+language+".png");}
                    All(form).OfType<Button>().Single(b=>b.Name=="resetCardStats").PerformClick();
                    Check(Convert.ToInt64(grid.Rows[0].Cells["DMG"].Value)==0 && Convert.ToInt64(totals.Rows[0].Cells["DMG"].Value)==100,"All Reset selected changes the observed row only");
                    All(form).OfType<Button>().Single(b=>b.Name=="resetTeamStats").PerformClick();
                    Check(Convert.ToInt64(totals.Rows[0].Cells["DMG"].Value)==0,"All Reset totals acts on observed totals");
                    damageTabs.SelectedIndex=0;form.RefreshWall();
                    Check(grid.Rows.Count==6 && Convert.ToInt64(totals.Rows[0].Cells["DMG"].Value)==measuredStats.Sum(s=>s.Damage),"Switching back to Team preserves its totals and client rows");
                    rows[0].Pulse.CombatMeter.Actors[0].Damage+=10000000;form.RefreshWall();
                    grid.Sort(grid.Columns["DMG"],System.ComponentModel.ListSortDirection.Descending);
                    Check(((ClientObservation)grid.Rows[0].Tag).Pid==rows[0].Pid,"Damage sorting is numeric across different digit lengths");
                    var selectedBefore=(ClientObservation)grid.CurrentRow.Tag;form.RefreshWall();
                    Check(grid.SortedColumn.Name=="DMG" && ((ClientObservation)grid.CurrentRow.Tag).Pid==selectedBefore.Pid,"Live refresh preserves sorting and character selection");
                    rows[0].Pulse.CombatMeter.Actors[0].Damage-=10000000;
                    metric.SelectedIndex=1;form.RefreshWall();
                    Check(grid.Columns.Contains("DMGs") && grid.Columns.Contains("DMGm") && grid.Columns.Contains("DMGh") && grid.Columns.Count==6,"Metric view compares totals and all three rate periods");
                    metric.SelectedIndex=8;form.RefreshWall();Check(grid.Columns.Count==30,"All statistics remain available together with horizontal scrolling");
                    metric.SelectedIndex=0;form.RefreshWall();
                    var resetTeam=All(form).OfType<Button>().Single(b=>b.Name=="resetTeamStats");
                    Check(resetTeam.Enabled,"Fresh meter enables the team reset control");
                    Check(resetTeam.Parent.Name=="teamStatisticsToolbar","Team reset remains in the top statistics toolbar");
                    Check(All(form).Single(c=>c.Name=="teamTotalsPanel").Height==44 && !All(form).OfType<Label>().Any(c=>c.Text=="TEAM TOTALS"||c.Text=="HOLDTOTAL"),"Compact statistics footer has no redundant team totals heading");
                    var cardReset=All(form).OfType<Button>().First(b=>b.Name=="resetCardStats");
                    Check(cardReset.Enabled,"Fresh measured character enables its card reset control");
                    cardReset.PerformClick();
                    Check(form.StatsFor(rows[3]).Damage==0 && form.StatsFor(rows[1]).Damage>0,"Card reset button clears only its own character period");
                    resetTeam.PerformClick();
                    Check(Convert.ToInt64(All(form).OfType<DataGridView>().Single(g=>g.Name=="teamStatisticsTotal").Rows[0].Cells["DMG"].Value)==0,"Team reset button updates the total display");
                    using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save("logs/team-previews/team-"+language+".png");}
                    var combo=All(form).OfType<NexusComboBox>().Single(c=>c.Name=="teamFilter");combo.SelectedIndex=1;Application.DoEvents();
                    Check(All(form).OfType<TeamCard>().Count()==2,"team filters account and character");
                    combo.SelectedIndex=0;

                    var preview=All(form).OfType<NexusCheckBox>().Single(c=>c.Name=="teamLivePreviews");preview.Checked=false;Application.DoEvents();
                    Check(All(form).OfType<TeamCard>().All(c=>c.Height==363),"client cards reserve attack and aggro panels without overlapping performance or actions");
                    form.UpdateViewedClient(rows,rows[0].Pid);
                    Check(form.IsViewing(rows[0]) && !form.IsSelected(rows[1]),"Foreground client is the only viewed and selected card");
                    form.UpdateViewedClient(rows,0);
                    Check(form.IsSelected(rows[0]) && !form.IsViewing(rows[0]),"Selection survives returning to the Control Panel");
                    form.UpdateViewedClient(rows,rows[1].Pid);
                    Check(form.IsViewing(rows[1]) && !form.IsSelected(rows[0]),"Switching game windows transfers the highlight");
                    rows[1].StartTicks++;
                    form.UpdateViewedClient(rows,0);
                    Check(!form.IsSelected(rows[1]),"A reused process ID does not inherit the previous selection");rows[1].StartTicks--;
                    now=Stopwatch.GetTimestamp();rows[0].Pulse.PublishedQpc=rows[0].Pulse.VitalsQpc=now;
                    Check(TeamPresentation.Attacking(rows[0],now),"Fresh combat state is displayed");
                    rows[0].Pulse.AggroQpc=0;
                    Check(!TeamPresentation.AggroCount(rows[0],now).HasValue,"Old monitors cannot claim no aggro");
                    rows[0].Pulse.AggroQpc=now;rows[0].Pulse.AggroCount=5;rows[0].Pulse.AggroNames=new[]{"Flea", "Rat", "Snake", "hidden fourth"};
                    Check(TeamPresentation.AggroCount(rows[0],now)==5 && !TeamPresentation.AggroNames(rows[0]).Contains("hidden fourth") && TeamPresentation.AggroNames(rows[0]).Contains("+2"),"Aggro keeps the full count but limits visible names to three");
                    Check(!TeamPresentation.AggroCount(rows[0],now+3*Stopwatch.Frequency).HasValue,"Stale aggro is unavailable");
                    rows[0].Pulse.Zoning=true;Check(!TeamPresentation.AggroCount(rows[0],now).HasValue,"Zoning clears aggro");rows[0].Pulse.Zoning=false;
                    rows[0].Pulse.AggroQpc=now+1;Check(!TeamPresentation.AggroCount(rows[0],now).HasValue,"Future aggro timestamps are rejected");rows[0].Pulse.AggroQpc=now;
                    rows[0].Pulse.AggroCount=0;Check(TeamPresentation.AggroCount(rows[0],now)==0,"Fresh zero is valid");rows[0].Pulse.AggroCount=5;
                    Check(!TeamPresentation.Attacking(rows[0],now+3*Stopwatch.Frequency),"Stale combat data cannot show an attacking border");
                    rows[0].Pulse.Zoning=true;Check(!TeamPresentation.Attacking(rows[0],now),"Zoning clears combat emphasis");rows[0].Pulse.Zoning=false;
                    string target=rows[0].Pulse.FightingTarget;rows[0].Pulse.FightingTarget="  \r\n ";
                    Check(TeamPresentation.Target(rows[0])==(language=="da"?"Ukendt mål":"Unknown target"),"Missing targets have an explicit fallback");rows[0].Pulse.FightingTarget=target;
                    form.UpdateViewedClient(rows,rows[0].Pid);
                    var selectedCard=All(form).OfType<TeamCard>().Single(c=>c.Observation==rows[0]);selectedCard.Update(rows[0],false);selectedCard.Arrange(334);
                    Check(selectedCard.AccessibleName.Contains(target) && selectedCard.AccessibleName.Contains(language=="da"?"Vises":"Viewing"),"Accessible card name includes viewed state and full combat target");
                    using(var image=new Bitmap(selectedCard.Width,selectedCard.Height)){selectedCard.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save("logs/team-previews/selected-combat-"+language+".png");}
                    rows[0].ExitedUtc=DateTime.UtcNow.AddMinutes(-1);form.RefreshWall();Check(All(form).OfType<TeamCard>().Count()==5,"old exited client removed");rows[0].ExitedUtc=null;
                    form.Size=new Size(780,580);form.RefreshWall();Check(All(form).OfType<TeamCard>().All(c=>c.Width>=320),"small layout scrolls without crushing cards");
                    // Real DWM registration against an inert test-owned window; no game used.
                    using(var source=new Form {Text="DWM fixture",Size=new Size(400,300),ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-17000,-17000)}) {
                        source.Show();rows[0].Pid=Process.GetCurrentProcess().Id; rows[0].StartTicks=start;
                        IntPtr thumbnail;
                        Check(TeamCard.Native.DwmRegisterThumbnail(form.Handle,source.Handle,out thumbnail)==0 && thumbnail!=IntPtr.Zero,"DWM thumbnail registered");
                        var props=new TeamCard.Native.Properties {Flags=1|4|8|16,Destination=new TeamCard.Native.Rect(new Rectangle(20,60,200,125)),Opacity=255,Visible=false,ClientOnly=true};
                        Check(TeamCard.Native.DwmUpdateThumbnailProperties(thumbnail,ref props)==0,"DWM properties ABI");
                        Check(TeamCard.Native.DwmUnregisterThumbnail(thumbnail)==0,"DWM release");
                        rows[0].Pid=100;
                        preview.Checked=true;form.RefreshWall();
                        var card=All(form).OfType<TeamCard>().First();
                        var stableCardSize=card.Size;
                        var stableActions=All(card).OfType<Button>().Select(b=>b.Bounds).ToArray();
                        foreach(var clientSize in new[]{new Size(480,300),new Size(640,480),new Size(960,540),new Size(300,600)}) {
                            source.ClientSize=clientSize;
                            TeamCard.Native.Rect clientRect;
                            Check(TeamCard.Native.GetClientRect(source.Handle,out clientRect),"Client dimensions available without window borders");
                            card.SetPreviewSourceSize(new Size(clientRect.Right-clientRect.Left,clientRect.Bottom-clientRect.Top));
                            Check(card.Size==stableCardSize && All(card).OfType<Button>().Select(b=>b.Bounds).SequenceEqual(stableActions),
                                "Restored client dimensions cannot resize a card or move its actions");
                            var imageRect=card.PreviewImageRect;
                            Check(card.PreviewRect.Contains(imageRect) && (imageRect.Width==card.PreviewRect.Width || imageRect.Height==card.PreviewRect.Height)
                                && Math.Abs(imageRect.Height-imageRect.Width*(double)clientSize.Height/clientSize.Width)<=1,
                                "Different client aspect ratios fit the stable preview frame without cropping or stretching");
                            form.UpdateViewedClient(rows,rows[0].Pid);card.Update(rows[0],true);card.Arrange(stableCardSize.Width);
                            Check(card.Size==stableCardSize,"Selecting a client preserves the card dimensions");
                            form.UpdateViewedClient(rows,0);
                            Check(card.Height==card.PreviewRect.Bottom+316,"Card grows with preview and preserves room for health, activity, aggro, memory, movement and actions");
                        }
                        var previous=card.PreviewRect;
                        card.SetPreviewSourceSize(Size.Empty);
                        Check(card.PreviewRect==previous,"Unavailable source dimensions preserve the last valid ratio");
                        Size originalSize=source.Size;
                        Check(BackgroundClients.MinimizeWindow(source.Handle),"Production minimization command accepts an inert fixture window");Application.DoEvents();
                        Check(source.WindowState==FormWindowState.Minimized,"Background client command minimizes the fixture window");
                        TeamCard.Native.ShowWindowAsync(source.Handle,9);Application.DoEvents();
                        Check(source.WindowState==FormWindowState.Normal && source.Size==originalSize,"Restoring a minimized fixture preserves its previous window size");
                    }
                }
            }
            Localization.Texts.SetLanguage("en");
            var fixtureRoot=Path.Combine(Path.GetTempPath(),"NexusTimersUi-"+Guid.NewGuid().ToString("N"));
            try {
                var prefs=Path.Combine(fixtureRoot,"Server","Prefs","Account","Char166");
                settings.Accounts.Add(new AccountConfig {Label="Fixture server",Server="127.0.0.1",Port=7500,PrefsPath=prefs});
                var store=new NexusPlayTools.ClockStore(fixtureRoot);
                store.Edit(d=>{var t=new NexusPlayTools.BossClock{Name="Example boss",Scope=NexusPlayTools.ToolFiles.Scope(prefs)};t.Start(DateTime.UtcNow,"Manual fixture");d.Timers.Add(t);});
                using(var timers=new TimersWindow(settings,store)) {
                    timers.ShowInTaskbar=false;timers.StartPosition=FormStartPosition.Manual;timers.Location=new Point(-18000,-18000);timers.Show();Application.DoEvents();
                    Check(All(timers).OfType<ListBox>().Single().Items.Count==1,"timer UI lists selected server");
                    using(var image=new Bitmap(timers.Width,timers.Height)){timers.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save("logs/team-previews/timers.png");}
                }
                using(var editor=new TimerEditor(null,"fixture")) {
                    editor.ShowInTaskbar=false;editor.StartPosition=FormStartPosition.Manual;editor.Location=new Point(-18000,-18000);editor.Show();Application.DoEvents();
                    using(var image=new Bitmap(editor.Width,editor.Height)){editor.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save("logs/team-previews/timer-editor.png");}
                    Check(All(editor).OfType<Button>().All(b=>b.Bottom<=b.Parent.ClientSize.Height),"timer editor save remains visible");
                    var kind=All(editor).OfType<NexusComboBox>().Single();
                    var options=All(editor).OfType<NexusCheckBox>().ToArray();
                    var observe=options.Single(c=>c.Text.StartsWith("Observe"));
                    var repeat=options.Single(c=>c.Text.StartsWith("Repeat"));
                    Check(observe.Enabled && !repeat.Enabled && !repeat.Checked,"Fixed timer cannot select incompatible repeat option");
                    observe.Checked=true;kind.SelectedIndex=(int)NexusPlayTools.TimerKind.Reset;
                    Check(repeat.Enabled && !observe.Enabled && !observe.Checked,"Switching to Reset clears incompatible death observation");
                    repeat.Checked=true;kind.SelectedIndex=(int)NexusPlayTools.TimerKind.Personal;
                    Check(!repeat.Enabled && !repeat.Checked && !observe.Enabled,"Personal timer clears repeat and disables death observation");
                    kind.SelectedIndex=(int)NexusPlayTools.TimerKind.ChanceWindow;
                    Check(observe.Enabled && !repeat.Enabled,"Chance window permits observation only");
                    kind.SelectedIndex=(int)NexusPlayTools.TimerKind.Fixed;observe.Checked=true;
                    var inputs=All(editor).OfType<TextBox>().ToArray();
                    inputs[0].Text="Tara";inputs[1].Text="61";inputs[2].Text="10";inputs[3].Text="0";
                    All(editor).OfType<Button>().Single(b=>b.Text=="Save").PerformClick();
                    Check(editor.DialogResult==DialogResult.OK && editor.Value.Seconds==3660 && editor.Value.WarningSeconds==600 && editor.Value.ObserveDeaths && !editor.Value.Repeat,"Reported Tara configuration saves after switching timer types");
                }
            } finally {
                string temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                if(Path.GetFullPath(fixtureRoot).StartsWith(temp,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(fixtureRoot).StartsWith("NexusTimersUi-")&&Directory.Exists(fixtureRoot))Directory.Delete(fixtureRoot,true);
            }
            Console.WriteLine("PASS: "+checks+" team data, filters, stale handling, layouts, DWM registration and disposal checks; no game interaction.");return 0;
        }
    }
}
