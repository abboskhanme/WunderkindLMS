import { useEffect, useMemo, useState } from 'react'
import { Search, Smartphone, CheckCircle2, Circle } from 'lucide-react'
import type { TeacherAppRow } from '@/types'
import { getTeacherAppUsers } from '@/api/services/parents'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

type ActivationFilter = 'all' | 'activated' | 'inactive'

function formatDateTime(iso: string | null): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${pad(d.getDate())}.${pad(d.getMonth() + 1)}.${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}

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
  return `${Math.floor(months / 12)} yil oldin`
}

/** Admin "Ilova → O'qituvchilar" — o'qituvchilar ilova faolligi + qurilmasi (Ota-onalarga o'xshash). */
export function TeacherAppPage() {
  const [rows, setRows] = useState<TeacherAppRow[]>([])
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [filter, setFilter] = useState<ActivationFilter>('all')

  useEffect(() => {
    getTeacherAppUsers()
      .then(setRows)
      .finally(() => setLoading(false))
  }, [])

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase()
    return rows.filter((r) => {
      const matchSearch =
        !q || r.fullName.toLowerCase().includes(q) || r.phone.toLowerCase().includes(q)
      const matchActivation =
        filter === 'all' || (filter === 'activated' ? r.isActivated : !r.isActivated)
      return matchSearch && matchActivation
    })
  }, [rows, search, filter])

  const stats = useMemo(() => {
    const activated = rows.filter((r) => r.isActivated).length
    return { total: rows.length, activated, inactive: rows.length - activated }
  }, [rows])

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">O'qituvchilar</h1>
        <p className="text-sm text-slate-400">
          Ilova foydalanuvchilari (o'qituvchilar) — faollik va qurilma ma'lumoti
        </p>
      </div>

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <StatCard label="Jami o'qituvchilar" value={stats.total} icon={Smartphone} color="slate" />
        <StatCard label="Ilovani aktivlashtirgan" value={stats.activated} icon={CheckCircle2} color="emerald" />
        <StatCard label="Hali aktivlashtirmagan" value={stats.inactive} icon={Circle} color="amber" />
      </div>

      <Card className="p-0">
        <div className="flex flex-wrap items-center gap-3 border-b border-slate-100 p-4">
          <div className="relative flex-1 min-w-[200px]">
            <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="O'qituvchi yoki telefon..."
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
                  f === filter ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500 hover:text-slate-700',
                )}
              >
                {f === 'all' ? 'Hammasi' : f === 'activated' ? 'Aktiv' : 'Aktiv emas'}
              </button>
            ))}
          </div>
        </div>

        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : filtered.length === 0 ? (
          <p className="py-12 text-center text-sm text-slate-400">
            {rows.length === 0 ? "O'qituvchilar topilmadi" : "Filtrga mos natija yo'q"}
          </p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">F.I.SH</th>
                  <th className="px-4 py-3">Telefon</th>
                  <th className="px-4 py-3">Holat</th>
                  <th className="px-4 py-3">Qurilma</th>
                  <th className="px-4 py-3">Aktivlashtirilgan</th>
                  <th className="px-4 py-3">Oxirgi kirish</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {filtered.map((r) => (
                  <tr key={r.teacherId} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3 font-medium text-slate-800">{r.fullName}</td>
                    <td className="px-4 py-3 text-slate-600">{r.phone || '—'}</td>
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
                    <td className="px-4 py-3 text-slate-600">
                      {r.deviceName ? (
                        <span className="inline-flex items-center gap-1">
                          <Smartphone className="h-3.5 w-3.5 text-slate-400" />
                          {r.deviceName}
                          {r.platform && <span className="text-[11px] text-slate-400">({r.platform})</span>}
                        </span>
                      ) : (
                        '—'
                      )}
                    </td>
                    <td className="px-4 py-3 text-slate-600">{formatDateTime(r.activatedAt)}</td>
                    <td className="px-4 py-3">
                      <div className="text-sm text-slate-700">{formatDateTime(r.lastSeenAt)}</div>
                      {r.lastSeenAt && <div className="text-[11px] text-slate-400">{timeAgo(r.lastSeenAt)}</div>}
                    </td>
                  </tr>
                ))}
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
