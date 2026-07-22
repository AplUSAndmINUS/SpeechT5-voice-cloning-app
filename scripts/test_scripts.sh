#!/usr/bin/env bash

set -euo pipefail

BACKEND_URL="${BACKEND_URL:-http://127.0.0.1:8000}"
SAMPLE_WAV="${1:-D:/AI/speech-backend/sample.wav}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
EMBEDDING_JSON="${SCRIPT_DIR}/embedding.json"
OUTPUT_WAV="${SCRIPT_DIR}/out.wav"

echo "Embedding sample: ${SAMPLE_WAV}"

curl --fail --silent --show-error \
	-X POST "${BACKEND_URL}/embed" \
	-F "file=@${SAMPLE_WAV};type=audio/wav" \
	-o "${EMBEDDING_JSON}"

python - "${EMBEDDING_JSON}" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as handle:
		data = json.load(handle)

embedding = data.get("embedding", [])
print(len(embedding))
PY

python - "${EMBEDDING_JSON}" <<'PY' | curl --fail --silent --show-error \
	-X POST "${BACKEND_URL}/tts" \
	-H "Content-Type: application/json" \
	--data-binary @- \
	-o "${OUTPUT_WAV}"
import json
import sys

with open(sys.argv[1], encoding="utf-8") as handle:
		data = json.load(handle)

payload = {
		"text": "Hello world. This is a backend smoke test.",
		"embedding": data["embedding"],
}

print(json.dumps(payload))
PY

echo "Saved embedding JSON to ${EMBEDDING_JSON}"
echo "Saved generated audio to ${OUTPUT_WAV}"
