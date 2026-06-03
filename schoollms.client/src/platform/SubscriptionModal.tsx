import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import axios from 'axios'
import { listPlatformModules, updateSubscription, type PlatformModule, type Tenant } from './api'

/** Maktab obunasini boshqarish: bo'limlar, narx va sana oralig'i (boshlanish–tugash). */
export function SubscriptionModal({ tenant, onClose, onSaved }: {
  tenant: Tenant
  onClose: () => void
  onSaved: () => Promise<void> | void
}) {
  const [modules, setModules] = useState<PlatformModule[]>([])
  const [enabled, setEnabled] = useState<Set<string>>(new Set(tenant.enabledModules))
  const [price, setPrice] = useState(String(tenant.subscriptionPrice ?? 0))
  const [startsAt, setStartsAt] = useState(tenant.subscriptionStartsAt ?? '')
  const [endsAt, setEndsAt] = useState(tenant.subscriptionEndsAt ?? '')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  useEffect(() => {
    // Bo'sh enabledModules = cheklovsiz → katalog kelganda hammasini belgilaymiz.
    listPlatformModules()
      .then((m) => {
        setModules(m)
        if (tenant.enabledModules.length === 0) setEnabled(new Set(m.map((x) => x.key)))
      })
      .catch(() => {})
  }, [tenant.enabledModules])

  function toggleModule(key: string) {
    setEnabled((prev) => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError(''); setLoading(true)
    try {
      await updateSubscription(tenant.id, {
        enabledModules: [...enabled],
        subscriptionPrice: price ? Number(price) : 0,
        subscriptionStartsAt: startsAt || null,
        subscriptionEndsAt: endsAt || null,
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
          <div className="cp-modal-h"><h3>Obunani boshqarish</h3></div>
          <div className="cp-modal-b">
            {error && <p className="cp-err">{error}</p>}

            <label className="cp-field">
              <span>Obuna narxi (so'm)</span>
              <input className="cp-input" type="number" min="0" value={price}
                onChange={(e) => setPrice(e.target.value)} placeholder="0" />
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
              {loading ? 'Saqlanmoqda…' : 'Saqlash'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
