using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;

namespace WinUiSmoke;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        InfoText.Text = $".NET {Environment.Version} / {RuntimeInformation.OSDescription}";
    }
}
