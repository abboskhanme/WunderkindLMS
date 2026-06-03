import { useEffect, useState } from 'react'
import { Plus, Eye, Pencil, Trash2, Check } from 'lucide-react'
import type { Staff, Credentials } from '@/types'
import {
  getStaff,
  createStaff,
  updateStaff,
  deleteStaff,
  getStaffCredentials,
  resetStaffPassword,
  setStaffPermissions,
  type StaffPayload,
} from '@/api/services/staff'
import { adminPermissions } from '@/config/constants'
import { useAuth } from '@/context/auth-context'
import { cn, randomPassword } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Input } from '@/components/ui/Input'
import { CredentialsBox } from '@/components/ui/CredentialsBox'

const POSITIONS = ['Kassir', 'Administrator', "Direktor o'rinbosari", 'Qorovul', 'Hisobchi']

export function StaffPage() {
  const { user } = useAuth()
  // Rollar (ruxsatlar)ni faqat tizim egasi (superadmin) o'zgartira oladi — backend ham shuni talab qiladi.
  const canManageRoles = user?.role === 'superadmin'

  const [staff, setStaff] = useState<Staff[]>([])
  const [loading, setLoading] = useState(true)
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<Staff | null>(null)
  const [form, setForm] = useState<StaffPayload>({ fullName: '', position: '' })
  // Yangi xodim yaratishda darrov beriladigan ruxsatlar
  const [formPerms, setFormPerms] = useState<Set<string>>(new Set())
  const [saving, setSaving] = useState(false)

  // Har bir xodim uchun tahrirlanayotgan ruxsatlar (id → kalitlar to'plami)
  const [draft, setDraft] = useState<Record<string, Set<string>>>({})
  const [savingPermsId, setSavingPermsId] = useState<string | null>(null)

  // Login/parol oynasi
  const [credOf, setCredOf] = useState<Staff | null>(null)
  const [creds, setCreds] = useState<Credentials | null>(null)
  const [credLoading, setCredLoading] = useState(false)

  const syncDraft = (list: Staff[]) =>
    setDraft(Object.fromEntries(list.map((s) => [s.id, new Set(s.permissions)])))

  useEffect(() => {
    getStaff()
      .then((list) => {
        setStaff(list)
        syncDraft(list)
      })
      .finally(() => setLoading(false))
  }, [])

  const openCreate = () => {
    setEditing(null)
    setForm({ fullName: '', position: '' })
    setFormPerms(new Set())
    setFormOpen(true)
  }
  const openEdit = (s: Staff) => {
    setEditing(s)
    setForm({ fullName: s.fullName, position: s.position })
    setFormOpen(true)
  }

  const showCredentials = (s: Staff) => {
    setCredOf(s)
    setCreds(null)
    setCredLoading(true)
    getStaffCredentials(s.id)
      .then(setCreds)
      .finally(() => setCredLoading(false))
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!form.fullName.trim()) return
    setSaving(true)
    try {
      if (editing) {
        const u = await updateStaff(editing.id, form)
        setStaff((p) => p.map((x) => (x.id === u.id ? u : x)))
        setFormOpen(false)
      } else {
        let created = await createStaff(form)
        // Yangi xodimga tanlangan rollarni darrov beramiz (faqat superadmin)
        if (canManageRoles && formPerms.size > 0) {
          created = await setStaffPermissions(created.id, [...formPerms])
        }
        setStaff((p) => [created, ...p])
        setDraft((d) => ({ ...d, [created.id]: new Set(created.permissions) }))
        setFormOpen(false)
        showCredentials(created) // login/parolni darrov ko'rsatamiz
      }
    } finally {
      setSaving(false)
    }
  }

  const handleDelete = (s: Staff) => {
    if (!confirm(`"${s.fullName}" xodimni o'chirasizmi? Akkaunti ham o'chadi.`)) return
    deleteStaff(s.id).then(() => {
      setStaff((p) => p.filter((x) => x.id !== s.id))
      setDraft((d) => {
        const { [s.id]: _, ...rest } = d
        return rest
      })
    })
  }

  const toggle = (staffId: string, key: string) =>
    setDraft((d) => {
      const next = new Set(d[staffId] ?? [])
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return { ...d, [staffId]: next }
    })

  const dirty = (s: Staff) => {
    const cur = draft[s.id] ?? new Set()
    return cur.size !== s.permissions.length || s.permissions.some((p) => !cur.has(p))
  }

  const savePerms = (s: Staff) => {
    const perms = [...(draft[s.id] ?? new Set())]
    setSavingPermsId(s.id)
    setStaffPermissions(s.id, perms)
      .then((u) => setStaff((p) => p.map((x) => (x.id === u.id ? u : x))))
      .finally(() => setSavingPermsId(null))
  }

  const toggleFormPerm = (key: string) =>
    setFormPerms((s) => {
      const next = new Set(s)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Xodimlar va rollar</h1>
          <p className="text-sm text-slate-400">
            O'qituvchi bo'lmagan ishchilar (kassir, administrator, ...)
            {canManageRoles ? ' — har biriga kerakli bo\'limlarni (rollarni) shu yerda belgilang.' : '.'}
          </p>
        </div>
        <Button onClick={openCreate}>
          <Plus className="h-4 w-4" /> Yangi xodim
        </Button>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : staff.length === 0 ? (
        <Card>
          <p className="py-12 text-center text-slate-400">
            Hali xodim qo'shilmagan. "Yangi xodim" tugmasi orqali qo'shing.
          </p>
        </Card>
      ) : (
        <div className="space-y-4">
          {staff.map((s) => {
            const cur = draft[s.id] ?? new Set<string>()
            return (
              <Card key={s.id}>
                <div className="mb-3 flex flex-wrap items-start justify-between gap-2">
                  <div>
                    <p className="font-semibold text-slate-800">{s.fullName}</p>
                    <p className="text-xs text-slate-400">
                      {s.position || 'Xodim'} · <code>{s.login}</code>
                    </p>
                  </div>
                  <div className="flex items-center gap-0.5">
                    <IconBtn icon={Eye} title="Login/parol" onClick={() => showCredentials(s)} />
                    <IconBtn icon={Pencil} title="Tahrirlash" onClick={() => openEdit(s)} />
                    <IconBtn icon={Trash2} title="O'chirish" danger onClick={() => handleDelete(s)} />
                  </div>
                </div>

                {canManageRoles ? (
                  <>
                    <div className="flex flex-wrap gap-2">
                      {adminPermissions.map((p) => {
                        const active = cur.has(p.key)
                        return (
                          <button
                            key={p.key}
                            type="button"
                            onClick={() => toggle(s.id, p.key)}
                            className={cn(
                              'inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-sm transition-colors',
                              active
                                ? 'border-brand-500 bg-brand-50 text-brand-700'
                                : 'border-slate-200 text-slate-600 hover:bg-slate-50',
                            )}
                          >
                            {active && <Check className="h-3.5 w-3.5" />}
                            {p.label}
                          </button>
                        )
                      })}
                    </div>
                    <div className="mt-3 flex justify-end">
                      <Button
                        onClick={() => savePerms(s)}
                        disabled={!dirty(s) || savingPermsId === s.id}
                      >
                        {savingPermsId === s.id ? 'Saqlanmoqda...' : 'Ruxsatlarni saqlash'}
                      </Button>
                    </div>
                  </>
                ) : (
                  <div className="flex flex-wrap gap-1.5">
                    {s.permissions.length === 0 ? (
                      <span className="text-xs text-slate-400">Ruxsatlar belgilanmagan</span>
                    ) : (
                      s.permissions.map((key) => {
                        const label = adminPermissions.find((p) => p.key === key)?.label ?? key
                        return (
                          <span
                            key={key}
                            className="rounded-full bg-slate-100 px-2.5 py-0.5 text-xs text-slate-600"
                          >
                            {label}
                          </span>
                        )
                      })
                    )}
                  </div>
                )}
              </Card>
            )
          })}
        </div>
      )}

      {/* Yaratish / tahrirlash */}
      <Modal
        open={formOpen}
        onClose={() => setFormOpen(false)}
        title={editing ? 'Xodimni tahrirlash' : 'Yangi xodim'}
        footer={
          <>
            <Button variant="secondary" onClick={() => setFormOpen(false)}>
              Bekor qilish
            </Button>
            <Button type="submit" form="staff-form" disabled={saving}>
              {saving ? 'Saqlanmoqda...' : 'Saqlash'}
            </Button>
          </>
        }
      >
        <form id="staff-form" onSubmit={handleSubmit} className="space-y-4">
          <Input
            label="F.I.SH"
            required
            value={form.fullName}
            onChange={(e) => setForm((f) => ({ ...f, fullName: e.target.value }))}
          />
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-600">Lavozim</label>
            <input
              list="staff-positions"
              value={form.position}
              onChange={(e) => setForm((f) => ({ ...f, position: e.target.value }))}
              placeholder="Masalan: Kassir"
              className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-800 outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
            />
            <datalist id="staff-positions">
              {POSITIONS.map((p) => (
                <option key={p} value={p} />
              ))}
            </datalist>
          </div>
          {editing && (
            <div>
              <label className="mb-1 block text-sm font-medium text-slate-600">Parolni almashtirish</label>
              <div className="flex items-start gap-2">
                <input
                  type="text"
                  autoComplete="new-password"
                  placeholder="Bo'sh qoldirilsa — parol o'zgarmaydi"
                  value={form.newPassword ?? ''}
                  onChange={(e) => setForm((f) => ({ ...f, newPassword: e.target.value }))}
                  className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-800 outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
                />
                <Button
                  type="button"
                  variant="secondary"
                  onClick={() => setForm((f) => ({ ...f, newPassword: randomPassword() }))}
                >
                  Generatsiya
                </Button>
              </div>
            </div>
          )}
          {!editing && canManageRoles && (
            <div>
              <label className="mb-1 block text-sm font-medium text-slate-600">
                Ruxsatlar (rollar)
              </label>
              <div className="flex flex-wrap gap-2">
                {adminPermissions.map((p) => {
                  const active = formPerms.has(p.key)
                  return (
                    <button
                      key={p.key}
                      type="button"
                      onClick={() => toggleFormPerm(p.key)}
                      className={cn(
                        'inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-sm transition-colors',
                        active
                          ? 'border-brand-500 bg-brand-50 text-brand-700'
                          : 'border-slate-200 text-slate-600 hover:bg-slate-50',
                      )}
                    >
                      {active && <Check className="h-3.5 w-3.5" />}
                      {p.label}
                    </button>
                  )
                })}
              </div>
            </div>
          )}
          {!editing && (
            <p className="text-xs text-slate-400">
              Saqlangach tizimga kirish uchun login va parol avtomatik yaratiladi va ko'rsatiladi.
              {canManageRoles
                ? ' Ruxsatlarni keyinroq ham har bir xodim kartasidan o\'zgartirishingiz mumkin.'
                : ' Ruxsatlarni (bo\'limlarni) tizim egasi belgilaydi.'}
            </p>
          )}
        </form>
      </Modal>

      {/* Login/parol */}
      <Modal
        open={!!credOf}
        onClose={() => setCredOf(null)}
        title={credOf ? `${credOf.fullName} — akkaunt` : 'Akkaunt'}
        footer={
          <Button variant="secondary" onClick={() => setCredOf(null)}>
            Yopish
          </Button>
        }
      >
        <CredentialsBox
          credentials={creds}
          loading={credLoading}
          onReset={
            credOf
              ? async () => {
                  const c = await resetStaffPassword(credOf.id)
                  setCreds(c)
                }
              : undefined
          }
        />
      </Modal>
    </div>
  )
}

function IconBtn({
  icon: Icon,
  title,
  onClick,
  danger,
}: {
  icon: typeof Eye
  title: string
  onClick: () => void
  danger?: boolean
}) {
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      className={cn(
        'rounded-lg p-1.5 transition-colors',
        danger
          ? 'text-slate-400 hover:bg-red-50 hover:text-red-600'
          : 'text-slate-400 hover:bg-slate-100 hover:text-slate-700',
      )}
    >
      <Icon className="h-4 w-4" />
    </button>
  )
}
