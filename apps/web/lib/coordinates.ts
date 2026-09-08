export type PixelBox = { x: number; y: number; width: number; height: number };
export function mapOcrBoxToPdf(box: PixelBox, renderedWidth: number, renderedHeight: number, pdfWidth: number, pdfHeight: number) {
  return { left: box.x / renderedWidth * pdfWidth, top: box.y / renderedHeight * pdfHeight, width: box.width / renderedWidth * pdfWidth, height: box.height / renderedHeight * pdfHeight };
}
