using System.Text.Json.Serialization;

namespace AiTranslator.WinUI.Models;

public sealed class AppSettings
{
    public const string DefaultBaseUrl = "https://api.openai.com/v1";
    public const string DefaultModel = "gpt-4o-mini";
    public const string DefaultSystemPrompt =
        "你是一个专业翻译助手。请将以下文本翻译为{target_lang}，自动识别源语言，只输出翻译结果，不要添加任何解释。";

    public static IReadOnlyList<string> TargetLanguages { get; } =
    [
        "中文", "英文", "日文", "韩文", "法文", "德文", "西班牙文",
        "俄文", "葡萄牙文", "意大利文", "阿拉伯文", "泰文", "越南文",
    ];

    [JsonPropertyName("api_key")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonPropertyName("base_url")]
    public string BaseUrl { get; set; } = DefaultBaseUrl;

    [JsonPropertyName("model")]
    public string Model { get; set; } = DefaultModel;

    [JsonPropertyName("system_prompt")]
    public string SystemPrompt { get; set; } = DefaultSystemPrompt;

    [JsonPropertyName("target_lang")]
    public string TargetLanguage { get; set; } = "中文";

    public string BuildPrompt() => SystemPrompt.Replace("{target_lang}", TargetLanguage, StringComparison.Ordinal);
}
