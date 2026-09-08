import json
import socket
from unittest.mock import patch
import fitz
import pytest
from fastapi.testclient import TestClient
from core.extractor import PdfExtractor, PageData, OcrToken
from core.ocr_engine import LocalOcrEngine, is_tesseract_available
from parsers.lab_parser import LabRowParser
from main import app

ROWS = ['Hemoglobin 10.8 g/dL 13.0 - 17.0', 'Creatinine 0.9 mg/dL 0.7 - 1.3', 'HbA1c 6.8 % 4.0 - 5.6']
EXPECTED = [('Hemoglobin', 10.8, 'g/dL', 13., 17.), ('Creatinine', .9, 'mg/dL', .7, 1.3), ('HbA1c', 6.8, '%', 4., 5.6)]
requires_engine = pytest.mark.skipif(not is_tesseract_available(), reason='Tesseract required')

def make_pdf(kinds):
    with fitz.open() as doc:
        for kind in kinds:
            page = doc.new_page()
            with fitz.open() as source:
                scan = source.new_page()
                target = page if kind == 'native' else scan
                for i, row in enumerate(ROWS):
                    target.insert_text((50, 100 + i * 40), row, fontsize=16)
                if kind != 'native':
                    page.insert_image(page.rect, stream=scan.get_pixmap(dpi=300).tobytes('png'))
        return doc.tobytes()

def analyse(kinds):
    response = TestClient(app).post('/analyse', files={'file': ('synthetic.pdf', make_pdf(kinds), 'application/pdf')})
    assert response.status_code == 200, response.text
    return response.json()

def assert_results(results, page):
    selected = [r for r in results if r['page'] == page]
    assert [(r['normalizedName'], r['value'], r['unit'], r['referenceMin'], r['referenceMax']) for r in selected] == EXPECTED
    return selected

def test_native_never_calls_ocr():
    with patch.object(LocalOcrEngine, 'ocr_from_bytes', side_effect=AssertionError('native page OCR')):
        body = analyse(['native'])
    assert (body['ocrRequired'], body['ocrApplied'], body['processingMode']) == (False, False, 'NATIVE')
    assert_results(body['results'], 1)

@requires_engine
def test_scanned_semantic_results_and_tokens():
    pdf = make_pdf(['scan'])
    native, required = PdfExtractor.extract_from_bytes(pdf)
    assert required and native[0].text == ''
    pages = LocalOcrEngine.ocr_from_bytes(pdf)
    assert pages[0].tokens
    assert all(t.text and 0 <= t.confidence <= 1 and t.width > 0 and t.height > 0 and t.page == 1 for t in pages[0].tokens)
    body = analyse(['scan'])
    assert (body['ocrRequired'], body['ocrApplied'], body['processingMode']) == (True, True, 'OCR')
    for result in assert_results(body['results'], 1):
        box = json.loads(result['boundingBoxJson'])
        assert box['width'] > 0 and box['height'] > 0 and box['page'] == 1 and box['dpi'] == 300
        assert set(result['fieldConfidences']) == {'name', 'value', 'unit', 'ref'}
        assert result['confidence'] <= min(result['fieldConfidences'].values())

@requires_engine
def test_mixed_pdf_only_scanned_page_runs_ocr():
    original = LocalOcrEngine._ocr_page
    seen = []
    def record(self, page, number):
        seen.append(number)
        return original(self, page, number)
    with patch.object(LocalOcrEngine, '_ocr_page', record):
        body = analyse(['native', 'scan', 'native'])
    assert seen == [2]
    assert (body['ocrRequired'], body['ocrApplied'], body['processingMode']) == (True, True, 'HYBRID')
    assert [p['source'] for p in body['pageSources']] == ['NATIVE_TEXT', 'OCR', 'NATIVE_TEXT']
    for page in (1, 2, 3):
        assert_results(body['results'], page)

@pytest.mark.parametrize('kinds,mode,count', [(['scan'], 'OCR', 0), (['native','scan','native'], 'HYBRID', 6)])
def test_unavailable_keeps_native_results(kinds, mode, count):
    with patch('main.is_tesseract_available', return_value=False), patch.object(LocalOcrEngine, 'ocr_from_bytes', side_effect=AssertionError('OCR unavailable')):
        body = analyse(kinds)
    assert (body['ocrRequired'], body['ocrApplied'], body['processingMode']) == (True, False, mode)
    assert len(body['results']) == count
    assert any(p['source'] == 'OCR_UNAVAILABLE' for p in body['pageSources'])

@pytest.mark.parametrize('weak_index,field', [(0,'name'), (1,'value'), (2,'unit'), (3,'ref'), (4,'ref'), (5,'ref')])
def test_weak_token_cannot_be_averaged_away(weak_index, field):
    tokens = [OcrToken(word, .21 if i == weak_index else .99, 10+i*100, 20, 90, 30, 2, (1,1,1)) for i, word in enumerate(ROWS[0].split())]
    item = LabRowParser().parse([PageData(2, ROWS[0], len(ROWS[0]), True, 'OCR', tokens)])[0]
    assert item.confidence == .21 and item.lowConfidence
    assert item.fieldConfidences[field] == .21
    assert json.loads(item.boundingBoxJson) == {'x':10, 'y':20, 'width':590, 'height':30, 'page':2, 'coordinateSpace':'rendered_pixels', 'dpi':300}

def test_garbage_native_layer_requires_ocr():
    assert not PdfExtractor.has_meaningful_text('!@#$ ' * 100)
    assert not PdfExtractor.has_meaningful_text('1234567890' * 10)
    assert PdfExtractor.has_meaningful_text(ROWS[0])

@requires_engine
def test_ocr_failure_is_explicit_and_preserves_other_pages():
    with patch('pytesseract.image_to_data', side_effect=RuntimeError('engine failed')):
        body = analyse(['native', 'scan', 'native'])
    assert body['ocrRequired'] and not body['ocrApplied']
    assert body['pageSources'][1]['source'] == 'OCR_FAILED'
    assert len(body['results']) == 6

@requires_engine
def test_processing_without_network():
    pdf = make_pdf(['native', 'scan', 'native'])
    with patch.object(socket.socket, 'connect', side_effect=AssertionError('network forbidden')):
        pages, required = PdfExtractor.extract_from_bytes(pdf)
        replacements = {p.page_number: p for p in LocalOcrEngine.ocr_from_bytes(pdf, {2})}
        results = LabRowParser().parse([replacements.get(p.page_number, p) for p in pages])
    assert required and len(results) == 9

@pytest.mark.parametrize('row,value,minimum,maximum', [
    ('Base Excess -2.5 mmol/L -3.0 - 3.0', -2.5, -3., 3.),
    ('Creatinine <=0.9 mg/dL <=1.3', .9, None, 1.3),
])
def test_signed_values_and_inequalities(row, value, minimum, maximum):
    item = LabRowParser().parse([PageData(1, row, len(row))])[0]
    assert (item.value, item.referenceMin, item.referenceMax) == (value, minimum, maximum)
    assert item.valueText in row
