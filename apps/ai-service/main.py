"""
FastAPI Medical Document Intelligence Service (Version 2).

Provides PDF text extraction and local OCR fallback for scanned documents.
All processing is local — no data leaves the machine.
"""
import logging
from typing import List

from fastapi import FastAPI, File, UploadFile, HTTPException, status
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel

from core.extractor import PdfExtractor
from core.ocr_engine import LocalOcrEngine, is_tesseract_available, get_tesseract_version
from parsers.base import LabResultItem
from parsers.lab_parser import LabRowParser

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

# Enable CORS for local cross-origin calls
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Instantiate the active parser (shared between text and OCR paths)
parser = LabRowParser()

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
    requiresOcr: bool
    ocrApplied: bool
    pages: List[int]
    results: List[LabResultItem]


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

    page_numbers = [p.page_number for p in pages]

    # -----------------------------------------------------------------------
    # Stage 2: Local OCR fallback for scanned/image PDFs
    # -----------------------------------------------------------------------
    if requires_ocr:
        if is_tesseract_available():
            logger.info(
                "Document '%s' has insufficient extractable text. Running local OCR...",
                file.filename,
            )
            try:
                ocr_pages = LocalOcrEngine.ocr_from_bytes(content)
            except Exception as e:
                logger.error("OCR failed for '%s': %s", file.filename, e)
                raise HTTPException(
                    status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
                    detail=f"Local OCR processing failed: {str(e)}",
                )

            # Parse OCR-extracted text through the same lab row parser
            try:
                extracted_results = parser.parse(ocr_pages)
            except Exception as e:
                raise HTTPException(
                    status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
                    detail=f"Error while parsing OCR content: {str(e)}",
                )

            logger.info(
                "OCR analysis complete for '%s': %d result(s) extracted.",
                file.filename,
                len(extracted_results),
            )

            return AnalysisResponse(
                requiresOcr=False,
                ocrApplied=True,
                pages=page_numbers,
                results=extracted_results,
            )

        else:
            # Tesseract not installed — graceful Stage 1 fallback
            logger.info(
                "Document '%s' requires OCR but Tesseract is unavailable. "
                "Returning requiresOcr=true.",
                file.filename,
            )
            return AnalysisResponse(
                requiresOcr=True,
                ocrApplied=False,
                pages=page_numbers,
                results=[],
            )

    # -----------------------------------------------------------------------
    # Text-based path: parse directly
    # -----------------------------------------------------------------------
    try:
        extracted_results = parser.parse(pages)
    except Exception as e:
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"Error while parsing document content: {str(e)}",
        )

    return AnalysisResponse(
        requiresOcr=False,
        ocrApplied=False,
        pages=page_numbers,
        results=extracted_results,
    )


if __name__ == "__main__":
    import uvicorn
    uvicorn.run("main:app", host="0.0.0.0", port=8000, reload=True)
