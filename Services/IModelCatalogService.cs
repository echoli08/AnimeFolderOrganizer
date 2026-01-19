using System.Collections.Generic;
using System.Threading.Tasks;
using AnimeFolderOrganizer.Models;

namespace AnimeFolderOrganizer.Services;

public interface IModelCatalogService
{
    Task<IReadOnlyList<string>> GetAvailableModelsAsync(ApiProvider provider, string? apiKey, bool forceRefresh);
}
