using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AnimeFolderOrganizer.Models;

namespace AnimeFolderOrganizer.Services;

public sealed class AnimeDbVerificationService : IAnimeDbVerificationService
{
    private const string SearchEndpoint = "https://db.animedb.jp/index.php/searchdata/?word=";
    private const string TermsCookie = "wptp_terms_261=accepted";
    private static readonly Regex TitleRegex = new("<h2 class=\"ttitle\">(.*?)</h2>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex BracketRegex = new(@"[\(（\[【〈＜<].*?[\)）\]】〉＞>]", RegexOptions.Compiled);
    private static readonly Regex SpaceRegex = new(@"[\s\u3000]+", RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, AnimeDbVerificationStatus> _cache = new(StringComparer.Ordinal);

    public AnimeDbVerificationService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<AnimeDbVerificationStatus> VerifyTitleAsync(string? title, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeTitle(title);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return AnimeDbVerificationStatus.Failed;
        }

        if (_cache.TryGetValue(normalized, out var cached))
        {
            return cached;
        }

        try
        {
            var url = $"{SearchEndpoint}{Uri.EscapeDataString(normalized)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("AnimeFolderOrganizer/1.0");
            request.Headers.Accept.ParseAdd("text/html");
            request.Headers.Add("Cookie", TermsCookie);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _cache[normalized] = AnimeDbVerificationStatus.Failed;
                return AnimeDbVerificationStatus.Failed;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var requested = NormalizeForMatch(title);
            if (string.IsNullOrWhiteSpace(requested))
            {
                _cache[normalized] = AnimeDbVerificationStatus.Failed;
                return AnimeDbVerificationStatus.Failed;
            }

            var matched = ExtractTitles(html).Any(candidate => IsTitleMatch(requested, candidate));
            var status = matched ? AnimeDbVerificationStatus.Verified : AnimeDbVerificationStatus.Failed;
            _cache[normalized] = status;
            return status;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AnimeDB verification failed: {ex}");
            _cache[normalized] = AnimeDbVerificationStatus.Failed;
            return AnimeDbVerificationStatus.Failed;
        }
    }

    private static IEnumerable<string> ExtractTitles(string html)
    {
        foreach (Match match in TitleRegex.Matches(html))
        {
            var raw = match.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var withoutTags = TagRegex.Replace(raw, string.Empty);
            var decoded = WebUtility.HtmlDecode(withoutTags);
            var trimmed = decoded.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                yield return trimmed;
            }
        }
    }

    private static bool IsTitleMatch(string requestedTitle, string candidate)
    {
        var normalizedCandidate = NormalizeForMatch(candidate);
        if (string.IsNullOrWhiteSpace(normalizedCandidate)) return false;
        return normalizedCandidate.Contains(requestedTitle, StringComparison.OrdinalIgnoreCase)
               || requestedTitle.Contains(normalizedCandidate, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        var trimmed = title.Trim();
        var withoutBrackets = BracketRegex.Replace(trimmed, string.Empty);
        return withoutBrackets.Trim();
    }

    private static string NormalizeForMatch(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        var cleaned = BracketRegex.Replace(title, string.Empty);
        cleaned = TagRegex.Replace(cleaned, string.Empty);
        cleaned = WebUtility.HtmlDecode(cleaned);
        cleaned = cleaned
            .Replace("・", string.Empty)
            .Replace("･", string.Empty)
            .Replace("：", string.Empty)
            .Replace(":", string.Empty)
            .Replace("！", string.Empty)
            .Replace("!", string.Empty)
            .Replace("？", string.Empty)
            .Replace("?", string.Empty)
            .Replace("～", string.Empty)
            .Replace("〜", string.Empty)
            .Replace("‐", string.Empty)
            .Replace("‑", string.Empty)
            .Replace("−", string.Empty)
            .Replace("-", string.Empty)
            .Replace("—", string.Empty)
            .Replace("–", string.Empty)
            .Replace(".", string.Empty);
        cleaned = SpaceRegex.Replace(cleaned, string.Empty);
        return cleaned.Trim().ToLowerInvariant();
    }
}
