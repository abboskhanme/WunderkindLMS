import { useMemo, useState } from 'react'
import { X } from 'lucide-react'
import { getMyAnalytics, getMyObservations } from '@/api/services/lessonAnalysis'
import type { LaObservationListItem } from '@/types/lessonAnalysis'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { AnalyticsCharts } from '@/components/lesson-analysis/AnalyticsCharts'
import { average, fmtDate } from '@/components/lesson-analysis/format'
import { DateRangeFields, EmptyState, Notice, ObservationTable, Score, StatTile } from '@/components/lesson-analysis/ui'
import { useLoad } from '@/components/lesson-analysis/useLoad'
import { TEACHER_LA_BASE } from '@/pages/admin/lesson-analysis/paths'

const linkTo = (item: LaObservationListItem) => `${TEACHER_LA_BASE}/${item.id}`

/**
 * O'qituvchi portali — "Dars tahlilim" (eski `Ratings.tsx` o'qituvchi ko'rinishi + o'z tahlillari).
 * Faqat o'zi haqida: hamkasbning ismi, fani yoki natijasi bu sahifada hech qayerda chiqmaydi,
 * reyting jadvali va eksport yo'q.
 */
export function MyLessonAnalysisPage() {
  const [dateFrom, setDateFrom] = useState('')
  const [dateTo, setDateTo] = useState('')
  const key = `${dateFrom}|${dateTo}`

  const list = useLoad(`my-obs|${key}`, () => getMyObservations({ dateFrom, dateTo }))
  // grafiklar ixtiyoriy — endpoint yiqilsa ham plitkalar va ro'yxat chiqadi
  const analytics = useLoad(`my-analytics|${key}`, () => getMyAnalytics({ dateFrom, dateTo }))

  const items = useMemo(() => {
    if (!list.data) return null
    // server faqat yakunlanganlarni beradi; sana oralig'ini mijozda ham qo'llaymiz
    return list.data
      .filter(
        (item) =>
          item.status === 'completed' &&
          (!dateFrom || item.lessonDate >= dateFrom) &&
          (!dateTo || item.lessonDate <= dateTo),
      )
      .sort((a, b) => b.lessonDate.localeCompare(a.lessonDate))
  }, [list.data, dateFrom, dateTo])

  const summary = useMemo(() => {
    const rows = items ?? []
    const best = rows.reduce<LaObservationListItem | null>(
      (top, item) => (!top || item.scorePercent > top.scorePercent ? item : top),
      null,
    )
    return {
      total: rows.length,
      // reyting = yakunlangan tahlillar foizining o'rtachasi (SPEC §4)
      rating: average(rows.map((item) => item.scorePercent)),
      last: rows.length > 0 ? rows[0].lessonDate : null,
      best,
    }
  }, [items])

  function clearFilters() {
    setDateFrom('')
    setDateTo('')
  }
  const filtered = Boolean(dateFrom || dateTo)
  const clearButton = (
    <Button variant="secondary" onClick={clearFilters} className="px-3 py-1.5">
      <X className="h-4 w-4" />
      Filtrlarni tozalash
    </Button>
  )

  return (
    <div className="min-w-0 space-y-4">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Mening ko‘rsatkichim</h1>
        <p className="text-sm text-slate-400">
          Yakunlangan dars kuzatuvlarim bo‘yicha shaxsiy natijalarim va o‘sish nuqtalarim
        </p>
      </div>

      <div className="space-y-3 sm:max-w-md">
        <div className="grid gap-3 sm:grid-cols-2">
          <DateRangeFields dateFrom={dateFrom} dateTo={dateTo} onFrom={setDateFrom} onTo={setDateTo} />
        </div>
        {filtered && clearButton}
      </div>

      {list.error && <Notice>{list.error}</Notice>}

      {!items ? (
        list.error ? null : <Loader label="Ko‘rsatkichlarim yuklanmoqda" />
      ) : items.length === 0 ? (
        <Card>
          <EmptyState
            title="Hozircha ko‘rsatkich yo‘q"
            hint={
              filtered
                ? 'Tanlangan sana oralig‘ida darsingiz tahlil qilinmagan — oraliqni kengaytirib ko‘ring'
                : 'Kuzatuvchi darsingizni tahlil qilib yakunlagach, natijangiz shu yerda paydo bo‘ladi'
            }
            action={filtered ? clearButton : undefined}
          />
        </Card>
      ) : (
        <>
          <div className="grid gap-4 [grid-template-columns:repeat(auto-fit,minmax(min(200px,100%),1fr))]">
            <StatTile
              label="O‘rtacha reytingim"
              value={summary.rating != null ? <Score percent={summary.rating} size="lg" /> : '—'}
              hint={`${summary.total} ta yakunlangan tahlil bo‘yicha`}
            />
            <StatTile label="Jami tahlillar" value={summary.total} hint="Darslarim bo‘yicha yakunlangan kuzatuvlar" />
            <StatTile
              label="Oxirgi tahlil"
              value={<span className="text-xl">{fmtDate(summary.last)}</span>}
              hint="Oxirgi kuzatilgan darsim sanasi"
            />
            <StatTile
              label="Eng yuqori natijam"
              value={summary.best ? <Score percent={summary.best.scorePercent} size="lg" /> : '—'}
              hint={
                summary.best
                  ? `${fmtDate(summary.best.lessonDate)} · ${summary.best.subject || 'Fan ko‘rsatilmagan'}`
                  : 'Natija hali hisoblanmagan'
              }
            />
          </div>

          {analytics.error ? (
            <Notice kind="info">Tahliliy grafiklar hozircha mavjud emas — {analytics.error}</Notice>
          ) : !analytics.data ? (
            <Card>
              <Loader label="Grafiklar tayyorlanmoqda…" />
            </Card>
          ) : (
            <AnalyticsCharts analytics={analytics.data} audience="teacher" />
          )}

          <section className="space-y-3 pt-2">
            <h2 className="font-semibold text-slate-800">Dars tahlillarim</h2>
            <ObservationTable items={items} linkTo={linkTo} showObserver={false} showStatus={false} />
          </section>
        </>
      )}
    </div>
  )
}
