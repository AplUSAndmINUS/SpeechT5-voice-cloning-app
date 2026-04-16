Write-Host "Creating models directory..."
New-Item -ItemType Directory -Force -Path "models" | Out-Null

Write-Host "Downloading SpeechT5 TTS..."
huggingface-cli download microsoft/speecht5_tts `
    --local-dir models/speecht5_tts `
    --local-dir-use-symlinks False

Write-Host "Downloading SpeechT5 HiFi-GAN vocoder..."
huggingface-cli download microsoft/speecht5_hifigan `
    --local-dir models/speecht5_hifigan `
    --local-dir-use-symlinks False

Write-Host "Downloading SpeechBrain x-vector speaker encoder..."
huggingface-cli download speechbrain/spkrec-xvect-voxceleb `
    --local-dir models/spkrec-xvect-voxceleb `
    --local-dir-use-symlinks False

Write-Host "All models downloaded successfully."

# Run the script (from the project root)
# .\scripts\download_models.ps1

# Install huggingface_hub if not already installed
# pip install huggingface_hub