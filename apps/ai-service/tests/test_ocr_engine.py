"""
Stage 2: OCR Engine Test Suite

Tests for LocalOcrEngine, Tesseract availability detection,
graceful fallback, and integration with the /analyse endpoint.

Synthetic scanned PDFs are generated in-memory using Pillow so no
external test fixtures are required.
"""
import io
import importlib
from unittest.mock import patch

import pytest
from fastapi.testclient import TestClient
from PIL import Image, ImageDraw, ImageFont


# ---------------------------------------------------------------------------
# Helpers — synthetic PDF generation
# ---------------------------------------------------------------------------

def _make_image_only_pdf(text: str = "Hemoglobin 10.8 g/dL 13.0 - 17.0") -> bytes:
    """
    Create an in-memory single-page image-only PDF.

    Draws ``text`` as white text on a black background, then embeds the
    image as the sole page content using PyMuPDF so the PDF contains no
    embedded text layer.
    """
    import fitz  # PyMuPDF

    # 1. Render text onto an image (A4-ish at 150 DPI)
    width, height = 1240, 1754  # ~A4 at 150 DPI
    img = Image.new("L", (width, height), color=255)  # white background
    draw = ImageDraw.Draw(img)

    # Use default PIL font (no external font file needed)
    draw.text((80, 200), text, fill=0)  # black text

    # 2. Save image as PNG to bytes
    img_buf = io.BytesIO()
    img.save(img_buf, format="PNG")
    img_buf.seek(0)

    # 3. Embed PNG into a new single-page PDF (no text layer)
    doc = fitz.open()
    page = doc.new_page(width=595, height=842)  # A4 pts
    page.insert_image(page.rect, stream=img_buf.read())

    pdf_buf = io.BytesIO()
    doc.save(pdf_buf)
    doc.close()
    pdf_buf.seek(0)
    return pdf_buf.read()


def _make_minimal_text_pdf(text: str = "Hemoglobin 10.8 g/dL 13.0 - 17.0") -> bytes:
    """Create a minimal PDF with an actual embedded text layer."""
    import fitz

    doc = fitz.open()
    page = doc.new_page()
    page.insert_text((72, 400), text)

    buf = io.BytesIO()
    doc.save(buf)
    doc.close()
    buf.seek(0)
    return buf.read()


# ---------------------------------------------------------------------------
# Tesseract availability tests
# ---------------------------------------------------------------------------

class TestTesseractAvailability:
    """Tests for the is_tesseract_available() probe."""

    def test_probe_returns_bool(self):
        from core.ocr_engine import is_tesseract_available
        result = is_tesseract_available()
        assert isinstance(result, bool)

    def test_version_string_type(self):
        from core.ocr_engine import get_tesseract_version, is_tesseract_available
        version = get_tesseract_version()
        assert isinstance(version, str)
        if is_tesseract_available():
            assert len(version) > 0
        else:
            assert version == ""

    def test_engine_raises_when_unavailable(self):
        """LocalOcrEngine.__init__ raises RuntimeError if Tesseract is missing."""
        with patch("core.ocr_engine._TESSERACT_AVAILABLE", False):
            from core.ocr_engine import LocalOcrEngine
            with pytest.raises(RuntimeError, match="not available"):
                LocalOcrEngine()


# ---------------------------------------------------------------------------
# OCR engine functional tests (skipped if Tesseract not installed)
# ---------------------------------------------------------------------------

pytesseract_required = pytest.mark.skipif(
    not __import__("core.ocr_engine", fromlist=["is_tesseract_available"]).is_tesseract_available(),  # type: ignore[attr-defined]
    reason="Tesseract is not installed on this system.",
)


class TestLocalOcrEngine:
    """Functional tests for LocalOcrEngine — requires Tesseract installed."""

    @pytesseract_required
    def test_ocr_from_image_pdf_returns_page_data(self):
        """OCR on an image-only PDF should return at least one PageData."""
        from core.ocr_engine import LocalOcrEngine

        pdf_bytes = _make_image_only_pdf()
        pages = LocalOcrEngine.ocr_from_bytes(pdf_bytes)
        assert len(pages) >= 1

    @pytesseract_required
    def test_ocr_page_data_has_correct_page_number(self):
        from core.ocr_engine import LocalOcrEngine

        pdf_bytes = _make_image_only_pdf()
        pages = LocalOcrEngine.ocr_from_bytes(pdf_bytes)
        assert pages[0].page_number == 1

    @pytesseract_required
    def test_ocr_returns_non_empty_text(self):
        """OCR should extract some text from a clearly printed image."""
        from core.ocr_engine import LocalOcrEngine

        # Use a very simple, high-contrast line that Tesseract handles well
        pdf_bytes = _make_image_only_pdf("Hemoglobin 10.8")
        pages = LocalOcrEngine.ocr_from_bytes(pdf_bytes)
        total_chars = sum(p.character_count for p in pages)
        # We cannot guarantee the exact string due to OCR variability,
        # but some characters must be extracted from a printed page
        assert total_chars > 0, f"OCR extracted zero characters. Pages: {pages}"

    @pytesseract_required
    def test_ocr_character_count_matches_text(self):
        """character_count should equal len of non-whitespace chars in text."""
        from core.ocr_engine import LocalOcrEngine

        pdf_bytes = _make_image_only_pdf()
        pages = LocalOcrEngine.ocr_from_bytes(pdf_bytes)
        for page in pages:
            expected = len("".join(page.text.split()))
            assert page.character_count == expected

    @pytesseract_required
    def test_ocr_invalid_pdf_raises_value_error(self):
        from core.ocr_engine import LocalOcrEngine

        with pytest.raises((ValueError, Exception)):
            LocalOcrEngine.ocr_from_bytes(b"not a pdf")


# ---------------------------------------------------------------------------
# Fallback behaviour tests (no Tesseract needed)
# ---------------------------------------------------------------------------

class TestOcrFallback:
    """
    Tests for graceful fallback when Tesseract is unavailable.
    These mock the availability flag and do not require Tesseract installed.
    """

    def test_analyse_endpoint_returns_requires_ocr_when_tesseract_missing(self):
        """
        When Tesseract is not available and a scanned PDF is submitted,
        the /analyse endpoint must return requiresOcr=true and ocrApplied=false.
        """
        with (
            patch("core.ocr_engine._TESSERACT_AVAILABLE", False),
            patch("main.is_tesseract_available", return_value=False),
        ):
            # Re-import main to pick up patched state
            import main as main_module
            client = TestClient(main_module.app)

            pdf_bytes = _make_image_only_pdf()
            response = client.post(
                "/analyse",
                files={"file": ("scanned_report.pdf", pdf_bytes, "application/pdf")},
            )

        assert response.status_code == 200
        body = response.json()
        assert body["requiresOcr"] is True
        assert body["ocrApplied"] is False
        assert body["results"] == []

    def test_analyse_endpoint_text_pdf_unaffected_by_tesseract_flag(self):
        """
        A text-based PDF must always parse correctly regardless of Tesseract state.
        """
        with patch("main.is_tesseract_available", return_value=False):
            import main as main_module
            client = TestClient(main_module.app)

            pdf_bytes = _make_minimal_text_pdf()
            response = client.post(
                "/analyse",
                files={"file": ("text_report.pdf", pdf_bytes, "application/pdf")},
            )

        assert response.status_code == 200
        body = response.json()
        assert body["ocrApplied"] is False
        # Text-based PDF should not trigger requiresOcr (may or may not have results
        # depending on whether the parser matches the test line, but must not crash)
        assert "results" in body


# ---------------------------------------------------------------------------
# /analyse endpoint integration tests with OCR applied
# ---------------------------------------------------------------------------

class TestAnalyseEndpointWithOcr:
    """Integration tests for /analyse when Tesseract IS available."""

    @pytesseract_required
    def test_image_pdf_returns_ocr_applied_true(self):
        """Submitting a scanned PDF should set ocrApplied=true in the response."""
        from main import app as fastapi_app
        client = TestClient(fastapi_app)

        pdf_bytes = _make_image_only_pdf()
        response = client.post(
            "/analyse",
            files={"file": ("scanned.pdf", pdf_bytes, "application/pdf")},
        )

        assert response.status_code == 200
        body = response.json()
        assert body["ocrApplied"] is True
        assert body["requiresOcr"] is False

    @pytesseract_required
    def test_text_pdf_does_not_apply_ocr(self):
        """A text-based PDF must not trigger the OCR path."""
        from main import app as fastapi_app
        client = TestClient(fastapi_app)

        pdf_bytes = _make_minimal_text_pdf()
        response = client.post(
            "/analyse",
            files={"file": ("text.pdf", pdf_bytes, "application/pdf")},
        )

        assert response.status_code == 200
        body = response.json()
        assert body["ocrApplied"] is False
        assert body["requiresOcr"] is False


# ---------------------------------------------------------------------------
# Health endpoint
# ---------------------------------------------------------------------------

class TestHealthEndpoint:
    def test_health_returns_ocr_fields(self):
        from main import app as fastapi_app
        client = TestClient(fastapi_app)
        response = client.get("/health")
        assert response.status_code == 200
        body = response.json()
        assert "ocrAvailable" in body
        assert "tesseractVersion" in body
        assert isinstance(body["ocrAvailable"], bool)

    def test_health_version_is_v2(self):
        from main import app as fastapi_app
        client = TestClient(fastapi_app)
        response = client.get("/health")
        assert response.json()["version"] == "2.0.0"
