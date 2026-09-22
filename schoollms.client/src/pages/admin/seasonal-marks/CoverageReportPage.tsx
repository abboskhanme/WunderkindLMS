/**
 * MAVSUMIY BAHOLASH HISOBOTI — `/admin/seasonal-marks/report`, screen 13 of
 * `docs/modules/admission-and-testing.md` §3.3.
 *
 * Per teacher, for one period: how many pupils they teach, how many carry a
 * seasonal mark, how many do not, and the percentage. Every number is the
 * server's (`GET /coverage`, §6.6 `CoverageRowDto`) — nothing is summed or
 * divided here. A report, not storage (§10 "Reports, not storage").
 *
 * The three counts open the pupils behind them (`/coverage/detail`).
 * Read-only: data and Excel, nothing else (§3.7).
 */
import { useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import { BarChart3, FileSpreadsheet } from 'lucide-react'
import {
  downloadCoverage,
  getCoverage,
  getSeasonalScope,
  seasonalErrorMessage,
  type CoverageQuery,
  type CoverageRowDto,
} from '@/api/services/seasonalMarks'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import { CoverageDetailModal, type CoverageDrill, type CoverageSlice } from './CoverageDetailModal'
import { PeriodPicker } from './PeriodPicker'
import { EmptyState, ErrorState, InlineError, Pager } from './StateViews'
import {
  controlClass,
  defaultPeriod,
  periodCaption,
  periodKey,
  periodKindLabels,
  toPeriodQuery,
  type PeriodSelection,
} from './periods'
import { teacherOptions } from './scopeOptions'
import { useRemote } from './useRemote'

const PAGE_SIZE = 50

function percentTone(p: number): { bar: string; text: string } {
  if (p >= 90) return { bar: 'fill-emerald-500', text: 'text-emerald-700' }
  if (p >= 50) return { bar: 'fill-amber-400', text: 'text-amber-700' }
  return { bar: 'fill-red-400', text: 'text-red-600' }
}

export function CoverageReportPage() {
  const [period, setPeriod] = useState<PeriodSelection>(() => defaultPeriod())
  const [teacherId, setTeacherId] = useState('')
  const [page, setPage] = useState(1)
  const [exporting, setExporting] = useState(false)
  const [exportError, setExportError] = useState<string | null>(null)
  const [drill, setDrill] = useState<CoverageDrill | null>(null)

  const scope = useRemote('scope', () => getSeasonalScope())
  const teachers = useMemo(() => (scope.data ? teacherOptions(scope.data) : []), [scope.data])

  const query: CoverageQuery = {
    ...toPeriodQuery(period),
    teacherIds: teacherId ? [teacherId] : undefined,
  }
  const report = useRemote(`${periodKey(period)}|${teacherId}|${page}`, () =>
    getCoverage({ ...query, page, limit: PAGE_SIZE }),
  )
  const rows = report.data?.items ?? []
  const total = report.data?.total ?? 0

  const handleExport = async () => {
    setExporting(true)
    setExportError(null)
    try {
      await downloadCoverage(query)
    } catch (err: unknown) {
      setExportError(seasonalErrorMessage(err))
    } finally {
      setExporting(false)
    }
  }

  const open = (teacher: CoverageRowDto, slice: CoverageSlice) => setDrill({ teacher, slice })

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Mavsumiy baholash hisoboti</h1>
          <p className="text-sm text-slate-400">
            O'qituvchilar kesimida: dars beradigan o'quvchilaridan nechtasi baholangan
          </p>
        </div>
        <Button
          variant="secondary"
          onClick={handleExport}
          disabled={exporting || report.loading || total === 0}
          title="Butun hisobot bo'yicha Excel fayl"
        >
          <FileSpreadsheet className="h-4 w-4" />
          {exporting ? 'Tayyorlanmoqda...' : 'Excel'}
        </Button>
      </div>

      {exportError && <InlineError message={exportError} />}

      <Card className="p-0">
        <div className="flex flex-wrap items-center gap-3 border-b border-slate-100 p-4">
          <PeriodPicker
            value={period}
            onChange={(next) => {
              setPage(1)
              setPeriod(next)
            }}
          />
          <select
            value={teacherId}
            onChange={(e) => {
              setPage(1)
              setTeacherId(e.target.value)
            }}
            disabled={Boolean(scope.error) || scope.loading}
            aria-label="O'qituvchi"
            className={cn(controlClass, 'max-w-[16rem]')}
          >
            <option value="">
              {scope.error ? "O'qituvchilar yuklanmadi" : "Barcha o'qituvchilar"}
            </option>
            {teachers.map((t) => (
              <option key={t.id} value={t.id}>
                {t.name}
              </option>
            ))}
          </select>
        </div>

        {report.loading ? (
          <Loader label="Hisobot tayyorlanmoqda..." />
        ) : report.error ? (
          <ErrorState
            title="Hisobotni yuklab bo'lmadi"
            message={seasonalErrorMessage(report.error)}
            onRetry={report.reload}
          />
        ) : rows.length === 0 ? (
          <EmptyState
            icon={BarChart3}
            title="Bu davr uchun o'qituvchi topilmadi"
            hint="Hisobot dars jadvalidagi sinf va fanlardan tuziladi — jadval tuzilganini tekshiring yoki boshqa davrni tanlang."
          />
        ) : (
          <>
            <div className="border-b border-slate-100 px-4 py-3 text-sm text-slate-600">
              {periodKindLabels[period.kind]}, {periodCaption(period)}
            </div>
            <div className={cn('overflow-x-auto', report.refreshing && 'opacity-60')}>
              <table className="w-full min-w-[48rem] text-left text-sm">
                <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-4 py-3">F.I.SH</th>
                    <th className="px-4 py-3 text-right">Jami o'quvchi</th>
                    <th className="px-4 py-3 text-right">Baholangan</th>
                    <th className="px-4 py-3 text-right">Baholanmagan</th>
                    <th className="px-4 py-3">Foiz</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100 tabular-nums">
                  {rows.map((r) => {
                    const tone = percentTone(r.percent)
                    return (
                      <tr key={r.teacherId} className="hover:bg-slate-50/60">
                        <td className="px-4 py-3 font-medium text-slate-800">
                          <button
                            type="button"
                            onClick={() => open(r, 'all')}
                            className="block max-w-[16rem] truncate text-left hover:text-brand-600"
                            title={r.fullName}
                          >
                            {r.fullName}
                          </button>
                        </td>
                        <td className="px-4 py-3 text-right">
                          <CountLink onClick={() => open(r, 'all')}>{r.totalStudents}</CountLink>
                        </td>
                        <td className="px-4 py-3 text-right">
                          <CountLink onClick={() => open(r, 'marked')}>{r.marked}</CountLink>
                        </td>
                        <td className="px-4 py-3 text-right">
                          <CountLink onClick={() => open(r, 'unmarked')} muted={r.unmarked === 0}>
                            {r.unmarked}
                          </CountLink>
                        </td>
                        <td className="px-4 py-3">
                          <div className="flex items-center gap-3">
                            {/* SVG attributes, not inline style: the width is data. */}
                            <svg className="h-1.5 w-24 shrink-0" aria-hidden="true">
                              <rect width="100%" height="100%" rx="3" className="fill-slate-100" />
                              <rect
                                width={`${Math.max(0, Math.min(r.percent, 100))}%`}
                                height="100%"
                                rx="3"
                                className={tone.bar}
                              />
                            </svg>
                            <span className={cn('text-sm font-semibold', tone.text)}>{r.percent}%</span>
                          </div>
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          </>
        )}

        <Pager page={page} limit={PAGE_SIZE} total={total} disabled={report.loading} onPage={setPage} />
      </Card>

      <CoverageDetailModal drill={drill} period={period} onClose={() => setDrill(null)} />
    </div>
  )
}

function CountLink({
  onClick,
  muted,
  children,
}: {
  onClick: () => void
  muted?: boolean
  children: ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'rounded-md px-1.5 py-0.5 font-medium transition-colors hover:bg-brand-50 hover:text-brand-700',
        muted ? 'text-slate-400' : 'text-brand-600',
      )}
    >
      {children}
    </button>
  )
}
