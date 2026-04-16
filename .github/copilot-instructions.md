# Copilot Instructions — SpeechT5 Voice Cloning App

## Project Purpose

This is a **local Python FastAPI backend** for cloning a speaker's voice and
generating podcast-quality speech using Microsoft's SpeechT5 family of models.
It is designed to run entirely offline on Windows 10/11 and be called from a
.NET MAUI Blazor Hybrid frontend over HTTP.

---

## Project Structure

```
app.py            # FastAPI application — all endpoints and model loading
requirements.txt  # Python dependencies
README.md         # Setup and usage guide
```

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

---

## Key Design Rules

1. **Models are loaded once at startup** via `@app.on_event("startup")`.
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
- Load the model in the `load_models()` startup handler.
- Call `.eval()` and move to `_device` after loading.
- Guard all inference calls with `torch.no_grad()`.

---

## Testing Guidance

Manual smoke test:

```bash
# Start service
uvicorn app:app --port 8000

# Extract embedding from a sample WAV
curl -X POST http://localhost:8000/embed -F "file=@sample.wav" -o emb.json

# Generate speech
curl -X POST http://localhost:8000/tts \
     -H "Content-Type: application/json" \
     -d "{\"text\":\"Hello world\",\"embedding\":$(cat emb.json | python -c 'import sys,json; print(json.load(sys.stdin)[\"embedding\"])')}" \
     --output out.wav
```

---

## Coding Conventions

- Python 3.10+ type hints throughout.
- Private module-level variables prefixed with `_`.
- All log messages use `logger.info / logger.exception` (never `print`).
- Do not commit model weights or large binary files.
- Do not add external API keys, tokens, or secrets.
