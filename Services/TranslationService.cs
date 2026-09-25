using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiTranslator.WinUI.Models;

namespace AiTranslator.WinUI.Services;

public sealed class TranslationService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    public async Task TranslateAsync(
        AppSettings settings,
        string sourceText,
        IProgress<string> chunks,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(
            settings,
            new ChatRequest(
                settings.Model,
                true,
                null,
                [new ChatMessage("user", $"{settings.BuildPrompt()}\n\n{sourceText}")]));

        using HttpResponseMessage response = await SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await ThrowForApiErrorAsync(response, cancellationToken);

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using StreamReader reader = new(stream);

        while (true)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            line = line.Trim();
            if (line.Length == 0 || !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string payload = line[5..].TrimStart();
            if (payload == "[DONE]")
            {
                return;
            }

            ChatChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<ChatChunk>(payload, JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            foreach (ChatChoice choice in chunk?.Choices ?? [])
            {
                if (!string.IsNullOrEmpty(choice.Delta?.Content))
                {
                    chunks.Report(choice.Delta.Content);
                }

                if (choice.FinishReason is not null)
                {
                    return;
                }
            }
        }
    }

    public async Task<string> TestConnectionAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(
            settings,
            new ChatRequest(
                settings.Model,
                false,
                5,
                [new ChatMessage("user", "Hi")]));

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        using HttpResponseMessage response = await SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token);
        await ThrowForApiErrorAsync(response, timeout.Token);
        return "连接成功！API 配置有效。";
    }

    public void Dispose() => _httpClient.Dispose();

    private HttpRequestMessage CreateRequest(AppSettings settings, ChatRequest body)
    {
        string baseUrl = settings.BaseUrl.Trim().TrimEnd('/');
        HttpRequestMessage request = new(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, completionOption, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranslationException("请求超时，请稍后重试。");
        }
        catch (HttpRequestException exception)
        {
            string message = exception.HttpRequestError is HttpRequestError.NameResolutionError
                or HttpRequestError.ConnectionError
                ? "无法连接到 API 服务器，请检查网络或 Base URL 设置。"
                : $"请求异常：{exception.Message}";
            throw new TranslationException(message, exception);
        }
    }

    private static async Task ThrowForApiErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        string detail = TryReadApiMessage(body) ?? body;
        throw new TranslationException($"API 返回错误 ({(int)response.StatusCode} {response.StatusCode})：{detail}");
    }

    private static string? TryReadApiMessage(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error", out JsonElement error)
                && error.TryGetProperty("message", out JsonElement message)
                    ? message.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("max_tokens")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? MaxTokens,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed class ChatChunk
    {
        [JsonPropertyName("choices")]
        public List<ChatChoice>? Choices { get; init; }
    }

    private sealed class ChatChoice
    {
        [JsonPropertyName("delta")]
        public ChatDelta? Delta { get; init; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; init; }
    }

    private sealed class ChatDelta
    {
        [JsonPropertyName("content")]
        public string? Content { get; init; }
    }
}

public sealed class TranslationException : Exception
{
    public TranslationException(string message)
        : base(message)
    {
    }

    public TranslationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
