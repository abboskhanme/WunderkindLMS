import { Link } from 'react-router-dom'
import { ArrowRight } from 'lucide-react'
import { getStudentFlow } from '@/api/services/studentFlow'
import { useAsync } from '@/hooks/useAsync'
import { Card } from '@/components/ui/Card'
import { cn } from '@/lib/utils'
import type { DashboardPeriod } from './dashboardPeriod'

/**
 * Bosh sahifa → "Kontingent" vidjeti: O'quvchilar oqimi (docs/modules/student-flow.md). The page's
 * period bar decides the dates; the link opens the full report with the same dates.
 */
export function StudentFlowCard({ period }: { period: DashboardPeriod }) {
  const { data, loading, error } = useAsync(() => getStudentFlow(period.from, period.to), [period.from, period.to])
  const t = data?.totals
  const reasons = (data?.byReason ?? []).slice(0, 3)
  const top = Math.max(1, ...reasons.map((r) => r.count))

  return (
    <Card>
      <div className="flex items-start justify-between gap-3">
        <div>
          <h2 className="font-semibold text-slate-800">O'quvchilar oqimi</h2>
          <p className="text-xs text-slate-400">{period.label}</p>
        </div>
        <Link
          to={`/admin/students/oqim?from=${period.from}&to=${period.to}`}
          className="flex items-center gap-1 text-xs font-medium text-brand-700 hover:underline"
        >
          Batafsil <ArrowRight className="h-3.5 w-3.5" />
        </Link>
      </div>
      <div className="mt-3">
        {loading ? (
          <p className="py-6 text-center text-sm text-slate-400">Yuklanmoqda...</p>
        ) : error ? (
          <p className="py-6 text-center text-sm text-red-600">{error}</p>
        ) : t ? (
          <>
            <div className="grid grid-cols-3 gap-2">
              <Figure label="Yangi" value={t.new} tone="text-emerald-600" />
              <Figure label="Faollashgan" value={t.activated} tone="text-violet-600" />
              <Figure label="Ketgan" value={t.left} tone="text-rose-600" />
            </div>
            <p className="mt-2 text-xs text-slate-400">
              Arxivdan qaytgan: {t.returned} · sof o'zgarish: {t.net > 0 ? `+${t.net}` : t.net}
            </p>
            <h3 className="mt-4 text-xs font-medium uppercase tracking-wide text-slate-400">Ketish sabablari</h3>
            {reasons.length === 0 ? (
              <p className="py-3 text-sm text-slate-400">Bu davrda ketgan o'quvchi yo'q.</p>
            ) : (
              <ul className="mt-1">
                {reasons.map((r) => (
                  <li key={r.name} className="py-1.5">
                    <div className="flex items-baseline justify-between gap-3 text-sm">
                      <span className="truncate text-slate-600" title={r.name}>
                        {r.name}
                      </span>
                      <span className="shrink-0 font-medium tabular-nums text-slate-800">
                        {r.count} <span className="text-xs font-normal text-slate-400">· {r.percent}%</span>
                      </span>
                    </div>
                    <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-slate-100">
                      <div className="h-full rounded-full bg-rose-400" style={{ width: `${Math.round((r.count / top) * 100)}%` }} />
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </>
        ) : null}
      </div>
    </Card>
  )
}

function Figure({ label, value, tone }: { label: string; value: number; tone?: string }) {
  return (
    <div className="min-w-0 rounded-xl bg-slate-50 px-3 py-2">
      <p className="truncate text-xs text-slate-500">{label}</p>
      <p className={cn('truncate text-base font-semibold tabular-nums text-slate-800', tone)}>{value}</p>
    </div>
  )
}
