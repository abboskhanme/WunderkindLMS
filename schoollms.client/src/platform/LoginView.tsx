import { useState } from 'react'
import type { FormEvent } from 'react'
import axios from 'axios'
import { ShieldCheck } from 'lucide-react'
import { platformLogin, type PlatformOwner } from './api'

function errorMessage(err: unknown): string {
  if (axios.isAxiosError(err)) {
    return (err.response?.data as { message?: string })?.message ?? 'Kirishda xatolik yuz berdi'
  }
  return err instanceof Error ? err.message : 'Kirishda xatolik yuz berdi'
}

export function LoginView({ onLogin }: { onLogin: (token: string, owner: PlatformOwner) => void }) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError(''); setLoading(true)
    try {
      const { token, owner } = await platformLogin(email, password)
      onLogin(token, owner)
    } catch (err) {
      setError(errorMessage(err))
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="cp-login">
      <div className="cp-login-card">
        <div className="cp-login-brand">
          <div className="cp-mark"><ShieldCheck size={26} /></div>
          <div>
            <h1>Intellect School — Control Plane</h1>
            <p>Loyiha boshlig'i kirishi</p>
          </div>
        </div>
        <form onSubmit={submit}>
          <label className="cp-field">
            <span>Login</span>
            <input className="cp-input" type="text" autoComplete="username"
              value={email} onChange={(e) => setEmail(e.target.value)} required />
          </label>
          <label className="cp-field">
            <span>Parol</span>
            <input className="cp-input" type="password" autoComplete="current-password"
              value={password} onChange={(e) => setPassword(e.target.value)} required />
          </label>
          {error && <p className="cp-err">{error}</p>}
          <button className="cp-btn primary" type="submit" disabled={loading} style={{ width: '100%', justifyContent: 'center' }}>
            {loading ? 'Kirilmoqda…' : 'Kirish'}
          </button>
        </form>
      </div>
    </div>
  )
}
