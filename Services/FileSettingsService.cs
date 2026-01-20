using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using AnimeFolderOrganizer.Models;

namespace AnimeFolderOrganizer.Services;

public class FileSettingsService : ISettingsService
{
    private const string FileName = "settings.json";
    private readonly string _filePath;
    private string? _geminiApiKey;
    private string? _openRouterApiKey;
    private string? _tmdbApiKey;
    private string? _groqApiKey;
    private string? _deepseekProxyApiKey;
    private string? _deepseekProxyBaseUrl;

    public ApiProvider ApiProvider { get; set; } = ApiProvider.Gemini;
    public string? GeminiApiKey
    {
        get => _geminiApiKey;
        set => _geminiApiKey = value;
    }

    public string? OpenRouterApiKey
    {
        get => _openRouterApiKey;
        set => _openRouterApiKey = value;
    }

    public string? TmdbApiKey
    {
        get => _tmdbApiKey;
        set => _tmdbApiKey = value;
    }

    public string? GroqApiKey
    {
        get => _groqApiKey;
        set => _groqApiKey = value;
    }

    public string? DeepseekProxyApiKey
    {
        get => _deepseekProxyApiKey;
        set => _deepseekProxyApiKey = value;
    }

    public string? DeepseekProxyBaseUrl
    {
        get => string.IsNullOrWhiteSpace(_deepseekProxyBaseUrl)
            ? "https://api.chatanywhere.tech/v1"
            : _deepseekProxyBaseUrl;
        set => _deepseekProxyBaseUrl = value;
    }

    public string? ApiKey
    {
        get => ApiProvider switch
        {
            ApiProvider.OpenRouter => _openRouterApiKey,
            ApiProvider.Gemini => _geminiApiKey,
            _ => null
        };
        set
        {
            if (ApiProvider == ApiProvider.OpenRouter)
            {
                _openRouterApiKey = value;
            }
            else if (ApiProvider == ApiProvider.Gemini)
            {
                _geminiApiKey = value;
            }
        }
    }
    public List<string> GeminiModels { get; set; } = new();
    public List<string> OpenRouterModels { get; set; } = new();
    public List<string> GroqModels { get; set; } = new();
    public List<string> DeepseekProxyModels { get; set; } = new();
    public string ModelName { get; set; } = ModelDefaults.GeminiDefaultModel;
    public string NamingFormat { get; set; } = "{Title} ({Year})";
    public NamingLanguage PreferredLanguage { get; set; } = NamingLanguage.TraditionalChinese;

    public FileSettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appData, "AnimeFolderOrganizer");
        Directory.CreateDirectory(appFolder);
        _filePath = Path.Combine(appFolder, FileName);
    }

    public async Task SaveAsync()
    {
        try
        {
            var data = new SettingsData(
                ApiProvider,
                ApiKey,
                GeminiApiKey,
                OpenRouterApiKey,
                TmdbApiKey,
                GroqApiKey,
                DeepseekProxyApiKey,
                DeepseekProxyBaseUrl,
                GeminiModels,
                OpenRouterModels,
                GroqModels,
                DeepseekProxyModels,
                ModelName,
                NamingFormat,
                PreferredLanguage);
            var json = JsonSerializer.Serialize(data);
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch
        {
            // Ignore save errors for now
        }
    }

    public async Task LoadAsync()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath);
                var data = JsonSerializer.Deserialize<SettingsData>(json);
                if (data != null)
                {
                    ApiProvider = data.ApiProvider;
                    var legacyKey = data.ApiKey;
                    GeminiApiKey = string.IsNullOrWhiteSpace(data.GeminiApiKey) ? legacyKey : data.GeminiApiKey;
                    OpenRouterApiKey = string.IsNullOrWhiteSpace(data.OpenRouterApiKey)
                        ? (data.ApiProvider == ApiProvider.OpenRouter ? legacyKey : null)
                        : data.OpenRouterApiKey;
                    TmdbApiKey = data.TmdbApiKey;
                    GroqApiKey = data.GroqApiKey;
                    DeepseekProxyApiKey = data.DeepseekProxyApiKey;
                    DeepseekProxyBaseUrl = string.IsNullOrWhiteSpace(data.DeepseekProxyBaseUrl)
                        ? "https://api.chatanywhere.tech/v1"
                        : data.DeepseekProxyBaseUrl;
                    GeminiModels = data.GeminiModels ?? new List<string>();
                    OpenRouterModels = data.OpenRouterModels ?? new List<string>();
                    GroqModels = data.GroqModels ?? new List<string>();
                    DeepseekProxyModels = data.DeepseekProxyModels ?? new List<string>();
                    var defaultModel = data.ApiProvider switch
                    {
                        ApiProvider.OpenRouter => ModelDefaults.OpenRouterDefaultModel,
                        ApiProvider.Groq => ModelDefaults.GroqDefaultModel,
                        ApiProvider.DeepseekProxy => ModelDefaults.DeepseekProxyDefaultModel,
                        _ => ModelDefaults.GeminiDefaultModel
                    };
                    ModelName = string.IsNullOrWhiteSpace(data.ModelName) ? defaultModel : data.ModelName;
                    NamingFormat = string.IsNullOrWhiteSpace(data.NamingFormat) ? "{Title} ({Year})" : data.NamingFormat;
                    PreferredLanguage = data.PreferredLanguage;
                }
            }
        }
        catch
        {
            // Ignore load errors, use defaults
        }
    }

    private record SettingsData(
        ApiProvider ApiProvider,
        string? ApiKey,
        string? GeminiApiKey,
        string? OpenRouterApiKey,
        string? TmdbApiKey,
        string? GroqApiKey,
        string? DeepseekProxyApiKey,
        string? DeepseekProxyBaseUrl,
        List<string>? GeminiModels,
        List<string>? OpenRouterModels,
        List<string>? GroqModels,
        List<string>? DeepseekProxyModels,
        string ModelName,
        string NamingFormat,
        NamingLanguage PreferredLanguage);
}
