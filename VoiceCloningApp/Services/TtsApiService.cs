using System.Net.Http.Json;

namespace VoiceCloningApp.Services;

/// <summary>
/// Wraps HTTP communication with the local Python SpeechT5 backend at
/// http://localhost:8000.
/// </summary>
public class TtsApiService
{
    private readonly HttpClient _httpClient;

    public TtsApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// POST a WAV file to /embed and return the 512-dim speaker embedding.
    /// Throws <see cref="HttpRequestException"/> on network / backend errors.
    /// Returns <c>null</c> when the response body cannot be parsed.
    /// </summary>
    public async Task<float[]?> GetEmbeddingAsync(Stream wavStream, string fileName)
    {
        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(wavStream);
        fileContent.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "file", fileName);

        using var response = await _httpClient.PostAsync("/embed", content);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<EmbedResponse>();
        return result?.Embedding;
    }

    /// <summary>
    /// POST text + embedding to /tts and return the raw WAV bytes.
    /// Throws <see cref="HttpRequestException"/> on network / backend errors.
    /// </summary>
    public async Task<byte[]> GenerateSpeechAsync(string text, float[] embedding)
    {
        var request = new TtsRequest { Text = text, Embedding = embedding };
        using var response = await _httpClient.PostAsJsonAsync("/tts", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    /// <summary>
    /// GET /health — returns true when the backend is up and models are loaded.
    /// </summary>
    public async Task<bool> CheckHealthAsync()
    {
        try
        {
            using var response = await _httpClient.GetAsync("/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // Private DTOs
    // -------------------------------------------------------------------------

    private sealed class EmbedResponse
    {
        public float[]? Embedding { get; set; }
    }

    private sealed class TtsRequest
    {
        public string Text { get; set; } = string.Empty;
        public float[] Embedding { get; set; } = Array.Empty<float>();
    }
}
