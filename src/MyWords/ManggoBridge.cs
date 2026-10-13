using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MyWords;

public sealed class ManggoBridge : IDisposable
{
    public const string Endpoint = "http://127.0.0.1:17653/words";
    private readonly HttpListener listener = new();
    private readonly CancellationTokenSource shutdown = new();
    private readonly AppController controller;
    private readonly Dictionary<string, (string Body, string Response)> completed = new();
    private readonly Queue<string> requestOrder = new();
    public ManggoBridge(AppController controller, int port = 17653)
    {
        this.controller = controller;
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    }
    public void Start() { listener.Start(); _ = ListenAsync(); }

    private async Task ListenAsync()
    {
        while (!shutdown.IsCancellationRequested)
        {
            try
            {
                var context = await listener.GetContextAsync().WaitAsync(shutdown.Token);
                await HandleAsync(context);
            }
            catch (Exception ex) when (ex is HttpListenerException or OperationCanceledException or ObjectDisposedException) { break; }
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var request = context.Request;
            if (request.RemoteEndPoint == null || !IPAddress.IsLoopback(request.RemoteEndPoint.Address) || !string.IsNullOrEmpty(request.Headers["Origin"]))
            { await Reply(context, 403, new { message = "仅接受本机插件请求。" }); return; }
            var expected = Encoding.UTF8.GetBytes("Bearer " + controller.Settings.Value.BridgeToken);
            var supplied = Encoding.UTF8.GetBytes(request.Headers["Authorization"] ?? "");
            if (!CryptographicOperations.FixedTimeEquals(expected, supplied))
            { await Reply(context, 401, new { message = "连接口令不正确，请从拾词设置中重新复制。" }); return; }
            if (request.HttpMethod != "POST" || request.Url?.AbsolutePath != "/words")
            { await Reply(context, 404, new { message = "接口不存在。" }); return; }
            if (request.ContentLength64 > 32768)
            { await Reply(context, 413, new { message = "收藏内容过长。" }); return; }
            using var buffer = new MemoryStream();
            var bytes = new byte[4096];
            int size;
            while ((size = await request.InputStream.ReadAsync(bytes, deadline.Token)) > 0)
            {
                if (buffer.Length + size > 32768) { await Reply(context, 413, new { message = "收藏内容过长。" }); return; }
                buffer.Write(bytes, 0, size);
            }
            var body = Encoding.UTF8.GetString(buffer.ToArray());
            var requestId = request.Headers["X-Request-Id"] ?? "";
            if (!Guid.TryParse(requestId, out _)) { await Reply(context, 400, new { message = "请求缺少有效编号。" }); return; }
            if (completed.TryGetValue(requestId, out var prior))
            {
                if (prior.Body != body) { await Reply(context, 409, new { message = "请求编号冲突。" }); return; }
                await ReplyJson(context, 200, prior.Response); return;
            }
            var input = JsonSerializer.Deserialize<BridgeInput>(body, WordStore.Json) ?? throw new ArgumentException("收藏内容为空。");
            var result = controller.Add(input.Word ?? "", new WordCard { Meaning = input.Meaning ?? "", Phonetic = input.Phonetic ?? "", PartOfSpeech = input.PartOfSpeech ?? "" });
            var response = JsonSerializer.Serialize(new { success = true, message = result.Message, count = result.Word.AddCount, repeats = result.Word.RepeatCount }, WordStore.Json);
            completed.Add(requestId, (body, response));
            requestOrder.Enqueue(requestId);
            if (requestOrder.Count > 256) completed.Remove(requestOrder.Dequeue());
            await ReplyJson(context, 200, response);
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException)
        { await SafeReply(context, 400, "请收藏英文单词或短语，并检查词卡内容。"); }
        catch (OperationCanceledException) { await SafeReply(context, 408, "请求超时，请重试。"); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { await SafeReply(context, 500, "保存失败，请打开拾词检查词库目录是否可写。"); }
        finally { context.Response.Close(); }
    }
    private static Task Reply(HttpListenerContext context, int status, object body) => ReplyJson(context, status, JsonSerializer.Serialize(body, WordStore.Json));
    private static async Task ReplyJson(HttpListenerContext context, int status, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.KeepAlive = false;
        await context.Response.OutputStream.WriteAsync(bytes);
    }
    private static async Task SafeReply(HttpListenerContext c, int status, string message) { try { await Reply(c, status, new { success = false, message }); } catch (Exception ex) when (ex is IOException or HttpListenerException or ObjectDisposedException) { } }
    public void Dispose() { shutdown.Cancel(); listener.Close(); shutdown.Dispose(); }
    private sealed class BridgeInput
    {
        public string? Word { get; set; }
        public string? Meaning { get; set; }
        public string? Phonetic { get; set; }
        public string? PartOfSpeech { get; set; }
    }
}
