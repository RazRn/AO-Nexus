using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using AOLauncher;

internal static class NewsTests
{
    private static int checks;
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        checks++;
        Console.WriteLine("PASS " + description);
    }

    private static int Main(string[] args)
    {
        try
        {
            string directory = args[0];
            var live = ServerNews.Sources.Single(source => source.Id == "live");
            var prk = ServerNews.Sources.Single(source => source.Id == "prk");
            var rebirth = ServerNews.Sources.Single(source => source.Id == "rebirth");
            foreach (var source in ServerNews.Sources)
            {
                Check(ServerNews.SafeUrl(source, source.FeedUrl) != null, source.Id + " official HTTPS feed accepted");
                foreach (string bad in new[] { "javascript:alert(1)", "file:///C:/Windows/win.ini", "https://attacker.example/story", "https://127.0.0.1/private", "https://" + new Uri(source.FeedUrl).Host + ".attacker.example/", "https://user@" + new Uri(source.FeedUrl).Host + "/", "http://" + new Uri(source.FeedUrl).Host + "/", "https://" + new Uri(source.FeedUrl).Host + ":4433/" })
                    Check(ServerNews.SafeUrl(source, bad) == null, source.Id + " rejects unsupported URL " + bad);
            }
            Check(ServerNews.PlainText("<script>alert('x')</script><b>Good &amp; safe</b>\r\n title", 100) == "Good & safe title", "HTML and script are never rendered");
            Check(ServerNews.PlainText(new string('a', 400), 240).Length == 240, "article summary is bounded");
            Check(ServerNews.Parse(rebirth, "<html>Layout changed</html>").Count == 0, "changed Rebirth markup produces no invented news");
            Check(ServerNews.Parse(live, "<html>Layout changed</html>").Count == 0, "changed Funcom markup produces no invented news");
            string atom = "<feed xmlns='http://www.w3.org/2005/Atom'><entry><title>Earlier</title><link href='https://project-rk.com/posts/earlier/'/><updated>2020-01-01</updated><content>Old</content></entry><entry><title>Latest &amp; accurate</title><link href='https://www.project-rk.me/posts/latest/'/><updated>2026-10-24</updated><content>&lt;script&gt;bad&lt;/script&gt;Text</content></entry><entry><title>Unsafe</title><link href='https://attacker.example/'/><updated>2030-01-01</updated></entry></feed>";
            var parsedAtom = ServerNews.Parse(prk, atom);
            Check(parsedAtom.Count == 2, "unsafe feed links discarded");
            Check(parsedAtom[0].Title == "Latest & accurate", "Atom entries sorted newest first rather than document order");
            Check(parsedAtom[0].Url == "https://project-rk.com/posts/latest/", "official historical PRK domain maps to current origin");
            Check(parsedAtom[0].Summary == "Text", "Atom HTML is converted to safe plain text");
            bool rejectedDtd = false;
            try { ServerNews.Parse(prk, "<!DOCTYPE feed [<!ENTITY xxe SYSTEM 'file:///C:/Windows/win.ini'>]><feed xmlns='http://www.w3.org/2005/Atom'>&xxe;</feed>"); } catch (System.Xml.XmlException) { rejectedDtd = true; }
            Check(rejectedDtd, "XML external entities and DTDs disabled");
            bool rejectedOversized = false;
            try { ServerNews.Parse(live, new string('x', ServerNews.MaximumResponseBytes + 1)); } catch (InvalidDataException) { rejectedOversized = true; }
            Check(rejectedOversized, "oversized parser input rejected");
            string rebirthHtml = "<article class='guest-card guest-card--news'><time datetime='2026-10-23'></time><h3>Patch &amp; fixes</h3><p class='guest-card__excerpt'>Useful text</p><a href='news.php?id=62'>Read</a></article>";
            var parsedRebirth = ServerNews.Parse(rebirth, rebirthHtml);
            Check(parsedRebirth.Count == 1 && parsedRebirth[0].Title == "Patch & fixes", "Rebirth article title and entities parsed");
            Check(parsedRebirth[0].PublishedUtc == new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc), "Rebirth publication date preserved");
            Check(parsedRebirth[0].Url == "https://www.ao-rebirth.com/news.php?id=62", "Rebirth relative article link resolved");
            string funcomHtml = "<div class='post'><h2><a href='/news/test'>Announcement</a></h2><p class='lead'>Posted on June 25th, 2023 by Editor</p><p>Article body</p><p class='read-more'>Read more...</p></div><ul class='pagination'>";
            var parsedFuncom = ServerNews.Parse(live, funcomHtml);
            Check(parsedFuncom.Count == 1 && parsedFuncom[0].PublishedUtc.Value.Year == 2023, "Funcom ordinal dates parsed without making old news current");
            Check(parsedFuncom[0].Summary == "Article body", "Funcom metadata/navigation omitted from excerpt");
            Check(ServerNews.Parse(live, "<div class='post'><h2>Empty</h2></div><ul class='pagination'>").Count == 0, "missing Funcom article metadata is handled safely");

            string testCache = Path.Combine(directory, "news-fixture-cache-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testCache);
            Check(ServerNews.ReadCache(prk, testCache).Articles.Count == 0, "missing cache is harmless");
            File.WriteAllText(Path.Combine(testCache, "prk.json"), "invalid json");
            Check(ServerNews.ReadCache(prk, testCache).Articles.Count == 0, "corrupt cache is ignored");
            var snapshot = new NewsSnapshot { SourceId = "prk", FetchedUtc = DateTime.UtcNow, Articles = parsedAtom };
            File.WriteAllText(Path.Combine(testCache, "prk.json"), new JavaScriptSerializer().Serialize(snapshot));
            var cached = ServerNews.ReadCache(prk, testCache);
            Check(cached.IsCached && cached.Articles.Count == 2, "last-good cache round trips without network");
            var offline = ServerNews.FetchAsync(prk, testCache, CancellationToken.None, (source, token) => Task.FromException<string>(new System.Net.Http.HttpRequestException("Offline test"))).GetAwaiter().GetResult();
            Check(offline.IsCached && offline.Unavailable && offline.Articles.Count == 2, "offline fetch retains last-good articles and reports cached state");
            Check(offline.FetchedUtc == cached.FetchedUtc, "offline fetch preserves the original fetch timestamp");
            var layoutChanged = ServerNews.FetchAsync(prk, testCache, CancellationToken.None, (source, token) => Task.FromResult("<html>Source changed format</html>")).GetAwaiter().GetResult();
            Check(layoutChanged.IsCached && layoutChanged.Unavailable && layoutChanged.Articles.Count == 2, "changed source format retains good cache rather than erasing news");
            var failedEmpty = ServerNews.FetchAsync(live, testCache, CancellationToken.None, (source, token) => Task.FromException<string>(new System.Net.Http.HttpRequestException("Offline test"))).GetAwaiter().GetResult();
            Check(failedEmpty.Unavailable && !failedEmpty.IsCached && failedEmpty.Articles.Count == 0, "offline first launch reports unavailable without fabricated headlines");
            snapshot.Articles[0].Url = "https://evil.example/";
            File.WriteAllText(Path.Combine(testCache, "prk.json"), new JavaScriptSerializer().Serialize(snapshot));
            Check(ServerNews.ReadCache(prk, testCache).Articles.Count == 1, "cached article URLs are revalidated");
            snapshot.FetchedUtc = DateTime.UtcNow.AddDays(1);
            File.WriteAllText(Path.Combine(testCache, "prk.json"), new JavaScriptSerializer().Serialize(snapshot));
            Check(ServerNews.ReadCache(prk, testCache).Articles.Count == 0, "future-dated cache rejected");
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                bool canceled = false;
                try { ServerNews.RefreshAsync(testCache, cancellation.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { canceled = true; }
                Check(canceled, "cancellation stops news work before requests");
            }

            if (args.Contains("--live"))
            {
                string cache = Path.Combine(directory, "news-live-cache");
                var results = ServerNews.RefreshAsync(cache, CancellationToken.None).GetAwaiter().GetResult();
                foreach (var result in results)
                {
                    Check(!result.Unavailable && !result.IsCached && result.Articles.Count > 0, result.SourceId + " current live feed fetched and parsed");
                    var first = result.Articles[0];
                    Console.WriteLine("VERIFIED " + result.SourceId + " | " + first.PublishedUtc.Value.ToString("yyyy-MM-dd") + " | " + first.Title + " | " + first.Url);
                    Check(ServerNews.ReadCache(ServerNews.Sources.Single(source => source.Id == result.SourceId), cache).Articles.Count == result.Articles.Count, result.SourceId + " current feed cached successfully");
                }
            }
            Console.WriteLine("SUCCESS " + checks + " news checks passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
