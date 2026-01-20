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
    private readonly List<string> _groqModelCache = new();
    private readonly List<string> _deepseekProxyModelCache = new();
    private string _lastModelName = ModelDefaults.GeminiDefaultModel;

    [ObservableProperty]
    private ApiProvider _apiProvider = ApiProvider.Gemini;
    
    [ObservableProperty]
    private string? _geminiApiKey;

    [ObservableProperty]
    private string? _openRouterApiKey;

    [ObservableProperty]
    private string? _tmdbApiKey;

    [ObservableProperty]
    private string? _groqApiKey;

    [ObservableProperty]
    private string? _deepseekProxyApiKey;

    [ObservableProperty]
    private string? _deepseekProxyBaseUrl;

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
        TmdbApiKey = _settingsService.TmdbApiKey;
        GroqApiKey = _settingsService.GroqApiKey;
        DeepseekProxyApiKey = _settingsService.DeepseekProxyApiKey;
        DeepseekProxyBaseUrl = string.IsNullOrWhiteSpace(_settingsService.DeepseekProxyBaseUrl)
            ? "https://api.chatanywhere.tech/v1"
            : _settingsService.DeepseekProxyBaseUrl;
        ModelName = _settingsService.ModelName;
        _lastModelName = string.IsNullOrWhiteSpace(ModelName) ? _lastModelName : ModelName;
        NamingFormat = _settingsService.NamingFormat;
        PreferredLanguage = _settingsService.PreferredLanguage;
        _geminiModelCache.AddRange(_settingsService.GeminiModels);
        _openRouterModelCache.AddRange(_settingsService.OpenRouterModels);
        _groqModelCache.AddRange(_settingsService.GroqModels);
        _deepseekProxyModelCache.AddRange(_settingsService.DeepseekProxyModels);
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
                FileName = provider switch
                {
                    ApiProvider.OpenRouter => "https://openrouter.ai/keys",
                    ApiProvider.Groq => "https://console.groq.com/keys",
                    ApiProvider.DeepseekProxy => "https://github.com/chatanywhere/GPT_API_free",
                    _ => "https://aistudio.google.com/app/apikey"
                },
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

    partial void OnGroqApiKeyChanged(string? value)
    {
        if (ApiProvider == ApiProvider.Groq)
        {
            ApiTestStatus = string.Empty;
        }
    }

    partial void OnDeepseekProxyApiKeyChanged(string? value)
    {
        if (ApiProvider == ApiProvider.DeepseekProxy)
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
            return;
        }

        if (provider == ApiProvider.OpenRouter && IsGeminiModelName(ModelName))
        {
            ModelName = GetDefaultModel(provider);
            return;
        }

        if (provider == ApiProvider.Groq && (IsGeminiModelName(ModelName) || IsOpenRouterModelName(ModelName) || IsDeepseekModelName(ModelName)))
        {
            ModelName = GetDefaultModel(provider);
            return;
        }

        if (provider == ApiProvider.DeepseekProxy && !IsDeepseekModelName(ModelName))
        {
            ModelName = GetDefaultModel(provider);
        }
    }

    private static bool IsGeminiModelName(string modelName)
    {
        return modelName.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOpenRouterModelName(string modelName)
    {
        return modelName.StartsWith("openrouter/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDeepseekModelName(string modelName)
    {
        return modelName.StartsWith("deepseek", StringComparison.OrdinalIgnoreCase);
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
        return provider switch
        {
            ApiProvider.OpenRouter => ModelDefaults.OpenRouterDefaultModel,
            ApiProvider.Groq => ModelDefaults.GroqDefaultModel,
            ApiProvider.DeepseekProxy => ModelDefaults.DeepseekProxyDefaultModel,
            _ => ModelDefaults.GeminiDefaultModel
        };
    }

    private static string BuildModelListTitle(ApiProvider provider)
    {
        return provider switch
        {
            ApiProvider.OpenRouter => "可選模型清單（免費額度，需先偵測）",
            ApiProvider.Groq => "可選模型清單（Groq，需先偵測）",
            ApiProvider.DeepseekProxy => "可選模型清單（DeepSeek 轉發，需先偵測）",
            _ => "可選模型清單（需先偵測）"
        };
    }

    [RelayCommand]
    private void ClearApiKey(ApiProvider provider)
    {
        switch (provider)
        {
            case ApiProvider.OpenRouter:
                OpenRouterApiKey = string.Empty;
                break;
            case ApiProvider.Groq:
                GroqApiKey = string.Empty;
                break;
            case ApiProvider.DeepseekProxy:
                DeepseekProxyApiKey = string.Empty;
                break;
            default:
                GeminiApiKey = string.Empty;
                break;
        }

        if (provider == ApiProvider)
        {
            ApiTestStatus = "API Key 已清除";
        }
    }

    private string? GetApiKey(ApiProvider provider)
    {
        return provider switch
        {
            ApiProvider.OpenRouter => OpenRouterApiKey,
            ApiProvider.Groq => GroqApiKey,
            ApiProvider.DeepseekProxy => DeepseekProxyApiKey,
            _ => GeminiApiKey
        };
    }

    private void LoadCachedModels(ApiProvider provider)
    {
        AvailableModels.Clear();
        var cached = provider switch
        {
            ApiProvider.OpenRouter => _openRouterModelCache,
            ApiProvider.Groq => _groqModelCache,
            ApiProvider.DeepseekProxy => _deepseekProxyModelCache,
            _ => _geminiModelCache
        };
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
        switch (provider)
        {
            case ApiProvider.OpenRouter:
                _openRouterModelCache.Clear();
                _openRouterModelCache.AddRange(models);
                break;
            case ApiProvider.Groq:
                _groqModelCache.Clear();
                _groqModelCache.AddRange(models);
                break;
            case ApiProvider.DeepseekProxy:
                _deepseekProxyModelCache.Clear();
                _deepseekProxyModelCache.AddRange(models);
                break;
            default:
                _geminiModelCache.Clear();
                _geminiModelCache.AddRange(models);
                break;
        }
    }

    private void UpdateSettingsSnapshot()
    {
        _settingsService.ApiProvider = ApiProvider;
        _settingsService.GeminiApiKey = GeminiApiKey;
        _settingsService.OpenRouterApiKey = OpenRouterApiKey;
        _settingsService.TmdbApiKey = TmdbApiKey;
        _settingsService.GroqApiKey = GroqApiKey;
        _settingsService.DeepseekProxyApiKey = DeepseekProxyApiKey;
        _settingsService.DeepseekProxyBaseUrl = DeepseekProxyBaseUrl;
        _settingsService.GeminiModels = new List<string>(_geminiModelCache);
        _settingsService.OpenRouterModels = new List<string>(_openRouterModelCache);
        _settingsService.GroqModels = new List<string>(_groqModelCache);
        _settingsService.DeepseekProxyModels = new List<string>(_deepseekProxyModelCache);
        if (string.IsNullOrWhiteSpace(ModelName))
        {
            ModelName = GetDefaultModel(ApiProvider);
        }

        _settingsService.ModelName = ModelName;
        _settingsService.NamingFormat = NamingFormat;
        _settingsService.PreferredLanguage = PreferredLanguage;
    }
}
