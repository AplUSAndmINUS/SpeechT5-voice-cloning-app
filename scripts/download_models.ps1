# Force UTF-8 output when the script is launched from the MAUI app so Python-
# based CLIs like `hf` don't fail on Unicode characters such as check marks.
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding
$env:PYTHONUTF8 = "1"
$env:PYTHONIOENCODING = "utf-8"
$env:NO_COLOR = "1"
$env:HF_HUB_DISABLE_PROGRESS_BARS = "1"

# Resolve the repo root and use absolute paths so the script behaves the same
# when launched from the app or from a terminal.
$repoRoot = Split-Path -Parent $PSScriptRoot
$modelsDir = Join-Path $repoRoot "models"

# Locate the hf CLI — prefer the virtual-environment copy so we don't depend
# on the system PATH having huggingface_hub installed.
$venvHf = Join-Path $repoRoot ".venv\Scripts\hf.exe"
if (Test-Path $venvHf) {
    $hfCmd = $venvHf
    Write-Host "Using venv hf: $hfCmd"
} else {
    $hfCmd = "hf"
    Write-Host "Using system hf command."
}

Write-Host "Creating models directory..."
New-Item -ItemType Directory -Force -Path $modelsDir | Out-Null

Write-Host "Downloading SpeechT5 TTS..."
& $hfCmd download microsoft/speecht5_tts `
    --local-dir (Join-Path $modelsDir "speecht5_tts")
if ($LASTEXITCODE -ne 0) { Write-Error "Failed to download speecht5_tts (exit $LASTEXITCODE)."; exit 1 }

Write-Host "Downloading SpeechT5 HiFi-GAN vocoder..."
& $hfCmd download microsoft/speecht5_hifigan `
    --local-dir (Join-Path $modelsDir "speecht5_hifigan")
if ($LASTEXITCODE -ne 0) { Write-Error "Failed to download speecht5_hifigan (exit $LASTEXITCODE)."; exit 1 }

Write-Host "Downloading SpeechBrain x-vector speaker encoder..."
& $hfCmd download speechbrain/spkrec-xvect-voxceleb `
    --local-dir (Join-Path $modelsDir "spkrec-xvect-voxceleb")
if ($LASTEXITCODE -ne 0) { Write-Error "Failed to download spkrec-xvect-voxceleb (exit $LASTEXITCODE)."; exit 1 }

Write-Host "All models downloaded successfully."

# Run the script (from the project root)
# .\scripts\download_models.ps1

# Install huggingface_hub if not already installed (provides the `hf` CLI)
# pip install huggingface_hub