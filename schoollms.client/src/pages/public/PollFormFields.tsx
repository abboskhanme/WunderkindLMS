/**
 * A poll's questions as a form — the public page `/sorovnoma/:token` and the editor's
 * "Ko'rinishi" preview render the same thing (docs/modules/surveys-forms.md).
 *
 * Controls are one size up from `components/ui` (16 px, thumb-sized), like the enrolment
 * form's `SurveyFields.tsx`: iOS zooms into any field under 16 px. The answer type and the
 * checks live in `pollAnswers.ts`.
 *
 * The Telegram Mini App has its own renderer (`ui-tg/src/components/PollSheet.jsx`) —
 * a separate build that cannot import from here.
 */
import { Star } from 'lucide-react'
import { Card } from '@/components/ui/Card'
import { cn } from '@/lib/utils'
import { POLL_OTHER, type PollQuestion } from '@/api/services/polls'
import { questionNumbers, type AnswerValue, type Answers } from './pollAnswers'

const controlBase =
  'w-full rounded-xl border bg-white px-3.5 py-3 text-base text-slate-800 outline-none transition-colors placeholder:text-slate-400'
const controlIdle = 'border-slate-200 focus:border-brand-400 focus:ring-2 focus:ring-brand-100'

interface FormProps {
  questions: PollQuestion[]
  answers: Answers
  errors: Record<string, string>
  onChange: (questionId: string, value: AnswerValue) => void
}

/** Every question in its own card; a section is a heading between them. Sections are not numbered. */
export function PollQuestionsForm({ questions, answers, errors, onChange }: FormProps) {
  const numbers = questionNumbers(questions)
  return (
    <>
      {questions.map((q) => {
        if (q.kind === 'section')
          return (
            <div key={q.id} className="px-1 pt-3">
              <h2 className="text-lg font-semibold text-slate-800">{q.text}</h2>
              {q.description && <p className="mt-1 whitespace-pre-line text-sm text-slate-500">{q.description}</p>}
            </div>
          )
        const error = errors[q.id]
        return (
          <Card key={q.id} className={cn('space-y-3', error && 'border-rose-200')}>
            <div>
              <p className="text-base font-semibold leading-snug text-slate-800">
                {numbers[q.id]}. {q.text}
                {q.required && <span className="text-rose-500"> *</span>}
              </p>
              {q.description && <p className="mt-1 whitespace-pre-line text-sm text-slate-500">{q.description}</p>}
            </div>
            <QuestionField q={q} value={answers[q.id] ?? {}} onChange={(v) => onChange(q.id, v)} />
            {error && (
              <p role="alert" className="text-sm text-rose-600">
                {error}
              </p>
            )}
          </Card>
        )
      })}
    </>
  )
}

function QuestionField({ q, value, onChange }: { q: PollQuestion; value: AnswerValue; onChange: (v: AnswerValue) => void }) {
  switch (q.kind) {
    case 'nps':
      return (
        <div>
          <div className="grid grid-cols-6 gap-1.5 sm:grid-cols-11">
            {Array.from({ length: 11 }, (_, n) => (
              <ScoreButton
                key={n}
                selected={value.score === n}
                tone={n >= 9 ? 'bg-emerald-500' : n >= 7 ? 'bg-amber-400' : 'bg-rose-500'}
                onClick={() => onChange({ score: n })}
              >
                {n}
              </ScoreButton>
            ))}
          </div>
          <EndLabels low="Umuman yo'q" high="Albatta" />
        </div>
      )
    case 'stars':
      return (
        <div className="flex gap-2">
          {[1, 2, 3, 4, 5].map((n) => (
            <button key={n} type="button" aria-label={`${n} yulduz`} onClick={() => onChange({ score: n })}>
              <Star className={cn('h-9 w-9', (value.score ?? 0) >= n ? 'fill-amber-400 text-amber-400' : 'text-slate-300')} />
            </button>
          ))}
        </div>
      )
    case 'scale': {
      const low = q.minValue ?? 1
      const high = q.maxValue ?? 5
      return (
        <div>
          <div className="flex flex-wrap gap-1.5">
            {Array.from({ length: high - low + 1 }, (_, i) => low + i).map((n) => (
              <ScoreButton key={n} selected={value.score === n} tone="bg-brand-600" onClick={() => onChange({ score: n })} wide>
                {n}
              </ScoreButton>
            ))}
          </div>
          {(q.minLabel || q.maxLabel) && <EndLabels low={q.minLabel ?? ''} high={q.maxLabel ?? ''} />}
        </div>
      )
    }
    case 'choice':
      return (
        <div className="space-y-2">
          {q.options.map((o) => (
            <OptionRow key={o} round selected={!value.otherOn && value.choice === o} onClick={() => onChange({ choice: o })}>
              {o}
            </OptionRow>
          ))}
          {q.allowOther && (
            <OtherRow round value={value} onChange={(other, on) => onChange({ otherOn: on, other })} />
          )}
        </div>
      )
    case 'multi': {
      const picked = value.choices ?? []
      const toggle = (o: string) =>
        onChange({ ...value, choices: picked.includes(o) ? picked.filter((x) => x !== o) : [...picked, o] })
      const hint =
        q.minChoices !== null && q.maxChoices !== null
          ? q.minChoices === q.maxChoices
            ? `${q.minChoices} ta tanlang`
            : `${q.minChoices}–${q.maxChoices} ta tanlang`
          : q.minChoices !== null
            ? `Kamida ${q.minChoices} ta tanlang`
            : q.maxChoices !== null
              ? `Ko'pi bilan ${q.maxChoices} ta tanlang`
              : null
      return (
        <div className="space-y-2">
          {hint && <p className="text-sm text-slate-400">{hint}</p>}
          {q.options.map((o) => (
            <OptionRow key={o} selected={picked.includes(o)} onClick={() => toggle(o)}>
              {o}
            </OptionRow>
          ))}
          {q.allowOther && <OtherRow value={value} onChange={(other, on) => onChange({ ...value, otherOn: on, other })} />}
        </div>
      )
    }
    case 'dropdown':
      return (
        <select
          value={value.choice ?? ''}
          onChange={(e) => onChange(e.target.value ? { choice: e.target.value } : {})}
          className={cn(controlBase, controlIdle)}
        >
          <option value="">Tanlang</option>
          {q.options.map((o) => (
            <option key={o} value={o}>
              {o}
            </option>
          ))}
        </select>
      )
    case 'number':
      return (
        <input
          inputMode="decimal"
          value={value.number ?? ''}
          onChange={(e) => onChange({ number: e.target.value })}
          placeholder={
            q.minValue !== null && q.maxValue !== null ? `${q.minValue} – ${q.maxValue}` : 'Son'
          }
          className={cn(controlBase, controlIdle)}
        />
      )
    case 'date':
      return (
        <input
          type="date"
          value={value.date ?? ''}
          onChange={(e) => onChange({ date: e.target.value })}
          className={cn(controlBase, controlIdle)}
        />
      )
    case 'short':
      return (
        <input
          value={value.text ?? ''}
          maxLength={500}
          onChange={(e) => onChange({ text: e.target.value })}
          placeholder="Javobingiz"
          className={cn(controlBase, controlIdle)}
        />
      )
    case 'text':
      return (
        <textarea
          value={value.text ?? ''}
          maxLength={2000}
          rows={4}
          onChange={(e) => onChange({ text: e.target.value })}
          placeholder="Javobingiz"
          className={cn(controlBase, controlIdle)}
        />
      )
    default:
      return null
  }
}

function ScoreButton({
  selected,
  tone,
  wide,
  onClick,
  children,
}: {
  selected: boolean
  tone: string
  wide?: boolean
  onClick: () => void
  children: React.ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'h-11 rounded-xl text-base font-semibold transition-colors',
        wide ? 'min-w-11 flex-1 px-2' : '',
        selected ? `${tone} text-white` : 'bg-slate-100 text-slate-600 hover:bg-slate-200',
      )}
    >
      {children}
    </button>
  )
}

function EndLabels({ low, high }: { low: string; high: string }) {
  return (
    <div className="mt-1.5 flex justify-between gap-3 text-xs text-slate-400">
      <span>{low}</span>
      <span className="text-right">{high}</span>
    </div>
  )
}

/** A tappable option: a round mark for "one of", a square one for "any of". */
function OptionRow({
  selected,
  round,
  onClick,
  children,
}: {
  selected: boolean
  round?: boolean
  onClick: () => void
  children: React.ReactNode
}) {
  return (
    <button
      type="button"
      role={round ? 'radio' : 'checkbox'}
      aria-checked={selected}
      onClick={onClick}
      className={cn(
        'flex w-full items-center gap-3 rounded-xl border px-3.5 py-3 text-left text-base transition-colors',
        selected ? 'border-brand-500 bg-brand-50 text-brand-700' : 'border-slate-200 bg-white text-slate-700 hover:bg-slate-50',
      )}
    >
      <span
        className={cn(
          'flex h-5 w-5 shrink-0 items-center justify-center border-2',
          round ? 'rounded-full' : 'rounded-md',
          selected ? 'border-brand-500 bg-brand-500' : 'border-slate-300',
        )}
      >
        {selected && <span className={cn('bg-white', round ? 'h-2 w-2 rounded-full' : 'h-2 w-2 rounded-sm')} />}
      </span>
      <span className="min-w-0 break-words">{children}</span>
    </button>
  )
}

function OtherRow({
  value,
  round,
  onChange,
}: {
  value: AnswerValue
  round?: boolean
  onChange: (other: string, on: boolean) => void
}) {
  return (
    <div className="space-y-2">
      <OptionRow round={round} selected={Boolean(value.otherOn)} onClick={() => onChange(value.other ?? '', !value.otherOn || Boolean(round))}>
        {POLL_OTHER}
      </OptionRow>
      {value.otherOn && (
        <input
          autoFocus
          value={value.other ?? ''}
          maxLength={500}
          onChange={(e) => onChange(e.target.value, true)}
          placeholder="O'z javobingiz"
          className={cn(controlBase, controlIdle)}
        />
      )}
    </div>
  )
}
