# SpeechT5 Voice Cloning App

A **local voice-cloning application** consisting of:

- **Python backend** (`app.py`) — FastAPI service using Microsoft's SpeechT5 models
- **Windows frontend** (`VoiceCloningApp/`) — .NET MAUI Blazor Hybrid desktop app

Both components run entirely offline — no cloud, no internet required.

> **The desktop app handles everything.** Model detection, one-click downloading,
> and starting/stopping the backend server are all managed from the **Setup**
> page inside the app. You do not need to run terminal commands manually.

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
- **Windows 10/11** (build 19041 or newer) — required for the desktop app
- A CUDA-capable GPU is recommended but not required (CPU works too)
- .NET SDK 10.0 + MAUI workload (frontend only)

> **Note:** The Python backend (`app.py`) can be run manually on macOS or Linux
> using `scripts/download_models.sh` and `uvicorn`. A dedicated macOS/Linux
> desktop frontend is not yet available.

---

## Quick Start

```powershell
# 1. Clone the repo
git clone https://github.com/AplUSAndmINUS/SpeechT5-voice-cloning-app.git
cd SpeechT5-voice-cloning-app

# 2. Create and activate a virtual environment (recommended)
python -m venv .venv
.venv\Scripts\activate

# 3. Install Python dependencies (includes huggingface_hub CLI)
pip install -r requirements.txt

# 4. Run the .NET MAUI desktop app (Windows only)
cd VoiceCloningApp
dotnet run -f net10.0-windows10.0.19041.0
```

Once the app is open, navigate to **Setup** and:
1. Click **Download Models** to fetch the three SpeechT5 models (~1–2 GB, one-time).
2. Click **Start Backend** to launch the uvicorn server.
3. Return to **Home** and follow steps 1 and 2 to clone your voice and generate audio.

---

## Frontend Setup Page

The **Setup** page (`/setup`) in the desktop app automates the tasks that
previously required a terminal:

| Feature | What it does |
|---|---|
| Backend location | Detects where `app.py` lives by walking up from the executable |
| Model status | Shows ✅ / ❌ for each of the three model directories |
| Download Models | Runs `scripts/download_models.ps1` via PowerShell with live log output |
| Start / Stop Backend | Launches `uvicorn app:app --port 8000` in the background; polls `/health` until ready |
| Server log | Streams stdout/stderr from uvicorn into the UI |

---

## .NET MAUI Blazor Hybrid Frontend

### Overview

`VoiceCloningApp/` is a Windows desktop application built with **.NET MAUI Blazor Hybrid**.
It provides a clean, minimal UI for:

1. **Setup** — detect and download the AI models, start/stop the local backend server.
2. **Voice Setup** — upload a WAV sample → POST to `/embed` → saves the speaker
   embedding locally (persists between sessions).
3. **Generate Audio** — paste or load a transcript → POST text + embedding to `/tts`
   → plays and downloads the generated WAV.

### Prerequisites

| Requirement | Version |
|---|---|
| .NET SDK | 10.0 or newer |
| .NET MAUI workload | installed via `dotnet workload install maui` |
| Windows | 10 version 1903 (build 19041) or newer |
| Python deps | installed via `pip install -r requirements.txt` (for the backend) |

### Installing the MAUI Workload

```powershell
dotnet workload install maui
```

### Building and Running

```powershell
cd VoiceCloningApp
dotnet run -f net10.0-windows10.0.19041.0
```

Or open `VoiceCloningApp/VoiceCloningApp.csproj` in **Visual Studio 2022** (17.8+)
and press **F5**.

The app will automatically find the backend root (the folder containing `app.py`).
Open the **Setup** page to download models and start the server — no separate
terminal session is required.

### Pages

| Page | URL | Description |
|---|---|---|
| Home | `/` | Overview, status at a glance, step-by-step navigation |
| Setup | `/setup` | Download models, start/stop backend, view live logs |
| Voice Setup | `/voice-setup` | Upload WAV → generate & save embedding |
| Generate Audio | `/generate-audio` | Enter transcript → generate & download WAV |

### Frontend Architecture

```
VoiceCloningApp/
  MauiProgram.cs              ← DI setup, HttpClient, services
  App.xaml / App.xaml.cs      ← MAUI application entry point
  MainPage.xaml               ← BlazorWebView host
  Components/
    _Imports.razor             ← shared using directives
    Routes.razor               ← Blazor router
    Layout/
      MainLayout.razor         ← sidebar + main content shell
      NavMenu.razor            ← navigation links
    Pages/
      Home.razor               ← landing page with status indicators
      Setup.razor              ← model download + backend start/stop
      VoiceSetup.razor         ← /embed integration
      GenerateAudio.razor      ← /tts integration + audio player
  Services/
    TtsApiService.cs           ← HttpClient wrapper for /embed and /tts
    EmbeddingStorageService.cs ← JSON file persistence for speaker embedding
    ModelSetupService.cs       ← model detection + download script runner
    BackendProcessService.cs   ← uvicorn process lifecycle management
  wwwroot/
    index.html                 ← BlazorWebView HTML host
    css/app.css                ← application styles
```

---

## Offline / Local Models

The service checks for a `models/` directory **in the same folder as `app.py`**
before reaching out to the Hugging Face Hub. If the corresponding sub-directory
exists the model is loaded entirely from disk — no internet required.

### Expected directory layout

```
SpeechT5-voice-cloning-app/
  app.py
  scripts/
    download_models.ps1      ← PowerShell download helper (used by the desktop app)
    download_models.sh       ← Bash download helper (manual backend use on macOS/Linux only)
  models/
    speecht5_tts/            ← SpeechT5 TTS processor + model weights
    speecht5_hifigan/        ← HiFi-GAN vocoder weights
    spkrec-xvect-voxceleb/   ← SpeechBrain speaker encoder
```

### Downloading the models — using the Setup page (recommended)

Open the desktop app, go to **Setup**, and click **Download Models**.
The app runs `scripts/download_models.ps1` automatically.

### Downloading the models — using the provided scripts (manual)

Two helper scripts in `scripts/` automate every step. They require the
`huggingface_hub` CLI, which is installed as part of `pip install -r requirements.txt`.

**Windows (PowerShell) — used by the desktop app:**

```powershell
.\scripts\download_models.ps1
```

**macOS / Linux (Bash) — for manual backend-only use:**

```bash
chmod +x scripts/download_models.sh
./scripts/download_models.sh
```

Both scripts:
1. Create the `models/` directory if it does not exist.
2. Download all three models from Hugging Face into their expected sub-directories.
3. Pass `--local-dir-use-symlinks False` so every file is a real copy (no dangling symlinks).
4. Print progress messages at each step and a final success confirmation.

### Downloading the models manually

You need the `huggingface_hub` CLI on your `PATH` (installed via `pip install -r requirements.txt`).
Then execute the following **once** while you still have an internet connection.

**Windows (PowerShell):**

```powershell
# Create the models directory
New-Item -ItemType Directory -Force models

# SpeechT5 TTS (processor + model)
huggingface-cli download microsoft/speecht5_tts `
    --local-dir models/speecht5_tts `
    --local-dir-use-symlinks False

# SpeechT5 HiFi-GAN vocoder
huggingface-cli download microsoft/speecht5_hifigan `
    --local-dir models/speecht5_hifigan `
    --local-dir-use-symlinks False

# SpeechBrain x-vector speaker encoder
huggingface-cli download speechbrain/spkrec-xvect-voxceleb `
    --local-dir models/spkrec-xvect-voxceleb `
    --local-dir-use-symlinks False
```

**macOS / Linux (Bash) — backend only, no desktop app:**

```bash
mkdir -p models
huggingface-cli download microsoft/speecht5_tts \
    --local-dir models/speecht5_tts \
    --local-dir-use-symlinks False
huggingface-cli download microsoft/speecht5_hifigan \
    --local-dir models/speecht5_hifigan \
    --local-dir-use-symlinks False
huggingface-cli download speechbrain/spkrec-xvect-voxceleb \
    --local-dir models/spkrec-xvect-voxceleb \
    --local-dir-use-symlinks False
```

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

The **recommended** way is via the **Setup** page in the desktop app, which
starts the backend with one click.

To start it manually from a terminal:

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
 ├── lifespan  → loads all models once into memory
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
| 500 | Internal model error |
| 503 | Models not yet loaded (service still starting up) |

---

## License

GPL-3.0 — see [LICENSE](LICENSE).
