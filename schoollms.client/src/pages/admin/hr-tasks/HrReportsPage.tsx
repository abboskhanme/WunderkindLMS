import { useState } from 'react'
import { Download } from 'lucide-react'
import { downloadReportCsv, downloadReportXlsx, getEmployeeReport, getSummaryReport, hrErrorMessage } from '@/api/services/hrTasks'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { EmptyState, Notice, PageHeader, SectionTitle, StatTile } from '@/components/hr-tasks/ui'
import { useToast } from '@/components/hr-tasks/hooks'
import { useLoad } from '@/components/hr-tasks/useLoad'
import { HR_PERIOD_LABEL, hrFormatNumber, hrFormatPercent, hrFormatRange } from '@/components/hr-tasks/lib'
import { EmployeeReportTable } from './EmployeeReportTable'
import { PeriodCard } from './PeriodCard'
import { usePeriodRange } from './period'
import { hrtTasksLink } from './paths'

/**
 * Reports — period totals, the per-employee table and CSV
 * (`/reports/summary`, `/reports/employees`, `/reports/export.csv`).
 */
export function HrReportsPage() {
  const period = usePeriodRange()
  const toast = useToast()
  const [exporting, setExporting] = useState<'csv' | 'xlsx' | null>(null)
  const { range, valid } = period

  const key = valid ? `${range.dateFrom}|${range.dateTo}` : null
  const summary = useLoad(key && `summary|${key}`, () => getSummaryReport(range))
  const report = useLoad(key && `employees|${key}`, () => getEmployeeReport(range))
  const stats = summary.data
  const rows = report.data?.items ?? report.previous?.items ?? []
  const loadingStats = summary.loading && !stats

  async function handleExport(format: 'csv' | 'xlsx'): Promise<void> {
    if (!valid) return
    setExporting(format)
    try {
      if (format === 'csv') await downloadReportCsv(range)
      else await downloadReportXlsx(range)
      toast.success(format === 'csv' ? 'CSV yuklab olindi' : 'Excel yuklab olindi')
    } catch (err) {
      toast.error(hrErrorMessage(err))
    } finally {
      setExporting(null)
    }
  }

  return (
    <div className="min-w-0 space-y-4">
      <PageHeader
        title="Hisobotlar"
        description={`${period.isCustom ? 'Tanlangan davr' : HR_PERIOD_LABEL[period.preset]} · ${hrFormatRange(range.dateFrom, range.dateTo)}`}
        actions={
          <>
            <Button variant="secondary" disabled={!valid || exporting !== null} onClick={() => void handleExport('xlsx')}>
              <Download className="h-4 w-4" />
              {exporting === 'xlsx' ? 'Yuklanmoqda…' : 'Excel'}
            </Button>
            <Button variant="secondary" disabled={!valid || exporting !== null} onClick={() => void handleExport('csv')}>
              <Download className="h-4 w-4" />
              {exporting === 'csv' ? 'Yuklanmoqda…' : 'CSV yuklab olish'}
            </Button>
          </>
        }
      />

      <PeriodCard period={period} />

      {summary.error && <Notice>{summary.error}</Notice>}

      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <StatTile label="Jami" value={hrFormatNumber(stats?.totalTasks)} hint={stats ? `${hrFormatNumber(stats.employeesCount)} ta xodim` : undefined} loading={loadingStats} errored={Boolean(summary.error)} />
        <StatTile label="Bajarildi" tone="success" value={hrFormatNumber(stats?.doneCount)} hint={stats ? hrFormatPercent(stats.donePercent) : undefined} loading={loadingStats} errored={Boolean(summary.error)} />
        <StatTile label="Bajarilmadi" tone={stats && stats.missedCount > 0 ? 'danger' : 'neutral'} value={hrFormatNumber(stats?.missedCount)} hint="Muddat o‘tib yopilganlar" loading={loadingStats} errored={Boolean(summary.error)} />
        <StatTile label="Kechikkan" tone={stats && stats.lateCount > 0 ? 'warning' : 'neutral'} value={hrFormatNumber(stats?.lateCount)} hint="Kechikib bajarilganlar" loading={loadingStats} errored={Boolean(summary.error)} />
        <StatTile label="Ochiq" value={hrFormatNumber(stats?.openCount)} hint={stats ? `${hrFormatNumber(stats.overdueCount)} tasi muddati o‘tgan` : undefined} loading={loadingStats} errored={Boolean(summary.error)} />
        <StatTile label="Qaytarildi" value={hrFormatNumber(stats?.returnedCount)} loading={loadingStats} errored={Boolean(summary.error)} />
        <StatTile label="Bekor qilingan" value={hrFormatNumber(stats?.cancelledCount)} loading={loadingStats} errored={Boolean(summary.error)} />
        <StatTile label="O‘zi qo‘shgan" value={hrFormatNumber(stats?.selfCreatedCount)} hint="Xodim o‘ziga yaratganlari" loading={loadingStats} errored={Boolean(summary.error)} />
      </div>

      <section>
        <SectionTitle hint="Qatorni bosing — o‘sha xodimning shu davrdagi topshiriqlari ochiladi">
          Xodimlar kesimida
        </SectionTitle>
        <EmployeeReportTable
          rows={rows}
          detailed
          loading={report.loading}
          dimmed={report.loading && rows.length > 0}
          linkTo={(row) => hrtTasksLink({ employeeId: row.employeeId, dateFrom: range.dateFrom, dateTo: range.dateTo })}
          empty={
            <Card>
              {report.error ? (
                <EmptyState
                  title="Hisobotni yuklab bo‘lmadi"
                  hint={report.error}
                  action={
                    <Button variant="secondary" onClick={report.reload}>
                      Qayta urinish
                    </Button>
                  }
                />
              ) : (
                <EmptyState title="Bu davrda topshiriq yo‘q" hint="Boshqa davrni tanlab ko‘ring." />
              )}
            </Card>
          }
        />
      </section>
      {toast.node}
    </div>
  )
}
