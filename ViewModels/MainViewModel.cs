using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AnimeFolderOrganizer.Models;
using AnimeFolderOrganizer.Services;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeFolderOrganizer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IMetadataProvider _metadataProvider;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ISettingsService _settingsService;
    private readonly ITextConverter _textConverter;
    private readonly IHistoryDbService _historyDbService;

    public MainViewModel(
        IMetadataProvider metadataProvider,
        IFolderPickerService folderPickerService,
        IServiceProvider serviceProvider,
        ISettingsService settingsService,
        ITextConverter textConverter,
        IHistoryDbService historyDbService)
    {
        _metadataProvider = metadataProvider;
        _folderPickerService = folderPickerService;
        _serviceProvider = serviceProvider;
        _settingsService = settingsService;
        _textConverter = textConverter;
        _historyDbService = historyDbService;
        FolderList = new ObservableCollection<AnimeFolderInfo>();
        HistoryItems = new ObservableCollection<HistoryItemViewModel>();
        PreferredLanguage = _settingsService.PreferredLanguage;
        NamingFormat = _settingsService.NamingFormat;
    }

    [ObservableProperty]
    private string _targetDirectory = string.Empty;

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
    [NotifyCanExecuteChangedFor(nameof(RestoreHistoryCommand))]
    private HistoryItemViewModel? _selectedHistoryItem;

    // 新增：全域語言偏好設定
    [ObservableProperty]
    private NamingLanguage _preferredLanguage = NamingLanguage.TraditionalChinese;

    [ObservableProperty]
    private string _namingFormat = "{Title} ({Year})";

    public ObservableCollection<AnimeFolderInfo> FolderList { get; }
    public ObservableCollection<HistoryItemViewModel> HistoryItems { get; }

    [RelayCommand(CanExecute = nameof(CanExecuteAction))]
    private void SelectFolder()
    {
        string? path = _folderPickerService.PickFolder();
        if (!string.IsNullOrWhiteSpace(path))
        {
            TargetDirectory = path;
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
            StatusMessage = "正在掃描與識別資料夾 (可能需要一些時間)...";
            FolderList.Clear();

            var directories = await Task.Run(() => Directory.GetDirectories(TargetDirectory));
            var dirInfos = directories.Select(d => new DirectoryInfo(d)).ToList();
            const int batchSize = 10;
            ScanProgress = 0;
            ScanProgressText = $"0/{dirInfos.Count}";

            for (var i = 0; i < dirInfos.Count; i += batchSize)
            {
                var batch = dirInfos.Skip(i).Take(batchSize).ToList();
                var names = batch.Select(d => d.Name).ToList();
                var results = await _metadataProvider.AnalyzeBatchAsync(names);

                for (var j = 0; j < batch.Count; j++)
                {
                    var dirInfo = batch[j];
                    var info = new AnimeFolderInfo(dirInfo.FullName, dirInfo.Name)
                    {
                        NamingFormat = NamingFormat
                    };

                    var result = j < results.Count ? results[j] : null;
                    if (result != null)
                    {
                        info.TitleJP = result.TitleJP;
                        info.TitleCN = result.TitleCN;
                        info.TitleTW = result.TitleTW;
                        info.TitleEN = result.TitleEN;
                        info.Year = result.Year;
                        info.MetadataId = result.Id;
                        info.IsIdentified = IsValidMetadata(result);

                        info.AnalyzedTitle = result.TitleTW ?? result.TitleCN ?? result.TitleJP;
                        info.SelectedTitle = GetPreferredTitle(info);
                        info.UpdateAvailableTitles();
                    }

                    FolderList.Add(info);
                }

                var processed = Math.Min(i + batch.Count, dirInfos.Count);
                ScanProgress = dirInfos.Count == 0 ? 0 : processed * 100.0 / dirInfos.Count;
                ScanProgressText = $"{processed}/{dirInfos.Count}";
                StatusMessage = $"正在掃描與識別... {processed}/{dirInfos.Count}";
            }

            StatusMessage = $"掃描完成，共找到 {FolderList.Count} 個資料夾";
        }
        catch (Exception ex)
        {
            StatusMessage = $"掃描發生錯誤: {ex.Message}";
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
                    successCount++;
                    await AddHistoryAsync(originalPath, newPath, "Success", "改名完成");
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
        }

        StatusMessage = "還原完成";
    }

    private static bool IsValidMetadata(AnimeMetadata metadata)
    {
        return !IsBlockedRenameId(metadata.Id);
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
        foreach (var item in FolderList)
        {
            item.NamingFormat = value;
        }
    }

    private string? GetPreferredTitle(AnimeFolderInfo info)
    {
        return PreferredLanguage switch
        {
            NamingLanguage.TraditionalChinese => 
                !string.IsNullOrWhiteSpace(info.TitleTW)
                    ? info.TitleTW
                    : _textConverter.ToTraditional(info.TitleCN ?? info.TitleJP ?? info.TitleEN),
            NamingLanguage.SimplifiedChinese =>
                !string.IsNullOrWhiteSpace(info.TitleCN)
                    ? info.TitleCN
                    : _textConverter.ToSimplified(info.TitleTW ?? info.TitleJP ?? info.TitleEN),
            NamingLanguage.Japanese =>
                !string.IsNullOrWhiteSpace(info.TitleJP)
                    ? info.TitleJP
                    : info.TitleTW ?? info.TitleCN ?? info.TitleEN,
            NamingLanguage.English =>
                !string.IsNullOrWhiteSpace(info.TitleEN)
                    ? info.TitleEN
                    : info.TitleJP ?? info.TitleTW ?? info.TitleCN,
            _ => info.TitleTW ?? info.TitleCN ?? info.TitleJP ?? info.TitleEN
        };
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
