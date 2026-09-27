import { useState } from 'react'
import { Link } from 'react-router-dom'
import { AlertTriangle, BellRing, CheckCircle2, ClipboardList, Download, Repeat, XCircle } from 'lucide-react'
import { downloadReportCsv, getEmployeeReport, getSummaryReport, getTasks, hrErrorMessage } from '@/api/services/hrTasks'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { EmptyState, Notice, PageHeader, SectionTitle, Skeleton, StatTile, primaryLinkCls, secondaryLinkCls } from '@/components/hr-tasks/ui'
import { useToast } from '@/components/hr-tasks/hooks'
import { useLoad } from '@/components/hr-tasks/useLoad'
import {
  HR_PERIOD_LABEL,
  hrFormatNumber,
  hrFormatPercent,
  hrFormatRange,
  hrFormatTime,
  hrIsOverdue,
  hrOverdueLabel,
} from '@/components/hr-tasks/lib'
import { EmployeeReportTable } from './EmployeeReportTable'
import { PeriodCard } from './PeriodCard'
import { usePeriodRange } from './period'
import { hrtPaths, hrtTasksLink } from './paths'

/** Row count of the "needs attention" and "self-created" lists. */
const SHORT_LIST_LIMIT = 10

/**
 * Dashboard — the old `HrDashboardPage` (period + counters + per-employee table +
 * "today" blocks). The period is in the URL, default "Bugun".
 */
export function HrDashboardPage() {
  const period = usePeriodRange()
  const toast = useToast()
  const [exporting, setExporting] = useState(false)
  const { range, valid } = period

  const key = valid ? `${range.dateFrom}|${range.dateTo}` : null
  const summary = useLoad(key && `summary|${key}`, () => getSummaryReport(range))
  const report = useLoad(key && `employees|${key}`, () => getEmployeeReport(range))

  const stats = summary.data
  const rows = report.data?.items ?? report.previous?.items ?? []
  const isToday = !period.isCustom && period.preset === 'today'

  const tasksLink = (employeeId?: string, extra: Record<string, string> = {}) =>
    hrtTasksLink({
      employeeId,
      ...(range.dateFrom && range.dateTo ? { dateFrom: range.dateFrom, dateTo: range.dateTo } : {}),
      ...extra,
    })

  async function handleExport(): Promise<void> {
    if (!valid) return
    setExporting(true)
    try {
      await downloadReportCsv(range)
      toast.success('CSV yuklab olindi')
    } catch (err) {
      toast.error(hrErrorMessage(err))
    } finally {
      setExporting(false)
    }
  }

  const reportEmpty = report.error ? (
    <Card>
      <EmptyState
        title="Hisobotni yuklab bo‘lmadi"
        hint={report.error}
        action={
          <Button variant="secondary" onClick={report.reload}>
            Qayta urinish
          </Button>
        }
      />
    </Card>
  ) : (
    <Card>
      <EmptyState
        title="Bu davrda topshiriq yo‘q"
        hint="Har kuni takrorlanadigan ishlarni shablon qilib qo‘ying — nusxalar o‘zi yaratiladi va hech narsa esdan chiqmaydi."
        action={
          <Link to={hrtPaths.templates} className={primaryLinkCls}>
            <Repeat className="h-4 w-4" />
            Takroriy topshiriq qo‘shish
          </Link>
        }
      />
    </Card>
  )

  const loadingStats = summary.loading && !stats

  return (
    <div className="min-w-0 space-y-4">
      <PageHeader
        title="Boshqaruv paneli"
        description={`${period.isCustom ? 'Tanlangan davr' : HR_PERIOD_LABEL[period.preset]} · ${hrFormatRange(range.dateFrom, range.dateTo)}`}
        actions={
          <>
            <Button variant="secondary" disabled={!valid || exporting} onClick={() => void handleExport()}>
              <Download className="h-4 w-4" />
              {exporting ? 'Yuklanmoqda…' : 'CSV yuklab olish'}
            </Button>
            <Link to={tasksLink()} className={secondaryLinkCls}>
              Barcha topshiriqlar
            </Link>
          </>
        }
      />

      <PeriodCard period={period} />

      {summary.error && <Notice>{summary.error}</Notice>}

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-5">
        <StatTile
          label="Topshiriqlar"
          value={hrFormatNumber(stats?.totalTasks)}
          hint={stats ? `${hrFormatNumber(stats.employeesCount)} ta xodim bo‘yicha` : undefined}
          icon={<ClipboardList className="h-4 w-4" />}
          tone="brand"
          loading={loadingStats}
          errored={Boolean(summary.error)}
          to={tasksLink()}
        />
        <StatTile
          label="Bajarildi"
          value={hrFormatNumber(stats?.doneCount)}
          hint={stats ? `${hrFormatPercent(stats.donePercent)} bajarilgan` : undefined}
          icon={<CheckCircle2 className="h-4 w-4" />}
          tone="success"
          loading={loadingStats}
          errored={Boolean(summary.error)}
        />
        <StatTile
          label="Bajarilmadi"
          value={hrFormatNumber(stats?.missedCount)}
          hint="Muddat o‘tib yopilganlar"
          icon={<XCircle className="h-4 w-4" />}
          tone={stats && stats.missedCount > 0 ? 'danger' : 'neutral'}
          loading={loadingStats}
          errored={Boolean(summary.error)}
        />
        <StatTile
          label="Muddati o‘tgan"
          value={hrFormatNumber(stats?.overdueCount)}
          hint="Hali ochiq, lekin muddati o‘tgan"
          icon={<AlertTriangle className="h-4 w-4" />}
          tone={stats && stats.overdueCount > 0 ? 'warning' : 'neutral'}
          loading={loadingStats}
          errored={Boolean(summary.error)}
        />
        <StatTile
          label="Tasdiqlanmagan"
          value={hrFormatNumber(stats?.unacceptedCount ?? 0)}
          hint="Xodim hali ko‘rmagan"
          icon={<BellRing className="h-4 w-4" />}
          tone={stats && (stats.unacceptedCount ?? 0) > 0 ? 'warning' : 'neutral'}
          loading={loadingStats}
          errored={Boolean(summary.error)}
          to={tasksLink(undefined, { accepted: 'false' })}
        />
      </div>

      <section>
        <SectionTitle hint="Qatorni bosing — o‘sha xodimning shu davrdagi topshiriqlari ochiladi">
          Xodimlar kesimida
        </SectionTitle>
        <EmployeeReportTable
          rows={rows}
          loading={report.loading}
          dimmed={report.loading && rows.length > 0}
          linkTo={(row) => tasksLink(row.employeeId)}
          empty={reportEmpty}
        />
      </section>

      {isToday && <TodayFocus today={period.today} />}
      {toast.node}
    </div>
  )
}

/** Two short lists for the "Bugun" period — not even requested for other periods. */
function TodayFocus({ today }: { today: string }) {
  // Overdue tasks are not date-bound: one may be left over from last month.
  const overdue = useLoad('overdue', () =>
    getTasks({ status: ['open', 'returned'], overdue: true, sort: 'due_at', limit: SHORT_LIST_LIMIT }),
  )
  const selfCreated = useLoad(`self|${today}`, () =>
    getTasks({ dateFrom: today, dateTo: today, source: ['self'], sort: '-created_at', limit: SHORT_LIST_LIMIT }),
  )

  // Stay correct even if the server ignores the `overdue` filter.
  const overdueItems = (overdue.data?.items ?? []).filter((task) => hrIsOverdue(task))
  const selfItems = selfCreated.data?.items ?? []

  return (
    <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
      <Card className="p-4">
        <SectionTitle hint="Muddati o‘tgan, lekin hali ochiq topshiriqlar">Diqqat talab qiladi</SectionTitle>
        {overdue.loading ? (
          <ListSkeleton />
        ) : overdue.error ? (
          <EmptyState
            title="Ro‘yxatni yuklab bo‘lmadi"
            hint={overdue.error}
            action={
              <Button variant="secondary" onClick={overdue.reload}>
                Qayta urinish
              </Button>
            }
          />
        ) : overdueItems.length === 0 ? (
          <EmptyState
            icon={<CheckCircle2 className="h-5 w-5" />}
            title="Kechikkan topshiriq yo‘q"
            hint="Hamma ish muddatida bajarilmoqda."
          />
        ) : (
          <ul className="divide-y divide-slate-100">
            {overdueItems.map((task) => (
              <li key={task.id}>
                <Link
                  to={hrtTasksLink({ employeeId: task.employeeId })}
                  className="flex items-start justify-between gap-3 rounded-lg px-2 py-2.5 transition-colors hover:bg-slate-50"
                >
                  <span className="min-w-0">
                    <span className="block truncate text-sm font-medium text-slate-800">{task.title}</span>
                    <span className="block truncate text-xs text-slate-400">{task.employeeName ?? '—'}</span>
                  </span>
                  <span className="shrink-0 whitespace-nowrap text-xs font-medium text-red-600">
                    {hrOverdueLabel(task.dueAt)}
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </Card>

      <Card className="p-4">
        <SectionTitle hint="Xodimlar o‘ziga yaratgan topshiriqlar">Bugun o‘zi qo‘shganlar</SectionTitle>
        {selfCreated.loading ? (
          <ListSkeleton />
        ) : selfCreated.error ? (
          <EmptyState
            title="Ro‘yxatni yuklab bo‘lmadi"
            hint={selfCreated.error}
            action={
              <Button variant="secondary" onClick={selfCreated.reload}>
                Qayta urinish
              </Button>
            }
          />
        ) : selfItems.length === 0 ? (
          <EmptyState title="Bugun qo‘shilgani yo‘q" hint="Xodimlar o‘ziga topshiriq yaratmagan." />
        ) : (
          <ul className="divide-y divide-slate-100">
            {selfItems.map((task) => (
              <li key={task.id} className="flex items-start justify-between gap-3 px-2 py-2.5">
                <span className="min-w-0">
                  <span className="block truncate text-sm font-medium text-slate-800">{task.title}</span>
                  <span className="block truncate text-xs text-slate-400">{task.employeeName ?? '—'}</span>
                </span>
                <span className="shrink-0 text-xs tabular-nums text-slate-400">{hrFormatTime(task.createdAt)}</span>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </div>
  )
}

function ListSkeleton() {
  return (
    <div className="space-y-3">
      {Array.from({ length: 4 }, (_, i) => (
        <div key={i} className="flex items-center justify-between gap-3">
          <Skeleton className="h-4 w-2/3" />
          <Skeleton className="h-4 w-16" />
        </div>
      ))}
    </div>
  )
}
