using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MyWords;

internal static class SelfTests
{
    public static async Task<int> RunAsync(string reportPath)
    {
        var report = new List<string>();
        var directory = Path.Combine(Path.GetDirectoryName(reportPath)!, "self-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var checks = 0;
        void Check(bool condition, string label) { if (!condition) throw new Exception("FAILED: " + label); checks++; }
        void Reject(Action action, string label) { try { action(); } catch { checks++; return; } throw new Exception("FAILED: " + label); }
        try
        {
            var file = Path.Combine(directory, "words.json");
            var store = new WordStore(file);
            var first = store.Add("  Approach  ", new WordCard { Meaning = "方法；接近" });
            var duplicate = store.Add("APPROACH", new WordCard { Meaning = "不应覆盖" });
            Check(first.IsNew && !duplicate.IsNew && duplicate.Word.AddCount == 2 && duplicate.Word.RepeatCount == 1, "deduplication and exact repeat count");
            Check(duplicate.Word.Card.Meaning == "方法；接近", "repeat keeps the original card");
            Check(WordStore.Normalize(" Pick   UP ") == "pick up", "phrase normalization");
            Check(WordStore.Normalize("don’t") == "don't", "curly apostrophe normalization");
            Reject(() => store.Add("<script>"), "non-word input is rejected");
            Reject(() => store.Add(new string('a', 101)), "overlong words are rejected");
            await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => store.Add("approach"))));
            Check(new WordStore(file).Snapshot().Words.Single().AddCount == 22, "concurrent writes persist without losing counts");
            Check(!File.ReadAllText(file).Contains("createdAt") && !File.ReadAllText(file).Contains("updatedAt"), "no collection timestamps are stored");
            report.Add("PASS: normalization, deduplication, persistence and concurrent counting");

            foreach (var word in "adapt bright calm daily eager focus gentle habit idea journey kind learn mindful notice open patient quiet read simple thrive useful vivid wonder yield zest".Split(' ')) store.Add(word);
            var day = new DateOnly(2026, 10, 13);
            var daily = store.EnsureDaily(day);
            Check(daily.DailyIds.Count == 20 && daily.DailyIds.Distinct().Count() == 20, "daily list has 20 unique existing words");
            Check(daily.DailyIds.SequenceEqual(new WordStore(file).EnsureDaily(day).DailyIds), "daily list survives restart");
            store.Add("another");
            Check(daily.DailyIds.SequenceEqual(store.EnsureDaily(day).DailyIds), "adding words does not reshuffle today's list");
            store.Rate(daily.DailyIds[0], "known", day);
            store.Rate(daily.DailyIds[0], "known", day);
            Check(store.Snapshot().CompletedIds.Count == 1, "review completion is idempotent");
            Reject(() => store.Rate(daily.DailyIds[0], "forgotten", day.AddDays(1)), "old review session cannot write on a new day");
            Check(store.EnsureDaily(day.AddDays(1)).CompletedIds.Count == 0, "new day resets progress");
            var small = new WordStore(Path.Combine(directory, "small.json"));
            small.Add("one"); small.Add("two");
            Check(small.EnsureDaily(day).DailyIds.Count == 2, "small library is not padded with repeats");
            report.Add("PASS: daily selection, restart stability, small library and day rollover");

            var target = store.Snapshot().Words[0];
            var original = JsonSerializer.Serialize(target.Card, WordStore.Json);
            store.UpdateCard(target.Id, new WordCard { Meaning = "用户的新编辑" });
            Check(!store.ApplyGeneratedCard(target.Id, original, new WordCard { Meaning = "过时 AI 结果" }), "late AI output cannot overwrite manual edits");
            var payload = JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = "stop", message = new { content = JsonSerializer.Serialize(new WordCard { Phonetic = "/wrong/", Meaning = "方法", Example = "Try a new approach." }, WordStore.Json) } } } });
            var parsed = AiClient.Parse(payload, "/əˈproʊtʃ/");
            Check(parsed.AiGenerated && parsed.Phonetic == "/əˈproʊtʃ/", "AI keeps dictionary pronunciation");
            Reject(() => AiClient.Parse("{\"choices\":[{\"message\":{\"content\":\"\"}}]}", ""), "empty AI output is rejected");
            Reject(() => AiClient.Parse("{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"{}\"}}]}", ""), "truncated AI output is rejected");
            Reject(() => store.UpdateCard(target.Id, new WordCard { Meaning = null! }), "null card fields are rejected");
            report.Add("PASS: AI output validation and protection for manual edits");

            var backup = Path.Combine(directory, "export.json");
            store.Export(backup);
            var imported = new WordStore(Path.Combine(directory, "imported.json"));
            var importedCount = imported.Import(backup);
            Check(importedCount == store.Snapshot().Words.Count && imported.Import(backup) == 0, "import merges without duplicate records");
            Check(imported.Snapshot().Words.Single(w => w.Text == "approach").AddCount == 22, "backup preserves counts");
            var corruptFile = Path.Combine(directory, "corrupt.json");
            File.WriteAllText(corruptFile, "{broken");
            Reject(() => new WordStore(corruptFile), "corrupt library is not silently reset");
            Check(File.ReadAllText(corruptFile) == "{broken", "corrupt original remains intact");
            var settings = new SettingsStore(directory);
            settings.Value.AutoEnrich = false;
            settings.Value.EncryptedApiKey = SettingsStore.Protect("self-test-placeholder");
            settings.Save(settings.Value);
            Check(new SettingsStore(directory).GetApiKey() == "self-test-placeholder", "Windows credential encryption round trip");
            settings.Value.EncryptedApiKey = "";
            settings.Save(settings.Value);
            report.Add("PASS: export/import, corrupt-file preservation and credential encryption");

            // Use a non-startup test key; never enable a real login entry during checks.
            var startupKeyPath = @"Software\MyWords\SelfTests-" + Guid.NewGuid().ToString("N");
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(startupKeyPath) ?? throw new IOException("Cannot create isolated registry test key.");
                key.SetValue("OtherApp", "leave unchanged");
                Check(!AutoStart.IsEnabled(key), "autostart is disabled when no entry exists");
                var executable = Path.Combine(directory, "app with spaces", "MyWords.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
                File.WriteAllBytes(executable, Array.Empty<byte>());
                AutoStart.SetEnabled(key, true, executable);
                Check(AutoStart.IsEnabled(key) && (string?)key.GetValue(AutoStart.ValueName) == $"\"{executable}\" --background", "autostart quotes paths and starts in the tray");
                Check(key.GetValueKind(AutoStart.ValueName) == RegistryValueKind.String, "autostart stores a literal command path");
                AutoStart.SetEnabled(key, false, executable);
                AutoStart.SetEnabled(key, false, executable);
                Check(!AutoStart.IsEnabled(key) && (string?)key.GetValue("OtherApp") == "leave unchanged", "disabling autostart is idempotent and preserves other entries");
                Reject(() => AutoStart.SetEnabled(key, true, Path.Combine(directory, "missing.exe")), "missing startup executable is rejected");
                Check(!AutoStart.IsEnabled(key), "failed autostart changes leave the entry disabled");
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(startupKeyPath, false); }
            report.Add("PASS: isolated registry startup entry, quoting, background launch and disabling");

            using var controller = new AppController(store, settings);
            var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start();
            var port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
            using var bridge = new ManggoBridge(controller, port); bridge.Start();
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            async Task<(HttpStatusCode Status, string Body)> Send(string requestId, string? token, string body, bool origin = false)
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/words");
                if (token != null) req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
                if (origin) req.Headers.TryAddWithoutValidation("Origin", "https://example.com");
                req.Headers.Add("X-Request-Id", requestId);
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using var res = await http.SendAsync(req);
                return (res.StatusCode, await res.Content.ReadAsStringAsync());
            }
            var operation = Guid.NewGuid().ToString();
            const string addBody = "{\"word\":\"bridge\",\"meaning\":\"桥梁\"}";
            Check((await Send(operation, null, addBody)).Status == HttpStatusCode.Unauthorized, "bridge requires pairing token");
            var saved = await Send(operation, settings.Value.BridgeToken, addBody);
            Check(saved.Status == HttpStatusCode.OK && JsonDocument.Parse(saved.Body).RootElement.GetProperty("count").GetInt32() == 1, "bridge saves words");
            await Send(operation, settings.Value.BridgeToken, addBody);
            Check(store.Snapshot().Words.Single(w => w.Text == "bridge").AddCount == 1, "network retry does not inflate repeat count");
            await Send(Guid.NewGuid().ToString(), settings.Value.BridgeToken, addBody);
            Check(store.Snapshot().Words.Single(w => w.Text == "bridge").AddCount == 2, "intentional repeat increments count");
            Check((await Send(Guid.NewGuid().ToString(), settings.Value.BridgeToken, addBody, true)).Status == HttpStatusCode.Forbidden, "browser-origin requests are rejected");
            Check((await Send(Guid.NewGuid().ToString(), settings.Value.BridgeToken, "not json")).Status == HttpStatusCode.BadRequest, "bad bridge payload rejected");
            Check((await Send(Guid.NewGuid().ToString(), settings.Value.BridgeToken, new string('x', 33000))).Status == HttpStatusCode.RequestEntityTooLarge, "oversized bridge payload rejected");
            report.Add("PASS: real loopback HTTP, authentication, retry safety and payload validation");
            report.Add($"ALL PASSED: {checks} targeted checks. No external AI request was sent.");
            File.WriteAllLines(reportPath, report, Encoding.UTF8);
            return 0;
        }
        catch (Exception ex)
        {
            report.Add(ex.ToString());
            File.WriteAllLines(reportPath, report, Encoding.UTF8);
            return 1;
        }
    }
}
