using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NexusShortcuts;
static class SyncUiTests
{
    sealed class Model : ISyncUiModel {
        public uint Values { get; private set; }
        public int Channel { get; set; } = 3;
        public float Tick { get; set; } = .1f;
        public string Character => "Fixture";
        public uint Profile => 123;
        public int Saved, Closed, Changes;
        public void Set(uint i,bool v) { Values = v ? Values | (1u << (int)i) : Values & ~(1u << (int)i); Changes++; }
        public void Save() { Saved++; }
        public void CloseClassic() { Closed++; }
    }
    sealed class Transport : IUiTransport {
        internal bool Ready = true;
        internal UiState Last;
        internal Queue<UiCommand> Commands = new Queue<UiCommand>();
        public bool Exchange(ref UiState s, ref UiCommand c) {
            Last=s;
            if((s.Flags&4)!=0) Commands.Clear();
            c=Commands.Count>0 ? Commands.Dequeue() : new UiCommand{Size=20};
            return Ready;
        }
        internal void Send(uint kind,uint index=0,uint value=0,float scalar=0) { Commands.Enqueue(new UiCommand{Size=20,Kind=kind,Index=index,Value=value,Scalar=scalar}); }
    }
    static int checks;
    static void Check(bool value,string message) { if(!value)throw new Exception(message); checks++; }
    static int Main() {
        try {
            Check(Marshal.SizeOf(typeof(UiState))==92,"native state ABI size");
            Check(Marshal.OffsetOf(typeof(UiState),"Character").ToInt32()==28,"native text ABI offset");
            Check(Marshal.SizeOf(typeof(UiCommand))==20,"native command ABI size");
            var model=new Model(); var transport=new Transport(); var ui=new SyncUiSession(model,transport);
            int classic=0, persisted=0; ui.Classic=()=>classic++; ui.PersistPreference=()=>persisted++;
            transport.Ready=false;
            Check(!ui.Open() && model.Closed==0,"missing/unsupported renderer retains classic window");
            transport.Ready=true; ui.Modern=false;
            Check(!ui.Open() && model.Closed==0,"opt-out retains classic window");
            ui.Modern=true;
            Check(ui.Open() && model.Closed==1 && transport.Last.Flags==3,"supported renderer opens modern and closes classic");
            Check(transport.Last.Character=="Fixture" && transport.Last.Profile==123,"character and layout identity passed");
            for(uint i=0;i<12;i++) {
                transport.Send(1,i,1); ui.Update(); Check((model.Values&(1u<<(int)i))!=0,"live toggle on "+i);
                transport.Send(1,i,0); ui.Update(); Check((model.Values&(1u<<(int)i))==0,"live toggle off "+i);
            }
            int changes=model.Changes;
            transport.Send(1,12,1); transport.Send(1,0,2); transport.Send(2,0,256);
            transport.Send(3,0,0,float.NaN); transport.Send(3,0,0,float.PositiveInfinity); transport.Send(3,0,0,-1);
            transport.Send(999); ui.Update();
            Check(changes==model.Changes && model.Channel==3 && model.Tick==.1f,"invalid native commands cannot alter settings");
            transport.Send(2,0,255); transport.Send(3,0,0,.25f); ui.Update();
            Check(model.Channel==255 && model.Tick==.25f,"connection edits use shared model");
            transport.Send(4); ui.Update(); Check(model.Saved==1,"Save invokes plugin persistence");
            transport.Send(5); ui.Update();
            Check(!ui.Modern && classic==1 && persisted==1 && transport.Last.Flags==4,"classic switch persists choice and releases capture");
            ui.Modern=true;
            for(int i=0;i<40;i++)transport.Send(4);
            ui.Update(); Check(model.Saved==17 && transport.Commands.Count==24,"per-update action processing is bounded");
            ui.Close(); Check(transport.Commands.Count==0 && transport.Last.Flags==4,"zoning/unload closes and discards queued actions");
            Check(new NativeUiTransport().Available==false,"standalone process needs no renderer or native injection");
            Console.WriteLine("PASS: "+checks+" Sync UI bridge and fallback checks; no game interaction."); return 0;
        } catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
