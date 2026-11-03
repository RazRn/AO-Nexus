using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace AOLauncher
{
    internal static class OptionalComponentsTests
    {
        static int checks;
        static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;}
        static void Reject(Action action,string text){try{action();}catch(Exception ex) when(ex is IOException || ex is InvalidDataException || ex is ArgumentException || ex is OperationCanceledException){checks++;return;}throw new Exception(text);}
        static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
        sealed class Download : HttpMessageHandler
        {
            internal Func<HttpRequestMessage,HttpResponseMessage> Reply;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(Reply(request));}
        }
        static int Main()
        {
            string root=Path.Combine(Path.GetTempPath(),"NexusComponentsTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            var original=OptionalComponents.CreateHandler;
            try
            {
                byte[] contents=Encoding.UTF8.GetBytes("inert renderer fixture");
                string archive=Path.Combine(root,"fixture.zip");
                using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))using(var output=zip.CreateEntry("randy31.dll").Open())output.Write(contents,0,contents.Length);
                byte[] zipBytes=File.ReadAllBytes(archive);
                var descriptor=new OptionalComponents.Descriptor{Id="aovk",Version="components-test",Size=zipBytes.Length,RequiredFile="randy31.dll",Url=OptionalComponents.Repository+"/releases/download/components-test/AO-Nexus-aovk.zip",
                    Manifest=Json.Serialize(new{PayloadSha256=Hash(zipBytes),Files=new[]{new{Path="randy31.dll",Length=contents.Length,Sha256=Hash(contents)}}})};
                Check(OptionalComponents.Parse(Json.Serialize(descriptor),"aovk").Id=="aovk","Descriptor roundtrip");
                string validJson=Json.Serialize(descriptor);
                descriptor.Retired=true;
                Reject(()=>OptionalComponents.Parse(Json.Serialize(descriptor),"aovk"),"Retired renderer descriptor rejected");
                Reject(()=>OptionalComponents.InstallAsync(descriptor,root,CancellationToken.None,null).GetAwaiter().GetResult(),"Retired renderer cannot be downloaded or reused from cache");
                descriptor.Retired=false;
                Reject(()=>OptionalComponents.Parse(validJson.Replace("github.com","evil.invalid"),"aovk"),"Other repository rejected");
                Reject(()=>OptionalComponents.Parse(validJson.Replace("randy31.dll","../randy31.dll"),"aovk"),"Invalid required path rejected");
                Reject(()=>OptionalComponents.Parse(validJson,"aosharp"),"Wrong component rejected");
                int requests=0;
                OptionalComponents.CreateHandler=()=>new Download{Reply=request=>{
                    requests++;
                    if(request.RequestUri.Host=="github.com"){var redirect=new HttpResponseMessage(HttpStatusCode.Found);redirect.Headers.Location=new Uri("https://release-assets.githubusercontent.com/fixture");return redirect;}
                    return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(zipBytes)};
                }};
                string cache=Path.Combine(root,"cache");
                string installed=OptionalComponents.InstallAsync(descriptor,cache,CancellationToken.None,null).GetAwaiter().GetResult();
                Check(File.ReadAllBytes(Path.Combine(installed,"randy31.dll")).SequenceEqual(contents),"Verified download installed");
                Check(requests==2,"GitHub asset redirect followed");
                Check(OptionalComponents.InstallAsync(descriptor,cache,CancellationToken.None,null).GetAwaiter().GetResult()==installed && requests==2,"Verified cache works offline without requests");
                File.WriteAllText(Path.Combine(installed,"randy31.dll"),"changed");
                Reject(()=>OptionalComponents.Installed(descriptor,cache),"Modified installed payload rejected");
                string badCache=Path.Combine(root,"bad-cache");
                var damaged=(byte[])zipBytes.Clone();damaged[damaged.Length-1]^=1;File.WriteAllBytes(archive,damaged);
                Reject(()=>OptionalComponents.InstallArchive(descriptor,badCache,archive,CancellationToken.None),"Bad download checksum rejected");
                Check(!Directory.Exists(badCache)||Directory.GetDirectories(badCache,"*",SearchOption.AllDirectories).Length==0,"Bad archive never published");
                File.WriteAllBytes(archive,zipBytes);
                Reject(()=>OptionalComponents.InstallArchive(descriptor,badCache,archive,new CancellationToken(true)),"Cancellation before publish");
                OptionalComponents.CreateHandler=()=>new Download{Reply=request=>{var r=new HttpResponseMessage(HttpStatusCode.Found);r.Headers.Location=new Uri("http://evil.invalid/file");return r;}};
                Reject(()=>OptionalComponents.InstallAsync(descriptor,badCache,CancellationToken.None,null).GetAwaiter().GetResult(),"Unsafe redirect rejected");
                OptionalComponents.CreateHandler=()=>new Download{Reply=request=>new HttpResponseMessage(HttpStatusCode.NotFound)};
                Reject(()=>OptionalComponents.InstallAsync(descriptor,badCache,CancellationToken.None,null).GetAwaiter().GetResult(),"Unpublished package leaves setup alone");
                OptionalComponents.CreateHandler=()=>new Download{Reply=request=>new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(zipBytes.Take(4).ToArray())}};
                Reject(()=>OptionalComponents.InstallAsync(descriptor,badCache,CancellationToken.None,null).GetAwaiter().GetResult(),"Truncated package rejected");
                Check(!new LauncherSettings().AutoInjectPlugins,"Fresh install does not automatically load plugins");
                Check(AOSharpPack.Locate(new LauncherSettings{AOSharpDisabled=true},out var error)==null && error!=null,"Disabled runtime cannot be located for loading");
                ReportChecks();CurationChecks();RecoveryChecks();
                Console.WriteLine("PASS: "+checks+" optional download/install, runtime acknowledgement and curation checks; no game files touched.");return 0;
            }
            finally{OptionalComponents.CreateHandler=original;Directory.Delete(root,true);}
        }
        static void RecoveryChecks()
        {
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var type=typeof(PluginAutoInjector);
            var trackType=type.GetNestedType("Track",System.Reflection.BindingFlags.NonPublic);
            var injector=new PluginAutoInjector();
            var settings=new LauncherSettings{AOSharpDisabled=true};
            foreach(bool automatic in new[]{false,true})
            {
                var track=Activator.CreateInstance(trackType,true);
                var row=new PluginAutoInjector.Row{CharacterName="Inert fixture"};
                if(automatic)type.GetMethod("Probe",flags).Invoke(injector,new object[]{settings,row,track});
                else type.GetMethod("Inject",flags).Invoke(injector,new object[]{settings,row,new PluginSet(),track,PlayStyles.None,false});
                Check((bool)trackType.GetField("Finished",flags).GetValue(track),"Missing runtime stops attempts before injection");
                type.GetMethod("RetryAfterPackChange",flags).Invoke(injector,new object[]{track,settings});
                Check((bool)trackType.GetField("Finished",flags).GetValue(track),"Unchanged missing configuration does not retry repeatedly");
                injector.ForgetPack();
                type.GetMethod("RetryAfterPackChange",flags).Invoke(injector,new object[]{track,settings});
                Check(!(bool)trackType.GetField("Finished",flags).GetValue(track) && (int)trackType.GetField("Attempts",flags).GetValue(track)==0
                    && trackType.GetField("LastError",flags).GetValue(track)==null,"Changing the runtime makes a pre-injection failure eligible again");
                trackType.GetField("Finished",flags).SetValue(track,true);
                injector.ForgetPack();type.GetMethod("RetryAfterPackChange",flags).Invoke(injector,new object[]{track,settings});
                Check((bool)trackType.GetField("Finished",flags).GetValue(track),"Completed or deliberately stopped loads stay stopped");
            }
        }
        static void ReportChecks()
        {
            var client=new PluginHostClient.ClientState{Pid=42,StartTicks=123,RequestId="new",Plugins=new[]{"good.dll","bad.dll","pending.dll"}};
            string json=Json.Serialize(new PluginRuntimeReport.Report{Format=1,Pid=42,StartTicks=123,RequestId="new",Plugins=new List<PluginRuntimeReport.Entry>{
                new PluginRuntimeReport.Entry{Path="good.dll",State="Started"},new PluginRuntimeReport.Entry{Path="bad.dll",State="Failed",Error="Example init failure"}},
                Conflicts=new List<PluginRuntimeReport.Conflict>{new PluginRuntimeReport.Conflict{Resource="/follow",Previous="good.dll",Current="bad.dll"}}});
            var results=PluginRuntimeReport.Parse(json,client,path=>null);
            Check(results[0].Status==PluginStatus.Confirmed,"Initialization acknowledgement accepted");
            Check(results[1].Status==PluginStatus.Failed && results[1].Message.Contains("init failure"),"Startup failure retained");
            Check(results[2].Status==PluginStatus.Pending,"Missing entry stays unconfirmed");
            Check(results[3].Status==PluginStatus.Conflict && results[3].Problem,"Conflicts surfaced");
            Check(PluginRuntimeReport.Parse(json.Replace("\"new\"","\"old\""),client,path=>null)==null,"Old request cannot acknowledge reload");
            client.StartTicks++;
            Check(PluginRuntimeReport.Parse(json,client,path=>null)==null,"Reused PID rejected");
        }
        static void CurationChecks()
        {
            var z=new PluginInfo{Id="pack:Z.Handler.Doc",Suite="Z",Group="Profession handlers"};
            var aosp=new PluginInfo{Id="pack:ProfessionHandler.Doc",Suite="AOSP",Group="Profession handlers"};
            var list=new[]{z,aosp};
            Check(PluginCuration.Tier(z,list)=="Recommended" && PluginCuration.Tier(aosp,list)=="Alternatives","One recommended handler suite");
            Check(PluginCuration.Tier(aosp,new[]{aosp})=="Recommended","AOSP fallback without Z");
            Check(PluginCuration.Caution(new PluginInfo{Id="pack:Z.TeamAcceptUtility"})!=null,"Invite behaviour visible");
            var settings=new LauncherSettings{PluginSetsInitialized=true,PluginSets=new List<PluginSet>{new PluginSet{Name="Doctor",Plugins=new List<string>{aosp.Id}}}};
            PluginSets.EnsureStarterSets(settings,new AOSharpPack{Plugins=list.ToList()});
            Check(settings.PluginSets[0].Plugins.Single()==aosp.Id,"Existing handler choice is preserved");
        }
    }
}
