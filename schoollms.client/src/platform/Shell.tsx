import { useCallback, useEffect, useState } from 'react'
import { Building2, Plus, LogOut, Settings } from 'lucide-react'
import { initials } from './bits'
import { listTenants, type PlatformOwner, type Tenant } from './api'
import { TenantsView } from './TenantsView'
import { TenantDetailView } from './TenantDetailView'
import { CreateWizard } from './CreateWizard'
import { AccountSettingsView } from './AccountSettingsView'
import { tenantUrl } from '@/lib/tenant'

type Nav = 'tenants' | 'settings'

export function Shell({ owner, onLogout, onOwnerUpdated }: {
  owner: PlatformOwner
  onLogout: () => void
  onOwnerUpdated: (o: PlatformOwner) => void
}) {
  const [nav, setNav] = useState<Nav>('tenants')
  const [tenants, setTenants] = useState<Tenant[]>([])
  const [activeId, setActiveId] = useState<string | null>(null)
  const [wizard, setWizard] = useState(false)

  const reload = useCallback(async () => {
    try { setTenants(await listTenants()) } catch { /* auth interceptor 401 — token tugagan */ }
  }, [])

  useEffect(() => { void reload() }, [reload])

  const active = tenants.find((t) => t.id === activeId) ?? null

  function open(id: string) { setActiveId(id) }
  function back() { setActiveId(null) }
  function goNav(n: Nav) { setNav(n); setActiveId(null) }

  function onCreated(t: Tenant) {
    setWizard(false)
    // Yangi maktab yaratilgach — o'sha maktab manziliga (subdomen) o'tamiz.
    window.location.href = tenantUrl(t.slug)
  }

  const title = active ? active.name
    : nav === 'settings' ? 'Sozlamalar'
    : 'Maktablar'

  return (
    <div className="cp-shell">
      <aside className="cp-side">
        <div className="cp-brand">
          <div className="cp-mark" style={{ width: 36, height: 36, borderRadius: 10 }}><Building2 size={18} /></div>
          <div><b>Intellect School</b><span>control plane</span></div>
        </div>

        <div className="cp-navlabel">Boshqaruv</div>
        <button className={`cp-nav ${nav === 'tenants' && !active ? 'active' : ''}`} onClick={() => goNav('tenants')}>
          <Building2 size={17} /> Maktablar <span className="cp-count">{tenants.length}</span>
        </button>
        <button className={`cp-nav ${nav === 'settings' && !active ? 'active' : ''}`} onClick={() => goNav('settings')}>
          <Settings size={17} /> Sozlamalar
        </button>

        <div className="cp-side-foot">
          <div className="cp-avatar">{initials(owner.fullName)}</div>
          <div className="cp-who" style={{ flex: 1, minWidth: 0 }}>
            <b>{owner.fullName}</b><span>Loyiha boshlig'i</span>
          </div>
          <button className="cp-btn" title="Chiqish" onClick={onLogout} style={{ padding: 8 }}>
            <LogOut size={16} />
          </button>
        </div>
      </aside>

      <div className="cp-main">
        <header className="cp-top">
          <h2>{title}</h2>
          <div className="cp-spacer" />
          <button className="cp-btn primary" onClick={() => setWizard(true)}>
            <Plus size={17} /> Yangi maktab
          </button>
        </header>

        <div className="cp-content">
          {active ? (
            <TenantDetailView tenant={active} onBack={back} onChanged={reload} />
          ) : nav === 'settings' ? (
            <AccountSettingsView owner={owner} onUpdated={onOwnerUpdated} />
          ) : (
            <TenantsView tenants={tenants} onOpen={open} onCreate={() => setWizard(true)} />
          )}
        </div>
      </div>

      {wizard && <CreateWizard onClose={() => setWizard(false)} onCreated={onCreated} />}
    </div>
  )
}
