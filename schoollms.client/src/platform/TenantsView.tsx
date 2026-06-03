import { useState } from 'react'
import { Search, Plus } from 'lucide-react'
import { StatusBadge, formatDate } from './bits'
import type { Tenant } from './api'

export function TenantsView({ tenants, onOpen, onCreate }: {
  tenants: Tenant[]
  onOpen: (id: string) => void
  onCreate: () => void
}) {
  const [q, setQ] = useState('')
  const filtered = tenants.filter((t) =>
    (t.name + ' ' + t.slug + ' ' + t.superAdminEmail).toLowerCase().includes(q.toLowerCase()))

  const count = (s: Tenant['status']) => tenants.filter((t) => t.status === s).length
  const cards = [
    { k: 'Jami maktablar', v: tenants.length },
    { k: 'Faol', v: count('active') },
    { k: 'Tayyorlanmoqda', v: count('provisioning') },
    { k: "To'xtatilgan", v: count('suspended') },
  ]

  return (
    <>
      <div className="cp-cards">
        {cards.map((c) => (
          <div key={c.k} className="cp-card">
            <div className="cp-k">{c.k}</div>
            <div className="cp-v">{c.v}</div>
          </div>
        ))}
      </div>

      <div className="cp-panel">
      <div className="cp-panel-h">
        <Search size={16} />
        <input className="cp-input" placeholder="Maktab qidirish…" value={q}
          onChange={(e) => setQ(e.target.value)} style={{ maxWidth: 280 }} />
        <div style={{ flex: 1 }} />
        <button className="cp-btn primary" onClick={onCreate}><Plus size={16} /> Yangi</button>
      </div>
      {filtered.length === 0 ? (
        <div className="cp-empty">Maktab topilmadi.</div>
      ) : (
        <table className="cp-table">
          <thead><tr><th>Nomi</th><th>Subdomen</th><th>Superadmin</th><th>Holat</th><th>Ochilgan</th></tr></thead>
          <tbody>
            {filtered.map((t) => (
              <tr key={t.id} className="cp-row" onClick={() => onOpen(t.id)}>
                <td>{t.name}</td>
                <td className="cp-slug">{t.slug}</td>
                <td>{t.superAdminEmail}</td>
                <td><StatusBadge status={t.status} /></td>
                <td>{formatDate(t.createdAt)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      </div>
    </>
  )
}
