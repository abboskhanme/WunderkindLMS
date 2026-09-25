import { useEffect, useState } from 'react'
import { Plus, Eye, Pencil, Trash2 } from 'lucide-react'
import type { AccessRole, Staff, Credentials } from '@/types'
import {
  getStaff,
  createStaff,
  updateStaff,
  deleteStaff,
  getStaffCredentials,
  resetStaffPassword,
  setStaffRole,
  type StaffPayload,
} from '@/api/services/staff'
import { getAccessRoles } from '@/api/services/accessRoles'
import { useAuth } from '@/context/auth-context'
import { cn, randomPassword } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Drawer } from '@/components/ui/Drawer'
import { Modal } from '@/components/ui/Modal'
import { PhotoUpload } from '@/components/ui/PhotoUpload'
import { UserAvatar } from '@/components/ui/UserAvatar'
import { Input, Select } from '@/components/ui/Input'
import { CredentialsBox } from '@/components/ui/CredentialsBox'

const POSITIONS = ['Kassir', 'Administrator', "Direktor o'rinbosari", 'Qorovul', 'Hisobchi']

export function StaffPage() {
  const { user } = useAuth()
  // Rol biriktirishni faqat tizim egasi (superadmin) qila oladi — backend ham shuni talab qiladi.
  const canManageRoles = user?.role === 'superadmin'

  const [staff, setStaff] = useState<Staff[]>([])
  const [roles, setRoles] = useState<AccessRole[]>([])
  const [loading, setLoading] = useState(true)
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<Staff | null>(null)
  const [form, setForm] = useState<StaffPayload>({ fullName: '', position: '' })
  // Biriktiriladigan rol ('' — rolsiz)
  const [formRoleId, setFormRoleId] = useState('')
  const [saving, setSaving] = useState(false)

  // Login/parol oynasi
  const [credOf, setCredOf] = useState<Staff | null>(null)
  const [creds, setCreds] = useState<Credentials | null>(null)
  const [credLoading, setCredLoading] = useState(false)

  useEffect(() => {
    Promise.all([getStaff(), getAccessRoles().catch(() => [] as AccessRole[])])
      .then(([list, roleList]) => {
        setStaff(list)
        setRoles(roleList)
      })
      .finally(() => setLoading(false))
  }, [])

  const openCreate = () => {
    setEditing(null)
    setForm({ fullName: '', position: '', avatarUrl: '' })
    setFormRoleId('')
    setFormOpen(true)
  }
  const openEdit = (s: Staff) => {
    setEditing(s)
    setForm({ fullName: s.fullName, position: s.position, avatarUrl: s.avatarUrl ?? '' })
    setFormRoleId(s.accessRoleId ?? '')
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

  /** Rol o'zgargan bo'lsa biriktiradi; xato bo'lsa xabar beradi, xodim esa saqlangan qoladi. */
  const applyRole = async (s: Staff): Promise<Staff> => {
    if (!canManageRoles || (s.accessRoleId ?? '') === formRoleId) return s
    try {
      return await setStaffRole(s.id, formRoleId || null)
    } catch (err) {
      alert(roleErrorMessage(err))
      return s
    }
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!form.fullName.trim()) return
    setSaving(true)
    try {
      if (editing) {
        const u = await applyRole(await updateStaff(editing.id, form))
        setStaff((p) => p.map((x) => (x.id === u.id ? u : x)))
        setFormOpen(false)
      } else {
        const created = await applyRole(await createStaff(form))
        setStaff((p) => [created, ...p])
        setFormOpen(false)
        showCredentials(created) // login/parolni darrov ko'rsatamiz
      }
      // Xodimlar soni ustuni "Rollar" sahifasida to'g'ri turishi uchun.
      getAccessRoles().then(setRoles).catch(() => undefined)
    } finally {
      setSaving(false)
    }
  }

  const handleDelete = (s: Staff) => {
    if (!confirm(`"${s.fullName}" xodimni o'chirasizmi? Akkaunti ham o'chadi.`)) return
    deleteStaff(s.id).then(() => setStaff((p) => p.filter((x) => x.id !== s.id)))
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Xodimlar</h1>
          <p className="text-sm text-slate-400">
            Tizimdan foydalanadigan xodimlar. Qaysi bo'limlarni ko'rishi biriktirilgan roldan olinadi
            (Boshqaruv → Rollar).
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
        <Card className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="w-12 px-4 py-3">№</th>
                  <th className="px-4 py-3">F.I.SH</th>
                  <th className="px-4 py-3">Login</th>
                  <th className="px-4 py-3">Lavozimi</th>
                  <th className="px-4 py-3">Rol</th>
                  <th className="px-4 py-3">Oxirgi faollik</th>
                  <th className="px-4 py-3 text-right">Amallar</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {staff.map((s, i) => (
                  <tr key={s.id} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3 text-slate-400">{i + 1}</td>
                    <td className="px-4 py-3">
                      <span className="flex items-center gap-3">
                        <UserAvatar fullName={s.fullName} avatarUrl={s.avatarUrl} className="h-9 w-9 text-xs" />
                        <span className="font-medium text-slate-800">{s.fullName}</span>
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      <code className="text-xs text-slate-500">{s.login}</code>
                    </td>
                    <td className="px-4 py-3 text-slate-600">{s.position || '—'}</td>
                    <td className="px-4 py-3">
                      {s.accessRoleName ? (
                        <span className="rounded-full bg-brand-50 px-2.5 py-0.5 text-xs font-medium text-brand-700">
                          {s.accessRoleName}
                        </span>
                      ) : (
                        <span
                          className="text-xs text-slate-400"
                          title={
                            s.permissions.length > 0
                              ? 'Rol biriktirilmagan — eski shaxsiy ruxsatlar amal qilyapti'
                              : undefined
                          }
                        >
                          {s.permissions.length > 0 ? `Rolsiz · ${s.permissions.length} ta ruxsat` : 'Rol yo\'q'}
                        </span>
                      )}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-slate-500">{lastSeen(s.lastLoginAt)}</td>
                    <td className="px-4 py-3">
                      <span className="flex justify-end gap-0.5">
                        <IconBtn icon={Eye} title="Login/parol" onClick={() => showCredentials(s)} />
                        <IconBtn icon={Pencil} title="Tahrirlash" onClick={() => openEdit(s)} />
                        <IconBtn icon={Trash2} title="O'chirish" danger onClick={() => handleDelete(s)} />
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>
      )}

      {/* Yaratish / tahrirlash */}
      <Drawer
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
          <PhotoUpload
            label="Profil rasmi"
            value={form.avatarUrl || null}
            onChange={(url) => setForm((f) => ({ ...f, avatarUrl: url ?? '' }))}
          />
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
          <div>
            <Select
              label="Rol"
              value={formRoleId}
              disabled={!canManageRoles}
              onChange={(e) => setFormRoleId(e.target.value)}
            >
              <option value="">— Rol biriktirilmagan —</option>
              {roles.map((r) => (
                <option key={r.id} value={r.id}>
                  {r.name}
                </option>
              ))}
            </Select>
            <p className="mt-1 text-xs text-slate-400">
              {canManageRoles
                ? 'Xodim faqat shu rolga berilgan bo\'limlarni ko\'radi. Ruxsatlar Boshqaruv → Rollar sahifasida.'
                : 'Rolni tizim egasi (superadmin) biriktiradi.'}
            </p>
            {editing && !editing.accessRoleId && editing.permissions.length > 0 && canManageRoles && (
              <p className="mt-1 text-xs text-amber-600">
                Hozir {editing.permissions.length} ta eski shaxsiy ruxsat amal qilyapti — rol tanlansa, ular
                rol ruxsatlari bilan almashadi.
              </p>
            )}
          </div>
          {!editing && (
            <p className="text-xs text-slate-400">
              Saqlangach tizimga kirish uchun login va parol avtomatik yaratiladi va ko'rsatiladi.
            </p>
          )}
        </form>
      </Drawer>

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

/** Rol biriktirish xatosi — ko'pincha rol o'zgargandan keyin eski sessiya (403): qayta kirish kerak. */
function roleErrorMessage(err: unknown): string {
  const status = (err as { response?: { status?: number } })?.response?.status
  if (status === 403 || status === 401)
    return "Rolni biriktirib bo'lmadi: sizning sessiyangizda superadmin huquqi yo'q. Tizimdan chiqib, qayta kiring."
  return "Rolni biriktirib bo'lmadi. Qaytadan urinib ko'ring."
}

/** "2026-09-24T09:46:00" → "24.09.2026 | 09:46"; kirmagan bo'lsa "—". */
function lastSeen(iso: string | null | undefined): string {
  if (!iso) return '—'
  const [date, time = ''] = iso.split('T')
  const [y, m, d] = date.split('-')
  return d && m && y ? `${d}.${m}.${y} | ${time.slice(0, 5)}` : iso
}
