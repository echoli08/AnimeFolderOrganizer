using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AnimeFolderOrganizer.Services;
using System.Diagnostics;
using AnimeFolderOrganizer.Models;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Threading;

namespace AnimeFolderOrganizer.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IModelCatalogService _modelCatalogService;
    private readonly IDialogService _dialogService;
    private readonly ISubShareDbService _subShareDbService;
    private readonly IFilePickerService _filePickerService;
    private readonly List<string> _geminiModelCache = new();
    private readonly List<string> _customApiModelCache = new();
    private string _lastModelName = ModelDefaults.GeminiDefaultModel;
    private bool _isSubShareDbBusy;

    [ObservableProperty]
    private ApiProvider _apiProvider = ApiProvider.Gemini;
    
    [ObservableProperty]
    private string? _geminiApiKey;

    [ObservableProperty]
    private string? _tmdbApiKey;

    [ObservableProperty]
    private string? _customApiKey;

    [ObservableProperty]
    private string? _customApiBaseUrl;

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

    [ObservableProperty]
    private string _subShareDbStatusText = string.Empty;

    [ObservableProperty]
    private string _subShareDbLastUpdatedText = "最近操作：—";

    public Action? CloseAction { get; set; }

    public ObservableCollection<string> AvailableModels { get; } = new();

    public ObservableCollection<string> AvailableCustomApiBaseUrls { get; } = new();

    public SettingsViewModel(
        ISettingsService settingsService,
        IModelCatalogService modelCatalogService,
        IDialogService dialogService,
        ISubShareDbService subShareDbService,
        IFilePickerService filePickerService)
    {
        _settingsService = settingsService;
        _modelCatalogService = modelCatalogService;
        _dialogService = dialogService;
        _subShareDbService = subShareDbService;
        _filePickerService = filePickerService;
        ApiProvider = _settingsService.ApiProvider;
        GeminiApiKey = _settingsService.GeminiApiKey;
        TmdbApiKey = _settingsService.TmdbApiKey;
        CustomApiKey = _settingsService.CustomApiKey;
        CustomApiBaseUrl = _settingsService.CustomApiBaseUrl;
        ModelName = _settingsService.ModelName;
        _lastModelName = string.IsNullOrWhiteSpace(ModelName) ? _lastModelName : ModelName;
        NamingFormat = _settingsService.NamingFormat;
        PreferredLanguage = _settingsService.PreferredLanguage;
        _geminiModelCache.AddRange(_settingsService.GeminiModels);
        _customApiModelCache.AddRange(_settingsService.CustomApiModels);
        ModelListTitle = BuildModelListTitle(ApiProvider);
        ApplyProviderDefaults(ApiProvider);
        LoadCachedModels(ApiProvider);

        // 以非同步方式載入 sub_share 資料庫狀態，避免阻塞 UI 執行緒。
        InitializeSubShareDbStatus();
    }

    private async void InitializeSubShareDbStatus()
    {
        try
        {
            SubShareDbStatusText = "讀取中...";
            await RefreshSubShareDbStatusAsync();
        }
        catch
        {
            // 初始化失敗不應影響設定頁面開啟
            SubShareDbStatusText = "本機狀態讀取失敗";
        }
    }

    private async Task RefreshSubShareDbStatusAsync()
    {
        var status = await _subShareDbService.GetStatusAsync();
        SubShareDbStatusText = BuildSubShareDbStatusText(status);
    }

    private static string BuildSubShareDbStatusText(SubShareDbStatus status)
    {
        var existsText = status.Exists ? "存在" : "不存在";
        var sizeText = status.Exists ? FormatFileSize(status.Size) : "-";
        var lastWriteText = status.LastWriteUtc.HasValue
            ? status.LastWriteUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : "-";
        var sourceUrl = string.IsNullOrWhiteSpace(status.SourceUrl) ? "-" : status.SourceUrl;

        return $"本機檔案：{existsText}\n大小：{sizeText}\n最後寫入：{lastWriteText}\n來源：{sourceUrl}";
    }

    private static string FormatFileSize(long bytes)
    {
        const double kb = 1024d;
        const double mb = 1024d * 1024d;

        if (bytes >= mb)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes / mb:0.0} MB");
        }

        if (bytes >= kb)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes / kb:0.0} KB");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{bytes} B");
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        UpdateSettingsSnapshot();
        await _settingsService.SaveAsync();
        CloseAction?.Invoke();
    }

    [RelayCommand]
    private async Task UpdateSubShareDbAsync()
    {
        if (_isSubShareDbBusy) return;
        _isSubShareDbBusy = true;

        try
        {
            SubShareDbLastUpdatedText = "更新中...";
            var result = await _subShareDbService.UpdateFromRemoteAsync(CancellationToken.None);
            if (!result.IsSuccess)
            {
                SubShareDbLastUpdatedText = "最近操作：更新失敗";
                _dialogService.ShowError(
                    "sub_share 更新失敗",
                    $"錯誤代碼: SUBSHARE_UPDATE_FAILED\n訊息: {result.ErrorMessage ?? "未知錯誤"}");
                return;
            }

            await RefreshSubShareDbStatusAsync();
            var localTime = result.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            SubShareDbLastUpdatedText = $"最近操作：更新成功（{localTime}）";
        }
        catch (Exception ex)
        {
            SubShareDbLastUpdatedText = "最近操作：更新失敗";
            _dialogService.ShowError(
                "sub_share 更新失敗",
                $"錯誤代碼: SUBSHARE_UPDATE_FAILED\n訊息: {ex.Message}");
        }
        finally
        {
            _isSubShareDbBusy = false;
        }
    }

    [RelayCommand]
    private async Task ImportSubShareDbAsync()
    {
        if (_isSubShareDbBusy) return;
        _isSubShareDbBusy = true;

        try
        {
            var filePath = _filePickerService.PickFile(
                "選擇 db.xml",
                "XML 檔案|*.xml|所有檔案|*.*",
                "xml");

            if (string.IsNullOrWhiteSpace(filePath))
            {
                SubShareDbLastUpdatedText = "最近操作：已取消";
                return;
            }

            SubShareDbLastUpdatedText = "匯入中...";
            var result = await _subShareDbService.ImportFromFileAsync(filePath, CancellationToken.None);
            if (!result.IsSuccess)
            {
                SubShareDbLastUpdatedText = "最近操作：匯入失敗";
                _dialogService.ShowError(
                    "sub_share 匯入失敗",
                    $"錯誤代碼: SUBSHARE_IMPORT_FAILED\n訊息: {result.ErrorMessage ?? "未知錯誤"}");
                return;
            }

            await RefreshSubShareDbStatusAsync();
            var localTime = result.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            SubShareDbLastUpdatedText = $"最近操作：匯入成功（{localTime}）";
        }
        catch (Exception ex)
        {
            SubShareDbLastUpdatedText = "最近操作：匯入失敗";
            _dialogService.ShowError(
                "sub_share 匯入失敗",
                $"錯誤代碼: SUBSHARE_IMPORT_FAILED\n訊息: {ex.Message}");
        }
        finally
        {
            _isSubShareDbBusy = false;
        }
    }

    [RelayCommand]
    private async Task DetectModelsAsync(ApiProvider provider)
    {
        // 在偵測前先將目前 VM 的值同步到 _settingsService
        // 確保 CustomApiModelCatalogService 讀到最新的 CustomApiBaseUrl
        UpdateSettingsSnapshot();

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
                // 沒有可用模型時，顯示錯誤彈窗，錯誤代碼為 EMPTY_MODELS
                ApiTestStatus = "偵測失敗或沒有可用模型";
                var errorMessage = $"提供者: {provider}\n錯誤代碼: EMPTY_MODELS\n訊息: 沒有偵測到任何可用模型";
                _dialogService.ShowError("模型偵測失敗", errorMessage);
                LoadCachedModels(provider);
                return;
            }

            UpdateModelCache(provider, models);
            UpdateModelList(models);
            ApiTestStatus = $"偵測完成，已載入 {models.Count} 個模型";
            UpdateSettingsSnapshot();
            await _settingsService.SaveAsync();
        }
        catch (ModelCatalogException ex)
        {
            // 處理 ModelCatalogException，顯示包含詳細錯誤資訊的彈窗
            ApiTestStatus = "偵測失敗，請確認 API Key";
            var statusCodeText = ex.StatusCode.HasValue ? $"HTTP {ex.StatusCode}" : "N/A";
            var errorMessage = $"提供者: {ex.Provider}\n錯誤類型: {ex.ErrorType}\n錯誤代碼: {statusCodeText}\n訊息: {ex.Message}";
            _dialogService.ShowError("模型偵測失敗", errorMessage);
            AvailableModels.Clear();
            LoadCachedModels(provider);
        }
        catch (Exception ex)
        {
            // 處理其他未知例外，顯示錯誤代碼為 EXCEPTION
            ApiTestStatus = "偵測失敗，請確認 API Key";
            var errorMessage = $"提供者: {provider}\n錯誤代碼: EXCEPTION\n訊息: {ex.Message}";
            _dialogService.ShowError("模型偵測失敗", errorMessage);
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
                    ApiProvider.CustomApi => "https://platform.openai.com/account/api-keys", // Default to OpenAI or generic
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

    partial void OnCustomApiKeyChanged(string? value)
    {
        if (ApiProvider == ApiProvider.CustomApi)
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

        if (provider == ApiProvider.CustomApi && IsGeminiModelName(ModelName))
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
        return provider switch
        {
            ApiProvider.CustomApi => ModelDefaults.CustomApiDefaultModel,
            _ => ModelDefaults.GeminiDefaultModel
        };
    }

    private static string BuildModelListTitle(ApiProvider provider)
    {
        return provider switch
        {
            ApiProvider.CustomApi => "可選模型清單（自訂 API，需先偵測）",
            _ => "可選模型清單（需先偵測）"
        };
    }

    [RelayCommand]
    private void ClearApiKey(ApiProvider provider)
    {
        switch (provider)
        {
            case ApiProvider.CustomApi:
                CustomApiKey = string.Empty;
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
            ApiProvider.CustomApi => CustomApiKey,
            _ => GeminiApiKey
        };
    }

    private void LoadCachedModels(ApiProvider provider)
    {
        AvailableModels.Clear();
        var cached = provider switch
        {
            ApiProvider.CustomApi => _customApiModelCache,
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
            case ApiProvider.CustomApi:
                _customApiModelCache.Clear();
                _customApiModelCache.AddRange(models);
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
        _settingsService.TmdbApiKey = TmdbApiKey;
        _settingsService.CustomApiKey = CustomApiKey;
        _settingsService.CustomApiBaseUrl = CustomApiBaseUrl;
        _settingsService.GeminiModels = new List<string>(_geminiModelCache);
        _settingsService.CustomApiModels = new List<string>(_customApiModelCache);
        if (string.IsNullOrWhiteSpace(ModelName))
        {
            ModelName = GetDefaultModel(ApiProvider);
        }

        _settingsService.ModelName = ModelName;
        _settingsService.NamingFormat = NamingFormat;
        _settingsService.PreferredLanguage = PreferredLanguage;
    }
}
