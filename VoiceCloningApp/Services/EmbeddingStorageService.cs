using System.Text.Json;

namespace VoiceCloningApp.Services;

/// <summary>
/// Persists the speaker embedding to a JSON file in the app's local data
/// directory so users only need to run voice setup once.
/// </summary>
public class EmbeddingStorageService
{
    private static readonly string _storagePath = Path.Combine(
        FileSystem.AppDataDirectory, "speaker_embedding.json");

    /// <summary>Returns true when a saved embedding exists on disk.</summary>
    public bool HasEmbedding => File.Exists(_storagePath);

    /// <summary>
    /// Saves <paramref name="embedding"/> to disk, overwriting any previous value.
    /// </summary>
    public async Task SaveAsync(float[] embedding)
    {
        var json = JsonSerializer.Serialize(new EmbeddingFile { Embedding = embedding });
        await File.WriteAllTextAsync(_storagePath, json);
    }

    /// <summary>
    /// Loads the saved embedding. Returns <c>null</c> when no embedding exists.
    /// </summary>
    public async Task<float[]?> LoadAsync()
    {
        if (!HasEmbedding)
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(_storagePath);
            var file = JsonSerializer.Deserialize<EmbeddingFile>(json);
            return file?.Embedding;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Deletes the saved embedding file.</summary>
    public void Delete()
    {
        if (File.Exists(_storagePath))
            File.Delete(_storagePath);
    }

    private sealed class EmbeddingFile
    {
        public float[]? Embedding { get; set; }
    }
}
