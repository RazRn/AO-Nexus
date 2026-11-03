using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AOLauncher
{
    static class CombatScriptsTests
    {
        static IEnumerable<System.Windows.Forms.Control> All(System.Windows.Forms.Control c){foreach(System.Windows.Forms.Control child in c.Controls){yield return child;foreach(var item in All(child))yield return item;}}
        static int checks;
        static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
        [STAThread] static int Main()
        {
            try {
                var owner=new CombatStats {Damage=300,PetDamage=100,NanoDamage=50,Healing=40,Xp=600,Seconds=30,Native=true,Observed=1};
                owner.Pets.Add(new PetCombatStats {Id=1,Name="beep",Stats=new CombatStats {Damage=40,Seconds=30}});
                owner.Pets.Add(new PetCombatStats {Id=2,Name="beep",Stats=new CombatStats {Damage=60,Seconds=30}});
                var scope=new CombatReportScope {Key="team",Context="Test",Total=new CombatStats {Damage=200,Seconds=30,Native=true,Observed=2},Rows=new List<CombatReportRow>{
                    new CombatReportRow {Name="Geek",Profession="Engi",Stats=owner},new CombatReportRow {Name="Missing"}}};
                var reports=CombatScripts.Build(new[]{scope},new DateTime(2026,11,1,16,0,0));
                var damage=reports.Single(r=>r.Command=="nexus-team-dmg");
                Check(damage.Preview.Contains("10/s | 600/m | 36k/h"),"Rates preserve elapsed session time including sub-minute sessions");
                Check(damage.Preview.Contains("Total 300") && damage.Preview.Contains("Control Panel total 200"),"Independent reset totals are labelled separately");
                Check(damage.Preview.Contains("200 avg/m"),"Damage footer matches Control Panel average across measured rows");
                Check(damage.Preview.Contains("Missing - not measured"),"Missing telemetry is not zero damage");
                var pets=reports.Single(r=>r.Command=="nexus-team-pets");
                Check(Regex.Matches(pets.Preview,"beep ").Count==1 && pets.Preview.Contains("100 |"),"Respawns group by owner and current pet name");
                Check(!pets.Preview.Contains("#1") && !pets.Preview.Contains("#2"),"Pet IDs are not shared");
                Check(reports.Single(r=>r.Command=="nexus-team-xp").Preview.Contains("600"),"Native XP is included");
                scope.Rows[0].Stats.Native=false;
                Check(CombatScripts.Build(new[]{scope},DateTime.Now).Single(r=>r.Command=="nexus-team-xp").Preview.Contains("No measurements"),"Unavailable XP is explicit");
                scope.Rows[0].Name="bad\r\n/start x <a href=\"oops\">%m&';";
                var hostile=CombatScripts.Files(CombatScripts.Build(new[]{scope},DateTime.Now));
                Check(!Encoding.ASCII.GetString(hostile["nexus-team-dmg"]).Contains("%m"),"Names cannot expand game macros");
                Check(!Encoding.ASCII.GetString(hostile["nexus-team-dmg"]).Contains("\n/start"),"Names cannot inject script commands");
                var large=new CombatReportScope {Key="all",Context=new string('x',96)};
                for(int i=0;i<128;i++) {
                    var stats=new CombatStats {Damage=1000000000000000,PetDamage=1000000000000,Seconds=0.01};
                    for(int p=0;p<2;p++)stats.Pets.Add(new PetCombatStats {Name="Pet"+p+new string('p',90),Stats=new CombatStats {Damage=100000000000,Seconds=0.01}});
                    large.Rows.Add(new CombatReportRow {Name="Actor"+i.ToString("D3")+new string('n',80),Profession="Engineer",Stats=stats});
                }
                var largeReports=CombatScripts.Build(new[]{large},DateTime.Now);
                Check(largeReports.Any(r=>r.Command=="nexus-all-pets-2"),"Large reports get bounded continuation commands");
                Check(!largeReports.Any(r=>r.Command.Contains("xp")),"Nearby observer does not invent other players' XP");
                var allLinks=largeReports.SelectMany(r=>r.Links).ToList();
                Check(allLinks.All(l=>Encoding.ASCII.GetByteCount(l)<=1000),"All large report links fit the byte budget");
                Check(allLinks.All(l=>Regex.Matches(l,"<font ").Count==Regex.Matches(l,"</font>").Count && Regex.Matches(l,"<a ").Count==1 && l.EndsWith("</a>")),"Page boundaries preserve valid independent markup");
                var damagePages=string.Join("\n",largeReports.Where(r=>r.Command.StartsWith("nexus-all-dmg")).SelectMany(r=>r.Links));
                Check(Enumerable.Range(0,128).All(i=>damagePages.Contains("Actor"+i.ToString("D3"))),"Pagination never drops an actor");
                var files=CombatScripts.Files(largeReports);
                Check(files.Count<=GuiScripts.MaxScripts && files.All(f=>GuiScripts.ValidName(f.Key) && f.Value.Length<=GuiScripts.MaxScriptBytes),"Generated suite fits file count and size limits");
                Check(files.Where(f=>f.Key.EndsWith("-preview")).All(f=>Encoding.ASCII.GetString(f.Value).Split(new[]{"\r\n"},StringSplitOptions.RemoveEmptyEntries).All(l=>l.StartsWith("/text "))),"Preview scripts are strictly private");
                Check(!string.Join("",allLinks).Contains("chatcmd:") && !string.Join("",allLinks).Contains("showfile"),"Recipients do not need local files or commands");
                var npc=CombatScripts.Build(new[]{new CombatReportScope {Key="npc",Rows=new List<CombatReportRow>{new CombatReportRow {Name="Filth Flea",Stats=new CombatStats {Damage=12,DamageTaken=200,Seconds=10}}}}},DateTime.Now);
                Check(npc.Single(r=>r.Command=="nexus-npc-dealt").Preview.Contains("Total 12") && npc.Single(r=>r.Command=="nexus-npc-taken").Preview.Contains("Total 200"),"NPC dealt and taken remain separate");
                string root=Path.Combine(Path.GetTempPath(),"NexusCombatScriptsTests-"+Guid.NewGuid().ToString("N"));
                string prefs=Path.Combine(root,"Prefs"),backup=Path.Combine(root,"Backups");Directory.CreateDirectory(prefs);File.WriteAllText(Path.Combine(prefs,"Prefs.xml"),"<Root/>");
                var settings=new LauncherSettings();settings.Accounts.Add(new AccountConfig {PrefsPath=prefs});
                settings.ControlPanel.ScriptReport="nexus-team-pets";settings.ControlPanel.ScriptDestination=prefs;
                int saves=0;
                using(var dialog=new CombatScriptsDialog(settings,()=>reports,savePreferences:()=>saves++)) {
                    var list=All(dialog).OfType<System.Windows.Forms.ListBox>().Single();
                    Check(((CombatScriptReport)list.SelectedItem).Command=="nexus-team-pets","Exporter restores the selected report");
                    Check(All(dialog).OfType<System.Windows.Forms.ComboBox>().Single().SelectedItem.ToString()==GuiScripts.ScriptsRoot(prefs),"Exporter restores its destination");
                    Check(saves==0,"Opening exporter does not overwrite preferences");
                    list.SelectedIndex=0;
                    Check(settings.ControlPanel.ScriptReport=="nexus-team-dmg" && saves==1,"Changing report saves the new preference");
                }
                var initial=new Dictionary<string,byte[]> {{"nexus-team-dmg",Encoding.ASCII.GetBytes("original")}};
                GuiScripts.WriteReports(prefs,initial,backup);
                var transaction=GuiScripts.WriteReports(prefs,CombatScripts.Files(reports),backup);
                Check(File.ReadAllText(Path.Combine(root,"scripts","nexus-team-dmg")).Contains("text://"),"Save writes usable files beside Prefs");
                GuiScripts.Restore(prefs,transaction.Id,backup);
                Check(File.ReadAllText(Path.Combine(root,"scripts","nexus-team-dmg"))=="original","Overwritten scripts are recoverable through standard backups");
                GuiScripts.BeforeWrite=i=>{if(i==1)throw new IOException("Test write failure");};
                bool failed=false;try{GuiScripts.WriteReports(prefs,CombatScripts.Files(reports),backup);}catch(IOException){failed=true;}finally{GuiScripts.BeforeWrite=null;}
                Check(failed && File.ReadAllText(Path.Combine(root,"scripts","nexus-team-dmg"))=="original","Partial save failure rolls back overwritten scripts");
                Console.WriteLine("PASS: "+checks+" combat script checks. Fixture: "+root);return 0;
            }catch(Exception error){Console.Error.WriteLine(error);return 1;}
        }
    }
}
