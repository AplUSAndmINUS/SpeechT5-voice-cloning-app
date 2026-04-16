#!/usr/bin/env bash
set -e

# Resolve the repo root regardless of where the script is invoked from.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
MODELS_DIR="${ROOT_DIR}/models"

echo "Creating models directory at ${MODELS_DIR}..."
mkdir -p "${MODELS_DIR}"

echo "Downloading SpeechT5 TTS..."
huggingface-cli download microsoft/speecht5_tts \
    --local-dir "${MODELS_DIR}/speecht5_tts" \
    --local-dir-use-symlinks False

echo "Downloading SpeechT5 HiFi-GAN vocoder..."
huggingface-cli download microsoft/speecht5_hifigan \
    --local-dir "${MODELS_DIR}/speecht5_hifigan" \
    --local-dir-use-symlinks False

echo "Downloading SpeechBrain x-vector speaker encoder..."
huggingface-cli download speechbrain/spkrec-xvect-voxceleb \
    --local-dir "${MODELS_DIR}/spkrec-xvect-voxceleb" \
    --local-dir-use-symlinks False

echo "All models downloaded successfully."

# Make executable
# chmod +x download_models.sh

# Run the script (from the project root)
# ./scripts/download_models.sh

# Install huggingface_hub if not already installed
# pip install huggingface_hub