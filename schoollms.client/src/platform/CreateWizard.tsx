import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import axios from 'axios'
import { createTenant, listPlatformModules, type PlatformModule, type Tenant } from './api'

const todayISO = () => new Date().toISOString().slice(0, 10)

function slugify(s: string): string {
  return s.toLowerCase().trim()
    .replace(/[\s_]+/g, '-')
    .replace(/[^a-z0-9-]/g, '')
    .replace(/-{2,}/g, '-')
    .replace(/^-+|-+$/g, '')
}

export function CreateWizard({ onClose, onCreated }: {
  onClose: () => void
  onCreated: (t: Tenant) => void
}) {
  const [name, setName] = useState('')
  const [slug, setSlug] = useState('')
  const [slugEdited, setSlugEdited] = useState(false)
  const [fullName, setFullName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  const [modules, setModules] = useState<PlatformModule[]>([])
  const [enabled, setEnabled] = useState<Set<string>>(new Set())
  const [startsAt, setStartsAt] = useState(todayISO())
  const [endsAt, setEndsAt] = useState('')
  const [price, setPrice] = useState('')

  // Bo'limlar katalogini yuklaymiz — boshlang'ich holatda hammasi belgilangan.
  useEffect(() => {
    listPlatformModules()
      .then((m) => {
        setModules(m)
        setEnabled(new Set(m.map((x) => x.key)))
      })
      .catch(() => {})
  }, [])

  function toggleModule(key: string) {
    setEnabled((prev) => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })
  }

  function onName(v: string) {
    setName(v)
    if (!slugEdited) setSlug(slugify(v))
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError(''); setLoading(true)
    try {
      const t = await createTenant({
        name, slug, superAdminFullName: fullName, superAdminEmail: email, superAdminPassword: password,
        enabledModules: [...enabled],
        subscriptionStartsAt: startsAt || null,
        subscriptionEndsAt: endsAt || null,
        subscriptionPrice: price ? Number(price) : 0,
      })
      onCreated(t)
    } catch (err) {
      setError(axios.isAxiosError(err)
        ? (err.response?.data as { message?: string })?.message ?? 'Maktab ochishda xatolik'
        : 'Maktab ochishda xatolik')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="cp-overlay" onClick={onClose}>
      <div className="cp-modal" onClick={(e) => e.stopPropagation()}>
        <form onSubmit={submit}>
          <div className="cp-modal-h"><h3>Yangi maktab ochish</h3></div>
          <div className="cp-modal-b">
            {error && <p className="cp-err">{error}</p>}

            <label className="cp-field">
              <span>Maktab nomi</span>
              <input className="cp-input" value={name} onChange={(e) => onName(e.target.value)}
                placeholder="1-son maktab" required />
            </label>

            <label className="cp-field">
              <span>Subdomen</span>
              <input className="cp-input" value={slug}
                onChange={(e) => { setSlug(slugify(e.target.value)); setSlugEdited(true) }}
                placeholder="school1" required />
            </label>
            <p className="cp-hint">Maktab manzili: <b>{slug || 'school1'}</b>.&lt;domen&gt; — faqat a-z, 0-9, '-'.</p>

            <label className="cp-field">
              <span>Superadmin (F.I.SH.)</span>
              <input className="cp-input" value={fullName} onChange={(e) => setFullName(e.target.value)}
                placeholder="Aliyev Vali" />
            </label>

            <label className="cp-field">
              <span>Superadmin login</span>
              <input className="cp-input" value={email} onChange={(e) => setEmail(e.target.value)}
                placeholder="admin@school1.uz" autoComplete="off" required />
            </label>

            <label className="cp-field">
              <span>Superadmin parol (kamida 8 belgi)</span>
              <input className="cp-input" type="text" value={password} onChange={(e) => setPassword(e.target.value)}
                autoComplete="new-password" required />
            </label>

            <div className="cp-2col">
              <label className="cp-field">
                <span>Obuna boshlanishi</span>
                <input className="cp-input" type="date" value={startsAt}
                  onChange={(e) => setStartsAt(e.target.value)} />
              </label>
              <label className="cp-field">
                <span>Obuna tugashi</span>
                <input className="cp-input" type="date" value={endsAt} min={startsAt || undefined}
                  onChange={(e) => setEndsAt(e.target.value)} />
              </label>
            </div>
            <p className="cp-hint">Maktab shu sanalar oralig'ida ishlaydi. Tugash sanasi bo'sh = muddatsiz.</p>

            <label className="cp-field">
              <span>Obuna narxi (so'm)</span>
              <input className="cp-input" type="number" min="0" value={price}
                onChange={(e) => setPrice(e.target.value)} placeholder="0" />
            </label>

            <div className="cp-field">
              <span>Ochiq bo'limlar ({enabled.size}/{modules.length})</span>
              <div className="cp-modules">
                {modules.map((m) => (
                  <label key={m.key} className="cp-check">
                    <input type="checkbox" checked={enabled.has(m.key)} onChange={() => toggleModule(m.key)} />
                    <span>{m.label}</span>
                  </label>
                ))}
              </div>
              <p className="cp-hint">Belgilanmagan bo'limlar maktab adminiga ko'rinmaydi.</p>
            </div>
          </div>
          <div className="cp-modal-f">
            <button type="button" className="cp-btn" onClick={onClose} disabled={loading}>Bekor qilish</button>
            <button type="submit" className="cp-btn primary" disabled={loading}>
              {loading ? 'Yaratilmoqda…' : 'Maktab ochish'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
