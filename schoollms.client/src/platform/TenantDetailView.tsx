import { useEffect, useState } from 'react'
import axios from 'axios'
import { ArrowLeft, ExternalLink, Pause, Play, Pencil, CreditCard } from 'lucide-react'
import { StatusBadge, formatDate } from './bits'
import { setTenantStatus, getTenantStats, type Tenant, type TenantStats } from './api'
import { EditTenantModal } from './EditTenantModal'
import { SubscriptionModal } from './SubscriptionModal'
import { tenantUrl } from '@/lib/tenant'

export function TenantDetailView({ tenant, onBack, onChanged }: {
  tenant: Tenant
  onBack: () => void
  onChanged: () => Promise<void> | void
}) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [stats, setStats] = useState<TenantStats | null>(null)
  const [editing, setEditing] = useState(false)
  const [editingSub, setEditingSub] = useState(false)
  const url = tenantUrl(tenant.slug)

  const d = new Date()
  const todayStr = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
  const expired = !!tenant.subscriptionEndsAt && tenant.subscriptionEndsAt < todayStr
  const notStarted = !!tenant.subscriptionStartsAt && tenant.subscriptionStartsAt > todayStr
  const startLabel = tenant.subscriptionStartsAt ? formatDate(tenant.subscriptionStartsAt) : '—'
  const endLabel = tenant.subscriptionEndsAt ? formatDate(tenant.subscriptionEndsAt) : 'muddatsiz'
  const rangeLabel = `${startLabel} — ${endLabel}`
    + (expired ? " (muddati o'tgan)" : notStarted ? ' (hali boshlanmagan)' : '')
  const rangeWarn = expired || notStarted
  const moduleLabel = tenant.enabledModules.length === 0
    ? 'Hammasi (cheklovsiz)'
    : `${tenant.enabledModules.length} ta bo'lim`
  const priceLabel = (tenant.subscriptionPrice ?? 0).toLocaleString('uz') + " so'm"

  // Maktab statistikasini (o'sha maktab DB'sidan) yuklaymiz.
  useEffect(() => {
    let on = true
    getTenantStats(tenant.id).then((s) => { if (on) setStats(s) }).catch(() => { if (on) setStats(null) })
    return () => { on = false }
  }, [tenant.id])

  const statCards: { k: string; v: number }[] = stats ? [
    { k: "O'qituvchilar", v: stats.teachers },
    { k: 'Xodimlar', v: stats.staff },
    { k: "O'quvchilar", v: stats.students },
    { k: 'Sinflar', v: stats.classes },
    { k: 'Ilova faollashtirgan', v: stats.appActivated },
    { k: 'Ilova qurilmalari', v: stats.appDevices },
  ] : []

  async function toggle() {
    const next = tenant.status === 'suspended' ? 'active' : 'suspended'
    setBusy(true); setError('')
    try {
      await setTenantStatus(tenant.id, next)
      await onChanged()
    } catch (err) {
      setError(axios.isAxiosError(err)
        ? (err.response?.data as { message?: string })?.message ?? 'Xatolik'
        : 'Xatolik')
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <button className="cp-btn" onClick={onBack} style={{ marginBottom: 16 }}>
        <ArrowLeft size={16} /> Orqaga
      </button>

      {statCards.length > 0 && (
        <div className="cp-cards">
          {statCards.map((c) => (
            <div key={c.k} className="cp-card">
              <div className="cp-k">{c.k}</div>
              <div className="cp-v">{c.v}</div>
            </div>
          ))}
        </div>
      )}

      <div className="cp-panel">
        <div className="cp-panel-h">
          <h3>{tenant.name}</h3>
          <div style={{ flex: 1 }} />
          <StatusBadge status={tenant.status} />
        </div>
        <div style={{ padding: 20 }}>
          {error && <p className="cp-err">{error}</p>}
          <dl className="cp-detail-grid">
            <dt>Subdomen</dt><dd>{tenant.slug}</dd>
            <dt>Manzil</dt><dd><a className="cp-link" href={url} target="_blank" rel="noreferrer">{url}</a></dd>
            <dt>Superadmin</dt><dd>{tenant.superAdminEmail}</dd>
            <dt>Holat</dt><dd><StatusBadge status={tenant.status} /></dd>
            <dt>Ochilgan</dt><dd>{formatDate(tenant.createdAt)}</dd>
            <dt>Obuna narxi</dt><dd>{priceLabel}</dd>
            <dt>Obuna muddati</dt>
            <dd style={rangeWarn ? { color: '#dc2626', fontWeight: 600 } : undefined}>{rangeLabel}</dd>
            <dt>Ochiq bo'limlar</dt><dd>{moduleLabel}</dd>
          </dl>

          <div className="cp-actions">
            <a className="cp-btn primary" href={url} target="_blank" rel="noreferrer">
              <ExternalLink size={16} /> Maktabni ochish
            </a>
            <button className="cp-btn" onClick={() => setEditing(true)}>
              <Pencil size={16} /> Tahrirlash
            </button>
            <button className="cp-btn" onClick={() => setEditingSub(true)}>
              <CreditCard size={16} /> Obuna
            </button>
            {tenant.status !== 'provisioning' && (
              <button className={`cp-btn ${tenant.status === 'suspended' ? '' : 'danger'}`} onClick={toggle} disabled={busy}>
                {tenant.status === 'suspended'
                  ? <><Play size={16} /> Faollashtirish</>
                  : <><Pause size={16} /> To'xtatish</>}
              </button>
            )}
          </div>
        </div>
      </div>

      {editing && (
        <EditTenantModal tenant={tenant} onClose={() => setEditing(false)} onSaved={onChanged} />
      )}
      {editingSub && (
        <SubscriptionModal tenant={tenant} onClose={() => setEditingSub(false)} onSaved={onChanged} />
      )}
    </>
  )
}
