using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AOSharp.Core;
using AOSharp.Core.IPC;
using AOSharp.Core.UI;

namespace NexusShortcuts
{
    // The AOSP receive callback ignores SyncMove. Suspend precisely that callback,
    // not the channel or attack/target callbacks. All mutations run on the game thread.
    internal sealed class FormationMovementLease : IDisposable
    {
        readonly Settings settings;
        readonly IPCChannel channel;
        readonly Type manager;
        readonly List<Action<int,IPCMessage>> moves;
        readonly Action<int,IPCMessage> receiver;
        readonly int index;
        readonly bool previous;
        bool disposed;
        internal static Tuple<Settings,Type> Discover()
        {
            var instance=Game.OnUpdate?.GetInvocationList().Select(d=>d.Target)
                .FirstOrDefault(o=>o?.GetType().FullName=="ManagerSync.ManagerSync");
            if(instance==null)throw new InvalidOperationException("Load the supported AOSP SyncManager first.");
            var type=instance.GetType();
            var settings=type.GetField("_settings",BindingFlags.Public|BindingFlags.Static)?.GetValue(null) as Settings;
            if(settings==null || !settings["Enable"].AsBool())throw new InvalidOperationException("Enable SyncManager first.");
            return Tuple.Create(settings,type);
        }
        internal static int CurrentChannel() => Discover().Item1["IPCChannel"].AsInt32();
        internal FormationMovementLease()
        {
            var found=Discover(); settings=found.Item1; manager=found.Item2;
            channel=manager.GetField("_IPCChannel",BindingFlags.Public|BindingFlags.Static)?.GetValue(null) as IPCChannel;
            var callbacks=typeof(IPCChannelBase).GetField("_callbacks",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(channel)
                as Dictionary<int,List<Action<int,IPCMessage>>>;
            if(callbacks==null || !callbacks.TryGetValue(200,out moves))throw new InvalidOperationException("Movement receiver unavailable.");
            receiver=moves.SingleOrDefault(d=>d.Method.DeclaringType?.FullName=="ManagerSync.Receiver" && d.Method.Name=="OnMoveMessage");
            if(receiver==null)throw new InvalidOperationException("Unrecognised movement receiver; formation was not started.");
            index=moves.IndexOf(receiver);previous=settings["SyncMove"].AsBool();
            settings["SyncMove"]=false;
            moves.Remove(receiver);
        }
        internal bool Intact => !disposed && !settings["SyncMove"].AsBool()
            && settings["Enable"].AsBool() && !moves.Contains(receiver)
            && ReferenceEquals(channel,manager.GetField("_IPCChannel",BindingFlags.Public|BindingFlags.Static).GetValue(null));
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            if(!moves.Contains(receiver))moves.Insert(Math.Min(index,moves.Count),receiver);
            // Do not overwrite a manual re-enable made while formation was running.
            if(!settings["SyncMove"].AsBool())settings["SyncMove"]=previous;
        }
    }
}
