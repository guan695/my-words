using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MyWords;

public sealed class AppController : IDisposable
{
    public WordStore Store { get; }
    public SettingsStore Settings { get; }
    private readonly AiClient ai = new();
    private readonly SemaphoreSlim aiQueue = new(1);
    private readonly HashSet<Guid> pending = new();
    private readonly CancellationTokenSource shutdown = new();
    public event Action<string, bool>? Notice;
    public event Action? BusyChanged;
    public AppController(WordStore store, SettingsStore settings) { Store = store; Settings = settings; }
    public bool IsBusy(Guid id) { lock (pending) return pending.Contains(id); }

    public AddResult Add(string text, WordCard? initial = null)
    {
        var result = Store.Add(text, initial);
        Notice?.Invoke(result.Message, false);
        if (result.IsNew && Settings.Value.AutoEnrich && !string.IsNullOrEmpty(Settings.Value.EncryptedApiKey)) _ = EnrichAsync(result.Word.Id);
        return result;
    }

    public async Task EnrichAsync(Guid id)
    {
        lock (pending) { if (!pending.Add(id)) return; }
        BusyChanged?.Invoke();
        var acquired = false;
        try
        {
            var original = Store.Snapshot().Words.FirstOrDefault(w => w.Id == id);
            if (original == null) return;
            var before = JsonSerializer.Serialize(original.Card, WordStore.Json);
            await aiQueue.WaitAsync(shutdown.Token);
            acquired = true;
            var result = await ai.GenerateAsync(original, Settings.Value, Settings.GetApiKey(), shutdown.Token);
            var saved = Store.ApplyGeneratedCard(id, before, result);
            Notice?.Invoke(saved ? $"{original.Text} 的词卡已整理好" : "词卡已有修改，保留了你的编辑内容。", false);
        }
        catch (OperationCanceledException) { if (!shutdown.IsCancellationRequested) Notice?.Invoke("AI 请求超时，单词已保存，可稍后重试。", true); }
        catch (HttpRequestException) { Notice?.Invoke("无法连接 DeepSeek，单词已保存，请检查网络后重试。", true); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Notice?.Invoke(ex.Message, true); }
        finally
        {
            if (acquired) aiQueue.Release();
            lock (pending) pending.Remove(id);
            BusyChanged?.Invoke();
        }
    }

    public void Dispose() { shutdown.Cancel(); ai.Dispose(); }
}
