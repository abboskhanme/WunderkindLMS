import { useState } from 'react'
import type { FormEvent } from 'react'
import axios from 'axios'
import { updateTenant, type Tenant } from './api'

export function EditTenantModal({ tenant, onClose, onSaved }: {
  tenant: Tenant
  onClose: () => void
  onSaved: () => Promise<void> | void
}) {
  const [name, setName] = useState(tenant.name)
  const [email, setEmail] = useState(tenant.superAdminEmail)
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError(''); setLoading(true)
    try {
      await updateTenant(tenant.id, {
        name,
        superAdminEmail: email,
        superAdminPassword: password || undefined,
      })
      await onSaved()
      onClose()
    } catch (err) {
      setError(axios.isAxiosError(err)
        ? (err.response?.data as { message?: string })?.message ?? 'Saqlashda xatolik'
        : 'Saqlashda xatolik')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="cp-overlay" onClick={onClose}>
      <div className="cp-modal" onClick={(e) => e.stopPropagation()}>
        <form onSubmit={submit}>
          <div className="cp-modal-h"><h3>Maktabni tahrirlash</h3></div>
          <div className="cp-modal-b">
            {error && <p className="cp-err">{error}</p>}

            <label className="cp-field">
              <span>Maktab nomi</span>
              <input className="cp-input" value={name} onChange={(e) => setName(e.target.value)} required />
            </label>

            <label className="cp-field">
              <span>Subdomen (o'zgartirib bo'lmaydi)</span>
              <input className="cp-input" value={tenant.slug} disabled style={{ opacity: 0.6 }} />
            </label>

            <label className="cp-field">
              <span>Superadmin login</span>
              <input className="cp-input" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="off" required />
            </label>

            <label className="cp-field">
              <span>Yangi parol (ixtiyoriy, kamida 8 belgi)</span>
              <input className="cp-input" type="text" value={password}
                onChange={(e) => setPassword(e.target.value)} autoComplete="new-password"
                placeholder="Bo'sh qoldirsangiz parol o'zgarmaydi" />
            </label>
          </div>
          <div className="cp-modal-f">
            <button type="button" className="cp-btn" onClick={onClose} disabled={loading}>Bekor qilish</button>
            <button type="submit" className="cp-btn primary" disabled={loading}>
              {loading ? 'Saqlanmoqda…' : 'Saqlash'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
