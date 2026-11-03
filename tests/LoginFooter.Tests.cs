using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Windows.Forms;

namespace AOLauncher
{
    internal static class LoginFooterTests
    {
        static int checks;
        static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
        static Control Field(MainForm form, string name) => (Control)typeof(MainForm).GetField(name, Flags).GetValue(form);
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        static void Pump() { for (int i=0; i<4; i++) Application.DoEvents(); }
        [STAThread]
        static int Main()
        {
            try { Run(); Console.WriteLine("PASS: " + checks + " login footer bounds and transition checks; no games launched."); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        static void Run()
        {
            Application.EnableVisualStyles(); ServerRules.Install();
            foreach (string language in new[] { "en", "da" })
            foreach (float scale in new[] { 1f, 1.5f })
            using (var form = new MainForm(true)) {
                var settings = (LauncherSettings)typeof(MainForm).GetField("settings", Flags).GetValue(form);
                settings.Language = language;
                typeof(MainForm).GetMethod("ApplyLanguage", Flags).Invoke(form, null);
                form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-18000,-18000); form.ShowInTaskbar = false;
                if (scale != 1) form.Scale(new SizeF(scale,scale));
                form.Show(); Pump();
                foreach (int width in new[] {1280,1100,1500,1280}) {
                    form.Size = new Size((int)(width*scale), (int)(930*scale)); Pump();
                    foreach (bool running in new[] {false,true,false,true,false}) {
                        typeof(MainForm).GetMethod("SetRunning", Flags).Invoke(form, new object[] {running}); Pump();
                        var start = Field(form,"startButton"); var stop = Field(form,"stopButton"); var auto = Field(form,"autoLoadCheck");
                        var launch = start.Parent;
                        Check(launch.ClientRectangle.Contains(start.Bounds), language+" start button overflows launch panel after running="+running+": "+start.Bounds+" / "+launch.ClientRectangle);
                        Check(launch.Parent.ClientRectangle.Contains(launch.Bounds), "Launch panel stays inside footer");
                        var active = running ? stop : auto;
                        Check(active.Parent.ClientRectangle.Contains(active.Bounds), "Active footer option fits its row: " + language + "/" + scale + "/" + running + " " + active.Bounds + " / " + active.Parent.ClientRectangle);
                        Check(stop.Visible == running && auto.Visible != running, "Stop and auto-load replace one another");
                        var graphicsLink=Field(form,"graphicsLink");
                        Check(launch.ClientRectangle.Contains(graphicsLink.Bounds) && graphicsLink.Enabled != running, "Graphics entry fits and cannot change during login");
                    }
                }
                using(var bitmap=new Bitmap(form.Width,form.Height))
                using(var graphics=Graphics.FromImage(bitmap)) {
                    var dc=graphics.GetHdc(); try { Check(PrintWindow(form.Handle,dc,0),"Footer preview renders"); } finally {graphics.ReleaseHdc(dc);}
                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"login-footer-"+language+"-"+(int)(scale*100)+".png"));
                }
            }
            GraphicsSelection();
        }
        static void Finish(Task task) {
            var clock=Stopwatch.StartNew();
            while(!task.IsCompleted && clock.ElapsedMilliseconds<5000) { Application.DoEvents(); Thread.Sleep(1); }
            Check(task.IsCompleted,"Graphics read completes"); task.GetAwaiter().GetResult();
        }
        static void GraphicsSelection()
        {
            Localization.Texts.SetLanguage("en");
            Check(MainForm.GraphicsStatusText(new bool?[]{true,true}).Contains("on ·"),"All folders on");
            Check(MainForm.GraphicsStatusText(new bool?[]{false,false}).Contains("off ·"),"All folders off");
            Check(MainForm.GraphicsStatusText(new bool?[]{true,false}).Contains("mixed"),"Mixed folder modes are explicit");
            Check(MainForm.GraphicsStatusText(new bool?[]{true,null}).Contains("check setup"),"Unknown files are not reported as stock");
            using(var form=new MainForm(true)) {
                var settings=(LauncherSettings)typeof(MainForm).GetField("settings",Flags).GetValue(form);
                settings.Language="en"; settings.Accounts.Clear();
                var first=new AccountConfig {GamePath=@"C:\NexusGraphicsFixture\A"};
                first.Characters.Add(new CharacterConfig {Name="Fixture",Selected=true});settings.Accounts.Add(first);
                var shared=new AccountConfig {GamePath=@"c:\NexusGraphicsFixture\A\"};
                shared.Characters.Add(new CharacterConfig {Selected=true});settings.Accounts.Add(shared);
                var other=new AccountConfig {GamePath=@"C:\NexusGraphicsFixture\B"};
                other.Characters.Add(new CharacterConfig {Selected=false});settings.Accounts.Add(other);
                Check(form.SelectedGraphicsFolders().Length==1,"Deduplicate selected folders; ignore unselected accounts");
                form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-18000,-18000);form.ShowInTaskbar=false;form.Show();Pump();
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                using(var entered=new ManualResetEvent(false))
                using(var release=new ManualResetEvent(false)) {
                    form.ReadGraphicsMode=folder=> {if(folder.EndsWith("A",StringComparison.OrdinalIgnoreCase)){entered.Set();release.WaitOne(5000);return true;}return false;};
                    var slow=form.RefreshGraphicsStatusAsync(true);
                    Check(entered.WaitOne(2000) && !slow.IsCompleted,"Renderer validation runs in background");
                    first.Characters[0].Selected=shared.Characters[0].Selected=false;other.Characters[0].Selected=true;
                    Finish(form.RefreshGraphicsStatusAsync(true));
                    Check(Field(form,"graphicsLink").Text.Contains("off ·"),"New selection reports its own renderer");
                    release.Set();Finish(slow);
                    Check(Field(form,"graphicsLink").Text.Contains("off ·"),"Late old selection cannot overwrite new status");
                }
                using(var dialog=new SettingsDialog(settings,other.GamePath)) {
                    dialog.ShowGraphics();
                    Check(dialog.settingsTabs.SelectedIndex==4,"Shortcut opens Graphics tab");
                    Check((string)((ComboBox)dialog.Controls.Find("graphicsGameFolder",true).Single()).SelectedItem==other.GamePath,"Graphics opens selected character folder, not first account");
                }
            }
        }
    }
}
