using System.Text.Json.Serialization;

namespace AiTranslator.WinUI.Models;

public sealed class DictionaryLookupResponse
{
    public bool Found { get; init; }
    public DictionaryEntry? Entry { get; init; }
}

public sealed class DictionaryEntry
{
    public string Word { get; init; } = string.Empty;
    public DictionaryPhonetics? Phonetics { get; init; }

    [JsonPropertyName("exam_tags")]
    public List<string>? ExamTags { get; init; }

    public List<DictionaryDefinition>? Definitions { get; init; }

    [JsonPropertyName("english_definitions")]
    public List<DictionaryEnglishDefinition>? EnglishDefinitions { get; init; }

    [JsonPropertyName("word_forms")]
    public List<DictionaryWordForm>? WordForms { get; init; }

    public List<DictionaryPhrase>? Phrases { get; init; }
    public List<DictionaryExample>? Examples { get; init; }
}

public sealed class DictionaryPhonetics
{
    public DictionaryPronunciation? Uk { get; init; }
    public DictionaryPronunciation? Us { get; init; }
}

public sealed class DictionaryPronunciation
{
    public string? Text { get; init; }
    public string? Audio { get; init; }
}

public sealed class DictionaryDefinition
{
    [JsonPropertyName("part_of_speech")]
    public string? PartOfSpeech { get; init; }

    public string? Meaning { get; init; }

    [JsonIgnore]
    public string DisplayText => string.IsNullOrWhiteSpace(PartOfSpeech)
        || (Meaning?.TrimStart().StartsWith(PartOfSpeech, StringComparison.OrdinalIgnoreCase) ?? false)
            ? Meaning ?? string.Empty
            : $"{PartOfSpeech} {Meaning}";
}

public sealed class DictionaryEnglishDefinition
{
    [JsonPropertyName("part_of_speech")]
    public string? PartOfSpeech { get; init; }

    public string? Definition { get; init; }

    [JsonIgnore]
    public string DisplayText => string.IsNullOrWhiteSpace(PartOfSpeech)
        || (Definition?.TrimStart().StartsWith(PartOfSpeech, StringComparison.OrdinalIgnoreCase) ?? false)
            ? Definition ?? string.Empty
            : $"{PartOfSpeech} {Definition}";
}

public sealed class DictionaryWordForm
{
    public string? Name { get; init; }
    public string? Value { get; init; }
}

public sealed class DictionaryPhrase
{
    public string? Phrase { get; init; }
    public string? Meaning { get; init; }
}

public sealed class DictionaryExample
{
    public string? Source { get; init; }
    public string? Translation { get; init; }
}
