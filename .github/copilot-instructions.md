# Copilot Instructions — SpeechT5 Voice Cloning App

## Project Purpose

This is a **local voice-cloning application** combining:

- A **Python FastAPI backend** (`app.py`) that runs SpeechT5 models offline.
- A **.NET MAUI Blazor Hybrid frontend** (`VoiceCloningApp/`) that provides the
  full user experience including model management and backend lifecycle control.

The frontend handles **all** backend operations — users never need to open a
terminal. All inference runs locally with no cloud dependency.

---

## Project Structure

```
app.py            # FastAPI application — all endpoints and model loading
requirements.txt  # Python dependencies
README.md         # Setup and usage guide
scripts/
  download_models.ps1   # PowerShell script — downloads all three models (Windows)
  download_models.sh    # Bash script — downloads all three models (macOS / Linux)
VoiceCloningApp/
  Services/
    TtsApiService.cs           # HttpClient wrapper for /embed and /tts
    EmbeddingStorageService.cs # Persists speaker embedding to disk
    ModelSetupService.cs       # Detects models, runs download scripts
    BackendProcessService.cs   # Starts/stops uvicorn process, streams logs
  Components/Pages/
    Home.razor          # Landing page with backend status indicators
    Setup.razor         # Model download + backend start/stop UI
    VoiceSetup.razor    # Voice profile creation via /embed
    GenerateAudio.razor # Speech generation via /tts
```

---

## Frontend Services

### `ModelSetupService`
- Walks up from `AppContext.BaseDirectory` to find the folder containing `app.py`
  (exposed as `BackendRoot`).
- Checks each of the three `models/` sub-directories for presence and non-empty
  content.
- `DownloadModelsAsync()` — runs `scripts/download_models.ps1` (Windows) or
  `download_models.sh` (macOS/Linux) and yields log lines as an `IAsyncEnumerable<string>`.

### `BackendProcessService`
- Singleton that owns the uvicorn `Process` for the app's lifetime.
- `StartAsync(backendRoot)` — launches uvicorn, polls `/health` every 1.5 s for
  up to 90 s, then sets `Status = Running`.
- Prefers `.venv/Scripts/uvicorn.exe` (Windows) or `.venv/bin/uvicorn` (Unix)
  over the system `uvicorn`.
- Exposes `IReadOnlyList<string> LogLines` and `event Action StatusChanged` for
  live UI updates.
- `Stop()` kills the entire process tree and sets `Status = Stopped`.

---

## Model Download Scripts

Both scripts live in `scripts/` and are invoked by `ModelSetupService`.
They can also be run manually:

| Script | Platform | Run with |
|---|---|---|
| `scripts/download_models.ps1` | Windows (PowerShell) | `.\scripts\download_models.ps1` |
| `scripts/download_models.sh` | macOS / Linux (Bash) | `./scripts/download_models.sh` |

- Both scripts create `models/` if it does not exist.
- Both pass `--local-dir-use-symlinks False` to ensure real file copies.
- The `huggingface_hub` CLI must be on `PATH` (installed via `pip install -r requirements.txt`).
- Do **not** commit the downloaded model weights (`models/` is in `.gitignore`).

---

## Technology Stack

| Layer | Choice | Reason |
|---|---|---|
| Web framework | FastAPI | async, fast, OpenAPI docs auto-generated |
| Server | Uvicorn | ASGI, single command startup |
| TTS | `microsoft/speecht5_tts` | high-quality offline TTS |
| Vocoder | `microsoft/speecht5_hifigan` | HiFi-GAN waveform synthesis |
| Speaker encoder | `speechbrain/spkrec-xvect-voxceleb` | 512-dim x-vectors compatible with SpeechT5 |
| Audio I/O | torchaudio + soundfile | resampling, WAV read/write |
| Data validation | Pydantic v2 | request/response schemas |
| Desktop UI | .NET MAUI Blazor Hybrid | cross-target, Blazor components |

---

## Key Design Rules

### Python Backend

1. **Models are loaded once at startup** via the FastAPI `lifespan`
   async context manager (`@asynccontextmanager` passed to `FastAPI(lifespan=...)`).
   Do **not** use the deprecated `@app.on_event("startup")` decorator.
   Never reload models per-request.

2. **The `/embed` endpoint** accepts any WAV (16 kHz or 44.1 kHz, mono or
   stereo). It resamples to 16 kHz and converts to mono automatically before
   extracting the x-vector.

3. **The `/tts` endpoint** accepts a JSON body with `text` (string) and
   `embedding` (list of 512 floats). Long texts are split at sentence
   boundaries to stay within SpeechT5's token limit. Audio chunks are
   concatenated with a short silence gap.

4. **Error codes** follow this convention:
   - `400` — caller error (bad audio, empty text, wrong embedding length)
   - `500` — model or internal error
   - `503` — models not yet ready (service still starting)

5. **No external API calls** are made after the initial one-time model
   download. All inference runs locally.

### .NET Frontend

6. **`BackendProcessService` is a singleton** — register with
   `builder.Services.AddSingleton<BackendProcessService>()`. Never create
   it per-request or per-page.

7. **UI components subscribe to `StatusChanged`** and call
   `InvokeAsync(StateHasChanged)` from the event handler to update safely
   from background threads. Always unsubscribe in `IDisposable.Dispose()`.

8. **Error messages** must never tell the user to run terminal commands.
   Direct them to the **Setup** page (`/setup`) instead.

9. **`ModelSetupService.BackendRoot`** may be `null` if `app.py` cannot be
   found. All service methods guard against this. The Setup page shows a
   clear error when the root is missing.

---

## Adding New Endpoints

- Place new endpoints in `app.py`.
- Use Pydantic `BaseModel` for JSON request bodies.
- Use `UploadFile` for file uploads.
- Always guard against unloaded models with a `503` check at the top of
  the endpoint.

---

## Adding New Models

- Declare a module-level `_my_model` variable (initialised to `None`).
- Load the model inside the `lifespan` async context manager (before the `yield`).
- Call `.eval()` and move to `_device` after loading.
- Guard all inference calls with `torch.no_grad()`.

---

## Testing Guidance

Manual smoke test (backend only):

```bash
# Activate venv then start service
uvicorn app:app --port 8000

# Extract embedding from a sample WAV
curl -X POST http://localhost:8000/embed -F "file=@sample.wav" -o emb.json

# Generate speech
curl -X POST http://localhost:8000/tts \
     -H "Content-Type: application/json" \
     -d "{\"text\":\"Hello world\",\"embedding\":$(cat emb.json | python -c 'import sys,json; print(json.load(sys.stdin)[\"embedding\"])')}" \
     --output out.wav
```

For the full flow, use the desktop app: open **Setup**, download models,
start the backend, then proceed to **Voice Setup** and **Generate Audio**.

---

## Coding Conventions

- Python 3.10+ type hints throughout.
- Private module-level variables prefixed with `_`.
- All log messages use `logger.info / logger.exception` (never `print`).
- Do not commit model weights or large binary files.
- Do not add external API keys, tokens, or secrets.
