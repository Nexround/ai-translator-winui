using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace AiTranslator.WinUI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(980, 700));
        AppWindow.Title = "翻译助手";

        RootFrame.Navigate(typeof(MainPage));
    }
}
