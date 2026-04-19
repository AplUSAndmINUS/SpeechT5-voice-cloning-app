"""
SpeechT5 Voice Cloning Backend Service
---------------------------------------
FastAPI backend exposing:
  POST /embed  — WAV file → speaker embedding (512-dim x-vector)
  POST /tts    — text + embedding → WAV audio stream

Run with:
    uvicorn app:app --port 8000
"""

import io
import logging
import re
import sys
from contextlib import asynccontextmanager
from pathlib import Path

import numpy as np
import soundfile as sf
import torch
import torchaudio
from fastapi import FastAPI, File, HTTPException, UploadFile
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import StreamingResponse
from pydantic import BaseModel
from speechbrain.utils.fetching import LocalStrategy
from transformers import (
    SpeechT5ForTextToSpeech,
    SpeechT5HifiGan,
    SpeechT5Processor,
)
from typing import List


def _patch_speechbrain_lazy_imports_for_windows() -> None:
    """Avoid SpeechBrain lazy-import failures triggered by Windows paths."""
    if sys.platform != "win32":
        return

    import inspect

    from speechbrain.utils.importutils import LazyModule

    if getattr(LazyModule.ensure_module, "__name__", "") == "_ensure_module_windows_aware":
        return

    original_ensure_module = LazyModule.ensure_module

    def _ensure_module_windows_aware(self, stacklevel: int):
        try:
            importer_frame = inspect.getframeinfo(sys._getframe(stacklevel + 1))
        except AttributeError:
            importer_frame = None

        if importer_frame is not None and importer_frame.filename.replace("\\", "/").endswith("/inspect.py"):
            raise AttributeError()

        return original_ensure_module(self, stacklevel)

    LazyModule.ensure_module = _ensure_module_windows_aware


_patch_speechbrain_lazy_imports_for_windows()

from speechbrain.inference.classifiers import EncoderClassifier

# ---------------------------------------------------------------------------
# Logging
# ---------------------------------------------------------------------------

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
)
logger = logging.getLogger(__name__)

# ---------------------------------------------------------------------------
# Model locations — prefer ./models/<name> when present, else HuggingFace Hub
# ---------------------------------------------------------------------------

_MODELS_DIR = Path(__file__).parent / "models"
_SCRIPTS_MODELS_DIR = Path(__file__).parent / "scripts" / "models"

_TTS_HUB_ID = "microsoft/speecht5_tts"
_VOCODER_HUB_ID = "microsoft/speecht5_hifigan"
_ENCODER_HUB_ID = "speechbrain/spkrec-xvect-voxceleb"

# Expected sub-directory names inside ./models/
_TTS_LOCAL_NAME = "speecht5_tts"
_VOCODER_LOCAL_NAME = "speecht5_hifigan"
_ENCODER_LOCAL_NAME = "spkrec-xvect-voxceleb"


def _resolve_model_source(hub_id: str, local_name: str) -> str:
    """
    Return the local path for *local_name*, checking two locations:
    1. ``./models/<local_name>`` — the canonical download destination.
    2. ``./scripts/models/<local_name>`` — fallback for manually placed models.
    If neither exists, return *hub_id* so Transformers / SpeechBrain will
    download from the HuggingFace Hub.
    """
    for candidate in (_MODELS_DIR / local_name, _SCRIPTS_MODELS_DIR / local_name):
        if candidate.is_dir() and any(candidate.iterdir()):
            logger.info("Loading model from local path: %s", candidate)
            return str(candidate)
    logger.info("Local path not found for '%s'; will fetch from Hub.", hub_id)
    return hub_id


def _is_local_model_path(source: str) -> bool:
    return Path(source).is_dir()


# ---------------------------------------------------------------------------
# Global model state (loaded once at startup)
# ---------------------------------------------------------------------------

_tts_processor: SpeechT5Processor | None = None
_tts_model: SpeechT5ForTextToSpeech | None = None
_vocoder: SpeechT5HifiGan | None = None
_speaker_encoder: EncoderClassifier | None = None
_device: torch.device | None = None

# SpeechT5 TTS max token limit; chunk long texts to stay under it.
_MAX_TOKENS = 600

# ---------------------------------------------------------------------------
# Lifespan: load all models into memory once at startup
# ---------------------------------------------------------------------------


@asynccontextmanager
async def lifespan(application: FastAPI):  # noqa: ARG001
    """Load ML models at startup and release at shutdown."""
    global _tts_processor, _tts_model, _vocoder, _speaker_encoder, _device

    _device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    logger.info("Using device: %s", _device)

    tts_source = _resolve_model_source(_TTS_HUB_ID, _TTS_LOCAL_NAME)
    vocoder_source = _resolve_model_source(_VOCODER_HUB_ID, _VOCODER_LOCAL_NAME)
    encoder_source = _resolve_model_source(_ENCODER_HUB_ID, _ENCODER_LOCAL_NAME)

    logger.info("Loading SpeechT5 TTS processor and model …")
    _tts_processor = SpeechT5Processor.from_pretrained(tts_source)
    _tts_model = SpeechT5ForTextToSpeech.from_pretrained(tts_source).to(_device)
    _tts_model.eval()

    logger.info("Loading SpeechT5 HiFi-GAN vocoder …")
    _vocoder = SpeechT5HifiGan.from_pretrained(vocoder_source).to(_device)
    _vocoder.eval()

    logger.info("Loading speaker encoder (x-vector) …")
    _speaker_encoder = EncoderClassifier.from_hparams(
        source=encoder_source,
        savedir=encoder_source if _is_local_model_path(encoder_source) else None,
        overrides={
            "pretrained_path": encoder_source,
        } if _is_local_model_path(encoder_source) else {},
        local_strategy=LocalStrategy.NO_LINK if _is_local_model_path(encoder_source) else LocalStrategy.SYMLINK,
        run_opts={"device": str(_device)},
    )

    logger.info("All models loaded and ready.")
    yield
    # Nothing to explicitly release — Python GC handles tensors.


# ---------------------------------------------------------------------------
# FastAPI app  (created *after* lifespan so it can reference it)
# ---------------------------------------------------------------------------

app = FastAPI(
    title="SpeechT5 Voice Cloning API",
    description=(
        "Local backend for generating speaker embeddings and cloning voice "
        "using Microsoft SpeechT5 models."
    ),
    version="1.0.0",
    lifespan=lifespan,
)

# Allow calls from the .NET MAUI Blazor Hybrid frontend (localhost only)
app.add_middleware(
    CORSMiddleware,
    allow_origins=[
        "http://localhost",
        "http://127.0.0.1",
        "http://localhost:5000",
        "http://localhost:5001",
        "http://localhost:7000",
        "http://localhost:8000",
    ],
    allow_methods=["POST", "GET"],
    allow_headers=["*"],
)


# ---------------------------------------------------------------------------
# Request / response schemas
# ---------------------------------------------------------------------------


class TTSRequest(BaseModel):
    text: str
    embedding: List[float]


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

_TARGET_SAMPLE_RATE = 16_000


def _load_and_normalise_audio(raw_bytes: bytes) -> torch.Tensor:
    """
    Load raw WAV bytes, resample from any sample rate to 16 kHz, and convert to mono.

    Returns a 2-D tensor of shape (1, num_samples).
    Raises HTTPException(400) on bad audio.
    """
    try:
        audio, sample_rate = sf.read(
            io.BytesIO(raw_bytes), dtype="float32", always_2d=True
        )
    except Exception as exc:
        raise HTTPException(
            status_code=400, detail=f"Failed to decode audio: {exc}"
        ) from exc

    waveform = torch.from_numpy(audio.T)

    # Resample if needed
    if sample_rate != _TARGET_SAMPLE_RATE:
        resampler = torchaudio.transforms.Resample(
            orig_freq=sample_rate, new_freq=_TARGET_SAMPLE_RATE
        )
        waveform = resampler(waveform)

    # Convert stereo → mono
    if waveform.shape[0] > 1:
        waveform = waveform.mean(dim=0, keepdim=True)

    return waveform  # shape: (1, T)


def _split_text(text: str) -> List[str]:
    """
    Split *text* into sentence-level chunks that fit within SpeechT5's
    token budget (_MAX_TOKENS).  Returns a list of non-empty strings.
    """
    # Naive sentence splitter — split at ". ", "! ", "? " and keep delimiter.
    sentences = re.split(r"(?<=[.!?])\s+", text.strip())
    chunks: List[str] = []
    current = ""

    for sentence in sentences:
        candidate = (current + " " + sentence).strip() if current else sentence
        ids = _tts_processor(text=candidate, return_tensors="pt")["input_ids"]
        if ids.shape[-1] > _MAX_TOKENS and current:
            # Flush current chunk and start fresh
            chunks.append(current)
            current = sentence
        else:
            current = candidate

    if current:
        chunks.append(current)

    return chunks or [text]


# ---------------------------------------------------------------------------
# Endpoints
# ---------------------------------------------------------------------------


@app.post("/embed", summary="Generate speaker embedding from a WAV file")
async def embed(file: UploadFile = File(...)):
    """
    Upload a WAV file (any sample rate, mono or stereo) and receive a
    512-dimensional x-vector speaker embedding as a JSON array of floats.
    The audio is automatically resampled to 16 kHz and converted to mono.

    The embedding can be stored by the caller and reused for any number of
    subsequent `/tts` requests.
    """
    if _speaker_encoder is None:
        raise HTTPException(status_code=503, detail="Models not yet loaded.")

    if not (file.filename or "").lower().endswith(".wav"):
        raise HTTPException(
            status_code=400,
            detail="Only WAV audio files are accepted (.wav extension required).",
        )

    raw = await file.read()
    if not raw:
        raise HTTPException(status_code=400, detail="Uploaded file is empty.")

    waveform = _load_and_normalise_audio(raw)  # (1, T)

    try:
        with torch.no_grad():
            embedding = _speaker_encoder.encode_batch(waveform)  # (1, 1, 512)
            embedding = torch.nn.functional.normalize(embedding, dim=2)
            embedding = embedding.squeeze()  # (512,)
    except Exception as exc:
        logger.exception("Speaker embedding failed")
        raise HTTPException(
            status_code=500, detail="Embedding extraction failed."
        ) from exc

    return {"embedding": embedding.cpu().tolist()}


@app.post("/tts", summary="Generate speech WAV from text and speaker embedding")
async def tts(request: TTSRequest):
    """
    POST JSON body:
    ```json
    {
      "text": "Your podcast script goes here …",
      "embedding": [ … 512 floats from /embed … ]
    }
    ```

    Returns a WAV audio file (16 kHz, mono) as a binary stream.
    Long texts are automatically split into sentence chunks and
    concatenated into a single output file.
    """
    if _tts_model is None or _vocoder is None or _tts_processor is None:
        raise HTTPException(status_code=503, detail="Models not yet loaded.")

    if not request.text.strip():
        raise HTTPException(status_code=400, detail="'text' must not be empty.")

    if not request.embedding:
        raise HTTPException(
            status_code=400, detail="'embedding' must not be empty."
        )

    if len(request.embedding) != 512:
        raise HTTPException(
            status_code=400,
            detail=f"Expected a 512-dim embedding, got {len(request.embedding)}.",
        )

    try:
        speaker_embedding = (
            torch.tensor(request.embedding, dtype=torch.float32)
            .unsqueeze(0)
            .to(_device)
        )  # (1, 512)
    except Exception as exc:
        raise HTTPException(
            status_code=400, detail=f"Invalid embedding values: {exc}"
        ) from exc

    chunks = _split_text(request.text)
    audio_parts: List[np.ndarray] = []

    try:
        with torch.no_grad():
            for chunk in chunks:
                inputs = _tts_processor(text=chunk, return_tensors="pt")
                input_ids = inputs["input_ids"].to(_device)
                speech = _tts_model.generate_speech(
                    input_ids, speaker_embedding, vocoder=_vocoder
                )
                audio_parts.append(speech.cpu().numpy())
    except Exception as exc:
        logger.exception("TTS generation failed")
        raise HTTPException(
            status_code=500, detail="TTS generation failed."
        ) from exc

    # Concatenate chunks (add a short silence between them)
    silence = np.zeros(int(_TARGET_SAMPLE_RATE * 0.25), dtype=np.float32)
    combined = audio_parts[0]
    for part in audio_parts[1:]:
        combined = np.concatenate([combined, silence, part])

    wav_buf = io.BytesIO()
    sf.write(wav_buf, combined, samplerate=_TARGET_SAMPLE_RATE, format="WAV")
    wav_buf.seek(0)

    return StreamingResponse(
        wav_buf,
        media_type="audio/wav",
        headers={"Content-Disposition": 'attachment; filename="speech.wav"'},
    )


# ---------------------------------------------------------------------------
# Health check
# ---------------------------------------------------------------------------


@app.get("/health", summary="Health / readiness check")
async def health():
    """Returns 200 when the service and all models are ready."""
    ready = all(
        m is not None
        for m in (_tts_processor, _tts_model, _vocoder, _speaker_encoder)
    )
    if not ready:
        raise HTTPException(status_code=503, detail="Models not yet loaded.")
    return {"status": "ok"}
