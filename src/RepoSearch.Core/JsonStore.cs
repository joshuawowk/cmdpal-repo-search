using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace RepoSearch.Core;

/// <summary>
/// Small atomic JSON file store for the on-disk caches. Every call takes a generated
/// <see cref="JsonTypeInfo{T}"/> so the extension keeps working when published trimmed.
/// </summary>
public static class JsonStore
{
    public static T? Load<T>(string path, JsonTypeInfo<T> typeInfo) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, typeInfo);
        }
        catch
        {
            // A corrupt or half-written cache must never break the extension; treat it as absent.
            return null;
        }
    }

    /// <summary>Writes via a temp file + replace, so a crash can't leave a truncated cache behind.</summary>
    public static void Save<T>(string path, T value, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var tmp = path + ".tmp";
            using (var stream = File.Create(tmp))
                JsonSerializer.Serialize(stream, value, typeInfo);

            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
        catch { /* cache writes are best-effort */ }
    }
}
