using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal sealed partial class MainForm
    {
        private CancellationTokenSource teamRelogCancellation;

        // Only a confirmed exit of the captured process makes a character eligible.
        // A new process for the same character must not be logged in a second time.
        internal static List<PresetCamping.Client> TeamRelogTargets(IEnumerable<PresetCamping.Client> requested,
            Func<PresetCamping.Client,bool> exited, IEnumerable<PresetCamping.Client> runningClients)
        {
            var active=runningClients.ToList();
            return requested.Where(exited).Where(client=>!active.Any(other=>other.CharacterId==client.CharacterId
                && string.Equals(other.AccountIdentity,client.AccountIdentity,StringComparison.OrdinalIgnoreCase)
                && string.Equals(other.Server,client.Server,StringComparison.OrdinalIgnoreCase) && other.Port==client.Port
                && PresetCamping.SamePath(other.GamePath,client.GamePath))).ToList();
        }

        private async Task RelogTeamClients(List<PresetCamping.Client> requested)
            => await RunTeamRecovery(requested,false,true);

        private async Task RecoverTeamClient(PresetCamping.Client client,bool restart)
            => await RunTeamRecovery(new List<PresetCamping.Client>{client},true,restart);

        private async Task RunTeamRecovery(List<PresetCamping.Client> requested,bool force,bool restartAfterExit)
        {
            if(smoke || shortcutActionBusy || guiOperationBusy || !PresetShortcutContextAvailable()) return;
            shortcutActionBusy=true;
            var cancellation=new CancellationTokenSource();teamRelogCancellation=cancellation;
            var handles=new Dictionary<PresetCamping.Client,Process>();
            var credentials=new Dictionary<string,string>();
            var prepared=new List<LaunchCharacter>();
            TeamWindow movementPanel=null;
            try
            {
                var scope=new LaunchPreset {Characters=settings.Accounts.SelectMany(account=>account.Characters
                    .Select(character=>new PresetCharacter {AccountId=account.Id,CharacterId=character.CharacterId})).ToList()};
                var clients=PresetCamping.Match(settings,scope,requested).Where(c=>(force || c.CanCamp) && PresetCamping.IsSameProcess(c))
                    .GroupBy(c=>c.ProcessId+":"+c.StartTimeUtcTicks).Select(g=>g.First()).ToList();
                if(clients.Count==0) {ShowInfo(AOLauncher.Localization.Texts.Get("Text_These_clients_have_closed_or_no_6C19F6AB5FE8"));return;}
                string summary=clients.Count==1?string.Format(AOLauncher.Localization.Texts.Get("Text_Relog_0_AABF9C1AC921"),clients[0].CharacterName)
                    :string.Format(AOLauncher.Localization.Texts.Get("Text_Relog_these_0_characters_EB63CECCB670"),clients.Count)+"\r\n\r\n"+string.Join(", ",clients.Select(c=>c.CharacterName));
                if(force) {
                    if(clients.Count!=1 || !ConfirmTeamWindowRecovery(clients[0].CharacterName,restartAfterExit,teamWindow))return;
                } else if(!ConfirmTeamWindowRelog(summary,teamWindow)) return;
                foreach(var client in clients)DevelopmentRecorder.Action(force?(restartAfterExit?"force-restart":"force-close"):"relog",client,"confirmed");
                var accounts=settings.Accounts.Where(a=>clients.Any(c=>string.Equals(c.AccountIdentity,"id:"+a.Id,StringComparison.OrdinalIgnoreCase))).ToList();
                // Prepare and validate credentials before sending any logout request.
                if(restartAfterExit && !EnsurePasswords(accounts,out credentials)) return;
                var request=restartAfterExit?CreateGuiRelogRequest(settings,clients,credentials):new LaunchRequest();
                request.QuietLaunch=settings.QuietLaunch;request.FastStartup=settings.FastStartup;
                prepared=request.Characters.ToList();credentials.Clear();
                if(restartAfterExit && prepared.Count!=clients.Count) throw new InvalidOperationException(AOLauncher.Localization.Texts.Get("Text_The_character_selection_changed_Try_relog_000BD17A22B9"));
                var presets=clients.ToDictionary(c=>c,c=>pluginInjector?.LaunchPresetFor(c.AccountIdentity.Substring(3),c.CharacterId,c.StartTimeUtcTicks));
                foreach(var client in clients) handles.Add(client,GuiSessions.OpenConfirmed(client));
                cancellation.Token.ThrowIfCancellationRequested();
                if(!force && teamWindow!=null && !teamWindow.IsDisposed) {
                    movementPanel=teamWindow;
                    await movementPanel.PrepareMovementForLogout();
                    cancellation.Token.ThrowIfCancellationRequested();
                }
                if(restartAfterExit) {
                    var serializer=new System.Web.Script.Serialization.JavaScriptSerializer();
                    var recoverySettings=serializer.Deserialize<LauncherSettings>(serializer.Serialize(settings));
                    await Task.Run(()=>teamRecovery.PrepareRelog(recoverySettings,clients));
                }
                stopRequested=false;SetRunning(true);
                startButton.Text=force?AOLauncher.Localization.Texts.Get("Text_Recovering_client_19B529CC1748"):AOLauncher.Localization.Texts.Get("Text_Relogging_F09533397E03");
                StatusText=force?AOLauncher.Localization.Texts.Get("Text_STATUS_Closing_the_confirmed_client_FD52B47F69B1"):AOLauncher.Localization.Texts.Get("Text_STATUS_Waiting_for_normal_logout_586CA3C1C360");
                var waiting=new List<Process>();
                foreach(var client in clients)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    if(force) {
                        bool exited=await Task.Run(()=>ClientRecovery.ForceClose(client,handles[client]));
                        if(!exited)throw new InvalidOperationException(AOLauncher.Localization.Texts.Get("Text_The_client_did_not_exit_Restart_BF729E1FBCED"));
                        AppendLog(client.CharacterName+": "+AOLauncher.Localization.Texts.Get("Text_force_close_completed_D4B0137E2CE0"));
                        continue;
                    }
                    bool accepted=false;
                    DevelopmentRecorder.Action("relog",client,"normal logout requested");
                    string result=await Task.Run(()=>PresetCamping.SendCamp(client,out accepted));
                    DevelopmentRecorder.Action("relog",client,result);
                    if(accepted)waiting.Add(handles[client]);
                    AppendLog(client.CharacterName+": "+Localization.Texts.T(result));
                }
                if(!force && waiting.Count>0) {
                    AppendLog(AOLauncher.Localization.Texts.Get("Text_Relog_waiting_up_to_90_seconds_345BE1D5732F"));
                    await GuiSessions.WaitForExitAsync(()=>waiting.All(p=>p.HasExited),TimeSpan.FromSeconds(90),cancellation.Token);
                }
                cancellation.Token.ThrowIfCancellationRequested();
                foreach(var client in clients)DevelopmentRecorder.Action("relog",client,handles[client].HasExited?"original process exited":"logout did not complete");
                if(!restartAfterExit)return;
                var active=await Task.Run(()=>PresetCamping.Find(settings,scope));
                cancellation.Token.ThrowIfCancellationRequested();
                var restart=TeamRelogTargets(clients,c=>handles[c].HasExited,active);
                foreach(var skipped in clients.Except(restart)) AppendLog(skipped.CharacterName+": "+AOLauncher.Localization.Texts.Get("Text_relog_skipped_a_client_is_still_CBA697049FA2"));
                if(restart.Count<clients.Count && !closing) ShowInfo(AOLauncher.Localization.Texts.Get("Text_Some_clients_did_not_log_out_38B92035C685"));
                request.Characters=prepared.Where(character=>restart.Any(c=>string.Equals(c.AccountIdentity,"id:"+character.AccountId,StringComparison.OrdinalIgnoreCase)
                    && c.CharacterId==character.CharacterId)).ToList();
                if(closing || stopRequested || request.Characters.Count==0) return;
                // Relog uses an isolated character selection, so CreateLaunchRequest
                // cannot see the saved preset. Restore its order/window policy only
                // after filtering clients that actually exited.
                ApplyPresetLaunchOrder(settings,request,true);
                foreach(var client in restart) pluginInjector?.RecordLaunch(client.AccountIdentity.Substring(3),client.CharacterId,presets[client]);
                foreach(var client in restart)DevelopmentRecorder.Action(force?"force-restart":"relog",client,"starting replacement via login queue");
                AppendLog(AOLauncher.Localization.Texts.Get("Text_Relog_logging_the_closed_characters_back_1C3B6F8BE8B6"));
                SetRunning(false);
                await RunLaunchRequestAsync(request,accounts.Where(a=>request.Characters.Any(c=>c.AccountId==a.Id)).ToList(),false);
            }
            catch(OperationCanceledException) {AppendLog(AOLauncher.Localization.Texts.Get("Text_Relog_cancelled_Characters_already_logged_out_3C6E946A90F5"));}
            catch(Exception error)
            {
                DiagnosticJournal.Exception("team-window","Relog/recovery failed",error);
                DevelopmentRecorder.Write("recovery-error",new {Type=error.GetType().Name,Message=DiagnosticRedactor.Current.Redact(error.Message)});
                if(!closing) ShowInfo(AOLauncher.Localization.Texts.Get("Text_Relog_61EF60748103")+error.Message);
            }
            finally
            {
                movementPanel?.FinishMovementLogout();
                credentials.Clear();foreach(var character in prepared) character.Password=null;
                foreach(var process in handles.Values) process.Dispose();
                teamRelogCancellation=null;cancellation.Dispose();shortcutActionBusy=false;
                if(!closing) SetRunning(false);
            }
        }
    }
}
