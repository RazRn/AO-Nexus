using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace AOLauncher
{
    internal static class LogWrapTests
    {
        static int checks;
        static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window,IntPtr dc,uint flags);
        static T Field<T>(LogViewer form,string name) => (T)typeof(LogViewer).GetField(name,Flags).GetValue(form);
        static void Refresh(LogViewer form) { typeof(LogViewer).GetMethod("RefreshLog",Flags).Invoke(form,null);Pump(); }
        static void Pump() { for(int i=0;i<4;i++) Application.DoEvents(); }
        static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
        [STAThread] static int Main()
        {
            try { Run(); Console.WriteLine("PASS: "+checks+" wrap, scrolling, selection, search, colour and follow checks; synthetic log only."); return 0; }
            catch(Exception error){Console.Error.WriteLine(error);return 1;}
        }
        static void Run()
        {
            Application.EnableVisualStyles(); NexusStrings.Register();
            string source="[12:00:00] Ready.\r\n[12:00:01] Warning: "+string.Join(" ",Enumerable.Repeat("Long diagnostic message",35))+"\r\n[12:00:02] Failed to start fixture.";
            foreach(string language in new[]{"en","da"})
            foreach(float scale in new[]{1f,1.5f})
            using(var form=new LogViewer()) {
                Localization.Texts.SetLanguage(language);form.RefreshLanguage();
                form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-18000,-18000);form.ShowInTaskbar=false;
                if(scale!=1)form.Scale(new SizeF(scale,scale));
                form.Show();form.SetLogText(source);Refresh(form);
                var output=Field<RichTextBox>(form,"output");var follow=Field<CheckBox>(form,"follow");var search=Field<TextBox>(form,"search");
                var wrap=Field<CheckBox>(form,"wrap");var host=(SlimScrollHost)output.Parent.Parent;
                Check(wrap.Text==(language=="en"?"Wrap lines":"Ombryd linjer"),"Toggle translated");
                int changes=0;form.WrapLinesChanged=value=>changes++;
                follow.Checked=false;Refresh(form);
                int position=output.Text.IndexOf("Failed");output.Select(position,6);Color errorColour=output.SelectionColor;
                string original=output.Text;int unwrapped=output.GetLineFromCharIndex(output.TextLength);
                Check(!output.WordWrap && host.HorizontalBar.Visible,"Unwrapped long lines have horizontal scrolling");
                form.WrapLines=true;Pump();
                Check(output.WordWrap && !host.HorizontalBar.Visible,"Wrapping removes horizontal scrolling");
                Check(output.GetLineFromCharIndex(output.TextLength)>unwrapped,"Long entries visibly wrap");
                Check(output.Text==original && output.SelectedText=="Failed" && output.SelectionColor==errorColour,"Text, selection and error colour survive wrapping");
                form.WrapLines=false;Pump();
                Check(host.HorizontalBar.Visible && output.Text==original && output.SelectedText=="Failed","Unwrap restores scroll without losing text/selection");
                Check(changes==2,"Preference callback receives each change");
                search.Text="Warning";Refresh(form);string filtered=output.Text;form.WrapLines=true;Pump();
                Check(output.Text==filtered && !output.Text.Contains("Failed") && Field<Label>(form,"count").Text.StartsWith("1"),"Wrapping preserves filtering and logical entry count");
                search.Clear();follow.Checked=true;form.SetLogText(source+"\r\n[12:00:03] Finished.");Refresh(form);
                Check(output.WordWrap && output.SelectionStart==output.TextLength && output.Text.EndsWith("Finished."),"Following new entries preserves wrap mode");
                foreach(int width in new[]{900,600,900}) {
                    form.Size=new Size((int)(width*scale),(int)(540*scale));Pump();
                    Check(wrap.Parent.ClientRectangle.Contains(wrap.Bounds),"Toggle stays within toolbar at "+width+" / "+scale);
                    Check(search.Width>50,"Search stays usable");
                }
                using(var bitmap=new Bitmap(form.Width,form.Height))
                using(var graphics=Graphics.FromImage(bitmap)) {
                    var dc=graphics.GetHdc();try{Check(PrintWindow(form.Handle,dc,0),"Log preview renders");}finally{graphics.ReleaseHdc(dc);}
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"log-wrap-"+language+"-"+(int)(scale*100)+".png"));
                }
            }
        }
    }
}
