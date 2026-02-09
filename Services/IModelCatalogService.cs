using System.Collections.Generic;
using System.Threading.Tasks;
using AnimeFolderOrganizer.Models;

namespace AnimeFolderOrganizer.Services;

public interface IModelCatalogService
{
    /// <summary>
    /// 取得指定 API 提供者可用的模型清單。
    /// </summary>
    /// <param name="provider">API 提供者類型。</param>
    /// <param name="apiKey">API 金鑰。</param>
    /// <param name="forceRefresh">是否強制重新整理快取。</param>
    /// <returns>可用模型 ID 清單。</returns>
    /// <exception cref="ModelCatalogException">當 API 回傳非成功狀態碼時擲回。</exception>
    Task<IReadOnlyList<string>> GetAvailableModelsAsync(ApiProvider provider, string? apiKey, bool forceRefresh);
}
