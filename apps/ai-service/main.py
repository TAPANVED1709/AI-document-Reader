"""
FastAPI Medical Document Intelligence Service (Version 2).

Provides PDF text extraction and local OCR fallback for scanned documents.
All processing is local — no data leaves the machine.
"""
import logging
import os
from typing import List

from fastapi import FastAPI, File, UploadFile, HTTPException, status
from pydantic import BaseModel

from core.extractor import PdfExtractor
from core.ocr_engine import LocalOcrEngine, is_tesseract_available, get_tesseract_version
from parsers.base import LabResultItem
from parsers.lab_parser import LabRowParser
from validation import ValidationEngine, load_config
from explanations import ExplanationRequest, ExplanationResponse, provider_from_config, PROMPT_VERSION
from trends import TrendRequest, build_trend, summarize_trend
from document_types import classify_document
from parsers.document_parsers import parse_discharge, parse_prescription, parse_radiology

logger = logging.getLogger(__name__)

app = FastAPI(
    title="AI Document Reader - Document Intelligence Service",
    version="2.0.0",
    description=(
        "Extracts laboratory and pathology test data from medical reports. "
        "Supports text-based PDFs (Stage 1) and scanned/image PDFs via local "
        "Tesseract OCR (Stage 2). No data is transmitted to external services."
    ),
)

# Instantiate the active parser (shared between text and OCR paths)
parser = LabRowParser()
validator = ValidationEngine(load_config())

# Log Tesseract availability at startup
if is_tesseract_available():
    logger.info("Tesseract OCR available (version %s). Scanned PDFs will be processed automatically.", get_tesseract_version())
else:
    logger.warning(
        "Tesseract OCR is NOT available. Scanned PDFs will return requiresOcr=true. "
        "Install tesseract-ocr and restart the service to enable OCR."
    )


# ---------------------------------------------------------------------------
# Response models
# ---------------------------------------------------------------------------

class HealthResponse(BaseModel):
    status: str
    service: str
    version: str
    ocrAvailable: bool
    tesseractVersion: str


class AnalysisResponse(BaseModel):
    requiresOcr: bool  # Compatibility alias of ocrRequired
    ocrRequired: bool
    processingMode: str
    pageSources: list[dict]
    ocrApplied: bool
    pages: List[int]
    results: List[LabResultItem]
    validationSummary: dict
    documentType: str = "UNKNOWN"
    documentTypeConfidence: float = 0.0
    documentTypeSignals: list[str] = []
    structuredData: dict = {}
    comments: list[str] = []


# ---------------------------------------------------------------------------
# Endpoints
# ---------------------------------------------------------------------------

@app.get("/health", response_model=HealthResponse)
async def health_check():
    """Health status endpoint. Reports Tesseract availability."""
    return HealthResponse(
        status="Healthy",
        service="ai-service",
        version="2.0.0",
        ocrAvailable=is_tesseract_available(),
        tesseractVersion=get_tesseract_version(),
    )

@app.get("/health/ready")
async def readiness_check():
    storage = os.getenv("STORAGE_ROOT", "storage/reports")
    storage_ready = os.path.isdir(storage) or os.access(os.path.dirname(storage) or ".", os.W_OK)
    return {"status": "Healthy" if storage_ready else "Degraded", "service": "ai-service", "tesseract": "Healthy" if is_tesseract_available() else "Unavailable", "storage": "Writable" if storage_ready else "Unavailable", "ollama": (await local_ai_health())["available"]}


@app.get("/health/local-ai")
async def local_ai_health():
    provider = os.getenv("LOCAL_LLM_PROVIDER", "ollama")
    model = os.getenv("LOCAL_LLM_MODEL", "")
    return {"enabled": True, "provider": provider, "model": model or "deterministic-fallback", "available": bool(model) if provider.lower() == "ollama" else True, "promptVersion": PROMPT_VERSION}

@app.post("/explain", response_model=ExplanationResponse)
async def explain_structured(request: ExplanationRequest):
    fallback = provider_from_config()
    if getattr(fallback, "provider", "") == "ollama":
        try: return await fallback.generate(request)
        except Exception: pass
    return await __import__("explanations.provider", fromlist=["DeterministicFallbackProvider"]).DeterministicFallbackProvider().generate(request)

@app.post("/trends/compare")
async def compare_trend(request: TrendRequest):
    trend = build_trend(request)
    trend["summary"] = summarize_trend(trend)
    return trend

@app.post("/analyse", response_model=AnalysisResponse)
async def analyse_document(file: UploadFile = File(...)):
    """
    Accepts a PDF document and runs full document intelligence analysis.

    Flow:
    1. Extract text per page via PyMuPDF.
    2. If text is sufficient  →  parse lab rows and return results.
    3. If text is insufficient (scanned PDF):
       a. If Tesseract is available  →  run local OCR, then parse lab rows.
       b. If Tesseract is unavailable  →  return requiresOcr=true (Stage 1 fallback).
    """
    if not file.filename:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Filename is missing.",
        )

    if not file.filename.lower().endswith(".pdf"):
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=f"Unsupported file format '{file.filename}'. Only PDF documents are supported.",
        )

    try:
        content = await file.read()
        if len(content) == 0:
            raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail="Uploaded PDF file is empty (0 bytes).")
        max_pages = int(os.getenv("MAX_PDF_PAGES", "50"))
        try:
            import fitz
            document = fitz.open(stream=content, filetype="pdf")
            page_count = len(document)
            document.close()
            if page_count > max_pages:
                raise HTTPException(status_code=413, detail="PAGE_LIMIT_EXCEEDED")
        except HTTPException:
            raise
        except Exception:
            raise HTTPException(status_code=400, detail="PDF_INVALID")
    except HTTPException:
        raise
    except Exception as e:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=f"Failed to read uploaded file: {str(e)}",
        )

    if len(content) == 0:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Uploaded PDF file is empty (0 bytes).",
        )

    # -----------------------------------------------------------------------
    # Stage 1: Text extraction
    # -----------------------------------------------------------------------
    try:
        pages, requires_ocr = PdfExtractor.extract_from_bytes(content)
    except Exception as e:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            detail=f"Corrupted or unreadable PDF: {str(e)}",
        )

    if requires_ocr and is_tesseract_available():
        needed = {p.page_number for p in pages if p.ocr_required}
        replacements = {p.page_number: p for p in LocalOcrEngine.ocr_from_bytes(content, needed)}
        pages = [replacements.get(p.page_number, p) for p in pages]
    else:
        for page in pages:
            if page.ocr_required:
                page.source = "OCR_UNAVAILABLE"

    parsed_pages = [p for p in pages if p.source in ("NATIVE_TEXT", "OCR")]
    classification = classify_document("\n".join(p.text for p in parsed_pages))
    structured_data = {}
    if classification.document_type == "DISCHARGE_SUMMARY":
        structured_data = parse_discharge("\n".join(p.text for p in parsed_pages))
    elif classification.document_type == "PRESCRIPTION":
        structured_data = parse_prescription("\n".join(p.text for p in parsed_pages))
    elif classification.document_type == "RADIOLOGY_REPORT":
        structured_data = parse_radiology("\n".join(p.text for p in parsed_pages))
    parsed_results = parser.parse(parsed_pages) if classification.document_type == "LAB_REPORT" else []
    page_sources = {p.page_number: p.source for p in pages}
    for result in parsed_results: result.sourceType = page_sources.get(result.page)
    validator.validate(parsed_results)
    validated_results = parsed_results
    applied = any(p.source == "OCR" for p in pages)
    # Mode describes the required extraction strategy even when OCR is unavailable.
    native = any(not p.ocr_required for p in pages)
    mode = "HYBRID" if requires_ocr and native else "OCR" if requires_ocr else "NATIVE"
    comments = extract_comments("\n".join(p.text for p in parsed_pages))
    return AnalysisResponse(
        requiresOcr=requires_ocr, ocrRequired=requires_ocr, ocrApplied=applied,
        processingMode=mode, pages=[p.page_number for p in pages],
        pageSources=[{"page": p.page_number, "source": p.source, "error": p.error} for p in pages],
        results=validated_results, validationSummary={"totalResults": len(validated_results), "autoAccepted": sum(not r.reviewRequired for r in validated_results), "reviewRequired": sum(r.reviewRequired for r in validated_results), "verified": 0, "corrected": 0},
        documentType=classification.document_type, documentTypeConfidence=classification.confidence,
        documentTypeSignals=classification.signals, structuredData=structured_data, comments=comments,
    )


def extract_comments(text: str) -> list[str]:
    """Keep common laboratory notes separate from result-row parsing."""
    import re
    patterns = (r"sample\s+hemolysed", r"repeat\s+advised", r"fasting\s+sample", r"reference\s+range\s+revised")
    return [line.strip() for line in text.splitlines() if any(re.search(pattern, line, re.I) for pattern in patterns)]


if __name__ == "__main__":
    import uvicorn
    uvicorn.run("main:app", host="0.0.0.0", port=8000, reload=True)
