using System;
using System.Linq;
using AOSharp.Core;
using AOSharp.Core.UI;
using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.ChatMessages;

namespace NexusTelemetry
{
    internal sealed class RallyBeacon : IDisposable
    {
        int? teamChannel;
        bool disposed;
        bool hadTeam;
        readonly Action<string,string[],ChatWindow> command;
        double lastSent=-100;
        internal RallyBeacon()
        {
            Network.ChatMessageReceived+=Received;
            command=(name,args,window)=> { if(!disposed)Send(false); };
            Chat.RegisterCommand("nexus-rally",command);
        }
        void Received(object sender,ChatMessageBody message)
        {
            // The first byte of the five-byte group identifier is its type (130=team).
            // Never fall back to the currently selected chat channel or vicinity.
            if(message is ChannelListMessage channel && channel.Unk1==130)teamChannel=channel.ChannelId;
            if(message is GroupMsgMessage group && (byte)group.MessageType==130)teamChannel=group.ChannelId;
        }
        internal void Update()
        {
            if(Game.IsZoning || DynelManager.LocalPlayer==null)return;
            bool current=InTeam();
            if(hadTeam && !current)teamChannel=null;
            hadTeam=current;
        }
        static bool InTeam()
        {
            var view=AOSharp.Common.Unmanaged.Imports.TeamViewModule_c.GetInstanceIfAny();
            return view!=IntPtr.Zero?AOSharp.Common.Unmanaged.Imports.TeamViewModule_c.IsInTeam(view)!=0:Team.IsInTeam;
        }
        internal void Send(bool stopped)
            => Send(stopped ? "Stopped" : "Rally");
        internal void Send(string reason)
        {
            if(disposed || Game.IsZoning || DynelManager.LocalPlayer==null)return;
            double now=System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency;
            if(now-lastSent<(reason=="Rally"?10:30))return;
            if(!InTeam() || !teamChannel.HasValue) {
                Chat.WriteLine("Nexus: team chat is not confirmed yet. Send one message in Team, then use /nexus-rally.");return;
            }
            var player=DynelManager.LocalPlayer;
            string text=RallyLink.Create(player.Name,Playfield.Name,player.Position.X,player.Position.Z,Playfield.Identity.Instance,reason);
            if(text==null)return;
            Network.Send(new GroupMsgMessage {MessageType=(GroupMessageType)130,ChannelId=teamChannel.Value,SenderId=(uint)player.Identity.Instance,Text=text});
            lastSent=now;
        }
        public void Dispose() {
            disposed=true;Network.ChatMessageReceived-=Received;
            try {
                var commands=typeof(Chat).GetField("_customCommands",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)
                    ?.GetValue(null) as System.Collections.Generic.Dictionary<string,Action<string,string[],ChatWindow>>;
                if(commands!=null && commands.TryGetValue("nexus-rally",out var current) && current==command)commands.Remove("nexus-rally");
            } catch(Exception ex) { System.Diagnostics.Trace.TraceWarning("Rally command detach: {0}",ex.Message); }
        }
    }
}
