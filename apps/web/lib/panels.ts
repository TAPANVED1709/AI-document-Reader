import type { LabResult } from './types';

export const PATHOLOGY_PANELS = ['CBC', 'Liver', 'Kidney', 'Diabetes', 'Lipids', 'Thyroid', 'Iron / Vitamins', 'Urine', 'Coagulation', 'Inflammation', 'Serology / Immunology', 'Electrolytes', 'Other'] as const;
export type PathologyPanel = typeof PATHOLOGY_PANELS[number];

const TEST_PANELS: Record<string, PathologyPanel> = {
  hemoglobin: 'CBC', hb: 'CBC', hgb: 'CBC', rbc: 'CBC', wbc: 'CBC', 'platelet count': 'CBC', platelets: 'CBC', mcv: 'CBC', mch: 'CBC', mchc: 'CBC', rdw: 'CBC', hematocrit: 'CBC', hct: 'CBC', pcv: 'CBC',
  ast: 'Liver', sgot: 'Liver', alt: 'Liver', sgpt: 'Liver', bilirubin: 'Liver', 'total bilirubin': 'Liver', 'direct bilirubin': 'Liver', 'indirect bilirubin': 'Liver', alp: 'Liver', ggt: 'Liver', albumin: 'Liver', globulin: 'Liver', 'total protein': 'Liver',
  creatinine: 'Kidney', urea: 'Kidney', bun: 'Kidney', 'uric acid': 'Kidney', egfr: 'Kidney',
  glucose: 'Diabetes', 'fasting glucose': 'Diabetes', 'post-prandial glucose': 'Diabetes', hba1c: 'Diabetes', 'estimated average glucose': 'Diabetes',
  'total cholesterol': 'Lipids', cholesterol: 'Lipids', hdl: 'Lipids', ldl: 'Lipids', vldl: 'Lipids', triglycerides: 'Lipids', 'non-hdl cholesterol': 'Lipids',
  tsh: 'Thyroid', t3: 'Thyroid', t4: 'Thyroid', 'free t3': 'Thyroid', 'free t4': 'Thyroid',
  ferritin: 'Iron / Vitamins', iron: 'Iron / Vitamins', 'serum iron': 'Iron / Vitamins', tibc: 'Iron / Vitamins', uibc: 'Iron / Vitamins', 'vitamin b12': 'Iron / Vitamins', 'vitamin d': 'Iron / Vitamins', folate: 'Iron / Vitamins',
  ph: 'Urine', 'specific gravity': 'Urine', protein: 'Urine', 'urine protein': 'Urine', ketones: 'Urine', 'rbc/hpf': 'Urine', 'wbc/hpf': 'Urine', casts: 'Urine', crystals: 'Urine',
  pt: 'Coagulation', inr: 'Coagulation', aptt: 'Coagulation', crp: 'Inflammation', 'hs-crp': 'Inflammation', esr: 'Inflammation',
  hbsag: 'Serology / Immunology', hiv: 'Serology / Immunology', hcv: 'Serology / Immunology', vdrl: 'Serology / Immunology', 'rheumatoid factor': 'Serology / Immunology',
  sodium: 'Electrolytes', potassium: 'Electrolytes', chloride: 'Electrolytes', calcium: 'Electrolytes', phosphorus: 'Electrolytes',
};

const key = (value: string) => value.toLowerCase().replace(/[^a-z0-9/]+/g, ' ').trim();

export function pathologyPanel(result: LabResult): PathologyPanel {
  const section = (result.sectionName || '').toLowerCase();
  if (section.includes('blood') || section.includes('hematology') || section === 'cbc') return 'CBC';
  if (section.includes('liver')) return 'Liver';
  if (section.includes('kidney') || section.includes('renal')) return 'Kidney';
  if (section.includes('diabetes') || section.includes('glucose')) return 'Diabetes';
  if (section.includes('lipid')) return 'Lipids';
  if (section.includes('thyroid')) return 'Thyroid';
  if (section.includes('iron') || section.includes('vitamin')) return 'Iron / Vitamins';
  if (section.includes('urine')) return 'Urine';
  if (section.includes('coagulation')) return 'Coagulation';
  if (section.includes('inflamm')) return 'Inflammation';
  if (section.includes('serology') || section.includes('immun')) return 'Serology / Immunology';
  if (section.includes('electrolyte')) return 'Electrolytes';
  return TEST_PANELS[key(result.normalizedTestName || result.originalTestName)] || 'Other';
}

export function groupPathologyResults(results: LabResult[]) {
  return PATHOLOGY_PANELS.map(panel => ({ panel, results: results.filter(result => pathologyPanel(result) === panel) })).filter(group => group.results.length > 0);
}
