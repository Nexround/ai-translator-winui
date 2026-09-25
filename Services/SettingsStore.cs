using System.Text.Json;
using AiTranslator.WinUI.Models;

namespace AiTranslator.WinUI.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public string ConfigPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".translator",
        "config.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return new AppSettings();
            }

            AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(ConfigPath), JsonOptions);
            return Normalize(settings ?? new AppSettings());
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        string? directory = Path.GetDirectoryName(ConfigPath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Normalize(settings), JsonOptions));
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.BaseUrl = ValueOrDefault(settings.BaseUrl, AppSettings.DefaultBaseUrl);
        settings.Model = ValueOrDefault(settings.Model, AppSettings.DefaultModel);
        settings.SystemPrompt = ValueOrDefault(settings.SystemPrompt, AppSettings.DefaultSystemPrompt);
        settings.TargetLanguage = AppSettings.TargetLanguages.Contains(settings.TargetLanguage)
            ? settings.TargetLanguage
            : "中文";
        settings.ApiKey ??= string.Empty;
        return settings;
    }

    private static string ValueOrDefault(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
