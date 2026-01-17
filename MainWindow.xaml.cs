using System.Windows;
using AnimeFolderOrganizer.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AnimeFolderOrganizer;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Loaded += async (_, _) =>
        {
            if (viewModel.LoadHistoryCommand.CanExecute(null))
            {
                await viewModel.LoadHistoryCommand.ExecuteAsync(null);
            }
        };
    }
}
