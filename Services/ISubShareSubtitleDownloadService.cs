using System.Threading;
using System.Threading.Tasks;
using AnimeFolderOrganizer.Models;

namespace AnimeFolderOrganizer.Services;

public interface ISubShareSubtitleDownloadService
{
    Task<bool?> HasRemoteSubtitlesAsync(string repoPath, CancellationToken cancellationToken);

    Task<SubShareSubtitleDownloadResult> DownloadAsync(
        AnimeFolderInfo info,
        string destinationRootFolder,
        CancellationToken cancellationToken);
}
