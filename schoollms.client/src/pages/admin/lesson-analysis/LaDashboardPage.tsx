import { Link } from 'react-router-dom'
import { BarChart3, ClipboardList, Sparkles, Users } from 'lucide-react'
import { getAnalytics, getDashboard } from '@/api/services/lessonAnalysis'
import type { LaTopTeacher } from '@/types/lessonAnalysis'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { AnalyticsCharts } from '@/components/lesson-analysis/AnalyticsCharts'
import { ChartCard, ColumnChart } from '@/components/lesson-analysis/charts'
import type { ChartDatum } from '@/components/lesson-analysis/charts'
import { asCount, initials } from '@/components/lesson-analysis/format'
import { EmptyState, Notice, Score, StatTile } from '@/components/lesson-analysis/ui'
import { useLoad } from '@/components/lesson-analysis/useLoad'
import { laPaths } from './paths'

/** Umumiy — eski `Dashboard.tsx` (staff ko'rinishi). */
export function LaDashboardPage() {
  const dashboard = useLoad('dashboard', getDashboard)
  // Grafiklar ixtiyoriy: endpoint yiqilsa ham hisoblagichlar chiqadi.
  const analytics = useLoad('analytics', () => getAnalytics())

  if (dashboard.error) return <Notice>{dashboard.error}</Notice>
  if (!dashboard.data) return <Loader label="Yuklanmoqda..." />
  const data = dashboard.data

  const distribution: ChartDatum[] = (analytics.data?.distribution ?? []).map((bucket) => ({
    label: bucket.bucket,
    value: bucket.count,
    rows: [`${bucket.count} ta o‘qituvchi`, `Reyting ${bucket.bucket} oralig‘ida`],
  }))

  const topRows: LaTopTeacher[] =
    analytics.data && analytics.data.topTeachers.length > 0
      ? analytics.data.topTeachers
      : data.top.map((row) => ({
          teacherId: row.teacherId,
          teacherName: row.teacherName,
          subject: row.subject,
          rating: row.rating,
          observationsCount: row.observationsCount,
        }))

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-slate-500">Maktab bo‘yicha dars kuzatuvi ko‘rsatkichlari</p>
        <div className="flex flex-wrap gap-2">
          <Link
            to={laPaths.rating}
            className="inline-flex items-center justify-center gap-2 rounded-lg border border-slate-200 bg-white px-4 py-2 text-sm font-medium text-slate-700 transition-colors hover:bg-slate-50"
          >
            To‘liq reyting
          </Link>
          <Link
            to={laPaths.create()}
            className="inline-flex items-center justify-center gap-2 rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-brand-700"
          >
            <Sparkles className="h-4 w-4" />
            Yangi dars tahlili
          </Link>
        </div>
      </div>

      <div className="grid gap-4 [grid-template-columns:repeat(auto-fit,minmax(min(200px,100%),1fr))]">
        <StatTile label="Faol o‘qituvchilar" value={data.teachersActive} icon={<Users className="h-4 w-4" />} />
        <StatTile
          label="Jami tahlillar"
          value={data.observationsTotal}
          hint={data.drafts > 0 ? `Qoralamalar: ${data.drafts}` : undefined}
          icon={<ClipboardList className="h-4 w-4" />}
        />
        <StatTile label="Oxirgi 30 kun" value={data.observationsLast30Days} />
        <StatTile label="O‘rtacha reyting" value={`${data.averageRating}%`} icon={<BarChart3 className="h-4 w-4" />} />
      </div>

      {analytics.error ? (
        <Notice kind="info">Tahliliy grafiklar hozircha mavjud emas — {analytics.error}</Notice>
      ) : !analytics.data ? (
        <Card>
          <Loader label="Grafiklar tayyorlanmoqda…" />
        </Card>
      ) : (
        <>
          <AnalyticsCharts analytics={analytics.data} audience="staff" />

          <div className="grid items-start gap-4 [grid-template-columns:repeat(auto-fit,minmax(min(420px,100%),1fr))]">
            <div className="grid min-w-0 gap-4">
              <ChartCard title="Reyting taqsimoti" caption="Nechta o‘qituvchi qaysi reyting oralig‘ida turibdi">
                <ColumnChart data={distribution} height={250} formatValue={asCount} ariaLabel="Reyting taqsimoti grafigi" />
              </ChartCard>

              {data.notEvaluated.length > 0 && (
                <Card>
                  <div className="flex items-center justify-between gap-3">
                    <h2 className="font-semibold text-slate-800">Hali tahlil qilinmagan</h2>
                    <span className="rounded-md bg-amber-50 px-2 py-0.5 text-xs font-semibold text-amber-700">
                      {data.notEvaluated.length}
                    </span>
                  </div>
                  <p className="mt-0.5 text-xs text-slate-400">Bu o‘qituvchilar reytingda qatnashmaydi</p>
                  <div className="mt-3.5 flex flex-wrap gap-2">
                    {data.notEvaluated.map((name) => (
                      <span key={name} className="rounded-md bg-slate-100 px-2 py-1 text-xs text-slate-600">
                        {name}
                      </span>
                    ))}
                  </div>
                </Card>
              )}
            </div>

            <Leaderboard rows={topRows} average={data.averageRating} />
          </div>
        </>
      )}
    </div>
  )
}

/**
 * Bitta reyting ro'yxati — "eng yaxshi" va "eng past" ikki karta emas: xodim kam bo'lsa
 * bir o'qituvchi ikkalasida ham chiqib qolardi. Maktab o'rtachasidan pastdagilar belgilanadi.
 */
function Leaderboard({ rows, average }: { rows: LaTopTeacher[]; average: number }) {
  return (
    <Card className="flex h-full min-w-0 flex-col overflow-hidden p-0">
      <div className="flex items-baseline justify-between gap-3 border-b border-slate-100 px-5 py-4">
        <div className="min-w-0">
          <h2 className="font-semibold text-slate-800">O‘qituvchilar reytingi</h2>
          <p className="mt-0.5 text-xs text-slate-400">Yakunlangan tahlillar bo‘yicha o‘rtacha foiz</p>
        </div>
        <Link to={laPaths.rating} className="shrink-0 text-sm font-medium text-brand-600 hover:underline">
          To‘liq ro‘yxat
        </Link>
      </div>

      {rows.length === 0 ? (
        <EmptyState title="Hozircha ma’lumot yo‘q" hint="Birinchi tahlil yakunlangach shu yerda ko‘rinadi" />
      ) : (
        <ul className="flex-1 divide-y divide-slate-100">
          {rows.map((row, index) => {
            const behind = average > 0 && row.rating < average
            return (
              <li key={row.teacherId}>
                <Link to={laPaths.teacher(row.teacherId)} className="flex items-center gap-3 px-5 py-3 transition-colors hover:bg-slate-50">
                  <span className="w-4 shrink-0 text-xs font-semibold tabular-nums text-slate-400">{index + 1}</span>
                  <span className="grid h-9 w-9 shrink-0 place-items-center rounded-full bg-slate-100 text-xs font-semibold text-slate-600">
                    {initials(row.teacherName)}
                  </span>
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-2">
                      <p className="truncate font-medium text-slate-800">{row.teacherName}</p>
                      {behind && (
                        <span className="shrink-0 rounded-md bg-amber-50 px-1.5 py-0.5 text-[11px] font-medium text-amber-700">
                          e’tibor
                        </span>
                      )}
                    </div>
                    <p className="truncate text-xs text-slate-400">
                      {row.subject || 'Fan ko‘rsatilmagan'} · {row.observationsCount} ta tahlil
                    </p>
                    <div className="mt-1.5 h-1 overflow-hidden rounded-full bg-slate-100">
                      <div
                        className="h-full rounded-full bg-brand-500"
                        style={{ width: `${Math.min(100, Math.max(0, row.rating))}%` }}
                      />
                    </div>
                  </div>
                  <Score percent={row.rating} />
                </Link>
              </li>
            )
          })}
        </ul>
      )}
    </Card>
  )
}
