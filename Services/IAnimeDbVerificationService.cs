using System.Threading;
using System.Threading.Tasks;
using AnimeFolderOrganizer.Models;

namespace AnimeFolderOrganizer.Services;

public interface IAnimeDbVerificationService
{
    Task<AnimeDbVerificationStatus> VerifyTitleAsync(string? title, CancellationToken cancellationToken = default);
}
