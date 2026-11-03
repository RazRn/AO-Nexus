using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

// Static load check for every plugin in an AOSharp pack: does each type and member a plugin (and the
// libraries next to it) uses exist in the assemblies it will run with? Resolution follows AOSharp's
// loader: the plugin's own folder, then the AOSharp folder, then .NET Framework 4.8. AOSharp itself
// hides plugins that fail to load, so the pack build runs this and stops on any problem.
//
// usage: PluginLoadCheck.exe <unpacked pack with plugins.json> <.NET Framework 4.8 reference assemblies>
// Prints one line per plugin (OK/FAIL) and exits with 1 when any plugin has a problem.
static class PluginCheck
{
    sealed class Resolver : BaseAssemblyResolver
    {
        readonly Dictionary<string, AssemblyDefinition> cache = new Dictionary<string, AssemblyDefinition>(StringComparer.OrdinalIgnoreCase);
        readonly string[] dirs;
        internal Resolver(params string[] dirs) { this.dirs = dirs; }
        public override AssemblyDefinition Resolve(AssemblyNameReference name)
        {
            if (cache.TryGetValue(name.Name, out var hit)) return hit;
            // AOSharp's loader never loads AOSharp.Core/Common/Bootstrap from a plugin folder: the copy
            // in the AOSharp folder is always used. Windows file names ignore case ("stateless.dll").
            var order = new[] { "AOSharp.Core", "AOSharp.Common", "AOSharp.Bootstrap" }.Contains(name.Name) ? dirs.Skip(1) : dirs;
            foreach (var dir in order.Where(Directory.Exists))
                foreach (var path in Directory.GetFiles(dir))
                {
                    string file = Path.GetFileName(path);
                    if (string.Equals(file, name.Name + ".dll", StringComparison.OrdinalIgnoreCase) || string.Equals(file, name.Name + ".exe", StringComparison.OrdinalIgnoreCase))
                        return cache[name.Name] = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = this });
                }
            throw new AssemblyResolutionException(name);
        }
    }

    static int Main(string[] args)
    {
        string root = args[0], framework = args[1];
        string aosharp = Path.Combine(root, "AOSharp");
        var catalog = (System.Collections.ArrayList)new System.Web.Script.Serialization.JavaScriptSerializer()
            .Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(root, "plugins.json")))["Plugins"];
        int bad = 0;
        foreach (Dictionary<string, object> item in catalog)
        {
            string id = (string)item["Id"], path = Path.Combine(root, ((string)item["Path"]).Replace('/', Path.DirectorySeparatorChar));
            string dir = Path.GetDirectoryName(path);
            var problems = new List<string>();
            var info = new List<string>();
            var helper = new List<string>();
            int members = 0;
            var resolver = new Resolver(dir, aosharp, framework, Path.Combine(framework, "Facades"));
            AssemblyDefinition main;
            try { main = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver }); }
            catch (Exception ex) { Console.WriteLine("FAIL\t" + id + "\tcannot read: " + ex.Message); bad++; continue; }

            // Entry point: a public class deriving from AOSharp.Core.AOPluginEntry (Nexus plugins too).
            bool entry = main.MainModule.Types.Any(type => Derives(type, "AOPluginEntry"));
            if (!entry) problems.Add("no AOPluginEntry class");

            // The plugin and every non-framework library it pulls in from its own folder.
            var queue = new Queue<AssemblyDefinition>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            queue.Enqueue(main); seen.Add(main.Name.Name);
            while (queue.Count > 0)
            {
                var assembly = queue.Dequeue();
                var module = assembly.MainModule;
                foreach (var reference in module.AssemblyReferences)
                {
                    AssemblyDefinition target;
                    try { target = resolver.Resolve(reference); }
                    catch { problems.Add(assembly.Name.Name + " needs missing assembly " + reference.Name + " " + reference.Version); continue; }
                    if (target.Name.Version != reference.Version && !IsFramework(target, framework)
                        && target.Name.PublicKeyToken != null && target.Name.PublicKeyToken.Length > 0)
                        // Microsoft helper libraries differ by version in stock AOSharp too; AOSharp's loader
                        // loads them from the plugin folder by file, so these are notes, not failures.
                        (reference.Name.StartsWith("System.") || reference.Name.StartsWith("Microsoft.") ? helper : problems).Add(reference.Name + " " + reference.Version + "→" + target.Name.Version);
                    else if (target.Name.Version != reference.Version && !IsFramework(target, framework))
                        info.Add(reference.Name + " " + reference.Version + "→" + target.Name.Version);
                    string location = target.MainModule.FileName;
                    bool local = location.StartsWith(dir, StringComparison.OrdinalIgnoreCase) || location.StartsWith(aosharp, StringComparison.OrdinalIgnoreCase);
                    if (local && seen.Add(target.Name.Name)) queue.Enqueue(target);
                }
                foreach (var type in module.GetTypeReferences())
                {
                    try { if (type.Resolve() == null) problems.Add(assembly.Name.Name + ": missing type " + type.FullName); }
                    catch (AssemblyResolutionException) { }
                }
                foreach (var member in module.GetMemberReferences())
                {
                    if (member.DeclaringType is ArrayType) continue;
                    members++;
                    try { if (member.Resolve() == null) problems.Add(assembly.Name.Name + ": missing " + member.FullName); }
                    catch (AssemblyResolutionException) { }
                }
            }
            problems = problems.Distinct().ToList();
            if (problems.Count > 0) bad++;
            Console.WriteLine((problems.Count == 0 ? "OK" : "FAIL") + "\t" + id + "\t" + string.Join(" | ", problems.Take(6)) + (problems.Count > 6 ? " | +" + (problems.Count - 6) + " more" : "")
                + "\t" + members + " calls checked" + (info.Count > 0 ? "; AOSharp " + string.Join(", ", info.Distinct()) : "") + (helper.Count > 0 ? "; helper libs " + helper.Distinct().Count() : ""));
        }
        Console.WriteLine("SUMMARY\t" + (catalog.Count - bad) + " ok, " + bad + " with problems, of " + catalog.Count);
        return bad == 0 ? 0 : 1;
    }

    static bool IsFramework(AssemblyDefinition assembly, string framework) =>
        assembly.MainModule.FileName.StartsWith(framework, StringComparison.OrdinalIgnoreCase);

    static bool Derives(TypeDefinition type, string name)
    {
        for (var current = type; current != null;)
        {
            if (current.BaseType == null) return false;
            if (current.BaseType.Name == name) return true;
            try { current = current.BaseType.Resolve(); } catch { return false; }
        }
        return false;
    }
}
