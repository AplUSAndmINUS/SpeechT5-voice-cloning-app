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

> **Note:** On first startup (if no local models are present) the models (~1–2 GB)
> are automatically downloaded from Hugging Face and cached locally. Subsequent
> starts use the local cache.
>
> To avoid any internet access at runtime, see
> [Offline / Local Models](#offline--local-models) below.

---

## Offline / Local Models

The service checks for a `models/` directory **in the same folder as `app.py`**
before reaching out to the Hugging Face Hub. If the corresponding sub-directory
exists the model is loaded entirely from disk — no internet required.

### Expected directory layout

```
SpeechT5-voice-cloning-app/
  app.py
  models/
    speecht5_tts/            ← SpeechT5 TTS processor + model weights
    speecht5_hifigan/        ← HiFi-GAN vocoder weights
    spkrec-xvect-voxceleb/   ← SpeechBrain speaker encoder
```

### Downloading the models manually

You need the `huggingface_hub` CLI (installed with `pip install -r requirements.txt`).
Run the commands below **once** while you still have an internet connection:

```bash
# Create the models directory
mkdir -p models

# SpeechT5 TTS (processor + model)
huggingface-cli download microsoft/speecht5_tts \
    --local-dir models/speecht5_tts \
    --local-dir-use-symlinks False

# SpeechT5 HiFi-GAN vocoder
huggingface-cli download microsoft/speecht5_hifigan \
    --local-dir models/speecht5_hifigan \
    --local-dir-use-symlinks False

# SpeechBrain x-vector speaker encoder
huggingface-cli download speechbrain/spkrec-xvect-voxceleb \
    --local-dir models/spkrec-xvect-voxceleb \
    --local-dir-use-symlinks False
```

> **Windows note:** Replace `mkdir -p` with `New-Item -ItemType Directory -Force models`
> (PowerShell) or `mkdir models` (cmd).

After downloading, `models/` will contain each model's `config.json`,
`pytorch_model.bin` / `model.safetensors`, tokenizer files, and any
SpeechBrain-specific assets. The service will detect and use them automatically
on the next startup, and a log line such as
`Loading model from local path: ...\models\speecht5_tts` will confirm it.

### Falling back to the Hub

If a sub-directory is **absent** the service silently falls back to downloading
from the Hugging Face Hub as usual. You can mix-and-match — for example, keep
the vocoder local but let the others download on demand.

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

| Model | HuggingFace ID | Local directory name |
|---|---|---|
| SpeechT5 TTS | `microsoft/speecht5_tts` | `models/speecht5_tts` |
| SpeechT5 HiFi-GAN Vocoder | `microsoft/speecht5_hifigan` | `models/speecht5_hifigan` |
| Speaker X-Vector Encoder | `speechbrain/spkrec-xvect-voxceleb` | `models/spkrec-xvect-voxceleb` |

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
