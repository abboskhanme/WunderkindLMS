/**
 * DARS TAHLILIM — kuzatuvchi yakunlagan dars tahlillari o'qituvchining o'z
 * hisobida (mijoz, 2026-09-28: "tahlil natijalari ham xodimlar akkauntida
 * bo'lishi kerak"). Veb sahifadagi "Dars tahlilim" bilan bir xil manba
 * (`/api/teacher/lesson-analysis`): faqat o'zi haqida, faqat yakunlanganlar.
 *
 * Tab EMAS: pastki panelda oltita tab turibdi, yettinchisi 320 px telefonda
 * yozuvlarni siqib qo'yardi. "Bugun" ekranida kartochka (oxirgi uchtasi +
 * o'rtacha natija), bosilsa butun ekranni yopadigan varaq — ro'yxat va bitta
 * tahlilning to'liq ko'rinishi. Telegram'ning "orqaga" tugmasi varaqni yopadi.
 *
 * Tahlil yo'q yoki yuklanmasa kartochka CHIQMAYDI: bu ikkinchi darajali ma'lumot,
 * kunning jadvali uning xatosi bilan buzilmasin (NewsCard naqshi).
 */
import { useCallback, useEffect, useState } from 'react'
import { Check, ChevronRight, MessageSquareText, Sparkles, X } from 'lucide-react'
import { Badge, Card, DateChip, Hero, Row } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { teacherApi } from '../../lib/teacherApi'
import { dayMonth, fullDate } from '../../lib/format'
import { haptic, showBackButton } from '../../lib/telegram'
import { AsyncBlock } from './shared'

/** Kartochkada ko'rinadigan oxirgi tahlillar soni. */
const CARD_ROWS = 3

/** Veb sahifadagi `Score` chegaralari: 85+ yashil, 65+ sariq, qolgani qizil. */
function scoreTone(percent) {
  if (percent >= 85) return 'success'
  if (percent >= 65) return 'brand'
  return 'danger'
}

function ScoreBadge({ percent }) {
  return <Badge tone={scoreTone(percent)}>{Math.round(percent)}%</Badge>
}

/** Yakunlanganlar, yangisi birinchi. */
function completed(list) {
  return (Array.isArray(list) ? list : [])
    .filter((o) => o.status === 'completed')
    .sort((a, b) => b.lessonDate.localeCompare(a.lessonDate))
}

/** Reyting = yakunlangan tahlillar foizining o'rtachasi (veb sahifa, SPEC §4). */
function averagePercent(rows) {
  if (rows.length === 0) return null
  return rows.reduce((sum, o) => sum + o.scorePercent, 0) / rows.length
}

function lessonTitle(o) {
  return [o.subject || 'Fan ko‘rsatilmagan', o.className].filter(Boolean).join(' · ')
}

function ObservationRow({ o, onClick }) {
  const [day, month = ''] = dayMonth(o.lessonDate).split(' ')
  return (
    <Row
      lead={<DateChip day={day} month={month.slice(0, 3)} tone="brand" />}
      title={lessonTitle(o)}
      subtitle={`Kuzatuvchi: ${o.observerName}`}
      right={
        <div className="flex shrink-0 items-center gap-1.5">
          <ScoreBadge percent={o.scorePercent} />
          <ChevronRight className="h-4 w-4 text-slate-300" />
        </div>
      }
      onClick={onClick}
    />
  )
}

/* ------------------------------------------------------------ kartochka */

export function LessonAnalysisCard() {
  const q = useAsync(() => teacherApi.lessonAnalyses(), [])
  const [sheet, setSheet] = useState(null) // null | { id: string | null }

  const rows = completed(q.data)
  if (q.error || rows.length === 0) return null
  const avg = averagePercent(rows)

  const open = (id) => {
    haptic('light')
    setSheet({ id })
  }

  return (
    <>
      <Card title="Dars tahlilim" action={avg !== null && <ScoreBadge percent={avg} />}>
        <p className="px-4 pb-1 text-[13px] text-slate-500">
          {rows.length} ta tahlil bo'yicha o'rtacha natija · oxirgisi {fullDate(rows[0].lessonDate)}
        </p>
        {rows.slice(0, CARD_ROWS).map((o) => (
          <ObservationRow key={o.id} o={o} onClick={() => open(o.id)} />
        ))}
        {rows.length > CARD_ROWS && (
          <button
            type="button"
            onClick={() => open(null)}
            className="flex w-full items-center justify-center gap-1 border-t border-slate-100 py-3 text-[14px] font-semibold text-brand-ink"
          >
            Barchasi ({rows.length})
            <ChevronRight className="h-4 w-4" />
          </button>
        )}
      </Card>
      {sheet && <LessonAnalysisSheet rows={rows} startId={sheet.id} onClose={() => setSheet(null)} />}
    </>
  )
}

/* --------------------------------------------------------------- varaq */

/**
 * Ro'yxat ↔ bitta tahlil. Kartochkadagi qatordan ochilsa — to'g'ridan-to'g'ri
 * tahlil ("orqaga" varaqni yopadi); "Barchasi" dan ochilsa — ro'yxat, undan
 * tahlilga o'tilsa "orqaga" ro'yxatga qaytaradi.
 */
function LessonAnalysisSheet({ rows, startId, onClose }) {
  const [id, setId] = useState(startId)
  const fromList = startId === null

  const back = useCallback(() => (id !== null && fromList ? setId(null) : onClose()), [id, fromList, onClose])
  useEffect(() => showBackButton(back), [back])

  return (
    <div role="dialog" aria-label="Dars tahlilim" className="fixed inset-0 z-40 overflow-y-auto overscroll-contain bg-ground">
      <div className="pb-[calc(24px+var(--tg-safe-bottom))]">
        <Hero
          title="Dars tahlilim"
          subtitle={id === null ? `${rows.length} ta yakunlangan tahlil` : 'Tahlil natijasi'}
          right={
            <button
              type="button"
              onClick={onClose}
              aria-label="Yopish"
              className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-brand-ink/10 text-brand-ink"
            >
              <X className="h-5 w-5" />
            </button>
          }
        />
        {id === null ? (
          <Card>
            {rows.map((o) => (
              <ObservationRow
                key={o.id}
                o={o}
                onClick={() => {
                  haptic('light')
                  setId(o.id)
                }}
              />
            ))}
          </Card>
        ) : (
          <ObservationDetail id={id} />
        )}
      </div>
    </div>
  )
}

function ObservationDetail({ id }) {
  const q = useAsync(() => teacherApi.lessonAnalysis(id), [id])
  return (
    <AsyncBlock query={q} loadingLabel="Tahlil yuklanmoqda…">
      {(o) => (
        <>
          <Card>
            <div className="flex items-center gap-4 px-4 py-4">
              <div className="min-w-0 flex-1">
                <p className="break-words text-[17px] font-bold leading-snug">{lessonTitle(o)}</p>
                <p className="mt-1 text-[13px] text-slate-500">
                  {fullDate(o.lessonDate)}
                  {o.lessonTime ? ` · ${o.lessonTime}` : ''}
                </p>
                <p className="text-[13px] text-slate-500">Kuzatuvchi: {o.observerName}</p>
              </div>
              <div className="shrink-0 text-right">
                <p className="text-[26px] font-extrabold leading-none tabular-nums">
                  {o.scoreDone}
                  <span className="text-slate-300">/{o.scoreTotal}</span>
                </p>
                <div className="mt-1.5">
                  <ScoreBadge percent={o.scorePercent} />
                </div>
              </div>
            </div>
          </Card>

          {o.aiSummary && <AiSummary raw={o.aiSummary} />}
          <TextCard title="Kuchli tomonlar" value={o.strengths} bar="bg-emerald-500" />
          <TextCard title="Rivojlanish sohalari" value={o.growthAreas} bar="bg-red-500" />
          <TextCard title="Kelgusi dars uchun tavsiya" value={o.nextLessonAdvice} bar="bg-brand" />
          <Criteria observation={o} />
        </>
      )}
    </AsyncBlock>
  )
}

function TextCard({ title, value, bar }) {
  if (!value) return null
  return (
    <Card>
      <div className="flex items-center gap-2.5 px-4 pt-4">
        <span className={'h-4 w-1 shrink-0 rounded-full ' + bar} />
        <h2 className="text-[16px] font-bold">{title}</h2>
      </div>
      <p className="whitespace-pre-wrap break-words px-4 pb-4 pt-2 text-[14px] leading-relaxed text-slate-700">{value}</p>
    </Card>
  )
}

/* ------------------------------------------------------------ AI xulosa */

function asList(value) {
  if (typeof value === 'string') return value.trim() ? [value.trim()] : []
  if (!Array.isArray(value)) return []
  return value.map((item) => String(item).trim()).filter(Boolean)
}

/**
 * Veb `parseAiSummary` ning nusxasi (Mini App alohida bundle): model JSON'i —
 * to'rt bo'lim; eski tizimdan ko'chgan tayyor matn — `null`, matnning o'zi chiqadi.
 */
function parseAiSummary(raw) {
  if (!raw) return null
  let text = raw.trim()
  const fence = text.match(/^```(?:json)?\s*([\s\S]*?)\s*```$/i)
  if (fence) text = fence[1]
  if (!text.startsWith('{')) return null
  try {
    const data = JSON.parse(text)
    const parts = {
      umumiy_xulosa: typeof data.umumiy_xulosa === 'string' ? data.umumiy_xulosa.trim() : '',
      kuchli_tomonlar: asList(data.kuchli_tomonlar),
      rivojlantirish: asList(data.rivojlantirish),
      tavsiyalar: asList(data.tavsiyalar),
    }
    const empty =
      !parts.umumiy_xulosa &&
      parts.kuchli_tomonlar.length === 0 &&
      parts.rivojlantirish.length === 0 &&
      parts.tavsiyalar.length === 0
    return empty ? null : parts
  } catch {
    return null
  }
}

function AiList({ title, items, tone }) {
  if (items.length === 0) return null
  return (
    <div className="mt-3">
      <p className={'text-[12px] font-bold uppercase tracking-wide ' + tone}>{title}</p>
      <ul className="mt-1.5 space-y-1">
        {items.map((item, i) => (
          <li key={i} className="flex gap-2 text-[14px] leading-relaxed text-slate-700">
            <span className="mt-2 h-1.5 w-1.5 shrink-0 rounded-full bg-slate-300" />
            <span className="min-w-0 break-words">{item}</span>
          </li>
        ))}
      </ul>
    </div>
  )
}

function AiSummary({ raw }) {
  const parts = parseAiSummary(raw)
  return (
    <Card>
      <div className="flex items-center gap-2 bg-brand/20 px-4 py-3">
        <Sparkles className="h-4 w-4" />
        <h2 className="text-[15px] font-bold">AI tahlil xulosasi</h2>
      </div>
      <div className="px-4 pb-4 pt-2">
        {parts ? (
          <>
            {parts.umumiy_xulosa && (
              <p className="mt-1 whitespace-pre-wrap text-[14px] leading-relaxed text-slate-800">{parts.umumiy_xulosa}</p>
            )}
            <AiList title="Kuchli tomonlar" items={parts.kuchli_tomonlar} tone="text-emerald-600" />
            <AiList title="Rivojlantirish kerak" items={parts.rivojlantirish} tone="text-red-500" />
            <AiList title="Tavsiyalar" items={parts.tavsiyalar} tone="text-brand-ink" />
          </>
        ) : (
          <p className="whitespace-pre-wrap break-words text-[14px] leading-relaxed text-slate-800">{raw}</p>
        )}
      </div>
    </Card>
  )
}

/* ------------------------------------------------------------- mezonlar */

/** Mezonlar bo'limlar bo'yicha; raqamlash qog'oz shakldagidek butun ro'yxat bo'ylab. */
function Criteria({ observation }) {
  const sections = observation.sections ?? []
  const answers = new Map((observation.details ?? []).map((d) => [d.criterionId, d]))
  const numbers = new Map(sections.flatMap((s) => s.criteria).map((c, i) => [c.id, i + 1]))

  return sections.map((section) => {
    const rows = section.criteria.filter((c) => answers.has(c.id))
    if (rows.length === 0) return null
    return (
      <Card key={section.id}>
        <p
          className="px-4 py-2.5 text-[12px] font-bold uppercase tracking-wide text-white"
          style={{ backgroundColor: section.color }}
        >
          {section.title}
        </p>
        {rows.map((criterion) => {
          const answer = answers.get(criterion.id)
          const tone =
            answer.isDone === true
              ? 'bg-emerald-50 text-emerald-600'
              : answer.isDone === false
                ? 'bg-red-50 text-red-600'
                : 'bg-slate-100 text-slate-400'
          return (
            <div key={criterion.id} className="flex items-start gap-3 border-t border-slate-100 px-4 py-3 first:border-t-0">
              <span className="mt-0.5 w-5 shrink-0 text-[12px] tabular-nums text-slate-400">{numbers.get(criterion.id)}</span>
              <span className={'mt-0.5 grid h-6 w-6 shrink-0 place-items-center rounded-lg ' + tone}>
                {answer.isDone === true ? (
                  <Check className="h-3.5 w-3.5" />
                ) : answer.isDone === false ? (
                  <X className="h-3.5 w-3.5" />
                ) : (
                  <span className="text-[12px]">–</span>
                )}
              </span>
              <div className="min-w-0 flex-1">
                {/* The text it was answered against — the sheet may have been edited since. */}
                <p className="break-words text-[14px] leading-snug text-slate-800">{answer.criterionText ?? criterion.text}</p>
                {answer.comment && (
                  <p className="mt-1.5 flex items-start gap-1.5 rounded-lg bg-slate-50 px-2.5 py-1.5 text-[12px] text-slate-600">
                    <MessageSquareText className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                    <span className="min-w-0 break-words">{answer.comment}</span>
                  </p>
                )}
              </div>
            </div>
          )
        })}
      </Card>
    )
  })
}
