"""
Local OCR Engine using Tesseract (pytesseract + Pillow + PyMuPDF).

Renders each PDF page to a high-resolution greyscale image and runs
Tesseract OCR on it. Returns the same PageData list as PdfExtractor so
the existing LabRowParser pipeline can be used unchanged.

All processing is local — no data is sent to any external service.
"""
from __future__ import annotations

import logging
import os
import shutil
from dataclasses import dataclass, field
from typing import List, Tuple

import fitz  # PyMuPDF
from PIL import Image, ImageEnhance

from core.extractor import PageData, OcrToken

logger = logging.getLogger(__name__)

# Tesseract availability flag — checked once at module import time.
_TESSERACT_AVAILABLE: bool = False
_TESSERACT_VERSION: str = ""


def _detect_tesseract() -> Tuple[bool, str]:
    """Probe whether pytesseract and the Tesseract binary are reachable."""
    try:
        import pytesseract  # type: ignore
        if shutil.which("tesseract") is None:
            candidates = [os.path.join(os.environ.get("ProgramFiles", r"C:\Program Files"), "Tesseract-OCR", "tesseract.exe"), os.path.join(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)"), "Tesseract-OCR", "tesseract.exe")]
            installed = next((path for path in candidates if os.path.isfile(path)), None)
            if installed:
                pytesseract.pytesseract.tesseract_cmd = installed
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

    # Tesseract config: OEM 3 (default, LSTM), PSM 4 (single column with variable-sized rows)
    TESSERACT_CONFIG: str = "--oem 3 --psm 4"

    def __init__(self) -> None:
        if not _TESSERACT_AVAILABLE:
            raise RuntimeError(
                "Tesseract OCR is not available on this system. "
                "Install tesseract-ocr and ensure it is on the PATH."
            )
        import pytesseract  # type: ignore  # noqa: PLC0415
        self._pytesseract = pytesseract

    @classmethod
    def ocr_from_bytes(cls, pdf_bytes: bytes, page_numbers=None) -> List[PageData]:
        engine = cls()
        try:
            doc = fitz.open(stream=pdf_bytes, filetype="pdf")
        except Exception as exc:
            raise ValueError(f"Failed to open PDF for OCR: {exc}") from exc
        with doc:
            return [engine._ocr_page(page, idx + 1) for idx, page in enumerate(doc)
                    if page_numbers is None or idx + 1 in page_numbers]

    def _ocr_page(self, page: fitz.Page, page_num: int) -> PageData:
        try:
            image = self._preprocess_image(self._render_page_to_pil(page))
            data = self._pytesseract.image_to_data(
                image, config=self.TESSERACT_CONFIG,
                output_type=self._pytesseract.Output.DICT,
            )
            tokens: list[OcrToken] = []
            for i, raw in enumerate(data["text"]):
                if not raw.strip():
                    continue
                try:
                    confidence = float(data["conf"][i]) / 100.0
                except (TypeError, ValueError):
                    confidence = 0.0
                tokens.append(OcrToken(
                    text=raw.strip(), confidence=max(0.0, min(1.0, confidence)),
                    x=int(data["left"][i]), y=int(data["top"][i]),
                    width=int(data["width"][i]), height=int(data["height"][i]),
                    page=page_num,
                    line=(data["block_num"][i], data["par_num"][i], data["line_num"][i]),
                ))
            # Tesseract's data is structured, but its row order should not be
            # treated as the source of truth. Keep the token metadata and
            # reconstruct deterministic lines from block/paragraph/line IDs.
            lines = {}
            for token in tokens:
                lines.setdefault(token.line, []).append(token)
            text = "\n".join(
                " ".join(token.text for token in sorted(lines[key], key=lambda t: t.x))
                for key in sorted(lines)
            )
            return PageData(page_num, text, len("".join(text.split())), True, "OCR", tokens)
        except Exception as exc:
            logger.warning("OCR failed on page %d: %s", page_num, exc)
            return PageData(page_num, "", 0, True, "OCR_FAILED", error=str(exc))

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
        """Grayscale and mild contrast only; no thresholding or sharpening.

        Pillow ImageFilter does not implement adaptive thresholding. Preserve
        decimals, signs and unit glyphs; leave binarization to Tesseract.
        """
        return ImageEnhance.Contrast(image.convert("L")).enhance(1.1)
