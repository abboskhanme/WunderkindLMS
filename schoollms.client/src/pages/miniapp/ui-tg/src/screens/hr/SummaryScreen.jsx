/**
 * NATIJAM — joriy oy bo'yicha o'z ko'rsatkichi (eski `screens/SummaryScreen.tsx`,
 * `GET /hr/me/summary`).
 *
 * Ataylab BITTA ekran va grafiksiz: xodimga "qancha bajardim" degan savolga
 * javob kerak, tahlil vositasi emas. Bajarilish foizi sariq hero'da, sanoqlar
 * 2×2 to'rda, raqamli (KPI) topshiriqlar — progress chiziqlari bilan.
 */
import { ChartColumn } from 'lucide-react'
import { Card, EmptyState, ErrorState, Hero, HeroProgress, Loader, Screen, StatGrid } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { hrApi, hrErrorMessage } from '../../lib/hrApi'
import { dayMonthYearLabel, formatAmount, percentOf, toNumber } from '../../lib/hrFormat'
import { ProgressBar } from './shared'
import { t } from './texts'

export function SummaryScreen({ heroTop }) {
  // `useAsync` xato matnini o'zi yig'adi; HR matnlari uchun xatoni o'zimiz ushlaymiz.
  const q = useAsync(async () => {
    try {
      return { ok: true, data: await hrApi.summary() }
    } catch (e) {
      return { ok: false, error: hrErrorMessage(e) }
    }
  }, [])

  const data = q.data?.ok ? q.data.data : null
  const error = q.data && !q.data.ok ? q.data.error : q.error
  const kpi = Array.isArray(data?.kpi) ? data.kpi : []

  // Backend `percent` bermasa ham ekran ishlashi kerak.
  const percent = data
    ? typeof data.percent === 'number'
      ? Math.round(data.percent)
      : percentOf(data.done ?? 0, data.total ?? 0)
    : 0

  return (
    <Screen>
      <Hero
        title={t('summary.title')}
        subtitle={
          data?.periodStart && data?.periodEnd
            ? t('summary.period', { from: dayMonthYearLabel(data.periodStart), to: dayMonthYearLabel(data.periodEnd) })
            : null
        }
      >
        {heroTop}
        {data && data.total > 0 && (
          <div className={heroTop ? 'mt-4' : ''}>
            <HeroProgress value={percent} max={100} lead={`${percent}%`} note={t('summary.percent')} />
          </div>
        )}
      </Hero>

      {q.loading && q.data === null ? (
        <Loader label={t('common.loading')} />
      ) : error ? (
        <ErrorState message={error} onRetry={q.reload} />
      ) : !data || data.total === 0 ? (
        <EmptyState icon={<ChartColumn className="h-10 w-10" />} title={t('summary.empty')} />
      ) : (
        <>
          <StatGrid
            items={[
              { label: t('summary.done'), value: String(data.done ?? 0) },
              { label: t('summary.missed'), value: String(data.missed ?? 0) },
              { label: t('summary.open'), value: String(data.open ?? 0) },
              { label: t('summary.late'), value: String(data.late ?? 0) },
            ]}
          />

          <Card title={t('summary.kpi_title')}>
            {kpi.length === 0 ? (
              <p className="px-4 pb-4 pt-1 text-[13px] text-slate-400">{t('summary.kpi_empty')}</p>
            ) : (
              kpi.map((row, index) => {
                const current = toNumber(row.currentAmount)
                const target = toNumber(row.targetAmount)
                const reached = target > 0 && current >= target
                const label =
                  row.unit === null || row.unit === undefined || row.unit === ''
                    ? t('kpi.progress_plain', { current: formatAmount(current), target: formatAmount(target) })
                    : t('kpi.progress', { current: formatAmount(current), target: formatAmount(target), unit: row.unit })
                return (
                  <div key={row.taskId ?? `kpi-${index}`} className="border-t border-slate-100 px-4 py-3 first:border-t-0">
                    <p className="break-words text-[15px] font-semibold leading-snug">{row.title}</p>
                    <ProgressBar percent={percentOf(current, target)} reached={reached} label={label} />
                  </div>
                )
              })
            )}
          </Card>
          <div className="h-4" />
        </>
      )}
    </Screen>
  )
}
