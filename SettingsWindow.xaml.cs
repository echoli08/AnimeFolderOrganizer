using System.Windows;
using AnimeFolderOrganizer.ViewModels;

namespace AnimeFolderOrganizer;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        
        // 讓 ViewModel 可以關閉視窗
        viewModel.CloseAction = () => this.Close();

    }
}
