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
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.System;
using Windows.UI.Core;

namespace AiTranslator.WinUI;

public sealed partial class MainPage : Page
{
    private readonly SettingsStore _settingsStore = new();
    private readonly TranslationService _translationService = new();
    private readonly DictionaryService _dictionaryService = new();
    private readonly MediaPlayer _audioPlayer = new();
    private AppSettings _settings;
    private CancellationTokenSource? _translationCancellation;
    private DictionaryEntry? _dictionaryEntry;
    private string? _dictionaryWord;
    private string? _dictionaryCopyText;
    private int _operationVersion;
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
        _audioPlayer.MediaFailed += (_, _) => DispatcherQueue.TryEnqueue(() =>
            SetStatus("发音播放失败，请检查网络后重试。", StatusKind.Error));
        UpdateActionLabel();
    }

    private async void TranslateButton_Click(object sender, RoutedEventArgs e) =>
        await StartActionAsync();

    private async void Page_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter || !IsControlKeyDown())
        {
            return;
        }

        args.Handled = true;
        if (!_isTranslating && MainView.Visibility == Visibility.Visible)
        {
            await StartActionAsync();
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

    private async Task StartActionAsync(bool forceAi = false)
    {
        if (_isTranslating)
        {
            return;
        }

        string source = SourceTextBox.Text.Trim();
        if (source.Length == 0)
        {
            SetStatus("请输入要翻译的文本", StatusKind.Warning);
            SourceTextBox.Focus(FocusState.Programmatic);
            return;
        }

        if (!forceAi && ShouldUseDictionary(source))
        {
            await LookupWordAsync(source);
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

        ShowTranslationView();
        TargetTextBox.Text = string.Empty;
        int operationVersion = ++_operationVersion;
        _translationCancellation?.Dispose();
        _translationCancellation = new CancellationTokenSource();
        SetTranslationState(true, false);
        SetStatus("正在翻译…", StatusKind.Normal);

        Progress<string> progress = new(chunk =>
        {
            if (operationVersion != _operationVersion)
            {
                return;
            }

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
            if (operationVersion == _operationVersion)
            {
                SetStatus("翻译完成", StatusKind.Success);
            }
        }
        catch (OperationCanceledException)
        {
            if (operationVersion == _operationVersion)
            {
                SetStatus("已取消", StatusKind.Normal);
            }
        }
        catch (TranslationException exception)
        {
            if (operationVersion == _operationVersion)
            {
                SetStatus($"错误：{exception.Message}", StatusKind.Error);
            }
        }
        catch (Exception exception)
        {
            if (operationVersion == _operationVersion)
            {
                SetStatus($"错误：{exception.Message}", StatusKind.Error);
            }
        }
        finally
        {
            if (operationVersion == _operationVersion)
            {
                SetTranslationState(false, false);
            }
        }
    }

    private bool ShouldUseDictionary(string source) =>
        DictionaryService.IsEnglishWord(source)
        && (!_targetLanguageManuallySet || TargetLanguageComboBox.SelectedItem is "中文");

    private async Task LookupWordAsync(string word)
    {
        if (!_targetLanguageManuallySet)
        {
            SelectTargetLanguage("中文");
        }

        _targetLanguageManuallySet = false;
        _dictionaryWord = word;
        _dictionaryEntry = null;
        _dictionaryCopyText = null;
        TargetTextBox.Text = string.Empty;
        TargetTextBox.Visibility = Visibility.Collapsed;
        DictionaryView.Visibility = Visibility.Visible;
        DictionaryLoadingView.Visibility = Visibility.Visible;
        DictionaryMessageView.Visibility = Visibility.Collapsed;
        DictionaryContentView.Visibility = Visibility.Collapsed;
        ResultTitleTextBlock.Text = "英汉词典";
        ResultSourceTextBlock.Text = "UAPI";
        CopyButton.Content = "复制释义";
        CopyButton.IsEnabled = false;

        int operationVersion = ++_operationVersion;
        _translationCancellation?.Dispose();
        _translationCancellation = new CancellationTokenSource();
        SetTranslationState(true, true);
        SetStatus($"正在查询 {word}…", StatusKind.Normal);

        try
        {
            DictionaryEntry? entry = await _dictionaryService.LookupAsync(word, _translationCancellation.Token);
            if (operationVersion != _operationVersion || SourceTextBox.Text.Trim() != word)
            {
                return;
            }

            if (entry is null)
            {
                ShowDictionaryMessage("词典暂未收录这个词。可以检查拼写，或使用 AI 翻译。");
                SetStatus("词典未收录", StatusKind.Warning);
            }
            else
            {
                ShowDictionaryEntry(entry);
                SetStatus("查词完成", StatusKind.Success);
            }
        }
        catch (OperationCanceledException)
        {
            if (operationVersion == _operationVersion && SourceTextBox.Text.Trim() == word)
            {
                ShowDictionaryMessage("查词已取消，可以重试。");
                SetStatus("已取消", StatusKind.Normal);
            }
        }
        catch (DictionaryLookupException exception)
        {
            if (operationVersion == _operationVersion)
            {
                ShowDictionaryMessage(exception.Message);
                SetStatus("词典查询失败", StatusKind.Error);
            }
        }
        finally
        {
            if (operationVersion == _operationVersion)
            {
                SetTranslationState(false, true);
            }
        }
    }

    private void ShowDictionaryMessage(string message)
    {
        DictionaryLoadingView.Visibility = Visibility.Collapsed;
        DictionaryContentView.Visibility = Visibility.Collapsed;
        DictionaryMessageTextBlock.Text = message;
        DictionaryMessageView.Visibility = Visibility.Visible;
    }

    private void ShowDictionaryEntry(DictionaryEntry entry)
    {
        _dictionaryEntry = entry;
        DictionaryWordTextBlock.Text = string.IsNullOrWhiteSpace(entry.Word) ? _dictionaryWord : entry.Word;
        DictionaryTagsTextBlock.Text = string.Join("  ·  ", entry.ExamTags ?? []);
        DictionaryTagsTextBlock.Visibility = string.IsNullOrEmpty(DictionaryTagsTextBlock.Text)
            ? Visibility.Collapsed : Visibility.Visible;

        DictionaryUkPhoneticTextBlock.Text = FormatPhonetic(entry.Phonetics?.Uk?.Text);
        DictionaryUsPhoneticTextBlock.Text = FormatPhonetic(entry.Phonetics?.Us?.Text);
        DictionaryUkAudioButton.IsEnabled = DictionaryService.GetAudioUri(_dictionaryWord!, "uk", entry.Phonetics?.Uk) is not null;
        DictionaryUsAudioButton.IsEnabled = DictionaryService.GetAudioUri(_dictionaryWord!, "us", entry.Phonetics?.Us) is not null;

        List<DictionaryDefinition> definitions = (entry.Definitions ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Meaning))
            .Take(8)
            .ToList();
        DictionaryDefinitionsItemsControl.ItemsSource = definitions;
        DictionaryDefinitionsSection.Visibility = definitions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        List<DictionaryExample> examples = (entry.Examples ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Source))
            .Take(3)
            .ToList();
        DictionaryExamplesItemsControl.ItemsSource = examples;
        DictionaryExamplesSection.Visibility = examples.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        List<DictionaryPhrase> phrases = (entry.Phrases ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Phrase))
            .Take(6)
            .ToList();
        DictionaryPhrasesItemsControl.ItemsSource = phrases;
        DictionaryPhrasesSection.Visibility = phrases.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        DictionaryFormsTextBlock.Text = string.Join("  ·  ", (entry.WordForms ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.Value))
            .Take(8)
            .Select(item => $"{item.Name} {item.Value}"));
        DictionaryFormsSection.Visibility = DictionaryFormsTextBlock.Text.Length > 0
            ? Visibility.Visible : Visibility.Collapsed;

        List<DictionaryEnglishDefinition> englishDefinitions = (entry.EnglishDefinitions ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Definition))
            .Take(6)
            .ToList();
        DictionaryEnglishItemsControl.ItemsSource = englishDefinitions;
        DictionaryEnglishSection.Visibility = englishDefinitions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DictionaryEnglishSection.IsExpanded = false;

        UpdateDictionaryLayout();

        _dictionaryCopyText = string.Join(Environment.NewLine, new[] { DictionaryWordTextBlock.Text }
            .Concat(definitions.Select(item => item.DisplayText)));
        DictionaryLoadingView.Visibility = Visibility.Collapsed;
        DictionaryMessageView.Visibility = Visibility.Collapsed;
        DictionaryContentView.Visibility = Visibility.Visible;
        CopyButton.IsEnabled = _dictionaryCopyText.Length > 0;
        DictionaryContentView.ChangeView(null, 0, null);
    }

    private void DictionaryContentView_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateDictionaryLayout();

    private void UpdateDictionaryLayout()
    {
        bool hasSecondaryContent = DictionaryExamplesSection.Visibility == Visibility.Visible
            || DictionaryPhrasesSection.Visibility == Visibility.Visible
            || DictionaryFormsSection.Visibility == Visibility.Visible
            || DictionaryEnglishSection.Visibility == Visibility.Visible;
        bool useTwoColumns = hasSecondaryContent && DictionaryContentView.ActualWidth >= 720;

        DictionarySecondaryColumn.Visibility = hasSecondaryContent ? Visibility.Visible : Visibility.Collapsed;
        DictionarySecondaryGridColumn.Width = useTwoColumns
            ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        DictionaryColumnsGrid.ColumnSpacing = useTwoColumns ? 20 : 0;
        DictionaryColumnsGrid.RowSpacing = hasSecondaryContent && !useTwoColumns ? 16 : 0;
        Grid.SetColumn(DictionarySecondaryColumn, useTwoColumns ? 1 : 0);
        Grid.SetRow(DictionarySecondaryColumn, useTwoColumns ? 0 : 1);
    }

    private static string FormatPhonetic(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "暂无音标" : $"/{text.Trim().Trim('/')}/";

    private void ShowTranslationView()
    {
        _audioPlayer.Pause();
        _dictionaryWord = null;
        _dictionaryEntry = null;
        _dictionaryCopyText = null;
        DictionaryView.Visibility = Visibility.Collapsed;
        TargetTextBox.Visibility = Visibility.Visible;
        ResultTitleTextBlock.Text = "译文";
        ResultSourceTextBlock.Text = "AI 翻译";
        CopyButton.Content = "复制";
        CopyButton.IsEnabled = true;
    }

    private async void RetryDictionaryButton_Click(object sender, RoutedEventArgs e)
    {
        string word = SourceTextBox.Text.Trim();
        if (!_isTranslating && DictionaryService.IsEnglishWord(word))
        {
            await LookupWordAsync(word);
        }
    }

    private async void UseAiTranslationButton_Click(object sender, RoutedEventArgs e) =>
        await StartActionAsync(forceAi: true);

    private void DictionaryUkAudioButton_Click(object sender, RoutedEventArgs e) =>
        PlayPronunciation("uk", "英式");

    private void DictionaryUsAudioButton_Click(object sender, RoutedEventArgs e) =>
        PlayPronunciation("us", "美式");

    private void PlayPronunciation(string accent, string label)
    {
        if (_dictionaryEntry is null || _dictionaryWord is null)
        {
            return;
        }

        DictionaryPronunciation? pronunciation = accent == "uk"
            ? _dictionaryEntry.Phonetics?.Uk : _dictionaryEntry.Phonetics?.Us;
        Uri? audioUri = DictionaryService.GetAudioUri(_dictionaryWord, accent, pronunciation);
        if (audioUri is null)
        {
            SetStatus("该词暂无发音", StatusKind.Warning);
            return;
        }

        _audioPlayer.Source = MediaSource.CreateFromUri(audioUri);
        _audioPlayer.Play();
        SetStatus($"正在播放{label}发音", StatusKind.Normal);
    }

    private void CancelTranslationButton_Click(object sender, RoutedEventArgs e)
    {
        _translationCancellation?.Cancel();
        _audioPlayer.Pause();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _translationCancellation?.Cancel();
        _operationVersion++;
        SetTranslationState(false, false);
        SourceTextBox.Text = string.Empty;
        TargetTextBox.Text = string.Empty;
        ShowTranslationView();
        SetStatus("已清空", StatusKind.Normal);
        SourceTextBox.Focus(FocusState.Programmatic);
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        string text = DictionaryView.Visibility == Visibility.Visible
            ? _dictionaryCopyText ?? string.Empty
            : TargetTextBox.Text;
        if (text.Length == 0)
        {
            SetStatus("暂无可复制的内容", StatusKind.Warning);
            return;
        }

        DataPackage dataPackage = new();
        dataPackage.SetText(text);
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
                await StartActionAsync();
            }
        });
    }

    private void SourceTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_dictionaryWord is not null
            && !string.Equals(SourceTextBox.Text.Trim(), _dictionaryWord, StringComparison.Ordinal))
        {
            if (_isTranslating)
            {
                _translationCancellation?.Cancel();
                _operationVersion++;
                SetTranslationState(false, false);
            }

            ShowTranslationView();
        }

        UpdateActionLabel();
    }

    private void TargetLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressLanguageSelection && TargetLanguageComboBox.SelectedItem is string language)
        {
            _settings.TargetLanguage = language;
            _targetLanguageManuallySet = true;
            UpdateActionLabel();
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
            UpdateActionLabel();
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

    private void SetTranslationState(bool isTranslating, bool isDictionary)
    {
        _isTranslating = isTranslating;
        TranslateButton.IsEnabled = !isTranslating;
        TranslateButtonText.Text = isTranslating
            ? isDictionary ? "查词中…" : "翻译中…"
            : ShouldUseDictionary(SourceTextBox.Text.Trim()) ? "查词  Ctrl+Enter" : "翻译  Ctrl+Enter";
        TranslateProgressRing.IsActive = isTranslating;
        TranslateProgressRing.Visibility = isTranslating ? Visibility.Visible : Visibility.Collapsed;
        CancelTranslationButton.Visibility = isTranslating ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateActionLabel()
    {
        if (!_isTranslating)
        {
            TranslateButtonText.Text = ShouldUseDictionary(SourceTextBox.Text.Trim())
                ? "查词  Ctrl+Enter" : "翻译  Ctrl+Enter";
        }
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
        _operationVersion++;
        _translationCancellation?.Cancel();
        _translationCancellation?.Dispose();
        _translationService.Dispose();
        _dictionaryService.Dispose();
        _audioPlayer.Dispose();
    }

    private enum StatusKind
    {
        Normal,
        Success,
        Warning,
        Error,
    }
}
