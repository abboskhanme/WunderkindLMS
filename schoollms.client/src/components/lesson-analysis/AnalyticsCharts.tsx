import type { LaAnalytics } from '@/types/lessonAnalysis'
import { BarChart, ChartCard, LineChart, Meter } from './charts'
import type { ChartDatum } from './charts'
import { asPercent, fitDomain, formatMonth, truncate } from './format'

/**
 * Oylik trend + bo'limlar bo'yicha bajarilish + eng ko'p bajarilmagan mezonlar.
 * Staff (maktab bo'yicha) va o'qituvchi (faqat o'zi) — matnlar eski ilovadagidek farq qiladi.
 */
export function AnalyticsCharts({ analytics, audience }: { analytics: LaAnalytics; audience: 'staff' | 'teacher' }) {
  const staff = audience === 'staff'

  const monthly: ChartDatum[] = analytics.monthly.map((point) => ({
    label: formatMonth(point.month),
    fullLabel: formatMonth(point.month, true),
    value: point.average,
    rows: [
      `O‘rtacha ${asPercent(point.average)}`,
      staff ? `${point.count} ta yakunlangan tahlil` : `${point.count} ta dars kuzatuvi`,
    ],
  }))
  // natijalar yuqori oraliqda to'planadi — 0–100 o'q grafikni bo'sh qoldirardi
  const trendDomain = fitDomain(monthly.map((point) => point.value))

  const sections: ChartDatum[] = analytics.sections.map((section) => ({
    label: section.title,
    value: section.percent,
    swatch: section.color,
    rows: [`${asPercent(section.percent)} bajarilgan`, `${section.done} / ${section.total} mezon`],
  }))
  const sectionsDone = analytics.sections.reduce((sum, section) => sum + section.done, 0)
  const sectionsTotal = analytics.sections.reduce((sum, section) => sum + section.total, 0)
  const sectionsPercent = sectionsTotal > 0 ? (sectionsDone / sectionsTotal) * 100 : 0

  const misses: ChartDatum[] = analytics.criteriaMisses.slice(0, 8).map((item) => ({
    label: truncate(item.text, 70),
    fullLabel: item.text,
    value: item.missPercent,
    note: item.sectionTitle,
    rows: [
      staff
        ? `${asPercent(item.missPercent)} kuzatuvda bajarilmagan`
        : `${asPercent(item.missPercent)} darsda bajarilmagan`,
      `${item.missed} / ${item.total} kuzatuv`,
    ],
  }))

  return (
    <>
      <ChartCard
        title={staff ? 'Oylar bo‘yicha o‘rtacha ko‘rsatkich' : 'Oylar bo‘yicha ko‘rsatkichim'}
        caption={
          staff
            ? 'Har oyda yakunlangan tahlillarning o‘rtacha foizi'
            : 'Har oyda yakunlangan darslarimning o‘rtacha foizi'
        }
      >
        <LineChart
          data={monthly}
          height={190}
          {...trendDomain}
          ariaLabel={staff ? 'Oylik o‘rtacha ko‘rsatkich grafigi' : 'Oylik ko‘rsatkichim grafigi'}
        />
      </ChartCard>

      <div className="grid items-start gap-4 [grid-template-columns:repeat(auto-fit,minmax(min(420px,100%),1fr))]">
        <ChartCard
          title="Bo‘limlar bo‘yicha bajarilish"
          caption={
            staff
              ? 'Darsning qaysi qismi maktab bo‘ylab eng ko‘p e’tibordan chetda qolmoqda'
              : 'Darsimning qaysi qismi eng ko‘p e’tibordan chetda qolmoqda'
          }
        >
          <Meter
            label="Umumiy bajarilish"
            value={sectionsPercent}
            note={`${sectionsDone} / ${sectionsTotal} mezon bajarilgan`}
          />
          <div className="mt-4">
            <BarChart data={sections} ariaLabel="Bo‘limlar bo‘yicha bajarilish grafigi" />
          </div>
        </ChartCard>

        <ChartCard
          title="Eng ko‘p bajarilmagan mezonlar"
          caption={
            staff
              ? 'Kuzatuvlarda eng ko‘p “bajarilmagan” deb belgilangan mezonlar'
              : 'Darslarimda eng ko‘p «bajarilmagan» deb belgilangan mezonlar'
          }
        >
          <BarChart data={misses} ariaLabel="Eng ko‘p bajarilmagan mezonlar grafigi" />
        </ChartCard>
      </div>
    </>
  )
}
