import { useState } from 'react'
import type { FormEvent } from 'react'
import axios from 'axios'
import { updateAccount, type PlatformOwner } from './api'

export function AccountSettingsView({ owner, onUpdated }: {
  owner: PlatformOwner
  onUpdated: (o: PlatformOwner) => void
}) {
  const [fullName, setFullName] = useState(owner.fullName)
  const [email, setEmail] = useState(owner.email)
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [error, setError] = useState('')
  const [ok, setOk] = useState('')
  const [loading, setLoading] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError(''); setOk(''); setLoading(true)
    try {
      const updated = await updateAccount({
        fullName,
        email,
        currentPassword: newPassword ? currentPassword : undefined,
        newPassword: newPassword || undefined,
      })
      onUpdated(updated)
      setCurrentPassword(''); setNewPassword('')
      setOk('Saqlandi.')
    } catch (err) {
      setError(axios.isAxiosError(err)
        ? (err.response?.data as { message?: string })?.message ?? 'Saqlashda xatolik'
        : 'Saqlashda xatolik')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="cp-panel" style={{ maxWidth: 520 }}>
      <div className="cp-panel-h"><h3>Akkaunt sozlamalari</h3></div>
      <form onSubmit={submit} style={{ padding: 20 }}>
        {error && <p className="cp-err">{error}</p>}
        {ok && <p className="cp-err" style={{ background: '#d9f5f1', color: '#0a5249' }}>{ok}</p>}

        <label className="cp-field">
          <span>Ism</span>
          <input className="cp-input" value={fullName} onChange={(e) => setFullName(e.target.value)} required />
        </label>

        <label className="cp-field">
          <span>Login (email)</span>
          <input className="cp-input" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="off" required />
        </label>

        <div style={{ borderTop: '1px solid var(--line)', margin: '6px 0 16px' }} />
        <p className="cp-hint" style={{ marginTop: 0 }}>Parolni o'zgartirish (ixtiyoriy) — bo'sh qoldirsangiz parol o'zgarmaydi.</p>

        <label className="cp-field">
          <span>Joriy parol</span>
          <input className="cp-input" type="password" value={currentPassword}
            onChange={(e) => setCurrentPassword(e.target.value)} autoComplete="current-password" />
        </label>

        <label className="cp-field">
          <span>Yangi parol (kamida 8 belgi)</span>
          <input className="cp-input" type="password" value={newPassword}
            onChange={(e) => setNewPassword(e.target.value)} autoComplete="new-password" />
        </label>

        <button className="cp-btn primary" type="submit" disabled={loading}>
          {loading ? 'Saqlanmoqda…' : 'Saqlash'}
        </button>
      </form>
    </div>
  )
}
