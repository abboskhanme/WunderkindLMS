/**
 * `finished` (§7.3) — the candidate's own result.
 *
 * A pure projection of `PublicResultDto`: nothing here is computed, graded or
 * inferred. The score, the percent and the per-subject breakdown are the
 * server's numbers. The per-question review is rendered only when the server
 * sends `questions`, which it does solely when the school has switched
 * `AdmissionShowAnswersToCandidate` on (§7.7, §13 Q1) — by default it is `null`
 * and this screen shows the score alone. The client never decides to show an
 * answer key.
 *
 * Reachable without the device cookie: once an attempt is finished the token
 * alone reads it (§7.3), so a candidate who reopens the link tomorrow on another
 * phone still sees their score.
 */
import { Check, CheckCircle2, X } from 'lucide-react'
import { Card } from '@/components/ui/Card'
import { cn } from '@/lib/utils'
import type {
  PublicCandidate,
  PublicFinishReason,
  PublicResult,
  PublicReviewQuestion,
} from '@/api/publicExamClient'

interface ResultViewProps {
  candidate: PublicCandidate | null
  result: PublicResult
}

const FINISH_REASON_TEXT: Record<PublicFinishReason, string> = {
  manual: "Testni o'zingiz yakunladingiz",
  timer: 'Ajratilgan vaqt tugadi — test avtomatik yakunlandi',
  admin: 'Test maktab tomonidan yakunlandi',
}

const numberFormat = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 2 })

function formatNumber(value: number): string {
  return numberFormat.format(value)
}

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

/** `DD.MM.YYYY, HH:mm` on the candidate's own clock. */
function formatDateTime(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return ''
  return `${pad(date.getDate())}.${pad(date.getMonth() + 1)}.${date.getFullYear()}, ${pad(date.getHours())}:${pad(date.getMinutes())}`
}

const OPTION_LETTERS = 'ABCDEFGH'

/**
 * A native `<progress>` — the width comes from `value`, not from an inline
 * style. The pseudo-element variants give it the same look in WebKit and Gecko.
 */
function Bar({ value, max }: { value: number; max: number }) {
  const safeMax = max > 0 ? max : 1
  return (
    <progress
      value={Math.min(safeMax, Math.max(0, value))}
      max={safeMax}
      className="h-2 w-full appearance-none overflow-hidden rounded-full bg-slate-100 [&::-moz-progress-bar]:rounded-full [&::-moz-progress-bar]:bg-brand-500 [&::-webkit-progress-bar]:bg-slate-100 [&::-webkit-progress-value]:rounded-full [&::-webkit-progress-value]:bg-brand-500"
    />
  )
}

function ReviewItem({
  question,
  number,
  subjectName,
}: {
  question: PublicReviewQuestion
  number: number
  subjectName: string | null
}) {
  return (
    <li className="space-y-3 border-t border-slate-100 pt-4 first:border-t-0 first:pt-0">
      <div className="space-y-1">
        {subjectName && <p className="text-xs font-medium text-slate-400">{subjectName}</p>}
        <p className="whitespace-pre-line break-words text-base text-slate-800">
          <span className="font-semibold text-slate-500">{number}. </span>
          {question.text}
        </p>
      </div>
      {question.imageUrl && (
        <img
          src={question.imageUrl}
          alt=""
          loading="lazy"
          className="max-h-60 max-w-full rounded-xl border border-slate-100 object-contain"
        />
      )}
      <ul className="space-y-2">
        {question.options.map((option, index) => {
          const correct = option.id === question.correctOptionId
          const chosen = option.id === question.selectedOptionId
          return (
            <li
              key={option.id}
              className={cn(
                'flex items-start gap-3 rounded-xl border px-3.5 py-2.5 text-base',
                correct
                  ? 'border-emerald-300 bg-emerald-50 text-emerald-800'
                  : chosen
                    ? 'border-rose-300 bg-rose-50 text-rose-700'
                    : 'border-slate-200 text-slate-600',
              )}
            >
              <span className="w-5 shrink-0 font-semibold">{OPTION_LETTERS[index] ?? index + 1}</span>
              <span className="min-w-0 flex-1 break-words">{option.text}</span>
              {correct && <Check className="mt-0.5 h-5 w-5 shrink-0 text-emerald-600" aria-label="To'g'ri javob" />}
              {chosen && !correct && <X className="mt-0.5 h-5 w-5 shrink-0 text-rose-500" aria-label="Sizning javobingiz" />}
            </li>
          )
        })}
      </ul>
      {question.selectedOptionId === null && (
        <p className="text-sm text-slate-400">Javob berilmagan</p>
      )}
    </li>
  )
}

export function ResultView({ candidate, result }: ResultViewProps) {
  const subjectNames = new Map(result.perSubject.map((s) => [s.subjectId, s.name]))
  const review = result.questions

  return (
    <div className="space-y-4">
      <Card className="flex flex-col items-center gap-4 py-7 text-center">
        <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-emerald-50">
          <CheckCircle2 className="h-7 w-7 text-emerald-500" />
        </div>
        <div className="space-y-1">
          <h1 className="text-lg font-semibold text-slate-800">Test yakunlandi</h1>
          {candidate && <p className="break-words text-base text-slate-600">{candidate.fullName}</p>}
          {result.finishReason && (
            <p className="text-sm text-slate-500">{FINISH_REASON_TEXT[result.finishReason]}</p>
          )}
          {result.finishedAt && (
            <p className="text-sm text-slate-400">{formatDateTime(result.finishedAt)}</p>
          )}
        </div>

        <div className="w-full space-y-2">
          <p className="text-5xl font-semibold tracking-tight text-slate-900 tabular-nums">
            {formatNumber(result.percent)}%
          </p>
          <Bar value={result.percent} max={100} />
        </div>

        <div className="grid w-full grid-cols-2 gap-2">
          <div className="rounded-xl bg-slate-50 px-3 py-3">
            <p className="text-lg font-semibold text-slate-800 tabular-nums">
              {formatNumber(result.totalPoints)} / {formatNumber(result.maxPoints)}
            </p>
            <p className="text-xs text-slate-500">ball</p>
          </div>
          <div className="rounded-xl bg-slate-50 px-3 py-3">
            <p className="text-lg font-semibold text-slate-800 tabular-nums">
              {result.correctCount} / {result.totalCount}
            </p>
            <p className="text-xs text-slate-500">to'g'ri javob</p>
          </div>
        </div>
      </Card>

      {result.perSubject.length > 0 && (
        <Card className="space-y-4">
          <h2 className="text-base font-semibold text-slate-800">Fanlar bo'yicha</h2>
          <ul className="space-y-4">
            {result.perSubject.map((subject) => (
              <li key={subject.subjectId} className="space-y-1.5">
                <div className="flex items-baseline justify-between gap-3">
                  <span className="min-w-0 break-words text-base text-slate-700">{subject.name}</span>
                  <span className="shrink-0 text-sm font-semibold text-slate-800 tabular-nums">
                    {formatNumber(subject.points)} / {formatNumber(subject.maxPoints)}
                  </span>
                </div>
                <Bar value={subject.points} max={subject.maxPoints} />
                <p className="text-xs text-slate-500 tabular-nums">
                  {subject.correct} / {subject.total} to'g'ri
                </p>
              </li>
            ))}
          </ul>
        </Card>
      )}

      {review && review.length > 0 && (
        <Card className="space-y-4">
          <h2 className="text-base font-semibold text-slate-800">Javoblar tahlili</h2>
          <ol className="space-y-4">
            {review.map((question, index) => (
              <ReviewItem
                key={question.id}
                question={question}
                number={index + 1}
                subjectName={subjectNames.get(question.subjectId) ?? null}
              />
            ))}
          </ol>
        </Card>
      )}

      <p className="px-2 text-center text-sm text-slate-500">
        Natijangiz maktabga yuborildi. Savollaringiz bo'lsa, maktab bilan bog'laning.
      </p>
    </div>
  )
}
