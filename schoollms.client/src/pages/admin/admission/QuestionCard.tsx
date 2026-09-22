/**
 * One question on the bank screen (§3.1 screen 4): number, text, optional
 * image, the A–F options with the correct one highlighted.
 *
 * The highlight is the answer key (§7.7). It exists on this admin screen
 * only, behind the read-gated `admission` key; the public exam page never
 * receives `isCorrect` and does not import this component.
 */
import { Check, Pencil, Trash2 } from 'lucide-react'
import type { Question } from '@/api/services/admissionBanks'
import { cn } from '@/lib/utils'
import { optionLetter } from './BankHelpers'

interface Props {
  question: Question
  /** 1-based position in the whole bank — stays put while searching. */
  number: number
  canWrite: boolean
  busy: boolean
  onEdit: (question: Question) => void
  onDelete: (question: Question, number: number) => void
}

export function QuestionCard({ question, number, canWrite, busy, onEdit, onDelete }: Props) {
  const options = [...question.options].sort((a, b) => a.order - b.order)

  return (
    <article className="rounded-2xl border border-slate-200/80 bg-white p-4 shadow-sm">
      <div className="flex items-start gap-3">
        <span className="flex h-7 min-w-7 shrink-0 items-center justify-center rounded-lg bg-slate-100 px-1.5 text-xs font-semibold text-slate-500">
          {number}
        </span>
        <p className="min-w-0 flex-1 whitespace-pre-line break-words pt-0.5 text-sm font-medium text-slate-800">
          {question.text}
        </p>
        {canWrite && (
          <div className="flex shrink-0 items-center gap-0.5">
            <button
              type="button"
              title="Tahrirlash"
              aria-label="Tahrirlash"
              disabled={busy}
              onClick={() => onEdit(question)}
              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 disabled:cursor-not-allowed disabled:text-slate-200"
            >
              <Pencil className="h-4 w-4" />
            </button>
            <button
              type="button"
              title="O'chirish"
              aria-label="O'chirish"
              disabled={busy}
              onClick={() => onDelete(question, number)}
              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600 disabled:cursor-not-allowed disabled:text-slate-200 disabled:hover:bg-transparent"
            >
              <Trash2 className="h-4 w-4" />
            </button>
          </div>
        )}
      </div>

      {question.imageUrl && (
        <a
          href={question.imageUrl}
          target="_blank"
          rel="noreferrer"
          className="mt-3 ml-10 block w-fit"
          title="Rasmni to'liq ochish"
        >
          <img
            src={question.imageUrl}
            alt=""
            loading="lazy"
            className="max-h-56 max-w-full rounded-xl border border-slate-100 object-contain"
          />
        </a>
      )}

      <ul className="mt-3 grid gap-2 pl-10 sm:grid-cols-2">
        {options.map((option, i) => (
          <li
            key={option.id}
            className={cn(
              'flex items-start gap-2.5 rounded-xl border px-3 py-2 text-sm',
              option.isCorrect
                ? 'border-emerald-200 bg-emerald-50 text-emerald-800'
                : 'border-slate-100 bg-slate-50/60 text-slate-600',
            )}
          >
            <span
              className={cn(
                'flex h-5 w-5 shrink-0 items-center justify-center rounded-full text-[11px] font-semibold',
                option.isCorrect ? 'bg-emerald-500 text-white' : 'bg-white text-slate-500',
              )}
            >
              {optionLetter(i)}
            </span>
            <span className="min-w-0 flex-1 break-words">{option.text}</span>
            {option.isCorrect && (
              <Check className="mt-0.5 h-4 w-4 shrink-0 text-emerald-600" aria-label="To'g'ri javob" />
            )}
          </li>
        ))}
      </ul>
    </article>
  )
}
