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

    public string? ApiKey
    {
        get => ApiProvider == ApiProvider.OpenRouter ? _openRouterApiKey : _geminiApiKey;
        set
        {
            if (ApiProvider == ApiProvider.OpenRouter)
            {
                _openRouterApiKey = value;
            }
            else
            {
                _geminiApiKey = value;
            }
        }
    }
    public List<string> GeminiModels { get; set; } = new();
    public List<string> OpenRouterModels { get; set; } = new();
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
                GeminiModels,
                OpenRouterModels,
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
                    GeminiModels = data.GeminiModels ?? new List<string>();
                    OpenRouterModels = data.OpenRouterModels ?? new List<string>();
                    var defaultModel = data.ApiProvider == ApiProvider.OpenRouter
                        ? ModelDefaults.OpenRouterDefaultModel
                        : ModelDefaults.GeminiDefaultModel;
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
        List<string>? GeminiModels,
        List<string>? OpenRouterModels,
        string ModelName,
        string NamingFormat,
        NamingLanguage PreferredLanguage);
}
