using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using AOSharp.Core;
using AOSharp.Core.UI;
using NexusPlayTools;

namespace NexusShortcuts
{
    // Extend the loaded SyncManager's existing XML and window, retaining its callbacks.
    internal sealed class SyncFormationControls : IDisposable
    {
        internal static readonly string[] Names = { "Column", "Line", "Wedge", "Echelon left", "Echelon right", "Ring" };
        readonly NexusFormation formation;
        readonly Action save;
        readonly string directory;
        Settings ui;
        SyncUiModel model;
        object instance;
        Type controller;
        IDictionary paths;
        object pathKey;
        string originalPath, generatedPath;
        IntPtr bound;
        Window window;
        internal bool Ready => model != null;
        internal SyncFormationControls(NexusFormation formation, Action save, string directory)
        { this.formation=formation; this.save=save; this.directory=directory; }

        internal static string ExtendXml(string source)
        {
            var doc = XDocument.Parse(source);
            if (doc.Descendants().Any(e => (string)e.Attribute("name") == "NexusFormationType")) return source;
            var movement = doc.Descendants().FirstOrDefault(e => (string)e.Attribute("opt_variable") == "SyncManager__SyncMove");
            if (movement == null) throw new InvalidDataException("This SyncManager layout has no Movement control.");
            var controls = XElement.Parse("<View view_layout=\"vertical\" layout_borders=\"Rect(22,3,5,5)\">" +
                "<OptionCheckBox label=\"Formation\" opt_category=\"AOSharp\" opt_type=\"variant\" opt_variable=\"NexusFormation__Enabled\"/>" +
                "<ComboBox name=\"NexusFormationType\" min_size=\"Point(180,20)\"/>" +
                "<View view_layout=\"horizontal\"><TextView value=\"Spacing (metres)  \"/><TextInputView name=\"NexusFormationSpacing\" min_size=\"Point(45,20)\" max_size=\"Point(60,20)\"/></View>" +
                "<Button name=\"NexusFormationApply\" label=\"Apply formation\"/>" +
                "<TextView name=\"NexusFormationStatus\" value=\"Off\"/>" +
                "<TextView value=\"Enable on the character you want to lead.\"/>" +
                "<TextView value=\"Local teammates on this sync channel follow.\"/></View>");
            movement.AddAfterSelf(controls); return doc.ToString();
        }
        void Discover()
        {
            var current = Game.OnUpdate?.GetInvocationList().Select(d=>d.Target).FirstOrDefault(o=>o!=null && o.GetType().FullName=="SyncManager.SyncManager" && o.GetType().Assembly.GetName().Name=="SyncManager");
            if (ReferenceEquals(instance,current)) return;
            Restore(); instance=current;
            if (current==null) return;
            model=new SyncUiModel(current);
            controller=current.GetType().Assembly.GetType("SyncManager.SettingsController");
            paths=(IDictionary)controller.GetField("settingsWindows").GetValue(null);
            foreach (DictionaryEntry pair in paths) {
                string file=pair.Value as string;
                if (file==null || !File.Exists(file)) continue;
                string xml=File.ReadAllText(file);
                if (!xml.Contains("SyncManager__SyncMove")) continue;
                Directory.CreateDirectory(directory); generatedPath=Path.Combine(directory,"NexusSyncManager.xml");
                File.WriteAllText(generatedPath,ExtendXml(xml)); pathKey=pair.Key;originalPath=file;break;
            }
            if (pathKey!=null) paths[pathKey]=generatedPath;
            if(ui==null) {ui=new Settings("NexusFormation");ui.AddVariable("Enabled",false);}
            ui["Enabled"]=false; // Saved preferences never start movement on load.
        }
        internal void Update()
        {
            Discover();
            if (model==null) {formation.UpdateTeam(-1);return;}
            int previousMode=formation.Mode;
            formation.UpdateTeam(model.Channel);
            if(previousMode!=formation.Mode)RefreshEnabled();
            var next=(Window)controller.GetField("settingsWindow").GetValue(null);
            if (next!=null && next.IsValid && next.FindView("NexusFormationType",out ComboBox shape)) {
                bool newWindow=!ReferenceEquals(window,next);window=next;
                if (bound!=next.Pointer || newWindow) {
                    bound=next.Pointer;shape.Clear();for(int i=0;i<Names.Length;i++)shape.AppendItem(i,Names[i]);
                    shape.Text=Names[(int)formation.Options.Shape];
                    if(next.FindView("NexusFormationSpacing",out TextInputView metres))metres.Text=formation.Options.Side.ToString("0.0",CultureInfo.InvariantCulture);
                    if(next.FindView("NexusFormationApply",out Button button))button.Clicked=(s,e)=> {try{Apply(ui["Enabled"].AsBool());}catch(Exception ex){ui["Enabled"]=formation.Mode!=0;Chat.WriteLine("Formation: "+ex.Message);}};
                    ui["Enabled"]=formation.Mode!=0;
                }
                bool enabled=ui["Enabled"].AsBool();
                if(enabled!=(formation.Mode!=0)) { try {Apply(enabled);} catch(Exception ex){ui["Enabled"]=formation.Mode!=0;Chat.WriteLine("Formation: "+ex.Message);} }
                if(next.FindView("NexusFormationStatus",out TextView status))status.Text=formation.Status;
            }
        }
        // Called after movement safety checks, so a stop cannot be mistaken for a new click.
        internal void RefreshEnabled() {if(ui!=null)ui["Enabled"]=formation.Mode!=0;}
        void Apply(bool enabled)
        {
            if(!enabled) {formation.Stop("Formation off");RefreshEnabled();}
            if(window==null || !window.IsValid) return;
            if(!window.FindView("NexusFormationType",out ComboBox shape) || !window.FindView("NexusFormationSpacing",out TextInputView spacing))return;
            int index=Array.IndexOf(Names,shape.Text);if(index<0)throw new ArgumentException("Choose a formation from the list.");
            float metres;if(!float.TryParse(spacing.Text.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out metres))throw new ArgumentException("Spacing must be a number from 1 to 20 metres.");
            Configure(enabled,index,metres);
        }
        internal void Configure(bool enabled,int shape,float metres)
        {
            // Turning off must always work, even while a distance field is invalid.
            if(!enabled) {formation.Stop("Formation off");RefreshEnabled();}
            var options=new FormationOptions {Shape=(FormationShape)shape,Side=metres,Row=metres,Behind=metres};options.Validate();
            if(enabled) {if(model==null)throw new InvalidOperationException("Load SyncManager first.");formation.LeadTeam(options.Shape,metres,model.Channel);}
            else {formation.Stop("Formation off");formation.Options=options;}
            RefreshEnabled();save();
        }
        void Restore()
        {
            if(paths!=null && pathKey!=null && (string)paths[pathKey]==generatedPath)paths[pathKey]=originalPath;
            if(window!=null && window.IsValid)window.Close();
            if(model!=null)formation.Stop("SyncManager unloaded");
            model=null;paths=null;pathKey=null;bound=IntPtr.Zero;window=null;
        }
        public void Dispose(){Restore();}
    }
}
