import { useEffect, useState } from 'react'
import { Check } from 'lucide-react'
import type { Staff } from '@/types'
import { getStaff, setStaffPermissions } from '@/api/services/staff'
import { adminPermissions } from '@/config/constants'
import { cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'

export function RolesPage() {
  const [staff, setStaff] = useState<Staff[]>([])
  const [loading, setLoading] = useState(true)
  // Tahrirlanayotgan ruxsatlar (xodim id → kalitlar to'plami)
  const [draft, setDraft] = useState<Record<string, Set<string>>>({})
  const [savingId, setSavingId] = useState<string | null>(null)

  useEffect(() => {
    getStaff()
      .then((list) => {
        setStaff(list)
        setDraft(Object.fromEntries(list.map((s) => [s.id, new Set(s.permissions)])))
      })
      .finally(() => setLoading(false))
  }, [])

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

  const save = (s: Staff) => {
    const perms = [...(draft[s.id] ?? new Set())]
    setSavingId(s.id)
    setStaffPermissions(s.id, perms)
      .then((u) => setStaff((p) => p.map((x) => (x.id === u.id ? u : x))))
      .finally(() => setSavingId(null))
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Rollar</h1>
        <p className="text-sm text-slate-400">
          Har bir xodim qaysi admin bo'limlarini ko'rishini belgilang (faqat tizim egasi)
        </p>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : staff.length === 0 ? (
        <Card>
          <p className="py-10 text-center text-slate-400">
            Hali xodim yo'q. Avval "Xodimlar" bo'limida xodim qo'shing.
          </p>
        </Card>
      ) : (
        <div className="space-y-4">
          {staff.map((s) => {
            const cur = draft[s.id] ?? new Set<string>()
            return (
              <Card key={s.id}>
                <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
                  <div>
                    <p className="font-semibold text-slate-800">{s.fullName}</p>
                    <p className="text-xs text-slate-400">{s.position || 'Xodim'} · {s.login}</p>
                  </div>
                  <Button onClick={() => save(s)} disabled={!dirty(s) || savingId === s.id}>
                    {savingId === s.id ? 'Saqlanmoqda...' : 'Saqlash'}
                  </Button>
                </div>
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
              </Card>
            )
          })}
        </div>
      )}
    </div>
  )
}
