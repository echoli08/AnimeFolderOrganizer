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
    private string? _tmdbApiKey;
    private string? _customApiKey;
    private string? _customApiBaseUrl;

    public ApiProvider ApiProvider { get; set; } = ApiProvider.Gemini;
    public string? GeminiApiKey
    {
        get => _geminiApiKey;
        set => _geminiApiKey = value;
    }

    public string? TmdbApiKey
    {
        get => _tmdbApiKey;
        set => _tmdbApiKey = value;
    }

    public string? CustomApiKey
    {
        get => _customApiKey;
        set => _customApiKey = value;
    }

    public string? CustomApiBaseUrl
    {
        get => string.IsNullOrWhiteSpace(_customApiBaseUrl)
            ? "https://api.openai.com/v1"
            : _customApiBaseUrl;
        set => _customApiBaseUrl = value;
    }

    public string? ApiKey
    {
        get => ApiProvider switch
        {
            ApiProvider.CustomApi => _customApiKey,
            ApiProvider.Gemini => _geminiApiKey,
            _ => null
        };
        set
        {
            if (ApiProvider == ApiProvider.CustomApi)
            {
                _customApiKey = value;
            }
            else if (ApiProvider == ApiProvider.Gemini)
            {
                _geminiApiKey = value;
            }
        }
    }
    public List<string> GeminiModels { get; set; } = new();
    public List<string> CustomApiModels { get; set; } = new();
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
                TmdbApiKey,
                CustomApiKey,
                CustomApiBaseUrl,
                GeminiModels,
                CustomApiModels,
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
                
                // 1. 先嘗試反序列化為目前的結構
                SettingsData? data = null;
                try 
                {
                    data = JsonSerializer.Deserialize<SettingsData>(json);
                }
                catch
                {
                    // 如果反序列化失敗（結構差異太大），data 會是 null
                }

                // 2. 使用 JsonDocument 處理遷移邏輯 (讀取舊欄位)
                // 即使 data == null 也要能讀取舊欄位並完成遷移
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // 讀取舊的 Provider 值 (Number 或 String)
                int oldProviderValue = 0;
                if (root.TryGetProperty("ApiProvider", out var providerProp))
                {
                    if (providerProp.ValueKind == JsonValueKind.Number)
                        oldProviderValue = providerProp.GetInt32();
                    else if (providerProp.ValueKind == JsonValueKind.String && int.TryParse(providerProp.GetString(), out var parsed))
                        oldProviderValue = parsed;
                }

                // 讀取舊的 Deepseek 設定
                string? oldDeepseekKey = null;
                if (root.TryGetProperty("DeepseekProxyApiKey", out var deepseekKeyProp) && deepseekKeyProp.ValueKind == JsonValueKind.String)
                {
                    oldDeepseekKey = deepseekKeyProp.GetString();
                }

                string? oldDeepseekUrl = null;
                if (root.TryGetProperty("DeepseekProxyBaseUrl", out var deepseekUrlProp) && deepseekUrlProp.ValueKind == JsonValueKind.String)
                {
                    oldDeepseekUrl = deepseekUrlProp.GetString();
                }

                // 讀取其他舊版常見欄位
                string? oldTmdbApiKey = null;
                if (root.TryGetProperty("TmdbApiKey", out var tmdbProp) && tmdbProp.ValueKind == JsonValueKind.String)
                {
                    oldTmdbApiKey = tmdbProp.GetString();
                }

                string? oldModelName = null;
                if (root.TryGetProperty("ModelName", out var modelProp) && modelProp.ValueKind == JsonValueKind.String)
                {
                    oldModelName = modelProp.GetString();
                }

                string? oldNamingFormat = null;
                if (root.TryGetProperty("NamingFormat", out var namingProp) && namingProp.ValueKind == JsonValueKind.String)
                {
                    oldNamingFormat = namingProp.GetString();
                }

                string? oldCustomApiBaseUrl = null;
                if (root.TryGetProperty("CustomApiBaseUrl", out var customApiUrlProp) && customApiUrlProp.ValueKind == JsonValueKind.String)
                {
                    oldCustomApiBaseUrl = customApiUrlProp.GetString();
                }

                // 無論 ApiProvider 為何，都先嘗試載入最後使用的 Base URL（避免回到預設值）
                if (string.IsNullOrWhiteSpace(_customApiBaseUrl))
                {
                    _customApiBaseUrl = !string.IsNullOrWhiteSpace(oldCustomApiBaseUrl)
                        ? oldCustomApiBaseUrl
                        : oldDeepseekUrl;
                }

                NamingLanguage oldPreferredLanguage = NamingLanguage.TraditionalChinese;
                if (root.TryGetProperty("PreferredLanguage", out var langProp))
                {
                    if (langProp.ValueKind == JsonValueKind.Number)
                        oldPreferredLanguage = (NamingLanguage)langProp.GetInt32();
                    else if (langProp.ValueKind == JsonValueKind.String && Enum.TryParse< NamingLanguage>(langProp.GetString(), out var parsedLang))
                        oldPreferredLanguage = parsedLang;
                }

                // 3. 執行遷移與賦值 (無論 data 是否為 null 都要執行)
                // 如果是舊的 Provider ID (Groq=2, Deepseek=3, OpenRouter=4 等)，強制轉為 CustomApi
                if (oldProviderValue > 1) 
                {
                    ApiProvider = ApiProvider.CustomApi;
                    // 嘗試遷移 Deepseek 設定 (BaseUrl 遷移不依賴 API Key 是否存在)
                    if (!string.IsNullOrWhiteSpace(oldDeepseekKey))
                    {
                        _customApiKey = oldDeepseekKey;
                    }
                    if (string.IsNullOrWhiteSpace(_customApiBaseUrl) && !string.IsNullOrWhiteSpace(oldDeepseekUrl))
                    {
                        _customApiBaseUrl = oldDeepseekUrl;
                    }
                }
                else if (data != null)
                {
                    // 只有在不是舊版格式時才使用 data 的 ApiProvider
                    ApiProvider = data.ApiProvider;
                }

                // ApiKey / GeminiApiKey / TmdbApiKey / CustomApiKey 遷移
                if (data != null)
                {
                    var legacyKey = data.ApiKey;
                    GeminiApiKey = string.IsNullOrWhiteSpace(data.GeminiApiKey) ? legacyKey : data.GeminiApiKey;
                    TmdbApiKey = data.TmdbApiKey;
                    
                    // 如果 CustomApiKey 尚未設定，嘗試從 data 讀取
                    if (string.IsNullOrWhiteSpace(_customApiKey))
                    {
                        _customApiKey = data.CustomApiKey;
                    }

                    // CustomApiBaseUrl 遷移：優先使用 _customApiBaseUrl (來自遷移)，其次是 data，最後保持空字串 (讓 getter 回傳預設值)
                    if (string.IsNullOrWhiteSpace(_customApiBaseUrl) && !string.IsNullOrWhiteSpace(data.CustomApiBaseUrl))
                    {
                        _customApiBaseUrl = data.CustomApiBaseUrl;
                    }
                    // DeepseekProxyBaseUrl fallback：當 CustomApiBaseUrl 為空時，嘗試使用舊欄位
                    if (string.IsNullOrWhiteSpace(_customApiBaseUrl) && !string.IsNullOrWhiteSpace(oldDeepseekUrl))
                    {
                        _customApiBaseUrl = oldDeepseekUrl;
                    }
                    // 注意：不要在這裡設預設值，否則 getter 的預設值會干擾判斷

                    GeminiModels = data.GeminiModels ?? new List<string>();
                    CustomApiModels = data.CustomApiModels ?? new List<string>();
                    
                    var defaultModel = ApiProvider == ApiProvider.CustomApi ? "gpt-4o-mini" : ModelDefaults.GeminiDefaultModel;
                    ModelName = string.IsNullOrWhiteSpace(data.ModelName) ? defaultModel : data.ModelName;
                    NamingFormat = string.IsNullOrWhiteSpace(data.NamingFormat) ? "{Title} ({Year})" : data.NamingFormat;
                    PreferredLanguage = data.PreferredLanguage;
                }
                else
                {
                    // data == null，完全依靠舊欄位遷移
                    if (string.IsNullOrWhiteSpace(_customApiKey) && !string.IsNullOrWhiteSpace(oldDeepseekKey))
                    {
                        _customApiKey = oldDeepseekKey;
                    }
                    // CustomApiBaseUrl 遷移：支援 data == null 時讀取舊 JSON 格式
                    if (string.IsNullOrWhiteSpace(_customApiBaseUrl) && !string.IsNullOrWhiteSpace(oldCustomApiBaseUrl))
                    {
                        _customApiBaseUrl = oldCustomApiBaseUrl;
                    }

                    GeminiApiKey = null;
                    TmdbApiKey = oldTmdbApiKey;
                    GeminiModels = new List<string>();
                    CustomApiModels = new List<string>();
                    
                    var defaultModel = ApiProvider == ApiProvider.CustomApi ? "gpt-4o-mini" : ModelDefaults.GeminiDefaultModel;
                    ModelName = string.IsNullOrWhiteSpace(oldModelName) ? defaultModel : oldModelName;
                    NamingFormat = string.IsNullOrWhiteSpace(oldNamingFormat) ? "{Title} ({Year})" : oldNamingFormat;
                    PreferredLanguage = oldPreferredLanguage;
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
        string? TmdbApiKey,
        string? CustomApiKey,
        string? CustomApiBaseUrl,
        List<string>? GeminiModels,
        List<string>? CustomApiModels,
        string ModelName,
        string NamingFormat,
        NamingLanguage PreferredLanguage);
}
