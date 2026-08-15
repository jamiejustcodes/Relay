using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Relay.Core.Interfaces;
using Relay.Core.Models;

namespace Relay.Infrastructure.Search;

public class WebSearchService : ISearchService
{
    private readonly HttpClient _httpClient;

    public WebSearchService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<SearchResultItem>();

        var results = new List<SearchResultItem>();

        try
        {
            // DuckDuckGo instant answer & HTML fallback for quick zero-config web searching
            string encodedQuery = Uri.EscapeDataString(query.Trim());
            string url = $"https://html.duckduckgo.com/html/?q={encodedQuery}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            using var response = await _httpClient.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                string html = await response.Content.ReadAsStringAsync(ct);

                // Extract search result links and snippets with regex
                var matches = Regex.Matches(html, @"<a class=""result__snippet""[^>]*href=""(?<url>[^""]+)""[^>]*>(?<snippet>.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                var titleMatches = Regex.Matches(html, @"<a class=""result__url""[^>]*>(?<title>.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

                for (int i = 0; i < Math.Min(matches.Count, 5); i++)
                {
                    string rawUrl = matches[i].Groups["url"].Value;
                    string snippet = CleanHtml(matches[i].Groups["snippet"].Value);
                    string title = i < titleMatches.Count ? CleanHtml(titleMatches[i].Groups["title"].Value) : query;

                    // Unpack DuckDuckGo redirect if present
                    if (rawUrl.Contains("uddg="))
                    {
                        var urlMatch = Regex.Match(rawUrl, @"uddg=(?<realUrl>[^&]+)");
                        if (urlMatch.Success)
                        {
                            rawUrl = Uri.UnescapeDataString(urlMatch.Groups["realUrl"].Value);
                        }
                    }

                    if (!string.IsNullOrEmpty(rawUrl) && !string.IsNullOrEmpty(snippet))
                    {
                        results.Add(new SearchResultItem(
                            Title: string.IsNullOrEmpty(title) ? query : title,
                            Url: rawUrl,
                            Snippet: snippet,
                            DisplayUrl: new Uri(rawUrl).Host
                        ));
                    }
                }
            }
        }
        catch
        {
            // Search error fallback
        }

        return results;
    }

    private static string CleanHtml(string rawHtml)
    {
        if (string.IsNullOrEmpty(rawHtml)) return string.Empty;
        string cleaned = Regex.Replace(rawHtml, @"<[^>]+>", " ");
        cleaned = System.Net.WebUtility.HtmlDecode(cleaned);
        return Regex.Replace(cleaned, @"\s+", " ").Trim();
    }
}
