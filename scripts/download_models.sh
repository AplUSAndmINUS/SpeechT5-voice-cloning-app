#!/usr/bin/env bash
set -e

echo "Creating models directory..."
mkdir -p models

echo "Downloading SpeechT5 TTS..."
huggingface-cli download microsoft/speecht5_tts \
    --local-dir models/speecht5_tts \
    --local-dir-use-symlinks False

echo "Downloading SpeechT5 HiFi-GAN vocoder..."
huggingface-cli download microsoft/speecht5_hifigan \
    --local-dir models/speecht5_hifigan \
    --local-dir-use-symlinks False

echo "Downloading SpeechBrain x-vector speaker encoder..."
huggingface-cli download speechbrain/spkrec-xvect-voxceleb \
    --local-dir models/spkrec-xvect-voxceleb \
    --local-dir-use-symlinks False

echo "All models downloaded successfully."

# Make executable
# chmod +x download_models.sh

# Run the script
# ./download_models.sh

# Install huggingface_hub if not already installed
# pip install huggingface_hub