using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Xml;
using System.Xml.Linq;

namespace AOLauncher
{
    internal sealed class NewsArticle
    {
        public string SourceId { get; set; }
        public string Title { get; set; }
        public string Summary { get; set; }
        public string Url { get; set; }
        public DateTime? PublishedUtc { get; set; }
    }

    internal sealed class NewsSnapshot
    {
        public string SourceId { get; set; }
        public DateTime FetchedUtc { get; set; }
        public List<NewsArticle> Articles { get; set; } = new List<NewsArticle>();
        public bool IsCached { get; set; }
        public bool Unavailable { get; set; }
    }

    internal sealed class NewsSource
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string FeedUrl { get; private set; }
        public string SiteUrl { get; private set; }

        public NewsSource(string id, string name, string feedUrl, string siteUrl)
        {
            Id = id; Name = name; FeedUrl = feedUrl; SiteUrl = siteUrl;
        }
    }

    // News is plain text and explicit HTTPS links. No remote HTML or script is rendered.
    internal static class ServerNews
    {
        internal const int MaximumResponseBytes = 1024 * 1024;
        private static readonly TimeSpan RegexLimit = TimeSpan.FromMilliseconds(300);
        internal static readonly NewsSource[] Sources =
        {
            new NewsSource("live", "Funcom AO", "https://www.anarchy-online.com/news", "https://www.anarchy-online.com/news"),
            new NewsSource("prk", "Project Rubi-Ka", "https://project-rk.com/feed.xml", "https://project-rk.com/"),
            new NewsSource("rebirth", "AO Rebirth", "https://www.ao-rebirth.com/index.php", "https://www.ao-rebirth.com/all_news.php")
        };
        private static readonly HttpClient Client = CreateClient();

        internal static string[] NormalizeSourceIds(IEnumerable<string> sourceIds)
        {
            var publicIds = Sources.Select(source => source.Id).ToArray();
            if (sourceIds == null) return publicIds;
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sourceId in sourceIds)
            {
                string id = (sourceId ?? string.Empty).Trim();
                if (string.Equals(id, "all", StringComparison.OrdinalIgnoreCase))
                    selected.UnionWith(publicIds);
                else if (string.Equals(id, "live-2019", StringComparison.OrdinalIgnoreCase))
                    selected.Add("live");
                else
                    selected.Add(id);
            }
            return publicIds.Concat(new[] { "custom" }).Where(selected.Contains).ToArray();
        }

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false, UseCookies = false,
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AONexus/1.0 (desktop news reader)");
            return client;
        }

        internal static string CacheDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AONexus", "News");

        internal static Task<NewsSnapshot[]> RefreshAsync(CancellationToken cancellationToken)
        {
            return RefreshAsync(CacheDirectory, cancellationToken);
        }

        internal static Task<NewsSnapshot[]> RefreshAsync(string cacheDirectory, CancellationToken cancellationToken)
        {
            return Task.WhenAll(Sources.Select(source => FetchAsync(source, cacheDirectory, cancellationToken)));
        }

        internal static Task<NewsSnapshot> FetchAsync(NewsSource source, string cacheDirectory, CancellationToken cancellationToken)
        {
            return FetchAsync(source, cacheDirectory, cancellationToken, DownloadAsync);
        }

        internal static async Task<NewsSnapshot> FetchAsync(NewsSource source, string cacheDirectory, CancellationToken cancellationToken, Func<NewsSource, CancellationToken, Task<string>> download)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = ReadCache(source, cacheDirectory);
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(12));
                    string content = await download(source, timeout.Token).ConfigureAwait(false);
                    var articles = Parse(source, content);
                    if (articles.Count == 0) throw new InvalidDataException("The source returned no recognized articles.");
                    var fresh = new NewsSnapshot { SourceId = source.Id, FetchedUtc = DateTime.UtcNow, Articles = articles };
                    WriteCache(fresh, cacheDirectory);
                    return fresh;
                }
            }
            catch (Exception error) when (error is HttpRequestException || error is IOException || error is InvalidDataException || error is XmlException || error is RegexMatchTimeoutException || error is OperationCanceledException || error is InvalidOperationException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                cached.IsCached = cached.Articles.Count != 0;
                cached.Unavailable = true;
                return cached;
            }
        }

        private static async Task<string> DownloadAsync(NewsSource source, CancellationToken cancellationToken)
        {
            var uri = new Uri(source.FeedUrl);
            for (int redirects = 0; redirects <= 3; redirects++)
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
                using (var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                    {
                        Uri next;
                        if (redirects == 3 || response.Headers.Location == null || !Uri.TryCreate(uri, response.Headers.Location, out next) || !IsAllowedUrl(source.Id, next))
                            throw new HttpRequestException("Unsupported news redirect.");
                        uri = next;
                        continue;
                    }
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw new InvalidDataException("News response exceeds the size limit.");
                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new MemoryStream())
                    {
                        var buffer = new byte[8192];
                        int count;
                        while ((count = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            if (output.Length + count > MaximumResponseBytes) throw new InvalidDataException("News response exceeds the size limit.");
                            output.Write(buffer, 0, count);
                        }
                        output.Position = 0;
                        using (var reader = new StreamReader(output, Encoding.UTF8, true)) return reader.ReadToEnd();
                    }
                }
            }
            throw new HttpRequestException("Too many news redirects.");
        }

        internal static List<NewsArticle> Parse(NewsSource source, string content)
        {
            if (content == null || content.Length > MaximumResponseBytes) throw new InvalidDataException("Invalid news response.");
            IEnumerable<NewsArticle> articles;
            if (source.Id == "prk") articles = ParseAtom(source, content);
            else if (source.Id == "rebirth") articles = ParseRebirth(source, content);
            else articles = ParseFuncom(source, content);
            return articles.Where(item => !string.IsNullOrWhiteSpace(item.Title) && SafeUrl(source, item.Url) != null)
                .GroupBy(item => item.Url, StringComparer.OrdinalIgnoreCase).Select(group => group.First())
                .OrderByDescending(item => item.PublishedUtc ?? DateTime.MinValue).Take(6).ToList();
        }

        private static IEnumerable<NewsArticle> ParseAtom(NewsSource source, string content)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumResponseBytes };
            XDocument document;
            using (var reader = XmlReader.Create(new StringReader(content), settings)) document = XDocument.Load(reader, LoadOptions.None);
            XNamespace ns = "http://www.w3.org/2005/Atom";
            foreach (var entry in document.Descendants(ns + "entry"))
            {
                var link = entry.Elements(ns + "link").FirstOrDefault(element => (string)element.Attribute("rel") == null || (string)element.Attribute("rel") == "alternate");
                string url = link == null ? null : (string)link.Attribute("href");
                // The current official Atom feed still uses its former project-rk.me origin.
                // Keep the article path and use the .com origin serving that same feed today.
                Uri old;
                if (Uri.TryCreate(url, UriKind.Absolute, out old) && (old.Host == "project-rk.me" || old.Host == "www.project-rk.me"))
                    url = "https://project-rk.com" + old.PathAndQuery;
                yield return Article(source, (string)entry.Element(ns + "title"), (string)entry.Element(ns + "summary") ?? (string)entry.Element(ns + "content"), url,
                    (string)entry.Element(ns + "published") ?? (string)entry.Element(ns + "updated"));
            }
        }

        private static IEnumerable<NewsArticle> ParseRebirth(NewsSource source, string content)
        {
            foreach (Match block in Matches(content, @"<article\b[^>]*class\s*=\s*[""'][^""']*guest-card--news[^""']*[""'][^>]*>(.*?)</article>"))
            {
                string html = block.Groups[1].Value;
                yield return Article(source, Capture(html, @"<h3\b[^>]*>(.*?)</h3>"), Capture(html, @"<p\b[^>]*class\s*=\s*[""'][^""']*guest-card__excerpt[^""']*[""'][^>]*>(.*?)</p>"),
                    Capture(html, @"<a\b[^>]*href\s*=\s*[""']([^""']*news\.php\?id=\d+)[""']"), Capture(html, @"<time\b[^>]*datetime\s*=\s*[""']([^""']+)[""']"));
            }
        }

        private static IEnumerable<NewsArticle> ParseFuncom(NewsSource source, string content)
        {
            foreach (Match block in Matches(content, @"<div\b[^>]*class\s*=\s*[""']post[""'][^>]*>(.*?)(?=<div\b[^>]*class\s*=\s*[""']post[""']|<ul\b[^>]*class\s*=\s*[""']pagination[""'])"))
            {
                string html = block.Groups[1].Value;
                string heading = Capture(html, @"<h2\b[^>]*>(.*?)</h2>");
                string date = Capture(html, @"Posted on\s+(.+?)\s+by\s");
                date = Regex.Replace(date, @"(\d+)(st|nd|rd|th)\b", "$1", RegexOptions.IgnoreCase, RegexLimit);
                string body = Regex.Replace(html, @"^.*?</h2>.*?</p>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexLimit);
                body = Regex.Replace(body, @"<p\b[^>]*class\s*=\s*[""']read-more[""'].*$", "", RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexLimit);
                yield return Article(source, heading, body, Capture(heading, @"href\s*=\s*[""']([^""']+)[""']"), date);
            }
        }

        private static NewsArticle Article(NewsSource source, string title, string summary, string url, string date)
        {
            DateTime parsed;
            return new NewsArticle
            {
                SourceId = source.Id, Title = PlainText(title, 160), Summary = PlainText(summary, 240), Url = SafeUrl(source, url),
                PublishedUtc = DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed) ? (DateTime?)parsed : null
            };
        }

        internal static string PlainText(string value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value)) return "";
            value = Regex.Replace(value, @"<(script|style)\b[^>]*>.*?</\1\s*>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexLimit);
            value = Regex.Replace(value, @"<[^>]+>", " ", RegexOptions.Singleline, RegexLimit);
            value = WebUtility.HtmlDecode(value);
            value = new string(value.Where(c => !char.IsControl(c) || char.IsWhiteSpace(c)).ToArray());
            value = Regex.Replace(value, @"\s+", " ", RegexOptions.None, RegexLimit).Trim();
            return value.Length > maximumLength ? value.Substring(0, maximumLength - 1).TrimEnd() + "…" : value;
        }

        private static string Capture(string input, string pattern)
        {
            var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexLimit);
            return match.Success ? match.Groups[1].Value : "";
        }

        private static MatchCollection Matches(string input, string pattern)
        {
            return Regex.Matches(input, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexLimit);
        }

        internal static string SafeUrl(NewsSource source, string value)
        {
            Uri uri;
            if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 || value.Any(char.IsControl) ||
                !Uri.TryCreate(new Uri(source.FeedUrl), WebUtility.HtmlDecode(value), out uri) || !IsAllowedUrl(source.Id, uri)) return null;
            return uri.AbsoluteUri;
        }

        internal static bool IsAllowedUrl(string sourceId, Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0) return false;
            string host = uri.IdnHost.ToLowerInvariant();
            if (sourceId == "live") return host == "anarchy-online.com" || host == "www.anarchy-online.com";
            if (sourceId == "prk") return host == "project-rk.com" || host == "www.project-rk.com";
            if (sourceId == "rebirth") return host == "ao-rebirth.com" || host == "www.ao-rebirth.com";
            return false;
        }

        internal static NewsSnapshot ReadCache(NewsSource source, string cacheDirectory)
        {
            var empty = new NewsSnapshot { SourceId = source.Id };
            try
            {
                string path = Path.Combine(cacheDirectory, source.Id + ".json");
                if (!File.Exists(path) || new FileInfo(path).Length > 128 * 1024) return empty;
                var snapshot = new JavaScriptSerializer { MaxJsonLength = 128 * 1024, RecursionLimit = 12 }.Deserialize<NewsSnapshot>(File.ReadAllText(path));
                if (snapshot == null || snapshot.SourceId != source.Id || snapshot.Articles == null || snapshot.FetchedUtc > DateTime.UtcNow.AddMinutes(5)) return empty;
                snapshot.Articles = snapshot.Articles.Where(a => a != null && a.SourceId == source.Id && SafeUrl(source, a.Url) != null)
                    .Take(6).Select(a => new NewsArticle { SourceId = source.Id, Title = PlainText(a.Title, 160), Summary = PlainText(a.Summary, 240), Url = SafeUrl(source, a.Url), PublishedUtc = a.PublishedUtc }).Where(a => a.Title.Length > 0).ToList();
                snapshot.IsCached = snapshot.Articles.Count > 0;
                return snapshot;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is InvalidOperationException || error is RegexMatchTimeoutException)
            {
                return empty;
            }
        }

        private static void WriteCache(NewsSnapshot snapshot, string cacheDirectory)
        {
            string temporary = null;
            try
            {
                Directory.CreateDirectory(cacheDirectory);
                string path = Path.Combine(cacheDirectory, snapshot.SourceId + ".json");
                temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(snapshot), Encoding.UTF8);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                // A read-only user profile does not prevent current news from being displayed.
            }
            finally
            {
                if (temporary != null) try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        internal static NewsSnapshot[] PreviewSnapshots()
        {
            // Verified source headlines on 24 October 2026. Used only by explicit UI preview mode.
            return new[]
            {
                Preview("rebirth", "Update for nanos & weapons !", "https://www.ao-rebirth.com/news.php?id=62", new DateTime(2026, 10, 23)),
                Preview("prk", "May Newsletter", "https://project-rk.com/posts/0023-may-newsletter/", new DateTime(2026, 5, 29)),
                Preview("live", "22nd Anniversary Celebration", "https://www.anarchy-online.com/news/22nd-anniversary-celebration", new DateTime(2023, 6, 25))
            };
        }

        private static NewsSnapshot Preview(string source, string title, string url, DateTime date)
        {
            return new NewsSnapshot { SourceId = source, FetchedUtc = new DateTime(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc), Articles = new List<NewsArticle> { new NewsArticle { SourceId = source, Title = title, Url = url, PublishedUtc = date, Summary = "" } } };
        }
    }
}

