"""
PDF Text Extraction Engine using PyMuPDF (fitz).
Extracts text per page and determines whether the document requires OCR.
"""
from dataclasses import dataclass, field
import re
from typing import List, Tuple

import fitz  # PyMuPDF
from PIL import Image


@dataclass
class OcrToken:
    text: str
    confidence: float
    x: int
    y: int
    width: int
    height: int
    page: int
    line: tuple


@dataclass
class PageData:
    page_number: int
    text: str
    character_count: int
    ocr_required: bool = False
    source: str = "NATIVE_TEXT"
    tokens: list[OcrToken] = field(default_factory=list)
    error: str | None = None


class PdfExtractor:
    """
    Extracts text per page from PDF documents without performing OCR.
    Detects scanned or image-only documents.
    """
    MIN_CHARS_THRESHOLD = 20

    @classmethod
    def has_meaningful_text(cls, text: str) -> bool:
        words = re.findall(r"[A-Za-z0-9]+", text)
        return sum(len(w) for w in words) >= cls.MIN_CHARS_THRESHOLD and sum(
            len(w) >= 2 and w.isalpha() for w in words
        ) >= 2

    @classmethod
    def extract_from_bytes(cls, pdf_bytes: bytes) -> Tuple[List[PageData], bool]:
        """
        Extracts text from PDF bytes.
        Returns:
            Tuple of (pages_data, requires_ocr)
        """
        pages: List[PageData] = []
        total_text_chars = 0

        try:
            doc = fitz.open(stream=pdf_bytes, filetype="pdf")
        except Exception as e:
            raise ValueError(f"Failed to open PDF document: {str(e)}") from e

        try:
            for idx, page in enumerate(doc):
                page_num = idx + 1
                page_text = cls._native_rows(page)
                cleaned_text = page_text.strip()
                char_count = len("".join(cleaned_text.split()))

                total_text_chars += char_count
                pages.append(PageData(
                    page_number=page_num,
                    text=page_text,
                    character_count=char_count,
                    ocr_required=not cls.has_meaningful_text(page_text),
                    source="NATIVE_TEXT" if cls.has_meaningful_text(page_text) else "OCR_PENDING",
                ))

            # If document has almost no extractable text, mark requires_ocr = True
            requires_ocr = any(p.ocr_required for p in pages) or not pages
            return pages, requires_ocr
        finally:
            doc.close()

    @staticmethod
    def _native_rows(page: fitz.Page) -> str:
        """Join same-baseline cells inside a PDF text block, never adjacent rows.

        Keep the PDF's block order: globally sorting by y interleaves independent
        parallel panels. Wrapped lines remain separate for bounded parser joining.
        """
        output = []
        for block in page.get_text("dict")["blocks"]:
            bands = []
            for line in block.get("lines", []):
                text = "".join(span["text"] for span in line["spans"]).strip()
                if not text:
                    continue
                x, y, _, _ = line["bbox"]
                band = next((b for b in bands if abs(b[0] - y) <= 2), None)
                if band is None:
                    band = [y, []]; bands.append(band)
                band[1].append((x, text))
            output.extend(" ".join(text for _, text in sorted(band[1])) for band in bands)
        return "\n".join(output)

    @staticmethod
    def render_page_to_image(page: fitz.Page, dpi: int = 300) -> Image.Image:
        """
        Render a single PyMuPDF page to a greyscale PIL Image.

        Used by LocalOcrEngine — exposed here so both the extractor and
        OCR engine share the same page-rendering logic.

        Args:
            page: A fitz.Page object (must still be open).
            dpi:  Target rendering resolution (default 300 DPI).

        Returns:
            A greyscale PIL Image of the rendered page.
        """
        scale = dpi / 72.0
        mat = fitz.Matrix(scale, scale)
        pixmap = page.get_pixmap(matrix=mat, colorspace=fitz.csGRAY, alpha=False)
        return Image.frombytes("L", (pixmap.width, pixmap.height), pixmap.samples)
