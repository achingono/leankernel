---
name: screenshot-ocr
description: "Read and analyze screenshots, PDFs, and images. Use vision models with fallback to PaddleOCR for text extraction when vision models are quota-limited or unavailable."
metadata:
  {
    "emoji": "📸",
    "requires": { "bins": ["paddleocr"] },
  }
---

# Screenshot & Image OCR Strategy

Handle screenshots, PDFs, and images with a robust fallback strategy when vision models are rate-limited or exhausted.

## Complete Fallback Strategy

### For General Tasks (Chat, Analysis, Reasoning)

The system falls back through this chain:

1. **azure/gpt-5.4-mini** — Primary (efficient)
2. **azure/gpt-5.4** — Higher quality Azure
3. **google/gemini-3.1-flash-lite-preview** — Gemini fast variant
4. **google/gemini-3-flash-preview** — Gemini default
5. **github-copilot/auto** — Auto-selected OpenAI
6. **ollama-remote/qwen3.6:35b** — Remote LAN (36B, reasoning)
7. **ollama-remote/gemma4:31b** — Remote LAN (31B, reasoning)
8. **ollama-remote/gemma4:26b** — Remote LAN (26B, lightweight)
9. **ollama/llama3.2:3b** — Local fallback (always available)

### For Vision Tasks (Screenshots, Images, PDFs)

When analyzing images, falls back in this order:

1. **google/gemini-2.0-flash** — Primary vision model
2. **google/gemini-2.5-pro** — High quality Gemini
3. **google/gemini-2.5-flash** — Fast Gemini
4. **github-copilot/gpt-5.4** — OpenAI GPT-4o vision
5. **github-copilot/auto** — Auto-selected OpenAI
6. **azure/gpt-5.4** — Azure OpenAI vision
7. **azure/gpt-5.4-mini** — Lighter Azure
8. **ollama-remote/llava:latest** — Remote LLaVA vision (192.168.1.39)
9. **ollama-remote/llava-phi3:latest** — Remote LLaVA Phi 3
10. **ollama-remote/bakllava:latest** — Remote BakLLaVA
11. **ollama/llama3.2:3b** — Local fallback

### OCR Fallback

If all vision models return 429 (quota exceeded) or 503 (unavailable), PaddleOCR extracts text locally and the text is sent to available text models for analysis.

## Text Model Fallback Chain

For general text analysis or when vision is exhausted, the remote ollama instance at 192.168.1.39:11434 provides:

- **ollama-remote/qwen3.6:35b** — 36B parameter model with strong reasoning
- **ollama-remote/gemma4:31b** — 31B parameter reasoning model
- **ollama-remote/gemma4:26b** — 26B parameter lighter variant
- **ollama/llama3.2:3b** — Local fallback (Docker)

These models are text-only but can analyze extracted text from PaddleOCR when vision models are unavailable.


## Using Vision Tools

When you need to analyze a screenshot, PDF, or image:

```bash
# Use the vision tool (automatic fallback on quota)
vision --image /path/to/screenshot.png --prompt "Describe what you see"

# Or use the pdf tool for multi-page documents
pdf --pdfs /path/to/document.pdf --prompt "Extract key information"
```

The tools will:
1. Try the primary vision model
2. Automatically fall back to the next model on quota/rate-limit errors
3. Continue down the chain until one succeeds or all fail

## When All Vision Models are Exhausted

If all cloud vision models return quota/rate-limit errors, the tool will:
1. Attempt to extract text using **PaddleOCR** (local, no quota)
2. Provide the extracted text to you for analysis

### PaddleOCR Text Extraction

PaddleOCR is a local OCR library with no API quotas or rate limits. Use it when:

- All vision models are quota-limited
- You need text extraction without cloud API dependency
- Fast offline processing is required
- Costs need to be minimized

**Important limitations of PaddleOCR**:
- Extracts **text only** — no image understanding/reasoning
- Less accurate than vision models (~85-95% vs 98%+ for Gemini)
- Cannot describe what's in an image, only read text
- Cannot extract tables or structured data
- May struggle with handwritten text or mixed scripts

### Manual PaddleOCR Extraction

If you need to manually extract text using PaddleOCR directly:

```bash
# Extract text from an image
python3 << 'PY'
from paddleocr import PaddleOCR

ocr = PaddleOCR(use_angle_cls=True, lang='en')
result = ocr.ocr('/path/to/screenshot.png', cls=True)

# Extract and print text
for line in result:
    for word_info in line:
        text = word_info[1][0]  # extracted text
        confidence = word_info[1][1]  # confidence score
        print(f"{text} (confidence: {confidence:.2f})")
PY
```

## Workflow: Screenshot Analysis When Vision Quota is Exhausted

1. **Text Extraction**: Use PaddleOCR to extract all text from the screenshot
   ```
   paddleocr → extracts text with confidence scores
   ```

2. **Text Processing**: Clean and organize the extracted text
   ```
   Remove low-confidence text, organize into sections
   ```

3. **Analysis**: Provide extracted text + extracted images to your available LLM
   ```
   Local LLM (llama3.2:3b) or next available cloud model
   ```

4. **Result**: Report findings with caveat that accuracy is lower than vision model analysis
   ```
   "Based on OCR extraction (no vision model available)..."
   ```

## Error Handling

### HTTP 429 (Quota Exceeded)

All providers hit their quota limit. Try:

1. Wait 60-300 seconds (rate limits reset periodically)
2. Retry the same request
3. If retry fails, fall back to PaddleOCR text extraction
4. Alert the user that vision model quota is exhausted

```
Vision models quota exhausted. Falling back to local OCR text extraction.
Accuracy will be lower than usual.
```

### HTTP 503 (Service Unavailable)

Model experiencing high demand. Automatic retry will use the next fallback.

### PaddleOCR Installation Missing

If PaddleOCR is not installed, you'll see an import error. This means the Docker container needs to be rebuilt:

```bash
# Rebuild the gateway image with PaddleOCR
docker compose build gateway
docker compose up -d gateway
```

## Tips for Better Results

### With Vision Models

- Provide clear prompts: "Extract the user's name from this form" (not "what do you see?")
- Include multiple angles/crops if available
- For PDFs, specify which pages to analyze
- Mention the document type for better context

### With PaddleOCR Extraction

- Verify extracted text carefully (errors are common)
- Ask the user to manually verify critical information
- Use high-confidence text only (skip confidence < 0.7)
- For tables, manually reconstruct from extracted text
- Be transparent: "This is from local OCR, may have errors"

### For Complex Documents

If a document is crucial and vision is exhausted:
1. Extract text with PaddleOCR
2. Ask user to review for accuracy
3. Provide extracted text to LLM for analysis
4. Report with lower confidence than usual

---

## Summary

✅ **Try vision models first** — they're smarter and more accurate  
⏸️ **Automatic fallback** — when one hits quota, the next is tried  
📝 **PaddleOCR fallback** — text extraction when all cloud models fail  
⚠️ **Manual verification** — always verify critical information  

When in doubt, ask the user or try again in a few minutes (rate limits reset).
