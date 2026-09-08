"""
PDF Text Extraction Engine using PyMuPDF (fitz).
Extracts text per page and determines whether the document requires OCR.
"""
from dataclasses import dataclass
from typing import List, Tuple

import fitz  # PyMuPDF
from PIL import Image


@dataclass
class PageData:
    page_number: int
    text: str
    character_count: int


class PdfExtractor:
    """
    Extracts text per page from PDF documents without performing OCR.
    Detects scanned or image-only documents.
    """
    MIN_CHARS_THRESHOLD = 30  # Minimum extractable non-whitespace characters

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
                page_text = page.get_text("text") or ""
                cleaned_text = page_text.strip()
                char_count = len("".join(cleaned_text.split()))

                total_text_chars += char_count
                pages.append(PageData(
                    page_number=page_num,
                    text=page_text,
                    character_count=char_count
                ))

            # If document has almost no extractable text, mark requires_ocr = True
            requires_ocr = (len(pages) == 0) or (total_text_chars < cls.MIN_CHARS_THRESHOLD)
            return pages, requires_ocr
        finally:
            doc.close()

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
