# Test: Embed a local audio file and save the resulting embedding to a JSON file.
$embedResponse = Invoke-RestMethod `
  -Uri http://127.0.0.1:8000/embed `
  -Method Post `
  -Form @{ file = Get-Item "D:\AI\speech-backend\sample.wav" }

$embedResponse.embedding.Length
$embedResponse | ConvertTo-Json -Depth 4 | Set-Content .\embedding.json

# Test: Use the embedding from the previous test to generate a TTS audio file.
$body = @{
  text = "Hello world. This is a backend smoke test."
  embedding = $embedResponse.embedding
} | ConvertTo-Json -Depth 6

Invoke-WebRequest `
  -Uri http://127.0.0.1:8000/tts `
  -Method Post `
  -ContentType "application/json" `
  -Body $body `
  -OutFile .\out.wav