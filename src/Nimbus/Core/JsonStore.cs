using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Nimbus.Roblox;

namespace Nimbus.Core;

// Source-generated serializers: no reflection at startup, which keeps launches fast.
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(WidgetSettings))]
[JsonSerializable(typeof(AppState))]
[JsonSerializable(typeof(ClientVersionInfo))]
[JsonSerializable(typeof(Dictionary<string, bool>))]
[JsonSerializable(typeof(List<HistoryEntry>))]
[JsonSerializable(typeof(Dictionary<string, Nimbus.Integrations.PlaytimeEntry>))]
[JsonSerializable(typeof(System.Text.Json.Nodes.JsonNode))]
internal partial class NimbusJson : JsonSerializerContext;

/// <summary>Loads and atomically saves one JSON file.</summary>
internal sealed class JsonStore<T>(string path, JsonTypeInfo<T> typeInfo) where T : class, new()
{
    private const string LogIdent = "JsonStore";

    public T Value { get; private set; } = new();

    public T Load()
    {
        try
        {
            if (File.Exists(path))
                Value = JsonSerializer.Deserialize(File.ReadAllText(path), typeInfo) ?? new T();
        }
        catch (Exception ex)
        {
            // A corrupted file should never brick the launcher: back it up and start fresh.
            Logger.Warn(LogIdent, $"Could not read {Path.GetFileName(path)}, starting with defaults: {ex.Message}");
            TryBackup();
            Value = new T();
        }

        return Value;
    }

    /// <summary>Back to defaults, saved straight away.</summary>
    public void Reset()
    {
        Value = new T();
        Save();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Value, typeInfo));
        File.Move(temp, path, overwrite: true);
    }

    private void TryBackup()
    {
        try { File.Copy(path, path + ".bak", overwrite: true); }
        catch { /* best effort */ }
    }
}
