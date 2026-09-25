using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiTranslator.WinUI.Models;

namespace AiTranslator.WinUI.Services;

public sealed class DictionaryService : IDisposable
{
    private static readonly Uri ApiOrigin = new("https://uapis.cn");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex EnglishWordPattern = new(
        @"\A[A-Za-z]+(?:[-'][A-Za-z]+)*\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly Dictionary<string, DictionaryEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    public DictionaryService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _ownsHttpClient = httpClient is null;
    }

    public static bool IsEnglishWord(string text) =>
        text.Length is > 0 and <= 64 && EnglishWordPattern.IsMatch(text);

    public async Task<DictionaryEntry?> LookupAsync(string word, CancellationToken cancellationToken)
    {
        if (!IsEnglishWord(word))
        {
            throw new ArgumentException("只能查询长度不超过 64 个字符的英文单词。", nameof(word));
        }

        if (_cache.TryGetValue(word, out DictionaryEntry? cached))
        {
            return cached;
        }

        Uri uri = new(ApiOrigin, $"/api/v1/dictionary/lookup?word={Uri.EscapeDataString(word)}");
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(uri, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DictionaryLookupException("词典请求超时，请重试。");
        }
        catch (HttpRequestException exception)
        {
            throw new DictionaryLookupException("无法连接 UAPI 词典，请检查网络后重试。", exception);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new DictionaryLookupException("UAPI 免费额度或请求频率已达上限，请稍后重试，或使用 AI 翻译。");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new DictionaryLookupException($"UAPI 词典暂不可用（HTTP {(int)response.StatusCode}），请稍后重试。");
            }

            DictionaryLookupResponse? result;
            try
            {
                result = await response.Content.ReadFromJsonAsync<DictionaryLookupResponse>(JsonOptions, cancellationToken);
            }
            catch (JsonException exception)
            {
                throw new DictionaryLookupException("词典返回了无法读取的数据。", exception);
            }

            if (result is null)
            {
                throw new DictionaryLookupException("词典返回了空数据，请重试。");
            }

            if (!result.Found)
            {
                return null;
            }

            DictionaryEntry entry = result.Entry
                ?? throw new DictionaryLookupException("词典结果缺少词条内容，请重试。");

            if (_cache.Count >= 128)
            {
                _cache.Clear();
            }

            _cache[word] = entry;
            return entry;
        }
    }

    public static Uri? GetAudioUri(string word, string accent, DictionaryPronunciation? pronunciation)
    {
        if (!IsEnglishWord(word) || accent is not ("uk" or "us") || string.IsNullOrWhiteSpace(pronunciation?.Audio))
        {
            return null;
        }

        // Build the documented UAPI URL ourselves instead of following a URL from the response.
        return new Uri(ApiOrigin, $"/api/v1/dictionary/audio?word={Uri.EscapeDataString(word)}&accent={accent}");
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}

public sealed class DictionaryLookupException : Exception
{
    public DictionaryLookupException(string message) : base(message) { }
    public DictionaryLookupException(string message, Exception innerException) : base(message, innerException) { }
}
