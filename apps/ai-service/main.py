"""
FastAPI Medical Document Intelligence Service (Version 1).
Provides PDF text extraction and conservative laboratory row parsing.
"""
from typing import List
from fastapi import FastAPI, File, UploadFile, HTTPException, status
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel

from core.extractor import PdfExtractor
from parsers.base import LabResultItem
from parsers.lab_parser import LabRowParser

app = FastAPI(
    title="AI Document Reader - Document Intelligence Service",
    version="1.0.0",
    description="Extracts laboratory and pathology test data from text-based medical reports."
)

# Enable CORS for local cross-origin calls
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Instantiate the active parser
parser = LabRowParser()


class HealthResponse(BaseModel):
    status: str
    service: str
    version: str


class AnalysisResponse(BaseModel):
    requiresOcr: bool
    pages: List[int]
    results: List[LabResultItem]


@app.get("/health", response_model=HealthResponse)
async def health_check():
    """Health status endpoint."""
    return HealthResponse(
        status="Healthy",
        service="ai-service",
        version="1.0.0"
    )


@app.post("/analyse", response_model=AnalysisResponse)
async def analyse_document(file: UploadFile = File(...)):
    """
    Accepts a PDF document, extracts text per page using PyMuPDF,
    determines if OCR is required, and parses tabular lab rows.
    """
    if not file.filename:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Filename is missing."
        )

    # Basic extension validation
    if not file.filename.lower().endswith(".pdf"):
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=f"Unsupported file format '{file.filename}'. Only PDF documents are supported."
        )

    try:
        content = await file.read()
    except Exception as e:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=f"Failed to read uploaded file: {str(e)}"
        )

    if len(content) == 0:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Uploaded PDF file is empty (0 bytes)."
        )

    # Extract text per page
    try:
        pages, requires_ocr = PdfExtractor.extract_from_bytes(content)
    except Exception as e:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            detail=f"Corrupted or unreadable PDF: {str(e)}"
        )

    page_numbers = [p.page_number for p in pages]

    # If document has insufficient extractable text, return requiresOcr = true
    if requires_ocr:
        return AnalysisResponse(
            requiresOcr=True,
            pages=page_numbers,
            results=[]
        )

    # Parse laboratory test rows
    try:
        extracted_results = parser.parse(pages)
    except Exception as e:
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"Error while parsing document content: {str(e)}"
        )

    return AnalysisResponse(
        requiresOcr=False,
        pages=page_numbers,
        results=extracted_results
    )


if __name__ == "__main__":
    import uvicorn
    uvicorn.run("main:app", host="0.0.0.0", port=8000, reload=True)
