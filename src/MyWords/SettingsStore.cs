using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MyWords;

public sealed class SettingsStore
{
    private readonly string file;
    public AppSettings Value { get; private set; }
    public SettingsStore(string directory)
    {
        file = Path.Combine(directory, "settings.json");
        Value = File.Exists(file) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file), WordStore.Json) ?? throw new InvalidDataException("设置文件为空。") : new();
        if (string.IsNullOrWhiteSpace(Value.BridgeToken) || string.IsNullOrWhiteSpace(Value.Model) || Value.Prompt == null) throw new InvalidDataException("设置文件格式不正确。");
        if (!File.Exists(file)) Save(Value);
    }
    public void Save(AppSettings value)
    {
        if (string.IsNullOrWhiteSpace(value.Model) || value.Model.Length > 100 || value.Prompt.Length > 8000) throw new ArgumentException("请检查模型名称和提示词长度。");
        WordStore.WriteAtomic(file, JsonSerializer.Serialize(value, WordStore.Json));
        Value = value;
    }
    public string GetApiKey() => string.IsNullOrEmpty(Value.EncryptedApiKey) ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(Value.EncryptedApiKey), null, DataProtectionScope.CurrentUser));
    public static string Protect(string key) => string.IsNullOrWhiteSpace(key) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), null, DataProtectionScope.CurrentUser));
}
