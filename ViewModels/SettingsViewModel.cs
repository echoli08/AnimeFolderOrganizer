using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AnimeFolderOrganizer.Services;
using System.Diagnostics;
using AnimeFolderOrganizer.Models;
using System.Collections.ObjectModel;
using System.Collections.Generic;

namespace AnimeFolderOrganizer.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IModelCatalogService _modelCatalogService;
    
    [ObservableProperty]
    private string? _apiKey;

    [ObservableProperty]
    private string _modelName = "gemini-2.5-flash-lite";

    [ObservableProperty]
    private string _namingFormat = "{Title} ({Year})";

    [ObservableProperty]
    private NamingLanguage _preferredLanguage = NamingLanguage.TraditionalChinese;

    public Action? CloseAction { get; set; }

    public ObservableCollection<string> AvailableModels { get; } = new();

    public SettingsViewModel(ISettingsService settingsService, IModelCatalogService modelCatalogService)
    {
        _settingsService = settingsService;
        _modelCatalogService = modelCatalogService;
        ApiKey = _settingsService.ApiKey;
        ModelName = _settingsService.ModelName;
        NamingFormat = _settingsService.NamingFormat;
        PreferredLanguage = _settingsService.PreferredLanguage;
        UpdateModelList(new[] { ModelName });
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        _settingsService.ApiKey = ApiKey;
        _settingsService.ModelName = ModelName;
        _settingsService.NamingFormat = NamingFormat;
        _settingsService.PreferredLanguage = PreferredLanguage;
        await _settingsService.SaveAsync();
        CloseAction?.Invoke();
    }

    [RelayCommand]
    private async Task LoadModelsAsync()
    {
        var models = await _modelCatalogService.GetAvailableModelsAsync(ApiKey);
        AvailableModels.Clear();

        if (models.Count == 0)
        {
            // 若無法取得模型列表，至少保留目前輸入值
            UpdateModelList(new[] { ModelName });
            return;
        }

        UpdateModelList(models);
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
    private void GetApiKey()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://aistudio.google.com/app/apikey",
                UseShellExecute = true
            });
        }
        catch
        {
            // Handle error opening browser
        }
    }
}
