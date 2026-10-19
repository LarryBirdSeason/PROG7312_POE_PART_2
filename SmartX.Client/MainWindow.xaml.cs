using System.Windows;
using SmartX.Client.Services;
using SmartX.Client.ViewModels;

namespace SmartX.Client;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new ShellViewModel(new ApiClient("http://localhost:5080"));
    }
}
