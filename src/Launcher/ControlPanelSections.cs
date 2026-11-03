using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal sealed class ControlPanelSectionPreference
    {
        public string Id { get; set; }
        public bool Collapsed { get; set; }
        public int Height { get; set; }
    }

    // A single scrolling stack keeps collapsed headers together and allows any order.
    internal sealed class ControlPanelSections : Panel
    {
        readonly List<Section> sections=new List<Section>();
        readonly Action changed;
        bool arranging;
        internal event Action ViewChanged;
        // Restoring focus to a previously selected card must not scroll the Team
        // header away just before a user clicks its movement controls.
        protected override Point ScrollToControl(Control activeControl) => DisplayRectangle.Location;
        internal ControlPanelSections(Action save)
        {
            Name="controlPanelSections";Dock=DockStyle.Fill;AutoScroll=true;
            BackColor=Theme.Background;DoubleBuffered=true;changed=save;
            SizeChanged+=(s,e)=>ArrangeSections();Scroll+=(s,e)=>ViewChanged?.Invoke();
        }
        internal void AddSection(string id,string title,Control content,int height)
        {
            var section=new Section(this,id,title,content,height);
            sections.Add(section);Controls.Add(section);ArrangeSections();
        }
        internal void Restore(IEnumerable<ControlPanelSectionPreference> saved,int oldSplitter)
        {
            var preferences=(saved??Enumerable.Empty<ControlPanelSectionPreference>()).Where(p=>p!=null).ToList();
            var ordered=new List<Section>();
            foreach(var p in preferences) {
                var section=sections.FirstOrDefault(s=>s.Id==p.Id);
                if(section==null || ordered.Contains(section))continue;
                section.Collapsed=p.Collapsed;section.ExpandedHeight=Math.Max(section.MinimumHeight,Math.Min(1600,p.Height>0?p.Height:section.ExpandedHeight));ordered.Add(section);
            }
            if(!preferences.Any(p=>p.Id=="damage") && oldSplitter>0)
                sections.First(s=>s.Id=="damage").ExpandedHeight=Math.Max(180,Math.Min(1600,oldSplitter));
            ordered.AddRange(sections.Where(s=>!ordered.Contains(s)));sections.Clear();sections.AddRange(ordered);ArrangeSections();
        }
        internal List<ControlPanelSectionPreference> Snapshot()=>sections.Select(s=>new ControlPanelSectionPreference {Id=s.Id,Collapsed=s.Collapsed,Height=s.ExpandedHeight}).ToList();
        internal void SetHeaderTools(string id,Control tools) => sections.First(s=>s.Id==id).SetTools(tools);
        internal void Toggle(string id) {var section=sections.First(s=>s.Id==id);section.Collapsed=!section.Collapsed;Changed();}
        internal void MoveSection(string id,int delta) {
            int index=sections.FindIndex(s=>s.Id==id),target=Math.Max(0,Math.Min(sections.Count-1,index+delta));
            if(index<0 || target==index)return;
            var section=sections[index];sections.RemoveAt(index);sections.Insert(target,section);Changed();
        }
        internal void ResizeSection(string id,int height) {var section=sections.First(s=>s.Id==id);section.ExpandedHeight=Math.Max(section.MinimumHeight,Math.Min(1600,height));Changed();}
        void Changed() {ArrangeSections();changed?.Invoke();}
        void ArrangeSections()
        {
            if(arranging || IsDisposed)return;arranging=true;
            try {
                int y=AutoScrollPosition.Y,width=Math.Max(320,ClientSize.Width-SystemInformation.VerticalScrollBarWidth-8);
                foreach(var section in sections) {
                    section.UpdateHeader(sections.IndexOf(section),sections.Count);
                    int headerHeight=section.HeaderHeight(width);
                    section.SetBounds(0,y,width,section.Collapsed?headerHeight:section.ExpandedHeight+headerHeight+6);
                    section.Content.Visible=!section.Collapsed;section.Grip.Visible=!section.Collapsed;
                    section.TabIndex=sections.IndexOf(section);y+=section.Height+6;
                }
                AutoScrollMinSize=new Size(0,y-AutoScrollPosition.Y);
            } finally {arranging=false;}
            ViewChanged?.Invoke();
        }
        sealed class Section : Panel
        {
            internal readonly string Id;
            internal readonly Control Content,Grip;
            internal int MinimumHeight=>Id=="timers"?400:180;
            internal bool Collapsed;
            internal int ExpandedHeight;
            readonly Button header,up,down;
            readonly Panel bar;
            Control tools;
            readonly string title;
            Point pressed;bool dragged;
            internal Section(ControlPanelSections owner,string id,string text,Control content,int height)
            {
                Id=id;title=text;Name="section-"+id;ExpandedHeight=height;Collapsed=id=="timers";Content=content;
                BackColor=Theme.Surface;
                header=new Button {Name="sectionHeader-"+id,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(10,0,0,0),FlatStyle=FlatStyle.Flat,BackColor=Theme.SurfaceRaised,ForeColor=Theme.Accent,AccessibleDescription=AOLauncher.Localization.Texts.Get("Text_Click_to_collapse_or_expand_Drag_C872027D408E")};
                up=new NexusButton {Name="sectionUp-"+id,Text="↑",AccessibleName=AOLauncher.Localization.Texts.Get("Text_Move_up_677FE7066A56")+text,Dock=DockStyle.Right,Width=34,FlatStyle=FlatStyle.Flat};
                down=new NexusButton {Name="sectionDown-"+id,Text="↓",AccessibleName=AOLauncher.Localization.Texts.Get("Text_Move_down_6C2AFEBD83DB")+text,Dock=DockStyle.Right,Width=34,FlatStyle=FlatStyle.Flat};
                bar=new Panel {Dock=DockStyle.Top,Height=34};bar.Controls.Add(header);bar.Controls.Add(up);bar.Controls.Add(down);
                Content.Dock=DockStyle.Fill;Controls.Add(Content);
                Grip=new Panel {Dock=DockStyle.Bottom,Height=6,Cursor=Cursors.SizeNS,BackColor=Theme.Border};Controls.Add(Grip);Controls.Add(bar);
                up.Click+=(s,e)=>owner.MoveSection(Id,-1);down.Click+=(s,e)=>owner.MoveSection(Id,1);
                header.Click+=(s,e)=>{if(!dragged)owner.Toggle(Id);};
                header.MouseDown+=(s,e)=>{pressed=e.Location;dragged=false;};
                header.MouseMove+=(s,e)=>{
                    if(e.Button!=MouseButtons.Left || dragged || new Rectangle(pressed.X-SystemInformation.DragSize.Width/2,pressed.Y-SystemInformation.DragSize.Height/2,SystemInformation.DragSize.Width,SystemInformation.DragSize.Height).Contains(e.Location))return;
                    dragged=true;header.DoDragDrop(this,DragDropEffects.Move);
                };
                header.FlatAppearance.BorderColor=Theme.Border;
                header.AllowDrop=true;
                header.DragEnter+=(s,e)=>header.BackColor=Theme.Selection;
                header.DragLeave+=(s,e)=>header.BackColor=Theme.SurfaceRaised;
                header.DragOver+=(s,e)=>{var source=e.Data.GetData(typeof(Section)) as Section;e.Effect=source!=null && source.Parent==owner && source!=this?DragDropEffects.Move:DragDropEffects.None;};
                header.DragDrop+=(s,e)=>{header.BackColor=Theme.SurfaceRaised;var source=e.Data.GetData(typeof(Section)) as Section;if(source!=null && source.Parent==owner)owner.MoveSection(source.Id,owner.sections.IndexOf(this)-owner.sections.IndexOf(source));};
                int startY=0,startHeight=0;
                Grip.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left){startY=Cursor.Position.Y;startHeight=ExpandedHeight;Grip.Capture=true;}};
                Grip.MouseMove+=(s,e)=>{if(Grip.Capture && e.Button==MouseButtons.Left)owner.ResizeSection(Id,startHeight+Cursor.Position.Y-startY);};
                Grip.MouseUp+=(s,e)=>Grip.Capture=false;
            }
            internal void UpdateHeader(int index,int count) {header.Text=(Collapsed?"▸  ":"▾  ")+title;up.Enabled=index>0;down.Enabled=index<count-1;}
            internal void SetTools(Control tools)
            {
                this.tools=tools;
                header.Dock=up.Dock=down.Dock=DockStyle.None;
                tools.Dock=DockStyle.None;bar.Controls.Add(tools);tools.BringToFront();
            }
            internal int HeaderHeight(int width)
            {
                if(tools==null)return 34;
                // Keep the title/reorder buttons one line high. Controls wrap beneath
                // them, instead of stretching the Team button into a tall empty block.
                header.SetBounds(0,0,Math.Max(90,width-68),30);
                up.SetBounds(width-68,0,34,30);down.SetBounds(width-34,0,34,30);
                int toolWidth=Math.Max(120,width-16);
                int toolHeight=tools.Visible?Math.Max(30,tools.GetPreferredSize(new Size(toolWidth,0)).Height):0;
                tools.SetBounds(8,34,toolWidth,toolHeight);
                bar.Height=tools.Visible?38+toolHeight:34;return bar.Height;
            }
        }
    }
}
