import { describe, expect, it } from 'vitest';
import { confidenceNeedsReview } from '../lib/confidence';
import { mapOcrBoxToPdf } from '../lib/coordinates';

describe('result workspace helpers', () => {
  it('flags only confidence below 75 percent for review', () => {
    expect(confidenceNeedsReview(0.74)).toBe(true);
    expect(confidenceNeedsReview(0.75)).toBe(false);
  });
  it('maps rendered OCR pixels to PDF coordinates by scale', () => {
    expect(mapOcrBoxToPdf({ x: 300, y: 150, width: 120, height: 60 }, 1200, 900, 600, 450)).toEqual({ left: 150, top: 75, width: 60, height: 30 });
  });
});
