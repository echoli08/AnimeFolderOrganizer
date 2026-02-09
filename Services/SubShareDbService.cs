using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AnimeFolderOrganizer.Models;

namespace AnimeFolderOrganizer.Services;

/// <summary>
/// sub_share 本地資料庫管理服務實作。
/// 提供資料庫狀態查詢、遠端更新與本機匯入功能。
/// </summary>
public class SubShareDbService : ISubShareDbService
{
    private const string PrimarySvnUrl = "https://svn.acgdev.com:505/!/#sub_share/view/head/trunk/Subtitles%20DataBase/Files/db.xml";
    private const string BackupGithubUrl = "https://raw.githubusercontent.com/foxofice/sub_share/master/Subtitles%20DataBase/Files/db.xml";
    private const string DbFileName = "db.xml";
    private readonly string _dbFolder;
    private readonly string _dbFilePath;
    private readonly HttpClient _httpClient;

    public SubShareDbService(HttpClient httpClient)
    {
        _httpClient = httpClient;

        var baseDir = AppContext.BaseDirectory;
        var preferredFolder = Path.Combine(baseDir, "subshare");
        var preferredPath = Path.Combine(preferredFolder, DbFileName);

        var legacyPath = GetLegacyDbPath();
        _dbFolder = preferredFolder;
        _dbFilePath = preferredPath;
        Directory.CreateDirectory(_dbFolder);

        // 相容舊版路徑：若舊資料庫較新/較大，優先複製到新路徑
        TryMigrateLegacyDb(preferredPath, legacyPath);
    }

    private static string GetLegacyDbPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "AnimeFolderOrganizer", "sub_share", DbFileName);
    }

    private static void TryMigrateLegacyDb(string preferredPath, string legacyPath)
    {
        try
        {
            if (!File.Exists(legacyPath))
            {
                return;
            }

            var legacyInfo = new FileInfo(legacyPath);
            if (!legacyInfo.Exists || legacyInfo.Length == 0)
            {
                return;
            }

            if (!File.Exists(preferredPath))
            {
                File.Copy(legacyPath, preferredPath, overwrite: true);
                return;
            }

            var preferredInfo = new FileInfo(preferredPath);
            if (!preferredInfo.Exists || preferredInfo.Length < legacyInfo.Length)
            {
                File.Copy(legacyPath, preferredPath, overwrite: true);
            }
        }
        catch
        {
            // 遷移失敗不影響主流程
        }
    }

    public Task<SubShareDbStatus> GetStatusAsync()
    {
        var status = new SubShareDbStatus
        {
            Exists = File.Exists(_dbFilePath),
            SourceUrl = $"Primary: {PrimarySvnUrl} | Backup: {BackupGithubUrl}"
        };

        if (status.Exists)
        {
            try
            {
                var info = new FileInfo(_dbFilePath);
                status = status with
                {
                    Size = info.Length,
                    LastWriteUtc = info.LastWriteTimeUtc
                };
            }
            catch
            {
                // 若無法取得檔案資訊，保持預設值
            }
        }

        return Task.FromResult(status);
    }

    public async Task<SubShareDbUpdateResult> UpdateFromRemoteAsync(CancellationToken ct)
    {
        var tempFilePath = Path.Combine(_dbFolder, $"db_{Guid.NewGuid():N}.tmp");

        try
        {
            var primaryResult = await TryDownloadAsync(PrimarySvnUrl, tempFilePath, useBasicAuth: true, ct);
            if (!primaryResult.Success)
            {
                var backupResult = await TryDownloadAsync(BackupGithubUrl, tempFilePath, useBasicAuth: false, ct);
                if (!backupResult.Success)
                {
                    return SubShareDbUpdateResult.Failed($"主來源失敗: {primaryResult.Message} / 備用來源失敗: {backupResult.Message}");
                }

                return SubShareDbUpdateResult.Succeeded(backupResult.FileSize);
            }

            return SubShareDbUpdateResult.Succeeded(primaryResult.FileSize);
        }
        catch (OperationCanceledException)
        {
            File.Delete(tempFilePath);
            return SubShareDbUpdateResult.Failed("下載已取消");
        }
        catch (Exception ex)
        {
            File.Delete(tempFilePath);
            return SubShareDbUpdateResult.Failed($"下載發生錯誤：{ex.Message}");
        }
    }

    private async Task<(bool Success, string Message, long FileSize)> TryDownloadAsync(
        string url,
        string tempFilePath,
        bool useBasicAuth,
        CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (useBasicAuth)
            {
                // SVN 測試帳號：test / 空密碼
                var credential = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("test:"));
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credential);
            }

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"HTTP {(int)response.StatusCode}", 0);
            }

            await using var httpStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await httpStream.CopyToAsync(fileStream, ct);

            fileStream.Dispose();
            var fileInfo = new FileInfo(tempFilePath);
            if (fileInfo.Length == 0)
            {
                File.Delete(tempFilePath);
                return (false, "下載的檔案大小為 0", 0);
            }

            ReplaceFileAtomically(tempFilePath, _dbFilePath);
            return (true, string.Empty, fileInfo.Length);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, 0);
        }
    }

    public async Task<SubShareDbUpdateResult> ImportFromFileAsync(string filePath, CancellationToken ct)
    {
        if (!File.Exists(filePath))
        {
            return SubShareDbUpdateResult.Failed($"來源檔案不存在：{filePath}");
        }

        var tempFilePath = Path.Combine(_dbFolder, $"db_{Guid.NewGuid():N}.tmp");

        try
        {
            // 複製至暫存檔
            await using var sourceStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var targetStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);

            await sourceStream.CopyToAsync(targetStream, ct);

            // 驗證檔案大小
            targetStream.Dispose();
            var fileInfo = new FileInfo(tempFilePath);
            if (fileInfo.Length == 0)
            {
                File.Delete(tempFilePath);
                return SubShareDbUpdateResult.Failed("匯入的檔案大小為 0");
            }

            // 原子性替換
            ReplaceFileAtomically(tempFilePath, _dbFilePath);

            return SubShareDbUpdateResult.Succeeded(fileInfo.Length);
        }
        catch (OperationCanceledException)
        {
            File.Delete(tempFilePath);
            return SubShareDbUpdateResult.Failed("匯入已取消");
        }
        catch (Exception ex)
        {
            File.Delete(tempFilePath);
            return SubShareDbUpdateResult.Failed($"匯入發生錯誤：{ex.Message}");
        }
    }

    /// <summary>
    /// 原子性替換目標檔案。
    /// 先嘗試刪除目標檔案（若存在），再將暫存檔移動為目標檔案。
    /// </summary>
    private static void ReplaceFileAtomically(string tempPath, string targetPath)
    {
        // 先嘗試刪除目標檔案（若存在），以便 File.Move 可直接替換
        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        File.Move(tempPath, targetPath);
    }
}
