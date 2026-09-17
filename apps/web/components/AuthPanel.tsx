'use client';
import { useState, type FormEvent } from 'react';
import { login, registerPatient } from '../lib/api';
import type { CurrentUser } from '../lib/types';

export default function AuthPanel({ user, onLogin }: { user: CurrentUser | null; onLogin: (user: CurrentUser) => void }) {
  const [register, setRegister] = useState(false);
  const [email, setEmail] = useState(''); const [password, setPassword] = useState('');
  const [firstName, setFirstName] = useState(''); const [lastName, setLastName] = useState('');
  const [confirmation, setConfirmation] = useState(''); const [error, setError] = useState('');
  const [notice, setNotice] = useState(''); const [busy, setBusy] = useState(false);
  const switchMode = () => { setRegister(!register); setPassword(''); setConfirmation(''); setError(''); setNotice(''); };
  const submit = async (event: FormEvent) => {
    event.preventDefault(); setError(''); setNotice('');
    if (register && (!firstName.trim() || !lastName.trim())) { setError('Enter your first and last name.'); return; }
    if (register && (password.length < 12 || password.length > 128 || !/[A-Z]/.test(password) || !/[0-9]/.test(password))) { setError('Use 12–128 characters, including an uppercase letter and a number.'); return; }
    if (register && password !== confirmation) { setError('Passwords do not match.'); return; }
    setBusy(true);
    try {
      if (register) {
        await registerPatient({ firstName: firstName.trim(), lastName: lastName.trim(), email: email.trim(), password });
        setRegister(false); setPassword(''); setConfirmation(''); setNotice('Your patient account is ready. Sign in to continue.');
      } else onLogin(await login(email.trim(), password));
    } catch (e) { setError(e instanceof Error ? e.message : 'Unable to complete this request. Please try again.'); }
    finally { setBusy(false); }
  };
  if (user) return <p className="mb-5 rounded-lg border border-line bg-white p-3 text-sm">Signed in as <b>{user.email}</b> · {user.role}</p>;
  const field = 'focus-ring mt-1 block w-full rounded-lg border border-line px-3 py-2';
  return <section className="mb-6 max-w-xl rounded-xl border border-line bg-white p-6 shadow-sm" aria-label="Patient account">
    <h3 className="mb-2 text-xl font-bold">{register ? 'Create patient account' : 'Sign in'}</h3>
    <p className="mb-5 text-sm text-slate-600">{register ? 'Keep your medical reports together in your private patient workspace.' : 'Access your reports and stored measurements.'}</p>
    <form onSubmit={submit} className="grid gap-4">
      {register && <div className="grid gap-4 sm:grid-cols-2"><label className="text-sm">First Name<input required maxLength={100} autoComplete="given-name" value={firstName} onChange={e => setFirstName(e.target.value)} className={field} /></label><label className="text-sm">Last Name<input required maxLength={100} autoComplete="family-name" value={lastName} onChange={e => setLastName(e.target.value)} className={field} /></label></div>}
      <label className="text-sm">Email<input required type="email" maxLength={254} autoComplete="email" value={email} onChange={e => setEmail(e.target.value)} className={field} /></label>
      <label className="text-sm">Password<input required type="password" maxLength={register ? 128 : undefined} autoComplete={register ? 'new-password' : 'current-password'} value={password} onChange={e => setPassword(e.target.value)} aria-describedby={register ? 'password-requirements' : undefined} className={field} /></label>
      {register && <><p id="password-requirements" className="text-xs text-slate-600">Use 12–128 characters, including an uppercase letter and a number.</p><label className="text-sm">Confirm Password<input required type="password" maxLength={128} autoComplete="new-password" value={confirmation} onChange={e => setConfirmation(e.target.value)} className={field} /></label></>}
      {error && <p role="alert" className="rounded bg-red-50 p-3 text-sm text-red-800">{error}</p>}
      {notice && <p role="status" className="rounded bg-teal-50 p-3 text-sm">{notice}</p>}
      <button disabled={busy} className="focus-ring rounded-lg bg-ink px-4 py-3 font-bold text-white disabled:opacity-60">{busy ? 'Please wait…' : register ? 'Register' : 'Log in'}</button>
      <button type="button" disabled={busy} onClick={switchMode} className="focus-ring text-sm font-semibold text-teal">{register ? 'Already have an account? Sign in' : 'Create patient account'}</button>
    </form>
  </section>;
}
