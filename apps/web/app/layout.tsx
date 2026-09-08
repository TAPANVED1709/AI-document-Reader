import './globals.css';
import type { Metadata } from 'next';

export const metadata: Metadata = { title: 'AI Document Reader', description: 'Review extracted laboratory results locally.' };
export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) { return <html lang="en"><body>{children}</body></html>; }
