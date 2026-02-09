using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AnimeFolderOrganizer.Models;

namespace AnimeFolderOrganizer.Services;

public sealed class SubShareSubtitleDownloadService : ISubShareSubtitleDownloadService
{
    private static readonly HashSet<string> SupportedSubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ass",
        ".ssa",
        ".srt",
        ".sub",
        ".txt"
    };

    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SubShareSubtitleDownloadService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<bool?> HasRemoteSubtitlesAsync(string repoPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(repoPath)) return null;

        try
        {
            var normalized = NormalizeRepoPath(repoPath);
            var listResult = await ListSubtitleFilesRecursiveAsync(normalized, cancellationToken);
            if (listResult.ErrorMessage != null)
            {
                return null;
            }

            return listResult.Files.Count > 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public async Task<SubShareSubtitleDownloadResult> DownloadAsync(
        AnimeFolderInfo info,
        string destinationRootFolder,
        CancellationToken cancellationToken)
    {
        if (info == null)
        {
            return new SubShareSubtitleDownloadResult(0, 0, "參數錯誤：info 為 null");
        }

        if (string.IsNullOrWhiteSpace(info.SubShareRepoPath))
        {
            return new SubShareSubtitleDownloadResult(0, 0, "此項目未命中 sub_share，無可下載字幕。");
        }

        if (string.IsNullOrWhiteSpace(destinationRootFolder) || !Directory.Exists(destinationRootFolder))
        {
            return new SubShareSubtitleDownloadResult(0, 0, "下載目的資料夾無效或不存在。");
        }

        try
        {
            var repoRoot = NormalizeRepoPath(info.SubShareRepoPath);
            var localRoot = Path.Combine(destinationRootFolder, SanitizePathSegment(GetRepoLastSegment(repoRoot)));
            Directory.CreateDirectory(localRoot);

            var listResult = await ListSubtitleFilesRecursiveAsync(repoRoot, cancellationToken);
            if (listResult.ErrorMessage != null)
            {
                return new SubShareSubtitleDownloadResult(0, 0, listResult.ErrorMessage);
            }

            var downloaded = 0;
            var skipped = 0;

            foreach (var file in listResult.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relative = GetRelativeRepoPath(repoRoot, file.Path);
                var localRelative = SanitizeRelativePath(relative);
                var localPath = Path.Combine(localRoot, localRelative);

                var localDir = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrWhiteSpace(localDir))
                {
                    Directory.CreateDirectory(localDir);
                }

                if (File.Exists(localPath))
                {
                    skipped++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(file.DownloadUrl))
                {
                    skipped++;
                    continue;
                }

                var bytesResult = await DownloadBytesAsync(file.DownloadUrl!, cancellationToken);
                if (bytesResult.ErrorMessage != null)
                {
                    return new SubShareSubtitleDownloadResult(downloaded, skipped, bytesResult.ErrorMessage);
                }

                await File.WriteAllBytesAsync(localPath, bytesResult.Bytes!, cancellationToken);
                downloaded++;
            }

            return new SubShareSubtitleDownloadResult(downloaded, skipped, null);
        }
        catch (OperationCanceledException)
        {
            // 取消不視為錯誤，由呼叫端自行決定要不要提示
            throw;
        }
        catch (Exception ex)
        {
            return new SubShareSubtitleDownloadResult(0, 0, $"下載失敗：{ex.Message}");
        }
    }

    private async Task<(List<GithubContentFile> Files, string? ErrorMessage)> ListSubtitleFilesRecursiveAsync(
        string repoRootPath,
        CancellationToken cancellationToken)
    {
        var results = new List<GithubContentFile>();
        var pending = new Stack<string>();
        pending.Push(repoRootPath);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = pending.Pop();
            var contentsResult = await GetContentsAsync(current, cancellationToken);
            if (contentsResult.ErrorMessage != null)
            {
                return (results, contentsResult.ErrorMessage);
            }

            foreach (var item in contentsResult.Items)
            {
                if (string.Equals(item.Type, "dir", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(item.Path))
                    {
                        pending.Push(item.Path);
                    }
                    continue;
                }

                if (!string.Equals(item.Type, "file", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Path))
                {
                    continue;
                }

                if (!IsSupportedSubtitle(item.Name))
                {
                    continue;
                }

                results.Add(new GithubContentFile(item.Path, item.DownloadUrl));
            }
        }

        return (results, null);
    }

    private async Task<(List<GithubContentItem> Items, string? ErrorMessage)> GetContentsAsync(
        string repoPath,
        CancellationToken cancellationToken)
    {
        var url = BuildContentsApiUrl(repoPath);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("AnimeFolderOrganizer/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return (new List<GithubContentItem>(), $"GitHub Contents API 取得失敗 ({(int)response.StatusCode}): {TrimForError(json)}");
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var list = JsonSerializer.Deserialize<List<GithubContentItem>>(doc.RootElement.GetRawText(), _jsonOptions)
                           ?? new List<GithubContentItem>();
                return (list, null);
            }

            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                var single = JsonSerializer.Deserialize<GithubContentItem>(doc.RootElement.GetRawText(), _jsonOptions);
                return (single != null ? new List<GithubContentItem> { single } : new List<GithubContentItem>(), null);
            }

            return (new List<GithubContentItem>(), "GitHub Contents API 回傳格式不支援。");
        }
        catch (JsonException ex)
        {
            return (new List<GithubContentItem>(), $"GitHub Contents API JSON 解析失敗: {ex.Message}");
        }
    }

    private async Task<(byte[]? Bytes, string? ErrorMessage)> DownloadBytesAsync(string downloadUrl, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        request.Headers.UserAgent.ParseAdd("AnimeFolderOrganizer/1.0");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return (null, $"字幕下載失敗 ({(int)response.StatusCode}): {TrimForError(body)}");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return (bytes, null);
    }

    private static bool IsSupportedSubtitle(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(ext)) return false;
        return SupportedSubtitleExtensions.Contains(ext);
    }

    private static string NormalizeRepoPath(string repoPath)
    {
        var trimmed = repoPath.Trim();
        while (trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            trimmed = trimmed.Substring(1);
        }
        return trimmed.Replace('\\', '/');
    }

    private static string BuildContentsApiUrl(string repoPath)
    {
        var normalized = NormalizeRepoPath(repoPath);
        var encoded = string.Join("/", normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        return $"https://api.github.com/repos/foxofice/sub_share/contents/{encoded}?ref=master";
    }

    private static string GetRepoLastSegment(string repoPath)
    {
        var normalized = NormalizeRepoPath(repoPath).TrimEnd('/');
        var idx = normalized.LastIndexOf('/');
        return idx >= 0 ? normalized[(idx + 1)..] : normalized;
    }

    private static string GetRelativeRepoPath(string repoRootPath, string fullRepoPath)
    {
        var root = NormalizeRepoPath(repoRootPath).TrimEnd('/');
        var full = NormalizeRepoPath(fullRepoPath);

        if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            var rel = full[root.Length..];
            return rel.TrimStart('/');
        }

        return full;
    }

    /// <summary>
    /// 清理相對路徑，防止目錄遍歷攻擊 (Path Traversal)。GitHub 路徑可能包含惡意的 ".." 或 "." 片段，
    /// 若不阻擋會導致檔案被寫入目標資料夾之外的地方。
    /// </summary>
    private static string SanitizeRelativePath(string relativeRepoPath)
    {
        var normalized = relativeRepoPath.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var sanitizedSegments = segments
            .Select(SanitizePathSegment)
            .Select(segment => segment is "." or ".." ? "_" : segment) // 防止路徑遍歷
            .ToArray();
        return Path.Combine(sanitizedSegments);
    }

    private static string SanitizePathSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment)) return "_";

        var invalid = Path.GetInvalidFileNameChars();
        var chars = segment.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "_" : sanitized;
    }

    private static string TrimForError(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var trimmed = text.Trim();
        return trimmed.Length > 240 ? trimmed.Substring(0, 240) + "..." : trimmed;
    }

    private sealed record GithubContentFile(string Path, string? DownloadUrl);

    private sealed class GithubContentItem
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("path")]
        public string? Path { get; init; }

        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("download_url")]
        public string? DownloadUrl { get; init; }
    }
}
