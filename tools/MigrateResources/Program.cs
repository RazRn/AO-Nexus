using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

// One-time, syntax-aware migration. Literal values (including escapes and multiline
// concatenation) come from Roslyn, never regular-expression unescaping.
class Program
{
    static readonly Dictionary<string, (string En, string Da)> entries = new();
    static readonly Dictionary<string, string> legacy = new(StringComparer.Ordinal);
    static string Literal(ExpressionSyntax expression) => expression switch
    {
        LiteralExpressionSyntax value when value.IsKind(SyntaxKind.StringLiteralExpression) => value.Token.ValueText,
        BinaryExpressionSyntax sum when sum.IsKind(SyntaxKind.AddExpression) && Literal(sum.Left) is string left && Literal(sum.Right) is string right => left + right,
        _ => null
    };

    static string Key(string en, string da)
    {
        string slug = string.Join("_", Regex.Matches(en, "[A-Za-z0-9]+").Cast<Match>().Take(6).Select(m => m.Value));
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(en + "\0" + da))).Substring(0, 12);
        string key = "Text_" + slug + "_" + hash;
        entries[key] = (en, da);
        return key;
    }

    static void Main(string[] args)
    {
        string root = Path.GetFullPath(args[0]);
        string resources = Path.Combine(root, "src/Common/Resources");
        if (Directory.Exists(resources)) throw new InvalidOperationException("Resources already exist; this migration must not be reapplied.");
        var dictionaries = new[] { "src/Common/Texts.cs", "src/Launcher/GuiStrings.cs", "src/Launcher/NexusStrings.cs" };
        foreach (string relative in dictionaries)
        {
            string path = Path.Combine(root, relative);
            string source = File.ReadAllText(path);
            var tree = CSharpSyntaxTree.ParseText(source).GetRoot();
            var field = tree.DescendantNodes().OfType<FieldDeclarationSyntax>().Single(f => f.Declaration.Variables.Any(v => v.Identifier.ValueText == "Danish"));
            foreach (var pair in field.DescendantNodes().OfType<InitializerExpressionSyntax>().Where(i => i.Expressions.Count == 2))
            {
                string en = Literal(pair.Expressions[0]), da = Literal(pair.Expressions[1]);
                if (en != null && da != null) legacy[en] = da;
            }
            if (!relative.EndsWith("/Texts.cs"))
            {
                source = source.Remove(field.SpanStart, field.Span.Length).Insert(field.SpanStart,
                    "private static IDictionary<string, string> Danish => Texts.Translations;");
                source = source.Replace("internal static void Register() => Texts.AddTranslations(Danish);", "internal static void Register() { /* Resources are loaded by Texts. */ }");
                File.WriteAllText(path, source);
            }
        }
        foreach (var pair in legacy) Key(pair.Key, pair.Value);
        int changed = 0;
        foreach (string path in Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains("\\obj\\") && !p.Contains("\\bin\\") && !p.Contains("\\Build\\") && !p.EndsWith("\\Common\\Texts.cs")))
        {
            string source = File.ReadAllText(path);
            var tree = CSharpSyntaxTree.ParseText(source).GetRoot();
            var edits = new List<TextChange>();
            foreach (var call in tree.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                string name = call.Expression is MemberAccessExpressionSyntax access ? access.Name.Identifier.ValueText
                    : (call.Expression as IdentifierNameSyntax)?.Identifier.ValueText;
                var arguments = call.ArgumentList.Arguments;
                if (name == "L" && arguments.Count == 2 && Literal(arguments[0].Expression) is string en && Literal(arguments[1].Expression) is string da)
                {
                    edits.Add(new TextChange(call.Span, "AOLauncher.Localization.Texts.Get(\"" + Key(en, da) + "\")"));
                }
            }
            if (edits.Count > 0)
            {
                File.WriteAllText(path, SourceText.From(source).WithChanges(edits).ToString());
                changed += edits.Count;
            }
        }
        Directory.CreateDirectory(resources);
        foreach (string language in new[] { "en", "da" })
        {
            var document = new XDocument(new XElement("strings", new XAttribute("language", language),
                entries.OrderBy(p => p.Key).Select(p => new XElement("string", new XAttribute("key", p.Key),
                    new XAttribute(XNamespace.Xml + "space", "preserve"),
                    // English is retained as a lookup alias for existing T/F callers.
                    language == "en" && legacy.ContainsKey(p.Value.En) && legacy[p.Value.En] == p.Value.Da ? new XAttribute("legacy", "true") : null,
                    language == "en" ? p.Value.En : p.Value.Da))));
            using var writer = XmlWriter.Create(Path.Combine(resources, "Strings." + language + ".xml"),
                new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, NewLineHandling = NewLineHandling.Entitize });
            document.Save(writer);
        }
        Console.WriteLine($"Migrated {changed} inline pairs; {entries.Count} resource entries; {legacy.Count} legacy aliases.");
    }
}
