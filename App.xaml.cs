using System.Net.Http;
using System.Windows;
using AnimeFolderOrganizer.Services;
using AnimeFolderOrganizer.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeFolderOrganizer;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public new static App Current => (App)Application.Current;

    public IServiceProvider Services { get; }

    public App()
    {
        Services = ConfigureServices();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Register Services
        services.AddSingleton<ISettingsService, FileSettingsService>();
        // 注意: GeminiMetadataProvider 需要 API Key 才能正常運作
        services.AddSingleton<GeminiMetadataProvider>();
        services.AddSingleton<OpenRouterMetadataProvider>();
        services.AddSingleton<GroqMetadataProvider>();
        services.AddSingleton<DeepseekProxyMetadataProvider>();
        services.AddSingleton<IMetadataProvider, MetadataProviderRouter>();
        services.AddSingleton<IFolderPickerService, FolderPickerService>();
        services.AddSingleton<ITextConverter, VbTextConverter>();
        services.AddSingleton<HttpClient>();
        services.AddSingleton<GeminiModelCatalogService>();
        services.AddSingleton<OpenRouterModelCatalogService>();
        services.AddSingleton<GroqModelCatalogService>();
        services.AddSingleton<DeepseekProxyModelCatalogService>();
        services.AddSingleton<IModelCatalogService, ModelCatalogService>();
        services.AddSingleton<IAnimeDbVerificationService, AnimeDbVerificationService>();
        services.AddSingleton<IHistoryDbService, HistoryDbService>();
        services.AddSingleton<IOfficialTitleLookupService, OfficialTitleLookupService>();

        // Register ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<SettingsViewModel>();

        // Register Views (Optional if using ViewFirst)
        services.AddTransient<MainWindow>();
        services.AddTransient<SettingsWindow>();

        return services.BuildServiceProvider();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settingsService = Services.GetRequiredService<ISettingsService>();
        await settingsService.LoadAsync();
        var historyDbService = Services.GetRequiredService<IHistoryDbService>();
        await historyDbService.InitializeAsync();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }
}
