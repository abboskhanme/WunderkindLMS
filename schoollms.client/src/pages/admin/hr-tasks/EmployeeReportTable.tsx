import type { ReactNode } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { ChevronRight } from 'lucide-react'
import type { HrEmployeeReportRow } from '@/types/hrTasks'
import { Card } from '@/components/ui/Card'
import { ProgressBar, Skeleton } from '@/components/hr-tasks/ui'
import { hrAmount, hrFormatNumber } from '@/components/hr-tasks/lib'
import { cn } from '@/lib/utils'

/**
 * "Xodimlar kesimida" — the old panel's table. A table on wide screens (it scrolls inside
 * its own box, never the page), cards on phones. A row opens that employee's tasks for
 * the same period.
 */
export function EmployeeReportTable({
  rows,
  loading,
  dimmed,
  detailed = false,
  linkTo,
  empty,
}: {
  rows: HrEmployeeReportRow[]
  loading: boolean
  /** Old rows stay dimmed while a new period loads. */
  dimmed?: boolean
  /** "Hisobotlar" page — adds the open/returned columns. */
  detailed?: boolean
  linkTo: (row: HrEmployeeReportRow) => string
  empty: ReactNode
}) {
  const navigate = useNavigate()

  if (loading && rows.length === 0) {
    return (
      <div className="space-y-2">
        {Array.from({ length: 4 }, (_, i) => (
          <Skeleton key={i} className="h-12 w-full" />
        ))}
      </div>
    )
  }
  if (rows.length === 0) return <>{empty}</>

  const th = 'px-3 py-3 font-medium'
  const num = 'px-3 py-3 text-right tabular-nums'

  return (
    <div className={cn('transition-opacity', dimmed && 'opacity-60')}>
      <Card className="hidden overflow-hidden p-0 sm:block">
        <div className="overflow-x-auto">
          <table className="w-full min-w-[52rem] text-left text-sm">
            <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
              <tr>
                <th className={cn(th, 'pl-5')}>Xodim</th>
                <th className={cn(th, 'text-right')}>Jami</th>
                <th className={cn(th, 'text-right')}>Bajarildi</th>
                <th className={cn(th, 'text-right')}>Bajarilmadi</th>
                <th className={cn(th, 'text-right')}>Kechikkan</th>
                {detailed && <th className={cn(th, 'text-right')}>Ochiq</th>}
                {detailed && <th className={cn(th, 'text-right')}>Qaytarildi</th>}
                <th className={cn(th, 'w-40')}>Foiz</th>
                <th className={cn(th, 'w-44')}>KPI</th>
                <th className="w-8 px-3 py-3">
                  <span className="sr-only">Ochish</span>
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {rows.map((row) => (
                <tr
                  key={row.employeeId}
                  tabIndex={0}
                  onClick={() => navigate(linkTo(row))}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter') navigate(linkTo(row))
                  }}
                  className="cursor-pointer transition-colors hover:bg-slate-50/70 focus-visible:bg-slate-50 focus-visible:outline-none"
                >
                  <td className="py-3 pl-5 pr-3">
                    <p className="truncate font-medium text-slate-800">{row.fullName}</p>
                    <p className="truncate text-xs text-slate-400">{row.positionName ?? 'Lavozim belgilanmagan'}</p>
                  </td>
                  <td className={cn(num, 'text-slate-600')}>{hrFormatNumber(row.totalTasks)}</td>
                  <td className={cn(num, 'text-slate-600')}>{hrFormatNumber(row.doneCount)}</td>
                  <td className={cn(num, row.missedCount > 0 ? 'text-red-600' : 'text-slate-600')}>
                    {hrFormatNumber(row.missedCount)}
                  </td>
                  <td className={cn(num, row.lateCount > 0 ? 'text-amber-600' : 'text-slate-600')}>
                    {hrFormatNumber(row.lateCount)}
                  </td>
                  {detailed && <td className={cn(num, 'text-slate-600')}>{hrFormatNumber(row.openCount)}</td>}
                  {detailed && <td className={cn(num, 'text-slate-600')}>{hrFormatNumber(row.returnedCount)}</td>}
                  <td className="px-3 py-3">
                    <ProgressBar percent={row.donePercent} showValue />
                  </td>
                  <td className="px-3 py-3">
                    {row.kpiTarget === null || row.kpiTarget === undefined ? (
                      <span className="text-slate-300">—</span>
                    ) : (
                      <ProgressBar
                        percent={row.kpiPercent ?? 0}
                        caption={`${hrFormatNumber(hrAmount(row.kpiCurrent))} / ${hrFormatNumber(hrAmount(row.kpiTarget))}`}
                      />
                    )}
                  </td>
                  <td className="px-3 py-3 text-right">
                    <ChevronRight className="inline h-4 w-4 text-slate-300" />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>

      <ul className="space-y-2 sm:hidden">
        {rows.map((row) => (
          <li key={row.employeeId}>
            <Link to={linkTo(row)} className="block rounded-2xl border border-slate-200/80 bg-white p-3 shadow-sm active:scale-[0.99]">
              <p className="truncate text-sm font-medium text-slate-800">{row.fullName}</p>
              <p className="truncate text-xs text-slate-400">{row.positionName ?? 'Lavozim belgilanmagan'}</p>
              <dl className="mt-2 flex flex-wrap gap-x-3 gap-y-1 text-xs tabular-nums">
                <Metric label="Jami" value={row.totalTasks} />
                <Metric label="Bajarildi" value={row.doneCount} />
                <Metric label="Bajarilmadi" value={row.missedCount} tone={row.missedCount > 0 ? 'text-red-600' : undefined} />
                <Metric label="Kechikkan" value={row.lateCount} tone={row.lateCount > 0 ? 'text-amber-600' : undefined} />
                {detailed && <Metric label="Ochiq" value={row.openCount} />}
                {detailed && <Metric label="Qaytarildi" value={row.returnedCount} />}
              </dl>
              <ProgressBar
                className="mt-2"
                percent={row.donePercent}
                showValue
                caption={
                  row.kpiTarget === null || row.kpiTarget === undefined
                    ? undefined
                    : `KPI ${hrFormatNumber(hrAmount(row.kpiCurrent))} / ${hrFormatNumber(hrAmount(row.kpiTarget))}`
                }
              />
            </Link>
          </li>
        ))}
      </ul>
    </div>
  )
}

function Metric({ label, value, tone }: { label: string; value: number; tone?: string }) {
  return (
    <div className="flex items-baseline gap-1">
      <dt className="text-slate-400">{label}</dt>
      <dd className={cn('font-medium text-slate-800', tone)}>{hrFormatNumber(value)}</dd>
    </div>
  )
}
