using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MyWords;

public sealed class WordStore
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    private readonly object gate = new();
    private readonly string file;
    private VocabularyData data;
    public string FilePath => file;
    public event Action? Changed;

    public WordStore(string file)
    {
        this.file = file;
        data = File.Exists(file) ? Read(File.ReadAllText(file, Encoding.UTF8)) : new();
    }

    public static string Normalize(string input)
    {
        var word = Regex.Replace(input.Normalize(NormalizationForm.FormKC).Trim(), @"\s+", " ").ToLowerInvariant().Replace('’', '\'');
        if (word.Length == 0 || word.Length > 100 || word.Split(' ').Length > 8 ||
            !Regex.IsMatch(word, @"^[a-z]+(?:[ '\-][a-z]+)*$", RegexOptions.CultureInvariant))
            throw new ArgumentException("请输入英文单词或短语（最多 8 个词），不要粘贴整段文字。");
        return word;
    }

    public VocabularyData Snapshot() { lock (gate) return Clone(data); }
    private static VocabularyData Clone(VocabularyData value) => JsonSerializer.Deserialize<VocabularyData>(JsonSerializer.Serialize(value, Json), Json)!;

    private void Change(Action<VocabularyData> action)
    {
        lock (gate)
        {
            var next = Clone(data);
            action(next);
            WriteAtomic(file, JsonSerializer.Serialize(next, Json));
            data = next;
        }
        Changed?.Invoke();
    }

    internal static void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            stream.Write(bytes);
            stream.Flush(true);
        }
        if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", true);
        else File.Move(temporary, path);
    }

    public AddResult Add(string text, WordCard? initialCard = null)
    {
        var key = Normalize(text);
        AddResult? result = null;
        Change(next =>
        {
            var existing = next.Words.FirstOrDefault(w => w.Text == key);
            if (existing != null)
            {
                existing.AddCount = checked(existing.AddCount + 1);
                result = new(existing, false);
                return;
            }
            var word = new WordEntry { Text = key, Card = initialCard ?? new() };
            ValidateCard(word.Card);
            next.Words.Add(word);
            result = new(word, true);
        });
        return result!;
    }

    public void UpdateCard(Guid id, WordCard card)
    {
        ValidateCard(card);
        Change(next =>
        {
            var word = next.Words.FirstOrDefault(w => w.Id == id) ?? throw new ArgumentException("这个单词已被删除。");
            word.Card = card;
        });
    }

    public void Delete(Guid id) => Change(next =>
    {
        next.Words.RemoveAll(w => w.Id == id);
        next.DailyIds.Remove(id);
        next.CompletedIds.Remove(id);
    });

    public bool ApplyGeneratedCard(Guid id, string expectedCardJson, WordCard card)
    {
        ValidateCard(card);
        var applied = false;
        Change(next =>
        {
            var word = next.Words.FirstOrDefault(w => w.Id == id);
            if (word == null || JsonSerializer.Serialize(word.Card, Json) != expectedCardJson) return;
            word.Card = card;
            applied = true;
        });
        return applied;
    }

    public VocabularyData EnsureDaily(DateOnly day)
    {
        lock (gate)
        {
            if (data.StudyDay == day.ToString("yyyy-MM-dd") || data.Words.Count == 0) return Clone(data);
            Change(next =>
            {
                next.StudyDay = day.ToString("yyyy-MM-dd");
                // A weighted draw still gives every word a chance; repeats never mean mastery.
                next.DailyIds = next.Words.OrderBy(w => -Math.Log(1 - Random.Shared.NextDouble()) /
                    ((w.Rating == "forgotten" ? 3 : w.Rating == "known" ? 0.7 : 1.5) + Math.Min(w.RepeatCount, 5) * 0.2))
                    .Take(20).Select(w => w.Id).ToList();
                next.CompletedIds.Clear();
            });
            return Clone(data);
        }
    }

    public void Rate(Guid id, string rating, DateOnly day)
    {
        if (rating is not ("known" or "fuzzy" or "forgotten")) throw new ArgumentException("无效的学习状态。");
        Change(next =>
        {
            if (next.StudyDay != day.ToString("yyyy-MM-dd")) throw new ArgumentException("新的一天到了，请重新打开今日学习。");
            if (!next.DailyIds.Contains(id)) throw new ArgumentException("这个单词不在今天的学习列表中。");
            next.Words.Single(w => w.Id == id).Rating = rating;
            if (!next.CompletedIds.Contains(id)) next.CompletedIds.Add(id);
        });
    }

    public void Export(string path) => WriteAtomic(path, JsonSerializer.Serialize(Snapshot(), Json));

    public int Import(string path)
    {
        var incoming = Read(File.ReadAllText(path, Encoding.UTF8));
        var count = 0;
        Change(next =>
        {
            foreach (var word in incoming.Words)
            {
                if (next.Words.Any(w => w.Text == word.Text)) continue;
                word.Id = Guid.NewGuid();
                next.Words.Add(word);
                count++;
            }
        });
        return count;
    }

    private static VocabularyData Read(string text)
    {
        if (text.Length > 30_000_000) throw new InvalidDataException("词库文件过大。");
        var loaded = JsonSerializer.Deserialize<VocabularyData>(text, Json) ?? throw new InvalidDataException("词库文件为空。");
        if (loaded.Version != 1 || loaded.Words == null || loaded.DailyIds == null || loaded.CompletedIds == null) throw new InvalidDataException("不支持的词库格式。");
        var keys = new HashSet<string>();
        var ids = new HashSet<Guid>();
        foreach (var word in loaded.Words)
        {
            if (word == null || string.IsNullOrWhiteSpace(word.Text)) throw new InvalidDataException("词库含有空词条。");
            word.Text = Normalize(word.Text);
            if (!keys.Add(word.Text) || !ids.Add(word.Id) || word.Id == Guid.Empty || word.AddCount < 1 || word.AddCount > 1_000_000 || word.Rating is not ("new" or "known" or "fuzzy" or "forgotten"))
                throw new InvalidDataException("词库含有无效或重复的词条。");
            ValidateCard(word.Card);
        }
        loaded.DailyIds = loaded.DailyIds.Where(ids.Contains).Distinct().Take(20).ToList();
        loaded.CompletedIds = loaded.CompletedIds.Where(loaded.DailyIds.Contains).Distinct().ToList();
        return loaded;
    }

    internal static void ValidateCard(WordCard? card)
    {
        if (card == null || new[] { card.Phonetic, card.PartOfSpeech, card.Meaning, card.Example, card.ExampleTranslation, card.Collocations, card.MemoryTip }.Any(s => s == null || s.Length > 4000))
            throw new ArgumentException("词卡字段必须是文本，每项不超过 4000 字。");
    }
}
