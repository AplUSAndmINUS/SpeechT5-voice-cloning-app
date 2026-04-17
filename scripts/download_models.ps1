# Locate the hf CLI — prefer the virtual-environment copy so we don't depend
# on the system PATH having huggingface_hub installed.
$venvHf = Join-Path $PSScriptRoot ".." ".venv" "Scripts" "hf.exe"
if (Test-Path $venvHf) {
    $hfCmd = $venvHf
    Write-Host "Using venv hf: $hfCmd"
} else {
    $hfCmd = "hf"
    Write-Host "Using system hf command."
}

Write-Host "Creating models directory..."
New-Item -ItemType Directory -Force -Path "models" | Out-Null

Write-Host "Downloading SpeechT5 TTS..."
& $hfCmd download microsoft/speecht5_tts `
    --local-dir models/speecht5_tts `
    --local-dir-use-symlinks False
if ($LASTEXITCODE -ne 0) { Write-Error "Failed to download speecht5_tts (exit $LASTEXITCODE)."; exit 1 }

Write-Host "Downloading SpeechT5 HiFi-GAN vocoder..."
& $hfCmd download microsoft/speecht5_hifigan `
    --local-dir models/speecht5_hifigan `
    --local-dir-use-symlinks False
if ($LASTEXITCODE -ne 0) { Write-Error "Failed to download speecht5_hifigan (exit $LASTEXITCODE)."; exit 1 }

Write-Host "Downloading SpeechBrain x-vector speaker encoder..."
& $hfCmd download speechbrain/spkrec-xvect-voxceleb `
    --local-dir models/spkrec-xvect-voxceleb `
    --local-dir-use-symlinks False
if ($LASTEXITCODE -ne 0) { Write-Error "Failed to download spkrec-xvect-voxceleb (exit $LASTEXITCODE)."; exit 1 }

Write-Host "All models downloaded successfully."

# Run the script (from the project root)
# .\scripts\download_models.ps1

# Install huggingface_hub if not already installed (provides the `hf` CLI)
# pip install huggingface_hub