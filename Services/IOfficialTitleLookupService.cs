using System.Threading;
using System.Threading.Tasks;
using AnimeFolderOrganizer.Models;

namespace AnimeFolderOrganizer.Services;

public interface IOfficialTitleLookupService
{
    Task<OfficialTitleResult?> LookupAsync(string japaneseTitle, CancellationToken cancellationToken = default);
}
