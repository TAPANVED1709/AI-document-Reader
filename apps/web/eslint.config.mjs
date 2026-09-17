import { defineConfig, globalIgnores } from 'eslint/config';
import nextVitals from 'eslint-config-next/core-web-vitals';

export default defineConfig([
  ...nextVitals,
  globalIgnores(['.next/**', '.next-stage6-verification/**', 'node_modules/**', 'public/pdfjs/**', 'next-env.d.ts']),
]);
