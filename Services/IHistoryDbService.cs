using AnimeFolderOrganizer.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AnimeFolderOrganizer.Services;

public interface IHistoryDbService
{
    Task InitializeAsync();
    Task AddAsync(RenameHistoryEntry entry);
    Task<IReadOnlyList<RenameHistoryEntry>> GetRecentAsync(int count);
}
