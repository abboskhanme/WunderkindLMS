import { useEffect, useState } from 'react'
import { ChevronDown, ChevronRight, Pencil, Plus, Trash2 } from 'lucide-react'
import type { AccessRole } from '@/types'
import {
  createAccessRole,
  deleteAccessRole,
  getAccessRoles,
  updateAccessRole,
} from '@/api/services/accessRoles'
import { ACCESS_CATALOG, ACCESS_PAGES, AI_ACCESS_KEY, encodeGrants, grantsOf, type AccessLevel } from '@/lib/access'
import { useAuth } from '@/context/auth-context'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Drawer } from '@/components/ui/Drawer'
import { Input, Textarea } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'

const ALL_PAGES = ACCESS_PAGES.map((p) => p.key)

const LEVELS: { value: AccessLevel; label: string }[] = [
  { value: 'none', label: "Yo'q" },
  { value: 'view', label: "Ko'rish" },
  { value: 'edit', label: "To'liq" },
]

/** Bir nechta sahifaning umumiy darajasi; har xil bo'lsa — null ("aralash"). */
function commonLevel(grants: Map<string, AccessLevel>, keys: string[]): AccessLevel | null {
  const levels = new Set(keys.map((k) => grants.get(k) ?? 'none'))
  return levels.size === 1 ? [...levels][0] : null
}

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
  const [grants, setGrants] = useState<Map<string, AccessLevel>>(new Map())
  const [aiAccess, setAiAccess] = useState(false)
  const [expanded, setExpanded] = useState<Set<string>>(new Set())
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
    setGrants(grantsOf(role?.permissions ?? []))
    setAiAccess((role?.permissions ?? []).includes(AI_ACCESS_KEY))
    setExpanded(new Set())
    setFormError(null)
    setOpen(true)
  }

  /** Bir yoki bir nechta sahifaga daraja qo'yish (menyu qatori — hamma sahifasiga). */
  const setLevel = (keys: string[], level: AccessLevel) =>
    setGrants((cur) => {
      const next = new Map(cur)
      for (const k of keys) {
        if (level === 'none') next.delete(k)
        else next.set(k, level)
      }
      return next
    })

  const toggleExpanded = (label: string) =>
    setExpanded((cur) => {
      const next = new Set(cur)
      if (next.has(label)) next.delete(label)
      else next.add(label)
      return next
    })

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!name.trim() || saving) return
    setSaving(true)
    setFormError(null)
    // Tartib — menyudagidek, tanlash tartibi emas.
    const payload = {
      name: name.trim(),
      description: description.trim(),
      // Sahifa darajalari + ulardan hosil bo'ladigan server bo'lim kalitlari (lib/access.ts).
      // + AI ulanish belgisi (menyu sahifasi emas — alohida kalit, lib/access.ts AI_ACCESS_KEY).
      permissions: [...encodeGrants(grants), ...(aiAccess ? [AI_ACCESS_KEY] : [])],
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
                      {rolesSummary(r.permissions)}
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

          <div className="flex items-center justify-between gap-3 rounded-xl border border-slate-200 bg-slate-50 px-3 py-2.5">
            <span className="text-sm font-medium text-slate-700">Barchasi</span>
            <LevelPicker value={commonLevel(grants, ALL_PAGES)} onChange={(l) => setLevel(ALL_PAGES, l)} />
          </div>

          <div className="space-y-2">
            {ACCESS_CATALOG.map((menu) => {
              const keys = menu.pages.map((p) => p.key)
              const single = menu.pages.length === 1 && menu.pages[0].label === menu.label
              const isOpen = expanded.has(menu.label)
              return (
                <section key={menu.label} className="rounded-xl border border-slate-200">
                  <div className="flex items-center justify-between gap-3 px-3 py-2.5">
                    {single ? (
                      <span className="text-sm font-medium text-slate-700">{menu.label}</span>
                    ) : (
                      <button
                        type="button"
                        onClick={() => toggleExpanded(menu.label)}
                        className="flex min-w-0 items-center gap-1.5 text-left text-sm font-medium text-slate-700"
                      >
                        {isOpen ? <ChevronDown className="h-4 w-4 shrink-0" /> : <ChevronRight className="h-4 w-4 shrink-0" />}
                        <span className="truncate">{menu.label}</span>
                        <span className="shrink-0 text-xs font-normal text-slate-400">{menu.pages.length}</span>
                      </button>
                    )}
                    <LevelPicker value={commonLevel(grants, keys)} onChange={(l) => setLevel(keys, l)} />
                  </div>
                  {!single && isOpen && (
                    <div className="space-y-1 border-t border-slate-100 px-3 py-2">
                      {menu.pages.map((p) => (
                        <div key={p.key} className="flex items-center justify-between gap-3 py-1 pl-5">
                          <span className="min-w-0 truncate text-sm text-slate-600">{p.label}</span>
                          <LevelPicker
                            value={grants.get(p.key) ?? 'none'}
                            onChange={(l) => setLevel([p.key], l)}
                            small
                          />
                        </div>
                      ))}
                    </div>
                  )}
                </section>
              )
            })}
          </div>
          <div className="flex items-start justify-between gap-3 rounded-xl border border-slate-200 px-3 py-2.5">
            <div className="min-w-0">
              <p className="text-sm font-medium text-slate-700">AI ulanish</p>
              <p className="mt-0.5 text-xs text-slate-400">
                ChatGPT, Claude kabi AI yordamchilarga maktab ma'lumotlarini faqat o'qish uchun ulashga ruxsat.
                AI faqat shu rolda ochilgan bo'limlarni ko'radi va hech narsani o'zgartira olmaydi.
              </p>
            </div>
            <button
              type="button"
              role="switch"
              aria-checked={aiAccess}
              aria-label="AI ulanish"
              onClick={() => setAiAccess((v) => !v)}
              className={cn(
                'relative mt-0.5 inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors',
                aiAccess ? 'bg-brand-500' : 'bg-slate-200',
              )}
            >
              <span
                className={cn(
                  'inline-block h-5 w-5 rounded-full bg-white shadow-sm transition-transform',
                  aiAccess ? 'translate-x-[22px]' : 'translate-x-0.5',
                )}
              />
            </button>
          </div>

          <p className="text-xs text-slate-400">
            Ko'rish — sahifa va uning barcha ma'lumoti ko'rinadi, o'zgartirib bo'lmaydi. To'liq — qo'shish,
            o'zgartirish va o'chirish ham. Chegirma, chiqim va qaytarimni tasdiqlash — faqat direktor.
          </p>

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

/** Rollar jadvalidagi qisqa yozuv: nechta sahifa, shundan nechtasi faqat ko'rish. */
function rolesSummary(permissions: string[]): string {
  const grants = grantsOf(permissions)
  const ai = permissions.includes(AI_ACCESS_KEY) ? ' · AI ulanish' : ''
  if (grants.size === 0) return ai ? 'AI ulanish' : '—'
  const view = [...grants.values()].filter((l) => l === 'view').length
  const total = grants.size === ALL_PAGES.length ? 'Barcha sahifa' : `${grants.size} ta sahifa`
  return (view > 0 ? `${total} (${view} tasi faqat ko'rish)` : total) + ai
}

/** Uch holatli tanlov: Yo'q / Ko'rish / To'liq. `null` — ichidagi sahifalar har xil. */
function LevelPicker({
  value,
  onChange,
  small,
}: {
  value: AccessLevel | null
  onChange: (level: AccessLevel) => void
  small?: boolean
}) {
  return (
    <div className="inline-flex shrink-0 rounded-lg bg-slate-100 p-0.5" role="radiogroup">
      {LEVELS.map((l) => {
        const active = value === l.value
        return (
          <button
            key={l.value}
            type="button"
            role="radio"
            aria-checked={active}
            onClick={() => onChange(l.value)}
            className={cn(
              'rounded-md font-medium transition-colors',
              small ? 'px-2 py-0.5 text-xs' : 'px-2.5 py-1 text-xs',
              active
                ? l.value === 'none'
                  ? 'bg-white text-slate-700 shadow-sm'
                  : l.value === 'view'
                    ? 'bg-white text-amber-700 shadow-sm'
                    : 'bg-white text-brand-700 shadow-sm'
                : 'text-slate-500 hover:text-slate-700',
            )}
          >
            {l.label}
          </button>
        )
      })}
    </div>
  )
}
