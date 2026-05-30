import { useEffect, useState } from 'react'
import { Plus, Eye, Pencil, Trash2 } from 'lucide-react'
import type { Staff, Credentials } from '@/types'
import {
  getStaff,
  createStaff,
  updateStaff,
  deleteStaff,
  getStaffCredentials,
  type StaffPayload,
} from '@/api/services/staff'
import { cn, randomPassword } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Input } from '@/components/ui/Input'
import { CredentialsBox } from '@/components/ui/CredentialsBox'

const POSITIONS = ['Kassir', 'Administrator', "Direktor o'rinbosari", 'Qorovul', 'Hisobchi']

export function StaffPage() {
  const [staff, setStaff] = useState<Staff[]>([])
  const [loading, setLoading] = useState(true)
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<Staff | null>(null)
  const [form, setForm] = useState<StaffPayload>({ fullName: '', position: '' })

  // Login/parol oynasi
  const [credOf, setCredOf] = useState<Staff | null>(null)
  const [creds, setCreds] = useState<Credentials | null>(null)
  const [credLoading, setCredLoading] = useState(false)

  useEffect(() => {
    getStaff()
      .then(setStaff)
      .finally(() => setLoading(false))
  }, [])

  const openCreate = () => {
    setEditing(null)
    setForm({ fullName: '', position: '' })
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

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!form.fullName.trim()) return
    if (editing) {
      updateStaff(editing.id, form).then((u) => setStaff((p) => p.map((x) => (x.id === u.id ? u : x))))
      setFormOpen(false)
    } else {
      createStaff(form).then((c) => {
        setStaff((p) => [c, ...p])
        setFormOpen(false)
        showCredentials(c) // yangi xodim login/parolini darrov ko'rsatamiz
      })
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
            O'qituvchi bo'lmagan ishchilar (kassir, administrator, ...). Ruxsatlar — Rollar bo'limida.
          </p>
        </div>
        <Button onClick={openCreate}>
          <Plus className="h-4 w-4" /> Yangi xodim
        </Button>
      </div>

      <Card className="p-0">
        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="w-10 px-4 py-3">#</th>
                  <th className="px-4 py-3">F.I.SH</th>
                  <th className="px-4 py-3">Lavozim</th>
                  <th className="px-4 py-3">Login</th>
                  <th className="px-4 py-3 text-right">Amallar</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {staff.map((s, i) => (
                  <tr key={s.id} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3 text-slate-400">{i + 1}</td>
                    <td className="px-4 py-3 font-medium text-slate-800">{s.fullName}</td>
                    <td className="px-4 py-3 text-slate-600">{s.position || '—'}</td>
                    <td className="px-4 py-3"><code className="text-slate-600">{s.login}</code></td>
                    <td className="px-4 py-3">
                      <div className="flex items-center justify-end gap-0.5">
                        <IconBtn icon={Eye} title="Login/parol" onClick={() => showCredentials(s)} />
                        <IconBtn icon={Pencil} title="Tahrirlash" onClick={() => openEdit(s)} />
                        <IconBtn icon={Trash2} title="O'chirish" danger onClick={() => handleDelete(s)} />
                      </div>
                    </td>
                  </tr>
                ))}
                {staff.length === 0 && (
                  <tr>
                    <td colSpan={5} className="px-4 py-12 text-center text-slate-400">
                      Hali xodim qo'shilmagan
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )}
      </Card>

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
            <Button type="submit" form="staff-form">
              Saqlash
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
          {!editing && (
            <p className="text-xs text-slate-400">
              Saqlangach tizimga kirish uchun login va parol avtomatik yaratiladi va ko'rsatiladi.
              Ruxsatlar (bo'limlar) Rollar bo'limida belgilanadi.
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
        <CredentialsBox credentials={creds} loading={credLoading} />
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
