import type { ReactNode } from 'react'
import { Check, MessageSquareText, Sparkles, X } from 'lucide-react'
import type { LaObservation, LaSection } from '@/types/lessonAnalysis'
import { Card } from '@/components/ui/Card'
import { cn } from '@/lib/utils'
import { fmtDate, parseAiSummary } from './format'
import { Score } from './ui'

/**
 * Tahlilning faqat o'qish ko'rinishi — staff va o'qituvchi sahifalari uchun umumiy
 * (eski `EvaluationDetail.tsx`). Amal tugmalari (`actions`) sahifaning o'zidan keladi.
 */
export function ObservationView({
  observation,
  sections,
  actions,
  aiExtra,
}: {
  observation: LaObservation
  sections: LaSection[]
  actions?: ReactNode
  /** AI xulosa yo'qligida staff uchun xato va "Qayta yaratish" (o'qituvchiga ko'rsatilmaydi). */
  aiExtra?: ReactNode
}) {
  const o = observation
  return (
    <div className="space-y-4">
      <div className="min-w-0">
        <h1 className="truncate text-xl font-semibold text-slate-800">{o.teacherName}</h1>
        <p className="mt-1 text-sm text-slate-500">
          {fmtDate(o.lessonDate)} · {o.subject || 'Fan yo‘q'} · {o.className || '—'}
          {o.lessonTime ? ` · ${o.lessonTime}` : ''}
        </p>
        <p className="mt-0.5 text-xs text-slate-400">
          Kuzatuvchi: {o.observerName}
          {o.templateName ? ` · Varaq: ${o.templateName}` : ''}
        </p>
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <Card className="flex items-center justify-between gap-3">
          <div>
            <p className="text-xs font-medium uppercase tracking-wide text-slate-400">Umumiy natija</p>
            <p className="mt-2 text-3xl font-semibold leading-none tabular-nums text-slate-800">
              {o.scoreDone}
              <span className="text-slate-300"> / {o.scoreTotal}</span>
            </p>
          </div>
          <Score percent={o.scorePercent} size="lg" />
        </Card>
        {actions && <Card className="flex flex-wrap items-center gap-2.5 lg:col-span-2">{actions}</Card>}
      </div>

      {o.aiSummary ? <AiSummaryBlock raw={o.aiSummary} /> : aiExtra}

      <div className="grid gap-4 [grid-template-columns:repeat(auto-fit,minmax(min(380px,100%),1fr))]">
        <TextBlock title="Kuchli tomonlar" tone="ok" value={o.strengths} />
        <TextBlock title="Rivojlanish sohalari" tone="bad" value={o.growthAreas} />
      </div>
      <TextBlock title="Kelgusi dars uchun tavsiya" tone="accent" value={o.nextLessonAdvice} />

      <CriteriaResults observation={o} sections={sections} />
    </div>
  )
}

function AiList({ title, items, tone }: { title: string; items: string[]; tone: string }) {
  if (items.length === 0) return null
  return (
    <div>
      <h3 className={cn('text-xs font-semibold uppercase tracking-wide', tone)}>{title}</h3>
      <ul className="mt-2 space-y-1.5">
        {items.map((item, index) => (
          <li key={index} className="flex gap-2 text-sm leading-relaxed text-slate-700">
            <span className="mt-2 h-1.5 w-1.5 shrink-0 rounded-full bg-slate-300" />
            <span>{item}</span>
          </li>
        ))}
      </ul>
    </div>
  )
}

/** AI xulosa: JSON bo'lsa — to'rt bo'lim; eski tizimdan ko'chgan matn bo'lsa — matnning o'zi. */
export function AiSummaryBlock({ raw }: { raw: string }) {
  const parts = parseAiSummary(raw)
  return (
    <Card className="overflow-hidden p-0">
      <header className="flex items-center gap-2.5 border-b border-brand-100 bg-brand-50 px-5 py-3.5 text-brand-800">
        <Sparkles className="h-4 w-4" />
        <h2 className="text-sm font-semibold">AI tahlil xulosasi</h2>
      </header>
      {parts ? (
        <div className="space-y-5 p-5">
          {parts.umumiy_xulosa && (
            <div>
              <h3 className="text-xs font-semibold uppercase tracking-wide text-slate-500">Umumiy xulosa</h3>
              <p className="mt-2 whitespace-pre-wrap text-sm leading-relaxed text-slate-800">{parts.umumiy_xulosa}</p>
            </div>
          )}
          <div className="grid gap-5 [grid-template-columns:repeat(auto-fit,minmax(min(260px,100%),1fr))]">
            <AiList title="Kuchli tomonlar" items={parts.kuchli_tomonlar} tone="text-emerald-600" />
            <AiList title="Rivojlantirish kerak" items={parts.rivojlantirish} tone="text-red-500" />
          </div>
          <AiList title="Tavsiyalar" items={parts.tavsiyalar} tone="text-brand-600" />
        </div>
      ) : (
        <div className="whitespace-pre-wrap p-5 text-sm leading-relaxed text-slate-800">{raw}</div>
      )}
    </Card>
  )
}

function TextBlock({ title, tone, value }: { title: string; tone: 'ok' | 'bad' | 'accent'; value: string | null }) {
  if (!value) return null
  const bar = { ok: 'bg-emerald-500', bad: 'bg-red-500', accent: 'bg-brand-500' }[tone]
  return (
    <Card className="overflow-hidden p-0">
      <header className="flex items-center gap-3 border-b border-slate-100 px-5 py-3.5">
        <span className={cn('h-4 w-1 shrink-0 rounded-full', bar)} />
        <h2 className="text-sm font-semibold text-slate-800">{title}</h2>
      </header>
      <p className="whitespace-pre-wrap p-5 text-sm leading-relaxed text-slate-700">{value}</p>
    </Card>
  )
}

/** Mezonlar natijasi bo'limlar bo'yicha; raqamlash qog'oz shakldagidek butun ro'yxat bo'ylab. */
function CriteriaResults({ observation, sections }: { observation: LaObservation; sections: LaSection[] }) {
  const answers = new Map(observation.details.map((d) => [d.criterionId, d]))
  const numbers = new Map(sections.flatMap((s) => s.criteria).map((c, index) => [c.id, index + 1]))

  return (
    <>
      {sections.map((section) => {
        const rows = section.criteria.filter((c) => answers.has(c.id))
        if (rows.length === 0) return null
        return (
          <Card key={section.id} className="overflow-hidden p-0">
            <header
              className="px-5 py-3 text-[13px] font-semibold uppercase tracking-wide text-white"
              style={{ backgroundColor: section.color }}
            >
              {section.title}
            </header>
            <ul className="divide-y divide-slate-100">
              {rows.map((criterion) => {
                const answer = answers.get(criterion.id)!
                return (
                  <li key={criterion.id} className="flex items-start gap-3 px-4 py-3 sm:px-5">
                    <span className="mt-0.5 w-6 shrink-0 text-xs font-medium tabular-nums text-slate-400">
                      {numbers.get(criterion.id)}
                    </span>
                    <span
                      className={cn(
                        'mt-0.5 grid h-6 w-6 shrink-0 place-items-center rounded-lg',
                        answer.isDone === true
                          ? 'bg-emerald-50 text-emerald-600'
                          : answer.isDone === false
                            ? 'bg-red-50 text-red-600'
                            : 'bg-slate-100 text-slate-400',
                      )}
                    >
                      {answer.isDone === true ? (
                        <Check className="h-3.5 w-3.5" />
                      ) : answer.isDone === false ? (
                        <X className="h-3.5 w-3.5" />
                      ) : (
                        <span className="text-xs">–</span>
                      )}
                    </span>
                    <div className="min-w-0">
                      {/* The text it was answered against — the sheet may have been edited since. */}
                      <p className="text-sm leading-snug text-slate-800">{answer.criterionText ?? criterion.text}</p>
                      {answer.comment && (
                        <p className="mt-1.5 flex items-start gap-1.5 rounded-lg bg-slate-50 px-2.5 py-1.5 text-xs text-slate-600">
                          <MessageSquareText className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                          {answer.comment}
                        </p>
                      )}
                    </div>
                  </li>
                )
              })}
            </ul>
          </Card>
        )
      })}
    </>
  )
}
