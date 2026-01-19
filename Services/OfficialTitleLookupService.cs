using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AnimeFolderOrganizer.Models;

namespace AnimeFolderOrganizer.Services;

public sealed partial class OfficialTitleLookupService : IOfficialTitleLookupService
{
    private const string TmdbBaseUrl = "https://api.themoviedb.org/3";
    private const string BangumiSearchUrl = "https://api.bgm.tv/search/subject/";
    private const string BangumiSubjectUrl = "https://api.bgm.tv/subject/";
    private const string AniListEndpoint = "https://graphql.anilist.co";

    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;
    private readonly ITextConverter _textConverter;

    public OfficialTitleLookupService(HttpClient httpClient, ISettingsService settingsService, ITextConverter textConverter)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _textConverter = textConverter;
    }

    public async Task<OfficialTitleResult?> LookupAsync(string japaneseTitle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(japaneseTitle)) return null;
        var normalized = japaneseTitle.Trim();

        var tmdb = await TryGetFromTmdbAsync(normalized, cancellationToken);
        var bangumi = NeedsBangumi(tmdb) ? await TryGetFromBangumiAsync(normalized, cancellationToken) : null;
        var anilist = NeedsAnilist(tmdb, bangumi) ? await TryGetFromAniListAsync(normalized, cancellationToken) : null;

        var titleJP = FirstNonEmpty(tmdb?.TitleJP, bangumi?.TitleJP, anilist?.TitleJP, normalized);
        var titleCN = FirstNonEmpty(tmdb?.TitleCN, bangumi?.TitleCN);
        var titleTW = FirstNonEmpty(tmdb?.TitleTW, bangumi?.TitleTW);
        var titleEN = FirstNonEmpty(tmdb?.TitleEN, bangumi?.TitleEN, anilist?.TitleEN);

        if (string.IsNullOrWhiteSpace(titleTW) && !string.IsNullOrWhiteSpace(titleCN))
        {
            titleTW = _textConverter.ToTraditional(titleCN);
        }

        if (string.IsNullOrWhiteSpace(titleCN)
            && string.IsNullOrWhiteSpace(titleTW)
            && string.IsNullOrWhiteSpace(titleEN))
        {
            return null;
        }

        return new OfficialTitleResult(titleJP, titleCN, titleTW, titleEN);
    }

    private static bool NeedsBangumi(OfficialTitleResult? tmdb)
    {
        if (tmdb == null) return true;
        return string.IsNullOrWhiteSpace(tmdb.TitleCN)
               || string.IsNullOrWhiteSpace(tmdb.TitleTW)
               || string.IsNullOrWhiteSpace(tmdb.TitleEN);
    }

    private static bool NeedsAnilist(OfficialTitleResult? tmdb, OfficialTitleResult? bangumi)
    {
        var titleEN = FirstNonEmpty(tmdb?.TitleEN, bangumi?.TitleEN);
        return string.IsNullOrWhiteSpace(titleEN);
    }

    private async Task<OfficialTitleResult?> TryGetFromTmdbAsync(string japaneseTitle, CancellationToken cancellationToken)
    {
        var apiKey = _settingsService.TmdbApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        try
        {
            var searchUrl = $"{TmdbBaseUrl}/search/multi?api_key={Uri.EscapeDataString(apiKey)}&language=ja-JP&query={Uri.EscapeDataString(japaneseTitle)}&include_adult=false&page=1";
            using var searchResponse = await _httpClient.GetAsync(searchUrl, cancellationToken);
            if (!searchResponse.IsSuccessStatusCode) return null;

            await using var searchStream = await searchResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var searchDoc = await JsonDocument.ParseAsync(searchStream, cancellationToken: cancellationToken);
            if (!searchDoc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var selected = results.EnumerateArray()
                .Select(item =>
                {
                    var mediaType = item.TryGetProperty("media_type", out var mt) && mt.ValueKind == JsonValueKind.String
                        ? mt.GetString()
                        : string.Empty;
                    var id = item.TryGetProperty("id", out var idValue) && idValue.TryGetInt32(out var value)
                        ? value
                        : 0;
                    return new { MediaType = mediaType, Id = id };
                })
                .FirstOrDefault(x => x.Id > 0 && (x.MediaType == "tv" || x.MediaType == "movie"));

            if (selected == null) return null;

            var translationsUrl = $"{TmdbBaseUrl}/{selected.MediaType}/{selected.Id}/translations?api_key={Uri.EscapeDataString(apiKey)}";
            using var translationsResponse = await _httpClient.GetAsync(translationsUrl, cancellationToken);
            if (!translationsResponse.IsSuccessStatusCode) return null;

            await using var translationsStream = await translationsResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var translationsDoc = await JsonDocument.ParseAsync(translationsStream, cancellationToken: cancellationToken);
            if (!translationsDoc.RootElement.TryGetProperty("translations", out var translations)
                || translations.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            string? titleJP = null;
            string? titleCN = null;
            string? titleTW = null;
            string? titleEN = null;

            foreach (var item in translations.EnumerateArray())
            {
                var lang = item.TryGetProperty("iso_639_1", out var langValue) && langValue.ValueKind == JsonValueKind.String
                    ? langValue.GetString()
                    : null;
                var region = item.TryGetProperty("iso_3166_1", out var regionValue) && regionValue.ValueKind == JsonValueKind.String
                    ? regionValue.GetString()
                    : null;
                var title = ExtractTmdbTitle(item);
                if (string.IsNullOrWhiteSpace(title)) continue;

                if (lang == "zh" && region == "TW")
                {
                    titleTW ??= title;
                }
                else if (lang == "zh" && region == "CN")
                {
                    titleCN ??= title;
                }
                else if (lang == "en")
                {
                    titleEN ??= title;
                }
                else if (lang == "ja")
                {
                    titleJP ??= title;
                }
            }

            if (string.IsNullOrWhiteSpace(titleCN)
                && string.IsNullOrWhiteSpace(titleTW)
                && string.IsNullOrWhiteSpace(titleEN))
            {
                return null;
            }

            return new OfficialTitleResult(
                TitleJP: titleJP,
                TitleCN: titleCN,
                TitleTW: titleTW,
                TitleEN: titleEN);
        }
        catch
        {
            return null;
        }
    }

    private async Task<OfficialTitleResult?> TryGetFromBangumiAsync(string japaneseTitle, CancellationToken cancellationToken)
    {
        try
        {
            var searchUrl = $"{BangumiSearchUrl}{Uri.EscapeDataString(japaneseTitle)}?type=2&responseGroup=small";
            using var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            request.Headers.UserAgent.ParseAdd("AnimeFolderOrganizer/1.0");
            request.Headers.Accept.ParseAdd("application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!doc.RootElement.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var normalized = NormalizeTitle(japaneseTitle);
            int? selectedId = null;

            foreach (var item in list.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idValue) || !idValue.TryGetInt32(out var id))
                {
                    continue;
                }

                var name = item.TryGetProperty("name", out var nameValue) && nameValue.ValueKind == JsonValueKind.String
                    ? nameValue.GetString()
                    : null;
                if (!string.IsNullOrWhiteSpace(name) && NormalizeTitle(name) == normalized)
                {
                    selectedId = id;
                    break;
                }

                if (selectedId == null)
                {
                    selectedId = id;
                }
            }

            if (selectedId == null) return null;

            var subjectUrl = $"{BangumiSubjectUrl}{selectedId}";
            using var subjectRequest = new HttpRequestMessage(HttpMethod.Get, subjectUrl);
            subjectRequest.Headers.UserAgent.ParseAdd("AnimeFolderOrganizer/1.0");
            subjectRequest.Headers.Accept.ParseAdd("application/json");

            using var subjectResponse = await _httpClient.SendAsync(subjectRequest, cancellationToken);
            if (!subjectResponse.IsSuccessStatusCode) return null;

            await using var subjectStream = await subjectResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var subjectDoc = await JsonDocument.ParseAsync(subjectStream, cancellationToken: cancellationToken);
            var titleJP = subjectDoc.RootElement.TryGetProperty("name", out var jpValue) && jpValue.ValueKind == JsonValueKind.String
                ? jpValue.GetString()
                : null;
            var titleCN = subjectDoc.RootElement.TryGetProperty("name_cn", out var cnValue) && cnValue.ValueKind == JsonValueKind.String
                ? cnValue.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(titleCN)) return null;

            return new OfficialTitleResult(
                TitleJP: titleJP,
                TitleCN: titleCN,
                TitleTW: null,
                TitleEN: null);
        }
        catch
        {
            return null;
        }
    }

    private async Task<OfficialTitleResult?> TryGetFromAniListAsync(string japaneseTitle, CancellationToken cancellationToken)
    {
        try
        {
            var payload = new
            {
                query = @"query ($search: String) { Media(search: $search, type: ANIME) { title { native english romaji } } }",
                variables = new { search = japaneseTitle }
            };
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(AniListEndpoint, content, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!doc.RootElement.TryGetProperty("data", out var data)
                || !data.TryGetProperty("Media", out var media)
                || !media.TryGetProperty("title", out var title))
            {
                return null;
            }

            var titleJP = title.TryGetProperty("native", out var nativeValue) && nativeValue.ValueKind == JsonValueKind.String
                ? nativeValue.GetString()
                : null;
            var titleEN = title.TryGetProperty("english", out var englishValue) && englishValue.ValueKind == JsonValueKind.String
                ? englishValue.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(titleEN) && string.IsNullOrWhiteSpace(titleJP)) return null;

            return new OfficialTitleResult(
                TitleJP: titleJP,
                TitleCN: null,
                TitleTW: null,
                TitleEN: titleEN);
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractTmdbTitle(JsonElement item)
    {
        if (!item.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (data.TryGetProperty("name", out var nameValue) && nameValue.ValueKind == JsonValueKind.String)
        {
            return nameValue.GetString();
        }

        if (data.TryGetProperty("title", out var titleValue) && titleValue.ValueKind == JsonValueKind.String)
        {
            return titleValue.GetString();
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string NormalizeTitle(string title)
    {
        var trimmed = title.Trim();
        var withoutBrackets = BracketRegex().Replace(trimmed, string.Empty);
        var withoutSymbols = SymbolRegex().Replace(withoutBrackets, string.Empty);
        var withoutSpaces = SpaceRegex().Replace(withoutSymbols, string.Empty);
        return withoutSpaces.ToLowerInvariant();
    }

    [GeneratedRegex(@"[\(（\[【〈＜<].*?[\)）\]】〉＞>]", RegexOptions.Compiled)]
    private static partial Regex BracketRegex();

    [GeneratedRegex(@"[!?！？:：\-\—\–\.]", RegexOptions.Compiled)]
    private static partial Regex SymbolRegex();

    [GeneratedRegex(@"[\s\u3000]+", RegexOptions.Compiled)]
    private static partial Regex SpaceRegex();
}
