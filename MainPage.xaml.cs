using System.Globalization;
using System.Text;
using AiTranslator.WinUI.Models;
using AiTranslator.WinUI.Services;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace AiTranslator.WinUI;

public sealed partial class MainPage : Page
{
    private readonly SettingsStore _settingsStore = new();
    private readonly TranslationService _translationService = new();
    private AppSettings _settings;
    private CancellationTokenSource? _translationCancellation;
    private bool _targetLanguageManuallySet;
    private bool _suppressLanguageSelection;
    private bool _isTranslating;

    public MainPage()
    {
        InitializeComponent();

        _settings = _settingsStore.Load();
        TargetLanguageComboBox.ItemsSource = AppSettings.TargetLanguages;
        SelectTargetLanguage(_settings.TargetLanguage);

        AddHandler(KeyDownEvent, new KeyEventHandler(Page_KeyDown), true);
    }

    private async void TranslateButton_Click(object sender, RoutedEventArgs e) =>
        await StartTranslationAsync();

    private async void Page_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter || !IsControlKeyDown())
        {
            return;
        }

        args.Handled = true;
        if (!_isTranslating && MainView.Visibility == Visibility.Visible)
        {
            await StartTranslationAsync();
        }
    }

    private static bool IsControlKeyDown() =>
        IsKeyDown(VirtualKey.Control);

    private static bool IsKeyDown(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;

    private void SourceTextBox_BeforeTextChanging(
        TextBox sender,
        TextBoxBeforeTextChangingEventArgs args)
    {
        if (IsControlKeyDown() && IsKeyDown(VirtualKey.Enter))
        {
            args.Cancel = true;
        }
    }

    private async Task StartTranslationAsync()
    {
        string source = SourceTextBox.Text.Trim();
        if (source.Length == 0)
        {
            SetStatus("请输入要翻译的文本", StatusKind.Warning);
            SourceTextBox.Focus(FocusState.Programmatic);
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            SetStatus("请先在设置中配置 API Key", StatusKind.Warning);
            OpenSettings();
            return;
        }

        if (!_targetLanguageManuallySet)
        {
            SelectTargetLanguage(IsPredominantlyChinese(source) ? "英文" : "中文");
        }

        _targetLanguageManuallySet = false;
        _settings.TargetLanguage = TargetLanguageComboBox.SelectedItem as string ?? "中文";

        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus($"无法保存设置：{exception.Message}", StatusKind.Error);
            return;
        }

        TargetTextBox.Text = string.Empty;
        _translationCancellation?.Dispose();
        _translationCancellation = new CancellationTokenSource();
        SetTranslationState(true);
        SetStatus("正在翻译…", StatusKind.Normal);

        Progress<string> progress = new(chunk =>
        {
            TargetTextBox.Text += chunk;
            TargetTextBox.SelectionStart = TargetTextBox.Text.Length;
        });

        try
        {
            await _translationService.TranslateAsync(
                _settings,
                source,
                progress,
                _translationCancellation.Token);
            SetStatus("翻译完成", StatusKind.Success);
        }
        catch (OperationCanceledException)
        {
            SetStatus("已取消", StatusKind.Normal);
        }
        catch (TranslationException exception)
        {
            SetStatus($"错误：{exception.Message}", StatusKind.Error);
        }
        catch (Exception exception)
        {
            SetStatus($"错误：{exception.Message}", StatusKind.Error);
        }
        finally
        {
            SetTranslationState(false);
        }
    }

    private void CancelTranslationButton_Click(object sender, RoutedEventArgs e) =>
        _translationCancellation?.Cancel();

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _translationCancellation?.Cancel();
        SourceTextBox.Text = string.Empty;
        TargetTextBox.Text = string.Empty;
        SetStatus("已清空", StatusKind.Normal);
        SourceTextBox.Focus(FocusState.Programmatic);
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (TargetTextBox.Text.Length == 0)
        {
            SetStatus("暂无可复制的译文", StatusKind.Warning);
            return;
        }

        DataPackage dataPackage = new();
        dataPackage.SetText(TargetTextBox.Text);
        Clipboard.SetContent(dataPackage);
        Clipboard.Flush();
        SetStatus("已复制到剪贴板", StatusKind.Success);
    }

    private void SourceTextBox_Paste(object sender, TextControlPasteEventArgs e)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Yield();
            if (!_isTranslating && SourceTextBox.Text.Trim().Length > 0)
            {
                await StartTranslationAsync();
            }
        });
    }

    private void TargetLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressLanguageSelection && TargetLanguageComboBox.SelectedItem is string language)
        {
            _settings.TargetLanguage = language;
            _targetLanguageManuallySet = true;
        }
    }

    private void OpenSettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        ApiKeyPasswordBox.Password = _settings.ApiKey;
        BaseUrlTextBox.Text = _settings.BaseUrl;
        ModelTextBox.Text = _settings.Model;
        SystemPromptTextBox.Text = _settings.SystemPrompt;
        TestStatusTextBlock.Text = string.Empty;
        MainView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
    }

    private void CancelSettingsButton_Click(object sender, RoutedEventArgs e) => CloseSettings();

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.ApiKey = ApiKeyPasswordBox.Password.Trim();
        _settings.BaseUrl = ValueOrDefault(BaseUrlTextBox.Text, AppSettings.DefaultBaseUrl);
        _settings.Model = ValueOrDefault(ModelTextBox.Text, AppSettings.DefaultModel);
        _settings.SystemPrompt = ValueOrDefault(SystemPromptTextBox.Text, AppSettings.DefaultSystemPrompt);

        try
        {
            _settingsStore.Save(_settings);
            SetStatus("设置已保存", StatusKind.Success);
            CloseSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TestStatusTextBlock.Text = $"保存失败：{exception.Message}";
            TestStatusTextBlock.Foreground = new SolidColorBrush(Colors.Firebrick);
        }
    }

    private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        AppSettings candidate = CreateSettingsFromFields();
        if (candidate.ApiKey.Length == 0)
        {
            TestStatusTextBlock.Text = "请先输入 API Key";
            TestStatusTextBlock.Foreground = new SolidColorBrush(Colors.DarkOrange);
            return;
        }

        TestConnectionButton.IsEnabled = false;
        TestProgressRing.IsActive = true;
        TestProgressRing.Visibility = Visibility.Visible;
        TestStatusTextBlock.Text = "正在测试…";
        TestStatusTextBlock.Foreground = new SolidColorBrush(Colors.Gray);

        try
        {
            string result = await _translationService.TestConnectionAsync(candidate, CancellationToken.None);
            TestStatusTextBlock.Text = $"✓ {result}";
            TestStatusTextBlock.Foreground = new SolidColorBrush(Colors.ForestGreen);
        }
        catch (TranslationException exception)
        {
            TestStatusTextBlock.Text = $"✗ {exception.Message}";
            TestStatusTextBlock.Foreground = new SolidColorBrush(Colors.Firebrick);
        }
        catch (OperationCanceledException)
        {
            TestStatusTextBlock.Text = "✗ 连接超时。";
            TestStatusTextBlock.Foreground = new SolidColorBrush(Colors.Firebrick);
        }
        finally
        {
            TestConnectionButton.IsEnabled = true;
            TestProgressRing.IsActive = false;
            TestProgressRing.Visibility = Visibility.Collapsed;
        }
    }

    private void ResetPromptButton_Click(object sender, RoutedEventArgs e) =>
        SystemPromptTextBox.Text = AppSettings.DefaultSystemPrompt;

    private void CloseSettings()
    {
        SettingsView.Visibility = Visibility.Collapsed;
        MainView.Visibility = Visibility.Visible;
    }

    private AppSettings CreateSettingsFromFields() => new()
    {
        ApiKey = ApiKeyPasswordBox.Password.Trim(),
        BaseUrl = ValueOrDefault(BaseUrlTextBox.Text, AppSettings.DefaultBaseUrl),
        Model = ValueOrDefault(ModelTextBox.Text, AppSettings.DefaultModel),
        SystemPrompt = ValueOrDefault(SystemPromptTextBox.Text, AppSettings.DefaultSystemPrompt),
        TargetLanguage = TargetLanguageComboBox.SelectedItem as string ?? "中文",
    };

    private void SelectTargetLanguage(string language)
    {
        _suppressLanguageSelection = true;
        TargetLanguageComboBox.SelectedItem = language;
        _settings.TargetLanguage = language;
        _suppressLanguageSelection = false;
    }

    private void SetTranslationState(bool isTranslating)
    {
        _isTranslating = isTranslating;
        TranslateButton.IsEnabled = !isTranslating;
        TranslateButtonText.Text = isTranslating ? "翻译中…" : "翻译  Ctrl+Enter";
        TranslateProgressRing.IsActive = isTranslating;
        TranslateProgressRing.Visibility = isTranslating ? Visibility.Visible : Visibility.Collapsed;
        CancelTranslationButton.Visibility = isTranslating ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetStatus(string message, StatusKind kind)
    {
        StatusTextBlock.Text = message;
        StatusTextBlock.Foreground = new SolidColorBrush(kind switch
        {
            StatusKind.Success => Colors.ForestGreen,
            StatusKind.Warning => Colors.DarkOrange,
            StatusKind.Error => Colors.Firebrick,
            _ => Colors.Gray,
        });
    }

    private static string ValueOrDefault(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static bool IsPredominantlyChinese(string text)
    {
        int totalLetters = 0;
        int cjkCharacters = 0;

        foreach (Rune rune in text.EnumerateRunes())
        {
            int value = rune.Value;
            bool isCjk = value is >= 0x4E00 and <= 0x9FFF
                or >= 0x3400 and <= 0x4DBF
                or >= 0x20000 and <= 0x2A6DF
                or >= 0xF900 and <= 0xFAFF;
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            bool isLetter = category is UnicodeCategory.UppercaseLetter
                or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter
                or UnicodeCategory.ModifierLetter
                or UnicodeCategory.OtherLetter;

            if (isLetter || isCjk)
            {
                totalLetters++;
                if (isCjk)
                {
                    cjkCharacters++;
                }
            }
        }

        return totalLetters > 0 && cjkCharacters * 5 > totalLetters;
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _translationCancellation?.Cancel();
        _translationCancellation?.Dispose();
        _translationService.Dispose();
    }

    private enum StatusKind
    {
        Normal,
        Success,
        Warning,
        Error,
    }
}
