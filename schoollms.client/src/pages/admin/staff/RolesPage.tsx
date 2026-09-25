import { useEffect, useState } from 'react'
import { Check, Pencil, Plus, Trash2 } from 'lucide-react'
import type { AccessRole } from '@/types'
import {
  createAccessRole,
  deleteAccessRole,
  getAccessRoles,
  updateAccessRole,
} from '@/api/services/accessRoles'
import { adminPermissionGroups, adminPermissions } from '@/config/constants'
import { useAuth } from '@/context/auth-context'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Drawer } from '@/components/ui/Drawer'
import { Input, Textarea } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'

const labelOf = (key: string) => adminPermissions.find((p) => p.key === key)?.label ?? key
const ALL_KEYS = adminPermissions.map((p) => p.key)

const errorText = (e: unknown, fallback: string) =>
  (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback

/**
 * Boshqaruv → Rollar. Ruxsatlar ROLGA beriladi; xodimga "Xodimlar" sahifasida
 * faqat rol biriktiriladi. Rolni saqlash uning barcha xodimlariga darrov ta'sir
 * qiladi (server a'zolarning ruxsatlarini shu saqlashda yangilaydi).
 *
 * Ko'rish — "Xodimlar" bo'limiga kira oladigan har kimga; yaratish, tahrirlash
 * va o'chirish — faqat superadmin (server ham shuni talab qiladi).
 */
export function RolesPage() {
  const { user } = useAuth()
  const canManage = user?.role === 'superadmin'

  const [roles, setRoles] = useState<AccessRole[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [open, setOpen] = useState(false)
  const [editing, setEditing] = useState<AccessRole | null>(null)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [perms, setPerms] = useState<Set<string>>(new Set())
  const [saving, setSaving] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

  useEffect(() => {
    getAccessRoles()
      .then(setRoles)
      .catch((e: unknown) => setError(errorText(e, "Rollarni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [])

  const openForm = (role: AccessRole | null) => {
    setEditing(role)
    setName(role?.name ?? '')
    setDescription(role?.description ?? '')
    setPerms(new Set(role?.permissions ?? []))
    setFormError(null)
    setOpen(true)
  }

  const toggle = (keys: string[], on: boolean) =>
    setPerms((cur) => {
      const next = new Set(cur)
      for (const k of keys) {
        if (on) next.add(k)
        else next.delete(k)
      }
      return next
    })

  const allOn = ALL_KEYS.every((k) => perms.has(k))

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!name.trim() || saving) return
    setSaving(true)
    setFormError(null)
    // Tartib — menyudagidek, tanlash tartibi emas.
    const payload = {
      name: name.trim(),
      description: description.trim(),
      permissions: ALL_KEYS.filter((k) => perms.has(k)),
    }
    try {
      const saved = editing ? await updateAccessRole(editing.id, payload) : await createAccessRole(payload)
      setRoles((list) =>
        (editing ? list.map((r) => (r.id === saved.id ? saved : r)) : [...list, saved]).sort((a, b) =>
          a.name.localeCompare(b.name, 'uz'),
        ),
      )
      setOpen(false)
    } catch (err) {
      setFormError(errorText(err, "Rolni saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  const handleDelete = (role: AccessRole) => {
    if (!confirm(`"${role.name}" rolini o'chirasizmi?`)) return
    deleteAccessRole(role.id)
      .then(() => setRoles((list) => list.filter((r) => r.id !== role.id)))
      .catch((e: unknown) => alert(errorText(e, "Rolni o'chirib bo'lmadi")))
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Rollar</h1>
          <p className="text-sm text-slate-400">
            Ruxsatlar rolga beriladi — xodimga "Xodimlar" sahifasida faqat rol biriktiriladi.
          </p>
        </div>
        {canManage && (
          <Button onClick={() => openForm(null)}>
            <Plus className="h-4 w-4" /> Rol qo'shish
          </Button>
        )}
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : error ? (
        <Card>
          <p className="py-12 text-center text-sm text-red-600">{error}</p>
        </Card>
      ) : roles.length === 0 ? (
        <Card>
          <p className="py-12 text-center text-slate-400">
            Hali rol yo'q.{canManage ? ' "Rol qo\'shish" tugmasi orqali birinchisini yarating.' : ''}
          </p>
        </Card>
      ) : (
        <Card className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="w-12 px-4 py-3">№</th>
                  <th className="px-4 py-3">Nomi</th>
                  <th className="px-4 py-3">Tavsif</th>
                  <th className="px-4 py-3">Ruxsatlar</th>
                  <th className="px-4 py-3 text-right">Xodimlar soni</th>
                  {canManage && <th className="px-4 py-3 text-right">Amallar</th>}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {roles.map((r, i) => (
                  <tr key={r.id} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3 text-slate-400">{i + 1}</td>
                    <td className="px-4 py-3 font-medium text-slate-800">{r.name}</td>
                    <td className="px-4 py-3 text-slate-600">{r.description || '—'}</td>
                    <td className="px-4 py-3 text-slate-500">
                      {r.permissions.length === ALL_KEYS.length
                        ? 'Barchasi'
                        : r.permissions.length === 0
                          ? '—'
                          : `${r.permissions.length} ta bo'lim`}
                    </td>
                    <td className="px-4 py-3 text-right tabular-nums text-slate-700">{r.staffCount}</td>
                    {canManage && (
                      <td className="px-4 py-3">
                        <span className="flex justify-end gap-0.5">
                          <button
                            type="button"
                            title="Tahrirlash"
                            onClick={() => openForm(r)}
                            className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                          >
                            <Pencil className="h-4 w-4" />
                          </button>
                          <button
                            type="button"
                            title={r.staffCount > 0 ? 'Rolda xodimlar bor' : "O'chirish"}
                            onClick={() => handleDelete(r)}
                            disabled={r.staffCount > 0}
                            className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600 disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:bg-transparent disabled:hover:text-slate-400"
                          >
                            <Trash2 className="h-4 w-4" />
                          </button>
                        </span>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>
      )}

      <Drawer
        open={open}
        onClose={() => setOpen(false)}
        title={editing ? 'Rolni tahrirlash' : "Rol qo'shish"}
        footer={
          <>
            <Button variant="secondary" onClick={() => setOpen(false)}>
              Ortga
            </Button>
            <Button type="submit" form="role-form" disabled={!name.trim() || saving}>
              {saving ? 'Saqlanmoqda...' : 'Saqlash'}
            </Button>
          </>
        }
      >
        <form id="role-form" onSubmit={handleSave} className="space-y-4">
          <Input label="Rol nomi" required value={name} onChange={(e) => setName(e.target.value)} />
          <Textarea label="Izoh" rows={3} value={description} onChange={(e) => setDescription(e.target.value)} />

          <PermCheck label="Barchasi" checked={allOn} onChange={(on) => toggle(ALL_KEYS, on)} strong />

          {adminPermissionGroups.map((g) => {
            const groupOn = g.keys.every((k) => perms.has(k))
            return (
              <section key={g.label} className="space-y-2">
                <div className="flex items-center justify-between">
                  <h4 className="text-xs font-semibold uppercase tracking-wide text-slate-400">{g.label}</h4>
                  <button
                    type="button"
                    onClick={() => toggle(g.keys, !groupOn)}
                    className="text-xs font-medium text-brand-600 hover:text-brand-700"
                  >
                    {groupOn ? 'Hammasini olish' : 'Hammasini tanlash'}
                  </button>
                </div>
                {g.keys.map((k) => (
                  <PermCheck key={k} label={labelOf(k)} checked={perms.has(k)} onChange={(on) => toggle([k], on)} />
                ))}
              </section>
            )
          })}

          {editing && editing.staffCount > 0 && (
            <p className="text-xs text-slate-400">
              O'zgarish shu roldagi {editing.staffCount} ta xodimga darrov ta'sir qiladi.
            </p>
          )}
          {formError && <p className="text-sm text-red-600">{formError}</p>}
        </form>
      </Drawer>
    </div>
  )
}

function PermCheck({
  label,
  checked,
  onChange,
  strong,
}: {
  label: string
  checked: boolean
  onChange: (on: boolean) => void
  strong?: boolean
}) {
  return (
    <button
      type="button"
      role="checkbox"
      aria-checked={checked}
      onClick={() => onChange(!checked)}
      className={cn(
        'flex w-full items-center gap-3 rounded-xl border px-3 py-2.5 text-left text-sm transition-colors',
        checked ? 'border-brand-200 bg-brand-50/60' : 'border-slate-200 hover:bg-slate-50',
        strong && 'font-medium',
      )}
    >
      <span
        className={cn(
          'flex h-5 w-5 shrink-0 items-center justify-center rounded-md border transition-colors',
          checked ? 'border-brand-600 bg-brand-600 text-white' : 'border-slate-300 bg-white',
        )}
      >
        {checked && <Check className="h-3.5 w-3.5" />}
      </span>
      <span className="text-slate-700">{label}</span>
    </button>
  )
}
