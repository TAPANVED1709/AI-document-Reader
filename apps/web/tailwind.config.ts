import type { Config } from 'tailwindcss';
const config: Config = { content: ['./app/**/*.{ts,tsx}'], theme: { extend: { colors: { ink: '#11233f', mist: '#f5f8fc', line: '#dbe4ef', teal: '#087f8c' } } }, plugins: [] };
export default config;
