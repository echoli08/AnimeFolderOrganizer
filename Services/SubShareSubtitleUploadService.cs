using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AnimeFolderOrganizer.Services;

/// <summary>
/// sub_share 字幕上傳前置包裝服務（不直接上傳，只建立本地上傳包）
/// </summary>
public sealed class SubShareSubtitleUploadService : ISubShareSubtitleUploadService
{
    public async Task<(bool Success, string Message)> CreateUploadPackageAsync(
        string repoPath,
        IReadOnlyList<string> filePaths,
        string destinationRootFolder,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(repoPath))
        {
            return (false, "sub_share 路徑無效，無法建立上傳包。");
        }

        if (filePaths == null || filePaths.Count == 0)
        {
            return (false, "未選擇任何字幕檔案。");
        }

        if (string.IsNullOrWhiteSpace(destinationRootFolder) || !Directory.Exists(destinationRootFolder))
        {
            return (false, "上傳包目的資料夾無效或不存在。");
        }

        try
        {
            var normalizedRepoPath = NormalizeRepoPath(repoPath);
            var packageRoot = Path.Combine(destinationRootFolder, SanitizeRelativePath(normalizedRepoPath));
            Directory.CreateDirectory(packageRoot);

            var copied = 0;
            foreach (var filePath in filePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    continue;
                }

                var fileName = Path.GetFileName(filePath);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                var targetPath = Path.Combine(packageRoot, SanitizePathSegment(fileName));
                if (File.Exists(targetPath))
                {
                    targetPath = ResolveDuplicateName(packageRoot, fileName);
                }

                await CopyFileAsync(filePath, targetPath, cancellationToken);
                copied++;
            }

            if (copied == 0)
            {
                return (false, "沒有可用的字幕檔案可建立上傳包。");
            }

            return (true, $"已建立上傳包，共 {copied} 個檔案。路徑：{packageRoot}");
        }
        catch (OperationCanceledException)
        {
            return (false, "上傳包建立已取消。");
        }
        catch (Exception ex)
        {
            return (false, $"建立上傳包失敗：{ex.Message}");
        }
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken ct)
    {
        const int bufferSize = 81920;
        await using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        await using var destStream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);
        await sourceStream.CopyToAsync(destStream, bufferSize, ct);
    }

    private static string NormalizeRepoPath(string repoPath)
    {
        var trimmed = repoPath.Trim().TrimStart('/');
        return trimmed.Replace('\\', '/');
    }

    private static string SanitizeRelativePath(string relativeRepoPath)
    {
        var normalized = relativeRepoPath.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var sanitizedSegments = segments
            .Select(SanitizePathSegment)
            .Select(segment => segment is "." or ".." ? "_" : segment)
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

    private static string ResolveDuplicateName(string folder, string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var index = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(folder, $"{name}_{index}{ext}");
            index++;
        }
        while (File.Exists(candidate));

        return candidate;
    }
}
