using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Data;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AnimeFolderOrganizer.Models;
using AnimeFolderOrganizer.Services;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;

namespace AnimeFolderOrganizer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IMetadataProvider _metadataProvider;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ISettingsService _settingsService;
    private readonly IModelCatalogService _modelCatalogService;
    private readonly IHistoryDbService _historyDbService;
    private readonly IAnimeDbVerificationService _animeDbVerificationService;
    private readonly IOfficialTitleLookupService _officialTitleLookupService;
    private readonly ICollectionView _folderView;
    private readonly Dictionary<string, Regex> _formatRegexCache = new(StringComparer.Ordinal);
    private string _lastModelName = ModelDefaults.GeminiDefaultModel;
    private const int MaxScanLogEntries = 400;

    public MainViewModel(
        IMetadataProvider metadataProvider,
        IFolderPickerService folderPickerService,
        IServiceProvider serviceProvider,
        ISettingsService settingsService,
        IModelCatalogService modelCatalogService,
        IHistoryDbService historyDbService,
        IAnimeDbVerificationService animeDbVerificationService,
        IOfficialTitleLookupService officialTitleLookupService)
    {
        _metadataProvider = metadataProvider;
        _folderPickerService = folderPickerService;
        _serviceProvider = serviceProvider;
        _settingsService = settingsService;
        _modelCatalogService = modelCatalogService;
        _historyDbService = historyDbService;
        _animeDbVerificationService = animeDbVerificationService;
        _officialTitleLookupService = officialTitleLookupService;
        FolderList = new ObservableCollection<AnimeFolderInfo>();
        _folderView = CollectionViewSource.GetDefaultView(FolderList);
        _folderView.Filter = FilterFolder;
        HistoryItems = new ObservableCollection<HistoryItemViewModel>();
        PreferredLanguage = _settingsService.PreferredLanguage;
        NamingFormat = _settingsService.NamingFormat;
        ApiProvider = _settingsService.ApiProvider;
        ModelName = _settingsService.ModelName;
        _lastModelName = string.IsNullOrWhiteSpace(ModelName) ? _lastModelName : ModelName;
        ApplyProviderDefaults(ApiProvider);
        LoadCachedModels(ApiProvider);
    }

    [ObservableProperty]
    private string _targetDirectory = string.Empty;

    [ObservableProperty]
    private ApiProvider _apiProvider = ApiProvider.Gemini;

    [ObservableProperty]
    private string _modelName = ModelDefaults.GeminiDefaultModel;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScanFoldersCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameFoldersCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenSettingsCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreHistoryCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreSelectedCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "就緒";

    [ObservableProperty]
    private double _scanProgress;

    [ObservableProperty]
    private string _scanProgressText = "0/0";

    [ObservableProperty]
    private double _renameProgress;

    [ObservableProperty]
    private string _renameProgressText = "0/0";

    [ObservableProperty]
    private string _renameStatusMessage = "等待中";

    [ObservableProperty]
    private bool _showRenamedFolders = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreHistoryCommand))]
    private HistoryItemViewModel? _selectedHistoryItem;

    // 新增：全域語言偏好設定
    [ObservableProperty]
    private NamingLanguage _preferredLanguage = NamingLanguage.TraditionalChinese;

    [ObservableProperty]
    private string _namingFormat = "{Title} ({Year})";

    public ObservableCollection<AnimeFolderInfo> FolderList { get; }
    public ObservableCollection<HistoryItemViewModel> HistoryItems { get; }
    public ObservableCollection<string> AvailableModels { get; } = new();
    public ObservableCollection<string> ScanLogs { get; } = new();

    [RelayCommand(CanExecute = nameof(CanExecuteAction))]
    private async Task SelectFolderAsync()
    {
        string? path = _folderPickerService.PickFolder();
        if (!string.IsNullOrWhiteSpace(path))
        {
            TargetDirectory = path;
            await LoadFolderSnapshotAsync(path);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteAction))]
    private void OpenSettings()
    {
        var settingsWindow = _serviceProvider.GetRequiredService<SettingsWindow>();
        settingsWindow.Owner = Application.Current.MainWindow;
        settingsWindow.ShowDialog();

        PreferredLanguage = _settingsService.PreferredLanguage;
        NamingFormat = _settingsService.NamingFormat;
        ModelName = _settingsService.ModelName;
        ApiProvider = _settingsService.ApiProvider;
        LoadCachedModels(ApiProvider);
    }

    [RelayCommand]
    private async Task LoadModelsAsync()
    {
        var apiKey = GetApiKey(ApiProvider);
        var models = await _modelCatalogService.GetAvailableModelsAsync(ApiProvider, apiKey, false);
        AvailableModels.Clear();

        if (models.Count == 0)
        {
            UpdateModelList(new[] { ModelName });
            return;
        }

        UpdateModelList(models);
    }

    [RelayCommand]
    private async Task LoadHistoryAsync()
    {
        try
        {
            var items = await _historyDbService.GetRecentAsync(200);
            HistoryItems.Clear();
            foreach (var item in items)
            {
                var vm = new HistoryItemViewModel(item);
                AttachHistoryItem(vm);
                HistoryItems.Add(vm);
            }

            RestoreSelectedCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadHistory failed: {ex}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanRestoreHistory))]
    private async Task RestoreHistoryAsync()
    {
        if (SelectedHistoryItem == null) return;

        try
        {
            IsBusy = true;
            RenameStatusMessage = "還原中";
            await RestoreEntryAsync(SelectedHistoryItem.Entry);
            RenameStatusMessage = "還原完成";
        }
        catch (Exception ex)
        {
            if (SelectedHistoryItem != null)
            {
                await AddHistoryAsync(
                    SelectedHistoryItem.NewPath,
                    SelectedHistoryItem.OriginalPath,
                    "RestoreFailed",
                    ex.Message);
            }
            StatusMessage = $"還原發生錯誤: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRestoreSelected))]
    private async Task RestoreSelectedAsync()
    {
        var targets = HistoryItems.Where(x => x.IsSelected).ToList();
        if (targets.Count == 0) return;

        try
        {
            IsBusy = true;
            RenameStatusMessage = "批次還原中";

            foreach (var item in targets)
            {
                await RestoreEntryAsync(item.Entry);
                item.IsSelected = false;
            }

            RenameStatusMessage = "批次還原完成";
        }
        finally
        {
            IsBusy = false;
        }
    }


    // 在 AnimeFolderOrganizer.ViewModels.MainViewModel 中

    // [修正 1] Regex 定義應該獨立放在類別層級，不要加 [RelayCommand]
    [GeneratedRegex(@"\[.*?\]|\(.*?\)")]
    private static partial Regex BracketRegex();

    // [修正 2] ScanFoldersAsync 才是 Command，需要加 [RelayCommand]
    [RelayCommand(CanExecute = nameof(CanExecuteAction))]
    private async Task ScanFoldersAsync()
    {
        if (string.IsNullOrWhiteSpace(TargetDirectory) || !Directory.Exists(TargetDirectory))
        {
            StatusMessage = "請選擇有效的資料夾路徑";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "正在掃描與識別資料夾...";
            ScanLogs.Clear();
            AddScanLog($"開始掃描: {TargetDirectory}");
            FolderList.Clear();

            // 使用 Task.Run 避免 UI 凍結
            var dirInfos = await Task.Run(() =>
                new DirectoryInfo(TargetDirectory).GetDirectories().ToList());

            var renamedPaths = await _historyDbService.GetRenamedPathSetAsync();

            // 預先篩選需要辨識的清單
            var pendingProcessing = new List<(AnimeFolderInfo Info, DirectoryInfo Dir)>();

            int processedCount = 0;
            int totalCount = dirInfos.Count;
            ScanProgressText = $"0/{totalCount}";

            foreach (var dirInfo in dirInfos)
            {
                var isRenamed = renamedPaths.Contains(dirInfo.FullName);
                var isFormatted = MatchesNamingFormat(dirInfo.Name, NamingFormat);

                var info = new AnimeFolderInfo(dirInfo.FullName, dirInfo.Name)
                {
                    NamingFormat = NamingFormat,
                    IsRenamed = isRenamed || isFormatted,
                    AnalyzedTitle = dirInfo.Name,
                    SelectedTitle = dirInfo.Name
                };
                info.UpdateAvailableTitles();
                FolderList.Add(info);

                if (isRenamed || isFormatted)
                {
                    processedCount++;
                    continue;
                }

                pendingProcessing.Add((info, dirInfo));
            }

            // 批次處理邏輯
            const int batchSize = 10;
            for (var i = 0; i < pendingProcessing.Count; i += batchSize)
            {
                var batch = pendingProcessing.Skip(i).Take(batchSize).ToList();
                var names = batch.Select(x => x.Dir.Name).ToList();

                // 1. 初次辨識 (使用原始名稱)
                var results = await _metadataProvider.AnalyzeBatchAsync(names);

                for (var j = 0; j < batch.Count; j++)
                {
                    var (info, dirInfo) = batch[j];
                    var result = j < results.Count ? results[j] : null;

                    if (result != null)
                    {
                        await ApplyMetadataAsync(info, result);
                    }

                    // 2. [重試機制] 如果驗證失敗，嘗試清理雜訊後再次辨識
                    if (info.VerificationStatus == AnimeDbVerificationStatus.Failed)
                    {
                        var cleanName = CleanFolderNameForRetry(dirInfo.Name);

                        // 只有當清理後的名稱變短且不同時才重試，避免浪費 API
                        if (!string.Equals(cleanName, dirInfo.Name, StringComparison.OrdinalIgnoreCase) && cleanName.Length > 2)
                        {
                            AddScanLog($"驗證失敗，嘗試清理雜訊重試: {cleanName}");

                            // 單筆重試
                            var retryResult = await _metadataProvider.AnalyzeAsync(cleanName);
                            if (retryResult != null)
                            {
                                await ApplyMetadataAsync(info, retryResult);

                                if (info.VerificationStatus == AnimeDbVerificationStatus.Verified)
                                {
                                    AddScanLog($"重試成功: {info.AnalyzedTitle}");
                                }
                            }
                        }
                    }

                    processedCount++;
                }

                ScanProgress = totalCount == 0 ? 0 : processedCount * 100.0 / totalCount;
                ScanProgressText = $"{processedCount}/{totalCount}";
                StatusMessage = $"正在掃描與識別... {processedCount}/{totalCount}";
            }

            StatusMessage = $"掃描完成，共找到 {FolderList.Count} 個資料夾";
        }
        catch (Exception ex)
        {
            StatusMessage = $"掃描發生錯誤: {ex.Message}";
            AddScanLog($"錯誤: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 輔助方法：移除資料夾名稱中的括號與常見雜訊，保留核心標題
    /// </summary>
    private static string CleanFolderNameForRetry(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return string.Empty;

        // 1. 使用 Regex 移除 [] 和 () 內的內容
        var cleaned = BracketRegex().Replace(folderName, " ");

        // 2. 移除常見的技術雜訊關鍵字
        var noise = new[] { "1080p", "720p", "mkv", "mp4", "x264", "x265", "AAC", "BDRip", "WebRip", "AVC", "Hi10P" };
        foreach (var n in noise)
        {
            cleaned = cleaned.Replace(n, " ", StringComparison.OrdinalIgnoreCase);
        }

        // 3. 移除多餘空白並修剪
        return string.Join(" ", cleaned.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    [RelayCommand(CanExecute = nameof(CanExecuteAction))]
    private async Task ReanalyzeFolderAsync(object? parameter)
    {
        var targets = ResolveSelection(parameter, info => !string.IsNullOrWhiteSpace(info.OriginalFolderName));
        if (targets.Count == 0)
        {
            StatusMessage = "請先選擇要重新辨識的資料夾。";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = $"重新辨識中... 0/{targets.Count}";

            const int batchSize = 10;
            var processed = 0;

            for (var i = 0; i < targets.Count; i += batchSize)
            {
                var batch = targets.Skip(i).Take(batchSize).ToList();
                var names = batch.Select(x => x.OriginalFolderName).ToList();
                var results = await _metadataProvider.AnalyzeBatchAsync(names);

                for (var j = 0; j < batch.Count; j++)
                {
                    var info = batch[j];
                    var result = j < results.Count ? results[j] : null;
                    if (result != null)
                    {
                        await ApplyMetadataAsync(info, result);
                    }

                    processed++;
                    StatusMessage = $"重新辨識中... {processed}/{targets.Count}";
                }
            }

            StatusMessage = $"重新辨識完成: {processed}/{targets.Count}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"重新辨識失敗: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteAction))]
    private async Task ReanalyzeByJapaneseTitleAsync(object? parameter)
    {
        var targets = ResolveSelection(parameter, info => !string.IsNullOrWhiteSpace(info.TitleJP));
        if (targets.Count == 0)
        {
            StatusMessage = "缺少日文標題，無法重新辨識。";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = $"使用日文標題重新辨識中... 0/{targets.Count}";

            const int batchSize = 10;
            var processed = 0;

            for (var i = 0; i < targets.Count; i += batchSize)
            {
                var batch = targets.Skip(i).Take(batchSize).ToList();
                var names = batch.Select(x => x.TitleJP!).ToList();
                var results = await _metadataProvider.AnalyzeBatchAsync(names);

                for (var j = 0; j < batch.Count; j++)
                {
                    var info = batch[j];
                    var result = j < results.Count ? results[j] : null;
                    if (result != null)
                    {
                        await ApplyMetadataAsync(info, result, confirmTitles: true, japaneseTitleOverride: info.TitleJP);
                    }

                    processed++;
                    StatusMessage = $"使用日文標題重新辨識中... {processed}/{targets.Count}";
                }
            }

            StatusMessage = $"重新辨識完成: {processed}/{targets.Count}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"重新辨識失敗: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // 批次處理已取代單筆處理流程

    [RelayCommand(CanExecute = nameof(CanExecuteAction))]
    private async Task RenameFoldersAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "正在執行批次改名...";
            RenameStatusMessage = "執行中";

            int successCount = 0;
            int failedCount = 0;
            var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var errors = new List<string>();
            var total = FolderList.Count;
            var processed = 0;
            RenameProgress = 0;
            RenameProgressText = $"0/{total}";

            foreach (var item in FolderList)
            {
                processed++;
                RenameProgress = total == 0 ? 0 : processed * 100.0 / total;
                RenameProgressText = $"{processed}/{total}";

                if (!item.IsIdentified)
                {
                    if (string.IsNullOrWhiteSpace(item.SelectedTitle)) continue;
                    if (IsBlockedRenameId(item.MetadataId)) continue;
                }

                string newName = item.GetSuggestedName(NamingFormat);
                string newPath = Path.Combine(Path.GetDirectoryName(item.OriginalPath)!, newName);

                // 如果名稱沒變，跳過
                if (newPath.Equals(item.OriginalPath, StringComparison.OrdinalIgnoreCase)) continue;

                if (!IsPathLengthValid(newPath))
                {
                    failedCount++;
                    errors.Add($"路徑過長: {newPath}");
                    await AddHistoryAsync(item.OriginalPath, newPath, "Skipped", "路徑過長");
                    continue;
                }

                if (!Directory.Exists(item.OriginalPath))
                {
                    failedCount++;
                    errors.Add($"來源不存在: {item.OriginalPath}");
                    await AddHistoryAsync(item.OriginalPath, newPath, "Failed", "來源不存在");
                    continue;
                }

                if (await IsDirectoryLockedAsync(item.OriginalPath))
                {
                    failedCount++;
                    errors.Add($"資料夾被佔用: {item.OriginalPath}");
                    await AddHistoryAsync(item.OriginalPath, newPath, "Skipped", "資料夾被佔用");
                    continue;
                }

                if (existingNames.Contains(newPath) || Directory.Exists(newPath))
                {
                    failedCount++;
                    errors.Add($"目標名稱重複: {newPath}");
                    await AddHistoryAsync(item.OriginalPath, newPath, "Skipped", "目標名稱重複");
                    continue; 
                }

                try
                {
                    if (!Directory.Exists(item.OriginalPath))
                    {
                        failedCount++;
                        errors.Add($"來源不存在: {item.OriginalPath}");
                        await AddHistoryAsync(item.OriginalPath, newPath, "Failed", "來源不存在");
                        continue;
                    }

                    var originalPath = item.OriginalPath;
                    await Task.Run(() => Directory.Move(item.OriginalPath, newPath));
                    existingNames.Add(newPath);
                    item.UpdateOriginalPath(newPath);
                    item.IsRenamed = true;
                    successCount++;
                    await AddHistoryAsync(originalPath, newPath, "Success", "改名完成");
                    RefreshFolderView();
                }
                catch (Exception ex)
                {
                    // 單一資料夾改名失敗不影響整批流程
                    failedCount++;
                    errors.Add($"改名失敗: {item.OriginalPath} => {newPath} ({ex.Message})");
                    await AddHistoryAsync(item.OriginalPath, newPath, "Failed", ex.Message);
                }
            }

            StatusMessage = failedCount == 0
                ? $"改名完成，成功處理 {successCount} 個項目"
                : $"改名完成，成功 {successCount}，失敗 {failedCount}";
            RenameStatusMessage = failedCount == 0 ? "完成" : "完成 (含失敗)";
            await LoadHistoryAsync();

            foreach (var error in errors)
            {
                System.Diagnostics.Debug.WriteLine(error);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"改名發生錯誤: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanExecuteAction() => !IsBusy;

    private bool CanRestoreHistory() => !IsBusy && SelectedHistoryItem != null;

    private bool CanRestoreSelected() => !IsBusy && HistoryItems.Any(x => x.IsSelected);

    private bool FilterFolder(object? obj)
    {
        if (ShowRenamedFolders) return true;
        return obj is AnimeFolderInfo info && !info.IsRenamed;
    }

    partial void OnApiProviderChanged(ApiProvider value)
    {
        _settingsService.ApiProvider = value;
        ApplyProviderDefaults(value);
        LoadCachedModels(value);
        _ = SaveSettingsAsync();
    }

    partial void OnModelNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            ModelName = _lastModelName;
            return;
        }

        _lastModelName = value;
        _settingsService.ModelName = value;
        EnsureModelInList(value);
        _ = SaveSettingsAsync();
    }

    private void EnsureModelInList(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return;
        if (AvailableModels.Any(m => string.Equals(m, modelName, StringComparison.OrdinalIgnoreCase))) return;
        AvailableModels.Add(modelName);
    }

    private async Task SaveSettingsAsync()
    {
        await _settingsService.SaveAsync();
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

    private void LoadCachedModels(ApiProvider provider)
    {
        AvailableModels.Clear();
        var cached = provider switch
        {
            ApiProvider.OpenRouter => _settingsService.OpenRouterModels,
            ApiProvider.Groq => _settingsService.GroqModels,
            ApiProvider.DeepseekProxy => _settingsService.DeepseekProxyModels,
            _ => _settingsService.GeminiModels
        };

        if (cached != null && cached.Count > 0)
        {
            UpdateModelList(cached);
        }
        else
        {
            UpdateModelList(new[] { ModelName });
        }

        EnsureModelInList(ModelName);
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

    private string? GetApiKey(ApiProvider provider)
    {
        return provider switch
        {
            ApiProvider.OpenRouter => _settingsService.OpenRouterApiKey,
            ApiProvider.Groq => _settingsService.GroqApiKey,
            ApiProvider.DeepseekProxy => _settingsService.DeepseekProxyApiKey,
            _ => _settingsService.GeminiApiKey
        };
    }

    private void RefreshFolderView()
    {
        _folderView.Refresh();
    }

    private void AddScanLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        ScanLogs.Add($"{timestamp} {message}");
        while (ScanLogs.Count > MaxScanLogEntries)
        {
            ScanLogs.RemoveAt(0);
        }
    }

    private bool MatchesNamingFormat(string folderName, string format)
    {
        try
        {
            if (_formatRegexCache.TryGetValue(format, out var cached))
            {
                return cached.IsMatch(folderName);
            }

            // 依格式動態建立 Regex，避免解析標題內容
            var pattern = Regex.Escape(format)
                .Replace("\\{Title\\}", ".+")
                .Replace("\\{TitleTW\\}", ".+")
                .Replace("\\{TitleCN\\}", ".+")
                .Replace("\\{TitleJP\\}", ".+")
                .Replace("\\{TitleEN\\}", ".+")
                .Replace("\\{Type\\}", ".+")
                .Replace("\\{Original\\}", ".+")
                .Replace("\\{Year\\}", "\\\\d{4}");

            pattern = $"^{pattern}$";
            var regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            _formatRegexCache[format] = regex;
            return regex.IsMatch(folderName);
        }
        catch
        {
            // 例外處理：格式異常時不視為已符合
            return false;
        }
    }

    private async Task AddHistoryAsync(string originalPath, string newPath, string status, string message)
    {
        try
        {
            var entry = new RenameHistoryEntry(
                TimestampUtc: DateTime.UtcNow,
                OriginalPath: originalPath,
                NewPath: newPath,
                Status: status,
                Message: message
            );
            await _historyDbService.AddAsync(entry);
            var vm = new HistoryItemViewModel(entry);
            AttachHistoryItem(vm);
            HistoryItems.Insert(0, vm);
            RestoreSelectedCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AddHistory failed: {ex}");
        }
    }

    private static List<AnimeFolderInfo> ResolveSelection(object? parameter, Func<AnimeFolderInfo, bool> predicate)
    {
        var results = new List<AnimeFolderInfo>();
        var seen = new HashSet<AnimeFolderInfo>();

        void AddItem(AnimeFolderInfo info)
        {
            if (predicate(info) && seen.Add(info))
            {
                results.Add(info);
            }
        }

        if (parameter is AnimeFolderInfo single)
        {
            AddItem(single);
            return results;
        }

        if (parameter is IList list)
        {
            foreach (var item in list)
            {
                if (item is AnimeFolderInfo info)
                {
                    AddItem(info);
                }
            }

            return results;
        }

        if (parameter is IEnumerable<AnimeFolderInfo> enumerable)
        {
            foreach (var info in enumerable)
            {
                AddItem(info);
            }
        }

        return results;
    }

    private async Task LoadFolderSnapshotAsync(string path)
    {
        if (!Directory.Exists(path)) return;

        try
        {
            IsBusy = true;
            StatusMessage = "載入資料夾清單...";
            FolderList.Clear();

            var directories = await Task.Run(() => Directory.GetDirectories(path));
            var renamedPaths = await _historyDbService.GetRenamedPathSetAsync();

            foreach (var dir in directories)
            {
                var dirInfo = new DirectoryInfo(dir);
                var info = new AnimeFolderInfo(dirInfo.FullName, dirInfo.Name)
                {
                    NamingFormat = NamingFormat,
                    IsRenamed = renamedPaths.Contains(dirInfo.FullName),
                    AnalyzedTitle = dirInfo.Name,
                    SelectedTitle = dirInfo.Name
                };

                info.UpdateAvailableTitles();
                FolderList.Add(info);
            }

            ScanProgress = 0;
            ScanProgressText = $"0/{FolderList.Count}";
            StatusMessage = $"已載入 {FolderList.Count} 個資料夾";
            RefreshFolderView();
        }
        catch (Exception ex)
        {
            StatusMessage = $"載入清單失敗: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AttachHistoryItem(HistoryItemViewModel item)
    {
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HistoryItemViewModel.IsSelected))
            {
                RestoreSelectedCommand.NotifyCanExecuteChanged();
            }
        };
    }

    private async Task RestoreEntryAsync(RenameHistoryEntry entry)
    {
        var sourcePath = entry.NewPath;
        var targetPath = entry.OriginalPath;

        if (!IsPathLengthValid(targetPath))
        {
            await AddHistoryAsync(sourcePath, targetPath, "RestoreSkipped", "目標路徑過長");
            StatusMessage = "還原失敗：目標路徑過長";
            return;
        }

        if (!Directory.Exists(sourcePath))
        {
            await AddHistoryAsync(sourcePath, targetPath, "RestoreFailed", "來源不存在");
            StatusMessage = "還原失敗：來源不存在";
            return;
        }

        if (Directory.Exists(targetPath))
        {
            await AddHistoryAsync(sourcePath, targetPath, "RestoreSkipped", "目標已存在");
            StatusMessage = "還原略過：目標已存在";
            return;
        }

        if (await IsDirectoryLockedAsync(sourcePath))
        {
            await AddHistoryAsync(sourcePath, targetPath, "RestoreSkipped", "資料夾被佔用");
            StatusMessage = "還原略過：資料夾被佔用";
            return;
        }

        await Task.Run(() => Directory.Move(sourcePath, targetPath));
        await AddHistoryAsync(sourcePath, targetPath, "RestoreSuccess", "還原完成");

        var item = FolderList.FirstOrDefault(x => x.OriginalPath.Equals(sourcePath, StringComparison.OrdinalIgnoreCase));
        if (item != null)
        {
            item.UpdateOriginalPath(targetPath);
            item.IsRenamed = false;
            RefreshFolderView();
        }

        StatusMessage = "還原完成";
    }

    private async Task ApplyMetadataAsync(AnimeFolderInfo info, AnimeMetadata result, bool confirmTitles = false, string? japaneseTitleOverride = null)
    {
        info.TitleJP = result.TitleJP;
        info.TitleCN = result.TitleCN;
        info.TitleTW = result.TitleTW;
        info.TitleEN = result.TitleEN;
        info.Type = result.Type;
        info.Year = result.Year;
        info.MetadataId = result.Id;

        var verificationTarget = string.IsNullOrWhiteSpace(result.TitleJP) ? info.TitleJP : result.TitleJP;
        var verificationStatus = IsValidMetadata(result)
            ? await VerifyTitleAsync(verificationTarget)
            : AnimeDbVerificationStatus.Failed;
        info.VerificationStatus = verificationStatus;
        info.IsIdentified = verificationStatus == AnimeDbVerificationStatus.Verified;

        info.AnalyzedTitle = result.TitleTW ?? result.TitleCN ?? result.TitleJP ?? result.TitleEN;
        info.SelectedTitle = GetPreferredTitle(info);
        info.UpdateAvailableTitles();

        if (confirmTitles)
        {
            await ApplyOfficialTitlesAsync(info, japaneseTitleOverride ?? info.TitleJP);
        }
    }

    private async Task ApplyOfficialTitlesAsync(AnimeFolderInfo info, string? japaneseTitle)
    {
        if (string.IsNullOrWhiteSpace(japaneseTitle)) return;

        var official = await _officialTitleLookupService.LookupAsync(japaneseTitle);
        if (official == null)
        {
            info.TitleCN = string.Empty;
            info.TitleTW = string.Empty;
            info.TitleEN = string.Empty;
            info.AnalyzedTitle = japaneseTitle;
            info.SelectedTitle = GetPreferredTitle(info);
            info.UpdateAvailableTitles();
            return;
        }

        info.TitleJP = FirstNonEmpty(official.TitleJP, info.TitleJP);
        info.TitleCN = official.TitleCN ?? string.Empty;
        info.TitleTW = official.TitleTW ?? string.Empty;
        info.TitleEN = official.TitleEN ?? string.Empty;
        info.AnalyzedTitle = FirstNonEmpty(info.TitleTW, info.TitleCN, info.TitleJP, info.TitleEN, japaneseTitle);
        info.SelectedTitle = GetPreferredTitle(info);
        info.UpdateAvailableTitles();
    }

    private static bool IsValidMetadata(AnimeMetadata metadata)
    {
        return !IsBlockedRenameId(metadata.Id);
    }

    private async Task<AnimeDbVerificationStatus> VerifyTitleAsync(string? title)
    {
        try
        {
            return await _animeDbVerificationService.VerifyTitleAsync(title);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AnimeDB verify failed: {ex}");
            return AnimeDbVerificationStatus.Failed;
        }
    }

    private static bool IsBlockedRenameId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        return id.Equals("no-key", StringComparison.OrdinalIgnoreCase)
               || id.Equals("rate-limit", StringComparison.OrdinalIgnoreCase)
               || id.Equals("quota-exceeded", StringComparison.OrdinalIgnoreCase)
               || id.Equals("model-not-found", StringComparison.OrdinalIgnoreCase);
    }

    partial void OnPreferredLanguageChanged(NamingLanguage value)
    {
        foreach (var item in FolderList)
        {
            item.SelectedTitle = GetPreferredTitle(item);
            item.UpdateAvailableTitles();
        }
    }

    partial void OnNamingFormatChanged(string value)
    {
        _formatRegexCache.Clear();
        foreach (var item in FolderList)
        {
            item.NamingFormat = value;
        }
    }

    partial void OnShowRenamedFoldersChanged(bool value)
    {
        _folderView.Refresh();
    }

    private string? GetPreferredTitle(AnimeFolderInfo info)
    {
        return PreferredLanguage switch
        {
            NamingLanguage.TraditionalChinese => FirstNonEmpty(
                info.TitleTW, info.TitleCN, info.TitleJP, info.TitleEN, info.AnalyzedTitle, info.OriginalFolderName),
            NamingLanguage.SimplifiedChinese => FirstNonEmpty(
                info.TitleCN, info.TitleTW, info.TitleJP, info.TitleEN, info.AnalyzedTitle, info.OriginalFolderName),
            NamingLanguage.Japanese => FirstNonEmpty(
                info.TitleJP, info.TitleTW, info.TitleCN, info.TitleEN, info.AnalyzedTitle, info.OriginalFolderName),
            NamingLanguage.English => FirstNonEmpty(
                info.TitleEN, info.TitleJP, info.TitleTW, info.TitleCN, info.AnalyzedTitle, info.OriginalFolderName),
            _ => FirstNonEmpty(
                info.TitleTW, info.TitleCN, info.TitleJP, info.TitleEN, info.AnalyzedTitle, info.OriginalFolderName)
        };
    }

    private static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsPathLengthValid(string path)
    {
        const int MaxPathLength = 260;
        return path.Length < MaxPathLength;
    }

    private static async Task<bool> IsDirectoryLockedAsync(string directoryPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(directoryPath))
                {
                    return true;
                }

                foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        using var stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    }
                    catch (Exception)
                    {
                        // 例外代表檔案可能被佔用或無權限，視為鎖定
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // 例外代表無法安全檢查，視為鎖定
                return true;
            }

            return false;
        });
    }
}
