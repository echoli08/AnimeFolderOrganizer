using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AnimeFolderOrganizer.Services;
using System.Diagnostics;
using AnimeFolderOrganizer.Models;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;

namespace AnimeFolderOrganizer.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IModelCatalogService _modelCatalogService;
    private readonly List<string> _geminiModelCache = new();
    private readonly List<string> _openRouterModelCache = new();
    private string _lastModelName = ModelDefaults.GeminiDefaultModel;

    [ObservableProperty]
    private ApiProvider _apiProvider = ApiProvider.Gemini;
    
    [ObservableProperty]
    private string? _geminiApiKey;

    [ObservableProperty]
    private string? _openRouterApiKey;

    [ObservableProperty]
    private string _modelName = ModelDefaults.GeminiDefaultModel;

    [ObservableProperty]
    private string _namingFormat = "{Title} ({Year})";

    [ObservableProperty]
    private NamingLanguage _preferredLanguage = NamingLanguage.TraditionalChinese;

    [ObservableProperty]
    private string _modelListTitle = "可選模型清單";

    [ObservableProperty]
    private string _apiTestStatus = string.Empty;

    public Action? CloseAction { get; set; }

    public ObservableCollection<string> AvailableModels { get; } = new();

    public SettingsViewModel(ISettingsService settingsService, IModelCatalogService modelCatalogService)
    {
        _settingsService = settingsService;
        _modelCatalogService = modelCatalogService;
        ApiProvider = _settingsService.ApiProvider;
        GeminiApiKey = _settingsService.GeminiApiKey;
        OpenRouterApiKey = _settingsService.OpenRouterApiKey;
        ModelName = _settingsService.ModelName;
        _lastModelName = string.IsNullOrWhiteSpace(ModelName) ? _lastModelName : ModelName;
        NamingFormat = _settingsService.NamingFormat;
        PreferredLanguage = _settingsService.PreferredLanguage;
        _geminiModelCache.AddRange(_settingsService.GeminiModels);
        _openRouterModelCache.AddRange(_settingsService.OpenRouterModels);
        ModelListTitle = BuildModelListTitle(ApiProvider);
        ApplyProviderDefaults(ApiProvider);
        LoadCachedModels(ApiProvider);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        UpdateSettingsSnapshot();
        await _settingsService.SaveAsync();
        CloseAction?.Invoke();
    }

    [RelayCommand]
    private async Task DetectModelsAsync(ApiProvider provider)
    {
        var apiKey = GetApiKey(provider);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ApiTestStatus = "請先輸入 API Key";
            return;
        }

        ApiTestStatus = "偵測中...";
        try
        {
            var models = await _modelCatalogService.GetAvailableModelsAsync(provider, apiKey, true);
            if (provider != ApiProvider)
            {
                ApiProvider = provider;
            }

            AvailableModels.Clear();

            if (models.Count == 0)
            {
                ApiTestStatus = "偵測失敗或沒有可用模型";
                LoadCachedModels(provider);
                return;
            }

            UpdateModelCache(provider, models);
            UpdateModelList(models);
            ApiTestStatus = $"偵測完成，已載入 {models.Count} 個模型";
            UpdateSettingsSnapshot();
            await _settingsService.SaveAsync();
        }
        catch
        {
            ApiTestStatus = "偵測失敗，請確認 API Key";
            AvailableModels.Clear();
            LoadCachedModels(provider);
        }
    }

    private void UpdateModelList(IEnumerable<string> models)
    {
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in models)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (unique.Add(name))
            {
                AvailableModels.Add(name);
            }
        }

        if (!string.IsNullOrWhiteSpace(ModelName) && !unique.Contains(ModelName))
        {
            AvailableModels.Add(ModelName);
        }
    }

    [RelayCommand]
    private void OpenApiKeyPage(ApiProvider provider)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = provider == ApiProvider.OpenRouter
                    ? "https://openrouter.ai/keys"
                    : "https://aistudio.google.com/app/apikey",
                UseShellExecute = true
            });
        }
        catch
        {
            // Handle error opening browser
        }
    }

    partial void OnApiProviderChanged(ApiProvider value)
    {
        ModelListTitle = BuildModelListTitle(value);
        ApplyProviderDefaults(value);
        ApiTestStatus = string.Empty;
        LoadCachedModels(value);
    }

    partial void OnGeminiApiKeyChanged(string? value)
    {
        if (ApiProvider == ApiProvider.Gemini)
        {
            ApiTestStatus = string.Empty;
        }
    }

    partial void OnOpenRouterApiKeyChanged(string? value)
    {
        if (ApiProvider == ApiProvider.OpenRouter)
        {
            ApiTestStatus = string.Empty;
        }
    }

    partial void OnModelNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            ModelName = _lastModelName;
            return;
        }

        _lastModelName = value;
        EnsureModelInList(value);
    }

    private void ApplyProviderDefaults(ApiProvider provider)
    {
        if (string.IsNullOrWhiteSpace(ModelName))
        {
            ModelName = GetDefaultModel(provider);
            return;
        }

        if (provider == ApiProvider.Gemini && !IsPrimaryGeminiModelName(ModelName))
        {
            ModelName = GetDefaultModel(provider);
        }
        else if (provider == ApiProvider.OpenRouter && IsGeminiModelName(ModelName))
        {
            ModelName = GetDefaultModel(provider);
        }
    }

    private static bool IsGeminiModelName(string modelName)
    {
        return modelName.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPrimaryGeminiModelName(string modelName)
    {
        if (!IsGeminiModelName(modelName)) return false;
        return modelName.Contains("-flash", StringComparison.OrdinalIgnoreCase)
               || modelName.Contains("-pro", StringComparison.OrdinalIgnoreCase)
               || modelName.Contains("-lite", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDefaultModel(ApiProvider provider)
    {
        return provider == ApiProvider.OpenRouter
            ? ModelDefaults.OpenRouterDefaultModel
            : ModelDefaults.GeminiDefaultModel;
    }

    private static string BuildModelListTitle(ApiProvider provider)
    {
        return provider == ApiProvider.OpenRouter
            ? "可選模型清單（免費額度，需先偵測）"
            : "可選模型清單（需先偵測）";
    }

    [RelayCommand]
    private void ClearApiKey(ApiProvider provider)
    {
        if (provider == ApiProvider.OpenRouter)
        {
            OpenRouterApiKey = string.Empty;
        }
        else
        {
            GeminiApiKey = string.Empty;
        }

        if (provider == ApiProvider)
        {
            ApiTestStatus = "API Key 已清除";
        }
    }

    private string? GetApiKey(ApiProvider provider)
    {
        return provider == ApiProvider.OpenRouter ? OpenRouterApiKey : GeminiApiKey;
    }

    private void LoadCachedModels(ApiProvider provider)
    {
        AvailableModels.Clear();
        var cached = provider == ApiProvider.OpenRouter ? _openRouterModelCache : _geminiModelCache;
        if (cached.Count > 0)
        {
            UpdateModelList(cached);
        }
        else
        {
            UpdateModelList(new[] { ModelName });
        }
    }

    private void EnsureModelInList(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return;
        if (AvailableModels.Any(m => string.Equals(m, modelName, StringComparison.OrdinalIgnoreCase))) return;
        AvailableModels.Add(modelName);
    }

    private void UpdateModelCache(ApiProvider provider, IReadOnlyList<string> models)
    {
        if (provider == ApiProvider.OpenRouter)
        {
            _openRouterModelCache.Clear();
            _openRouterModelCache.AddRange(models);
        }
        else
        {
            _geminiModelCache.Clear();
            _geminiModelCache.AddRange(models);
        }
    }

    private void UpdateSettingsSnapshot()
    {
        _settingsService.ApiProvider = ApiProvider;
        _settingsService.GeminiApiKey = GeminiApiKey;
        _settingsService.OpenRouterApiKey = OpenRouterApiKey;
        _settingsService.GeminiModels = new List<string>(_geminiModelCache);
        _settingsService.OpenRouterModels = new List<string>(_openRouterModelCache);
        if (string.IsNullOrWhiteSpace(ModelName))
        {
            ModelName = GetDefaultModel(ApiProvider);
        }

        _settingsService.ModelName = ModelName;
        _settingsService.NamingFormat = NamingFormat;
        _settingsService.PreferredLanguage = PreferredLanguage;
    }
}
