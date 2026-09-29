using CommunityToolkit.Mvvm.ComponentModel;

namespace AiTranslator.WinUI.ViewModels;

/// <summary>
/// UI state shared by the translator page. Network and media work stays behind
/// the page's injected service modules; this type keeps view state observable
/// and makes the binding surface small and testable.
/// </summary>
public sealed class MainPageViewModel : ObservableObject
{
    private string _sourceText = string.Empty;
    private string _targetText = string.Empty;
    private string _statusText = "就绪";
    private string _actionLabel = "翻译  Ctrl+Enter";
    private string _targetLanguage = "中文";
    private bool _isBusy;

    public string SourceText { get => _sourceText; set => SetProperty(ref _sourceText, value); }
    public string TargetText { get => _targetText; set => SetProperty(ref _targetText, value); }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
    public string ActionLabel { get => _actionLabel; set => SetProperty(ref _actionLabel, value); }
    public string TargetLanguage { get => _targetLanguage; set => SetProperty(ref _targetLanguage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }
}
