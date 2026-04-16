# SpeechT5 Voice Cloning App

A **local Python backend service** for cloning a speaker's voice and generating
podcast-quality speech using Microsoft's SpeechT5 models — no cloud, no internet,
fully offline.

---

## Features

| Endpoint | Description |
|---|---|
| `POST /embed` | Upload a WAV file → receive a 512-dim speaker embedding |
| `POST /tts` | Post text + embedding → download a cloned-voice WAV file |
| `GET /health` | Readiness check |

---

## Requirements

- Python 3.10 or newer
- Windows 10/11, macOS, or Linux
- A CUDA-capable GPU is recommended but not required (CPU works too)

---

## Installation

```bash
# 1. Clone the repo
git clone https://github.com/AplUSAndmINUS/SpeechT5-voice-cloning-app.git
cd SpeechT5-voice-cloning-app

# 2. Create and activate a virtual environment (recommended)
python -m venv .venv

# Windows
.venv\Scripts\activate

# macOS / Linux
source .venv/bin/activate

# 3. Install dependencies
pip install -r requirements.txt
```

> **Note:** On first startup the models (~1–2 GB) are automatically downloaded
> from Hugging Face and cached locally. Subsequent starts use the local cache.

---

## Running the Service

```bash
uvicorn app:app --port 8000
```

The service is then available at <http://localhost:8000>.

Interactive API docs (Swagger UI): <http://localhost:8000/docs>

---

## Usage Examples

### 1 — Generate a speaker embedding

```bash
curl -X POST http://localhost:8000/embed \
     -F "file=@my_voice_sample.wav" \
     -o embedding.json
```

Returns:

```json
{ "embedding": [ -0.021, 0.043, ... ] }
```

Save the JSON — you can reuse the same embedding for every TTS request.

---

### 2 — Generate speech from text

```bash
curl -X POST http://localhost:8000/tts \
     -H "Content-Type: application/json" \
     -d '{
           "text": "Hello, this is my cloned podcast voice.",
           "embedding": [ -0.021, 0.043, ... ]
         }' \
     --output podcast.wav
```

The file `podcast.wav` is a 16 kHz mono WAV you can open directly in
Adobe Audition, Audacity, or any audio editor.

---

### 3 — .NET MAUI / Blazor Hybrid integration

Use `HttpClient` to call `http://localhost:8000/embed` and
`http://localhost:8000/tts` from your Blazor component.

```csharp
// Example: POST text + embedding and save the WAV
using var response = await httpClient.PostAsJsonAsync(
    "http://localhost:8000/tts",
    new { text = "Hello world", embedding = embeddingArray });

response.EnsureSuccessStatusCode();
var wavBytes = await response.Content.ReadAsByteArrayAsync();
await File.WriteAllBytesAsync("output.wav", wavBytes);
```

---

## Architecture

```
app.py
 ├── @startup  → loads all models once into memory
 ├── POST /embed
 │    ├── torchaudio.load()  → decode WAV
 │    ├── Resample to 16 kHz (if needed)
 │    ├── Convert to mono (if needed)
 │    └── speechbrain EncoderClassifier → 512-dim x-vector
 ├── POST /tts
 │    ├── Split long text into sentence chunks
 │    ├── SpeechT5Processor (tokenise each chunk)
 │    ├── SpeechT5ForTextToSpeech.generate_speech()
 │    ├── SpeechT5HifiGan vocoder
 │    └── Concatenate chunks → stream WAV
 └── GET /health → readiness probe
```

### Models Used

| Model | HuggingFace ID |
|---|---|
| SpeechT5 TTS | `microsoft/speecht5_tts` |
| SpeechT5 HiFi-GAN Vocoder | `microsoft/speecht5_hifigan` |
| Speaker X-Vector Encoder | `speechbrain/spkrec-xvect-voxceleb` |

---

## Error Handling

| HTTP Status | Meaning |
|---|---|
| 400 | Invalid input (bad audio, missing text/embedding, wrong dims) |
| 500 | Internal model error (see response body for safe message) |
| 503 | Models not yet loaded (service still starting up) |

---

## License

GPL-3.0 — see [LICENSE](LICENSE).
