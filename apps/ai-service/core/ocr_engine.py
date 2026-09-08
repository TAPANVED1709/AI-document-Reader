"""
Local OCR Engine using Tesseract (pytesseract + Pillow + PyMuPDF).

Renders each PDF page to a high-resolution greyscale image and runs
Tesseract OCR on it. Returns the same PageData list as PdfExtractor so
the existing LabRowParser pipeline can be used unchanged.

All processing is local — no data is sent to any external service.
"""
from __future__ import annotations

import logging
from dataclasses import dataclass, field
from typing import List, Optional, Tuple

import fitz  # PyMuPDF
from PIL import Image, ImageFilter

from core.extractor import PageData

logger = logging.getLogger(__name__)

# Tesseract availability flag — checked once at module import time.
_TESSERACT_AVAILABLE: bool = False
_TESSERACT_VERSION: str = ""


def _detect_tesseract() -> Tuple[bool, str]:
    """Probe whether pytesseract and the Tesseract binary are reachable."""
    try:
        import pytesseract  # type: ignore
        version = pytesseract.get_tesseract_version()
        return True, str(version)
    except Exception as exc:  # noqa: BLE001
        logger.warning(
            "Tesseract OCR is not available: %s. "
            "Scanned PDFs will not be processed automatically.",
            exc,
        )
        return False, ""


_TESSERACT_AVAILABLE, _TESSERACT_VERSION = _detect_tesseract()


def is_tesseract_available() -> bool:
    """Return True if Tesseract is installed and reachable."""
    return _TESSERACT_AVAILABLE


def get_tesseract_version() -> str:
    """Return the detected Tesseract version string, or empty string if unavailable."""
    return _TESSERACT_VERSION


class LocalOcrEngine:
    """
    Renders PDF pages to images and runs Tesseract OCR to extract text.

    Only instantiable when Tesseract is available; raises RuntimeError otherwise.
    Use :func:`is_tesseract_available` to check before instantiating.
    """

    # Rendering resolution: 300 DPI gives a good balance of accuracy vs speed
    RENDER_DPI: int = 300

    # Minimum non-whitespace chars in OCR output to consider a page successful
    MIN_OCR_CHARS: int = 10

    # Tesseract config: OEM 3 (default, LSTM), PSM 6 (assume a uniform block of text)
    TESSERACT_CONFIG: str = "--oem 3 --psm 6"

    def __init__(self) -> None:
        if not _TESSERACT_AVAILABLE:
            raise RuntimeError(
                "Tesseract OCR is not available on this system. "
                "Install tesseract-ocr and ensure it is on the PATH."
            )
        import pytesseract  # type: ignore  # noqa: PLC0415
        self._pytesseract = pytesseract

    @classmethod
    def ocr_from_bytes(cls, pdf_bytes: bytes) -> List[PageData]:
        """
        Run OCR on all pages of a PDF supplied as raw bytes.

        Returns a list of :class:`~core.extractor.PageData` objects, one per
        page, containing the OCR-extracted text. Pages that produce very little
        text are included with whatever text was found (possibly empty string).

        Raises:
            RuntimeError: If Tesseract is not installed.
            ValueError:   If the PDF bytes cannot be opened by PyMuPDF.
        """
        engine = cls()
        return engine._ocr_document(pdf_bytes)

    def _ocr_document(self, pdf_bytes: bytes) -> List[PageData]:
        """Internal: iterate pages, render, OCR, collect results."""
        try:
            doc = fitz.open(stream=pdf_bytes, filetype="pdf")
        except Exception as exc:
            raise ValueError(f"Failed to open PDF for OCR: {exc}") from exc

        results: List[PageData] = []
        try:
            for idx, page in enumerate(doc):
                page_num = idx + 1
                text = self._ocr_page(page, page_num)
                char_count = len("".join(text.split()))
                results.append(
                    PageData(
                        page_number=page_num,
                        text=text,
                        character_count=char_count,
                    )
                )
        finally:
            doc.close()

        logger.info(
            "OCR completed: %d page(s) processed, total chars=%d",
            len(results),
            sum(p.character_count for p in results),
        )
        return results

    def _ocr_page(self, page: fitz.Page, page_num: int) -> str:
        """Render a single fitz.Page to an image and run Tesseract OCR on it."""
        try:
            pil_image = self._render_page_to_pil(page)
            preprocessed = self._preprocess_image(pil_image)
            text: str = self._pytesseract.image_to_string(
                preprocessed, config=self.TESSERACT_CONFIG
            )
            char_count = len("".join(text.split()))
            if char_count < self.MIN_OCR_CHARS:
                logger.debug(
                    "Page %d produced very few OCR characters (%d). "
                    "Image quality may be low.",
                    page_num,
                    char_count,
                )
            return text
        except Exception as exc:  # noqa: BLE001
            logger.warning("OCR failed on page %d: %s", page_num, exc)
            return ""

    def _render_page_to_pil(self, page: fitz.Page) -> Image.Image:
        """Render a PyMuPDF page to a PIL Image at RENDER_DPI resolution."""
        # Scale matrix for DPI: 72 is PyMuPDF's native DPI
        scale = self.RENDER_DPI / 72.0
        mat = fitz.Matrix(scale, scale)
        pixmap = page.get_pixmap(matrix=mat, colorspace=fitz.csGRAY, alpha=False)
        # Convert to PIL Image via raw bytes
        return Image.frombytes("L", (pixmap.width, pixmap.height), pixmap.samples)

    @staticmethod
    def _preprocess_image(image: Image.Image) -> Image.Image:
        """
        Apply light pre-processing to improve Tesseract accuracy.

        Steps:
        1. Sharpen slightly to enhance character edges.
        2. Return as-is (greyscale already set during rendering).

        Heavy thresholding is intentionally avoided — it can hurt accuracy
        on low-contrast or noisy scans. Tesseract's built-in binarisation
        (OEM 3) handles most cases well.
        """
        sharpened = image.filter(ImageFilter.SHARPEN)
        return sharpened
