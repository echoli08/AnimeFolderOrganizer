using System.Collections.Generic;
using System.Threading.Tasks;

namespace AnimeFolderOrganizer.Services;

public interface IModelCatalogService
{
    Task<IReadOnlyList<string>> GetAvailableModelsAsync(string? apiKey);
}
