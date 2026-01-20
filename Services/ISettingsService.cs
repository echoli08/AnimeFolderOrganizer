using System.Collections.Generic;
using System.Threading.Tasks;
using AnimeFolderOrganizer.Models;

namespace AnimeFolderOrganizer.Services;

public interface ISettingsService
{
    ApiProvider ApiProvider { get; set; }
    string? GeminiApiKey { get; set; }
    string? OpenRouterApiKey { get; set; }
    string? TmdbApiKey { get; set; }
    string? GroqApiKey { get; set; }
    string? DeepseekProxyApiKey { get; set; }
    string? DeepseekProxyBaseUrl { get; set; }
    string? ApiKey { get; set; }
    List<string> GeminiModels { get; set; }
    List<string> OpenRouterModels { get; set; }
    List<string> GroqModels { get; set; }
    List<string> DeepseekProxyModels { get; set; }
    string ModelName { get; set; }
    string NamingFormat { get; set; }
    NamingLanguage PreferredLanguage { get; set; }
    Task SaveAsync();
    Task LoadAsync();
}
