"""
Base Parser abstraction for medical document intelligence.
"""
from abc import ABC, abstractmethod
from typing import List, Optional
from pydantic import BaseModel, Field
from core.extractor import PageData


class LabResultItem(BaseModel):
    boundingBoxJson: Optional[str] = None
    fieldConfidences: dict[str, float] = Field(default_factory=dict)
    lowConfidence: bool = False
    originalName: str = Field(..., description="Raw test name extracted from document")
    normalizedName: Optional[str] = Field(None, description="Normalized standard test name")
    value: Optional[float] = Field(None, description="Extracted numeric result if convertible")
    valueText: str = Field(..., description="Raw textual result value as printed")
    unit: Optional[str] = Field(None, description="Measurement unit (e.g. g/dL, mg/dL, %)")
    referenceMin: Optional[float] = Field(None, description="Lower reference boundary")
    referenceMax: Optional[float] = Field(None, description="Upper reference boundary")
    referenceText: Optional[str] = Field(None, description="Extracted reference interval string")
    page: int = Field(1, description="Source page number in PDF")
    confidence: float = Field(0.8, ge=0.0, le=1.0, description="Confidence score for this extraction")


class BaseParser(ABC):
    """Abstract base class for document parsers."""

    @abstractmethod
    def parse(self, pages: List[PageData]) -> List[LabResultItem]:
        """Parse extracted page data into structured lab result items."""
        pass
