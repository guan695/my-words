using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace MyWords;

public sealed class WordCard
{
    public string Phonetic { get; set; } = "";
    public string PartOfSpeech { get; set; } = "";
    public string Meaning { get; set; } = "";
    public string Example { get; set; } = "";
    public string ExampleTranslation { get; set; } = "";
    public string Collocations { get; set; } = "";
    public string MemoryTip { get; set; } = "";
    public bool AiGenerated { get; set; }
}

public sealed class WordEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = "";
    public int AddCount { get; set; } = 1;
    public string Rating { get; set; } = "new";
    public WordCard Card { get; set; } = new();
    [JsonIgnore] public int RepeatCount => AddCount - 1;
    [JsonIgnore] public string RepeatLabel => RepeatCount == 0 ? "新收录" : $"重复 {RepeatCount} 次";
    [JsonIgnore] public string Brief => string.IsNullOrWhiteSpace(Card.Meaning) ? "等待补充释义" : Card.Meaning.Replace('\n', ' ');
    [JsonIgnore] public string RatingLabel => Rating switch { "known" => "记住了", "fuzzy" => "模糊", "forgotten" => "待加强", _ => "未学习" };
}

public sealed class VocabularyData
{
    public int Version { get; set; } = 1;
    public List<WordEntry> Words { get; set; } = new();
    public string StudyDay { get; set; } = "";
    public List<Guid> DailyIds { get; set; } = new();
    public List<Guid> CompletedIds { get; set; } = new();
}

public sealed record AddResult(WordEntry Word, bool IsNew)
{
    public string Message => IsNew ? $"已收录 {Word.Text}" : $"{Word.Text} 已收藏 {Word.AddCount} 次，重复 {Word.RepeatCount} 次";
}

public sealed class AppSettings
{
    public string EncryptedApiKey { get; set; } = "";
    public string Model { get; set; } = "deepseek-flash";
    public string Prompt { get; set; } = DefaultPrompt;
    public bool AutoEnrich { get; set; } = true;
    public string BridgeToken { get; set; } = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
    public const string DefaultPrompt = "面向英语基础一般的中文学习者。用简洁中文解释最常用的含义，给出自然、简短的英文例句和中文翻译，补充 1—2 个常用搭配及一句实用记忆提示。音标使用美式 IPA；不确定时留空，不编造词源。";
}
