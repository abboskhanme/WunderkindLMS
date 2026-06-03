import { useEffect, useMemo, useState } from 'react'
import { Search, Smartphone, CheckCircle2, Circle, ChevronDown } from 'lucide-react'
import type { ParentRow } from '@/types'
import { getParents } from '@/api/services/parents'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

type ActivationFilter = 'all' | 'activated' | 'inactive'

/** "YYYY-MM-DDThh:mm:ss" ni o'qiladigan ko'rinishga keltirish. */
function formatDateTime(iso: string | null): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${pad(d.getDate())}.${pad(d.getMonth() + 1)}.${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/** Hozirgi vaqtdan farqi (masalan "2 kun oldin"). */
function timeAgo(iso: string | null): string {
  if (!iso) return ''
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return ''
  const diff = Date.now() - d.getTime()
  const m = Math.floor(diff / 60000)
  if (m < 1) return 'hozir'
  if (m < 60) return `${m} daqiqa oldin`
  const h = Math.floor(m / 60)
  if (h < 24) return `${h} soat oldin`
  const days = Math.floor(h / 24)
  if (days < 30) return `${days} kun oldin`
  const months = Math.floor(days / 30)
  if (months < 12) return `${months} oy oldin`
  const years = Math.floor(months / 12)
  return `${years} yil oldin`
}

/**
 * Admin "Ilova → Ota-onalar" sahifasi. Telefon bo'yicha guruhlangan ota-onalar:
 * ilova aktivlashtirilganmi, oxirgi marta qachon kirgan, farzandlari ro'yxati.
 */
export function ParentsPage() {
  const [rows, setRows] = useState<ParentRow[]>([])
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [filter, setFilter] = useState<ActivationFilter>('all')
  const [expanded, setExpanded] = useState<Set<string>>(new Set())

  useEffect(() => {
    getParents()
      .then(setRows)
      .finally(() => setLoading(false))
  }, [])

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase()
    return rows.filter((r) => {
      const matchSearch =
        !q ||
        r.fullName.toLowerCase().includes(q) ||
        r.phone.toLowerCase().includes(q) ||
        r.children.some((c) => c.fullName.toLowerCase().includes(q))
      const matchActivation =
        filter === 'all' ||
        (filter === 'activated' ? r.isActivated : !r.isActivated)
      return matchSearch && matchActivation
    })
  }, [rows, search, filter])

  // Statistika: jami / aktivlashtirilgan / aktivlashtirilmagan
  const stats = useMemo(() => {
    const activated = rows.filter((r) => r.isActivated).length
    return { total: rows.length, activated, inactive: rows.length - activated }
  }, [rows])

  const toggleExpand = (key: string) =>
    setExpanded((prev) => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Ota-onalar</h1>
        <p className="text-sm text-slate-400">
          Ilova foydalanuvchilari (ota-onalar) — telefon raqami bo'yicha guruhlangan
        </p>
      </div>

      {/* Statistik kartochkalar */}
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <StatCard label="Jami ota-onalar" value={stats.total} icon={Smartphone} color="slate" />
        <StatCard
          label="Ilovani aktivlashtirgan"
          value={stats.activated}
          icon={CheckCircle2}
          color="emerald"
        />
        <StatCard
          label="Hali aktivlashtirmagan"
          value={stats.inactive}
          icon={Circle}
          color="amber"
        />
      </div>

      <Card className="p-0">
        {/* Filtrlar */}
        <div className="flex flex-wrap items-center gap-3 border-b border-slate-100 p-4">
          <div className="relative flex-1 min-w-[200px]">
            <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Ota-ona, telefon yoki farzand nomi..."
              className={cn(control, 'w-full pl-9')}
            />
          </div>
          <div className="flex gap-1 rounded-lg bg-slate-100 p-1">
            {(['all', 'activated', 'inactive'] as ActivationFilter[]).map((f) => (
              <button
                key={f}
                type="button"
                onClick={() => setFilter(f)}
                className={cn(
                  'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                  f === filter
                    ? 'bg-white text-brand-700 shadow-sm'
                    : 'text-slate-500 hover:text-slate-700',
                )}
              >
                {f === 'all' ? 'Hammasi' : f === 'activated' ? 'Aktiv' : 'Aktiv emas'}
              </button>
            ))}
          </div>
        </div>

        {/* Jadval */}
        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : filtered.length === 0 ? (
          <p className="py-12 text-center text-sm text-slate-400">
            {rows.length === 0 ? "Ota-onalar topilmadi (o'quvchilar hali kiritilmagan)" : 'Filtrga mos natija yo\'q'}
          </p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="w-8 px-2 py-3"></th>
                  <th className="px-4 py-3">Ota-ona F.I.SH</th>
                  <th className="px-4 py-3">Telefon</th>
                  <th className="px-4 py-3">Farzandlar</th>
                  <th className="px-4 py-3">Holat</th>
                  <th className="px-4 py-3">Qurilma</th>
                  <th className="px-4 py-3">Aktivlashtirilgan</th>
                  <th className="px-4 py-3">Oxirgi kirish</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {filtered.map((r, i) => {
                  const key = `${r.phone}-${i}`
                  const isOpen = expanded.has(key)
                  return (
                    <>
                      <tr
                        key={key}
                        className="cursor-pointer hover:bg-slate-50/60"
                        onClick={() => toggleExpand(key)}
                      >
                        <td className="px-2 py-3 text-slate-400">
                          <ChevronDown
                            className={cn(
                              'h-4 w-4 transition-transform',
                              isOpen ? 'rotate-180' : '',
                            )}
                          />
                        </td>
                        <td className="px-4 py-3 font-medium text-slate-800">
                          {r.fullName || <span className="text-slate-400">—</span>}
                        </td>
                        <td className="px-4 py-3 text-slate-600">{r.phone || '—'}</td>
                        <td className="px-4 py-3">
                          <span className="rounded-md bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
                            {r.childrenCount}{' '}
                            {r.childrenCount === 1 ? 'farzand' : 'farzand'}
                          </span>
                        </td>
                        <td className="px-4 py-3">
                          {r.isActivated ? (
                            <span className="inline-flex items-center gap-1 rounded-md bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-700">
                              <CheckCircle2 className="h-3.5 w-3.5" /> Aktiv
                            </span>
                          ) : (
                            <span className="inline-flex items-center gap-1 rounded-md bg-amber-50 px-2 py-0.5 text-xs font-medium text-amber-700">
                              <Circle className="h-3.5 w-3.5" /> Kirmagan
                            </span>
                          )}
                        </td>
                        <td className="px-4 py-3 text-sm text-slate-600">
                          {r.deviceName ? (
                            <span className="inline-flex items-center gap-1">
                              <Smartphone className="h-3.5 w-3.5 text-slate-400" />
                              {r.deviceName}
                              {r.platform && (
                                <span className="text-[11px] text-slate-400">({r.platform})</span>
                              )}
                            </span>
                          ) : (
                            '—'
                          )}
                        </td>
                        <td className="px-4 py-3 text-sm text-slate-600">
                          {formatDateTime(r.activatedAt)}
                        </td>
                        <td className="px-4 py-3">
                          <div className="text-sm text-slate-700">{formatDateTime(r.lastSeenAt)}</div>
                          {r.lastSeenAt && (
                            <div className="text-[11px] text-slate-400">{timeAgo(r.lastSeenAt)}</div>
                          )}
                        </td>
                      </tr>
                      {isOpen && (
                        <tr key={`${key}-detail`} className="bg-slate-50/40">
                          <td colSpan={8} className="px-4 py-3">
                            <div className="rounded-lg border border-slate-200 bg-white">
                              <table className="w-full text-sm">
                                <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                                  <tr>
                                    <th className="px-3 py-2 text-left">Farzand</th>
                                    <th className="px-3 py-2 text-left">Sinf</th>
                                    <th className="px-3 py-2 text-left">Birinchi kirish</th>
                                    <th className="px-3 py-2 text-left">Oxirgi kirish</th>
                                    <th className="px-3 py-2 text-left">Qurilma</th>
                                    <th className="px-3 py-2 text-left">App ID</th>
                                  </tr>
                                </thead>
                                <tbody>
                                  {r.children.map((c) => (
                                    <tr key={c.studentId} className="border-t border-slate-100">
                                      <td className="px-3 py-2 font-medium text-slate-700">{c.fullName}</td>
                                      <td className="px-3 py-2 text-slate-600">{c.className}</td>
                                      <td className="px-3 py-2 text-slate-600">{formatDateTime(c.firstLoginAt)}</td>
                                      <td className="px-3 py-2 text-slate-600">{formatDateTime(c.lastLoginAt)}</td>
                                      <td className="px-3 py-2 text-slate-600">
                                        {c.deviceName || '—'}
                                        {c.platform ? ` (${c.platform})` : ''}
                                      </td>
                                      <td className="px-3 py-2 text-slate-500">
                                        <code className="text-xs">{c.appId || '—'}</code>
                                      </td>
                                    </tr>
                                  ))}
                                </tbody>
                              </table>
                            </div>
                          </td>
                        </tr>
                      )}
                    </>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </div>
  )
}

function StatCard({
  label,
  value,
  icon: Icon,
  color,
}: {
  label: string
  value: number
  icon: typeof Smartphone
  color: 'slate' | 'emerald' | 'amber'
}) {
  const colors = {
    slate: 'bg-slate-100 text-slate-600',
    emerald: 'bg-emerald-100 text-emerald-600',
    amber: 'bg-amber-100 text-amber-600',
  }[color]
  return (
    <Card>
      <div className="flex items-center gap-3">
        <div className={cn('flex h-10 w-10 items-center justify-center rounded-lg', colors)}>
          <Icon className="h-5 w-5" />
        </div>
        <div>
          <p className="text-xs uppercase tracking-wide text-slate-400">{label}</p>
          <p className="text-2xl font-semibold text-slate-800">{value}</p>
        </div>
      </div>
    </Card>
  )
}
