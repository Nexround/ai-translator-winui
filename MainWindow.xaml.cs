using System;
using Microsoft.UI.Windowing;
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
        AppWindow.Title = "翻译助手";

        RootFrame.Navigate(typeof(MainPage));
        RootFrame.Loaded += RootFrame_Loaded;
    }

    private void RootFrame_Loaded(object sender, RoutedEventArgs e)
    {
        RootFrame.Loaded -= RootFrame_Loaded;

        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var scale = RootFrame.XamlRoot.RasterizationScale;
        var margin = (int)Math.Round(24 * scale);
        var width = Math.Max(1, Math.Min((int)Math.Round(900 * scale), workArea.Width - margin * 2));
        var height = Math.Max(1, Math.Min((int)Math.Round(860 * scale), workArea.Height - margin * 2));

        AppWindow.MoveAndResize(
            new RectInt32(
                workArea.X + (workArea.Width - width) / 2,
                workArea.Y + (workArea.Height - height) / 2,
                width,
                height),
            displayArea);
    }
}
