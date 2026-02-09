using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AnimeFolderOrganizer.Services;

/// <summary>
/// sub_share 字幕上傳前置包裝服務（建立上傳資料夾結構）
/// </summary>
public interface ISubShareSubtitleUploadService
{
    Task<(bool Success, string Message)> CreateUploadPackageAsync(
        string repoPath,
        IReadOnlyList<string> filePaths,
        string destinationRootFolder,
        CancellationToken cancellationToken);
}
