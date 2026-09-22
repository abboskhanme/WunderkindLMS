/**
 * The subjects of an ONLINE (admission) exam — one row per question bank
 * (§5.6, §8.1). Mounted with `key={grade}`, so choosing another grade starts
 * a fresh load; the initial state is the loading state and the effect writes
 * only from its callbacks.
 *
 * WHAT IT DOES NOT DO. It does not decide whether the exam can be published —
 * the server does, with the §8.1 reason. It only says, next to each row, what
 * the server will say: the bank is not ready, or holds fewer questions than
 * asked for.
 */
import { useEffect, useState } from 'react'
import { AlertTriangle, ArrowDown, ArrowUp, Plus, RefreshCw, X } from 'lucide-react'
import { examsErrorMessage, listBankOptions, type BankOption } from '@/api/services/exams'
import { Button } from '@/components/ui/Button'
import { cn } from '@/lib/utils'
import { bankStateLabels, formatPoints, gradeLabel } from './examLabels'
import { move, newSectionDraft, parsePositiveInt, type SectionDraft } from './examForm'

const field =
  'w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100'

interface Props {
  grade: number
  sections: SectionDraft[]
  /** `seededTimeLimit` — the sum of the chosen banks' minutes (§5.6), for the parent to offer. */
  onChange: (sections: SectionDraft[], seededTimeLimit: number | null) => void
  /** Only after a submit attempt — the form does not shout while it is being filled. */
  showProblems: boolean
}

type LoadState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; banks: BankOption[] }

export function BankSectionsEditor({ grade, sections, onChange, showProblems }: Props) {
  const [state, setState] = useState<LoadState>({ status: 'loading' })
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    let cancelled = false
    listBankOptions(grade)
      .then((banks) => {
        if (!cancelled) setState({ status: 'ready', banks })
      })
      .catch((err: unknown) => {
        if (!cancelled) setState({ status: 'error', message: examsErrorMessage(err, 'banks.load') })
      })
    return () => {
      cancelled = true
    }
  }, [grade, attempt])

  const retry = () => {
    setState({ status: 'loading' })
    setAttempt((n) => n + 1)
  }

  if (state.status === 'loading') {
    return <p className="py-3 text-sm text-slate-400">Test bazalari yuklanmoqda…</p>
  }

  if (state.status === 'error') {
    return (
      <div className="flex flex-wrap items-center gap-3 rounded-lg border border-red-200 bg-red-50 px-3 py-2">
        <p className="flex-1 text-sm text-red-700">{state.message}</p>
        <Button variant="secondary" onClick={retry}>
          <RefreshCw className="h-4 w-4" /> Qayta urinish
        </Button>
      </div>
    )
  }

  const banks = state.banks
  const byId = new Map(banks.map((b) => [b.id, b]))

  if (banks.length === 0) {
    return (
      <p className="rounded-lg bg-slate-50 px-3 py-3 text-sm text-slate-500">
        {gradeLabel(grade)} uchun test bazasi yo'q. Avval "Qabul → Test bazasi" bo'limida shu sinf
        uchun baza yarating va savollarni kiriting.
      </p>
    )
  }

  /** Sum of the chosen banks' minutes — the §5.6 seed for the exam's time limit. */
  const seed = (next: SectionDraft[]): number | null => {
    let total = 0
    let any = false
    for (const s of next) {
      const minutes = byId.get(s.bankId)?.timeLimitMin
      if (minutes) {
        total += minutes
        any = true
      }
    }
    return any ? total : null
  }

  const emit = (next: SectionDraft[]) => onChange(next, seed(next))

  const pickBank = (index: number, bankId: string) => {
    const bank = byId.get(bankId)
    emit(
      sections.map((s, i) =>
        i === index
          ? {
              ...s,
              bankId,
              subjectId: bank?.subjectId ?? '',
              questionCount: bank?.questionsPerTest ? String(bank.questionsPerTest) : s.questionCount,
            }
          : s,
      ),
    )
  }

  const setCount = (index: number, value: string) =>
    emit(sections.map((s, i) => (i === index ? { ...s, questionCount: value } : s)))

  const remove = (index: number) => emit(sections.filter((_, i) => i !== index))

  const usedSubjects = (index: number) =>
    new Set(sections.filter((_, i) => i !== index).map((s) => s.subjectId))

  const canAdd = sections.length < banks.length

  return (
    <div className="space-y-2">
      {sections.length === 0 && (
        <p className={cn('text-sm', showProblems ? 'text-red-600' : 'text-slate-400')}>
          Kamida bitta fan (test bazasi) qo'shing.
        </p>
      )}

      {sections.map((s, index) => {
        const bank = byId.get(s.bankId)
        const count = parsePositiveInt(s.questionCount)
        const taken = usedSubjects(index)
        const countBad = s.bankId !== '' && (count === null || Number.isNaN(count))
        return (
          <div key={s.key} className="rounded-xl border border-slate-100 bg-slate-50/60 p-3">
            <div className="flex flex-wrap items-center gap-2">
              <span className="w-5 text-center text-xs font-medium text-slate-400">{index + 1}</span>
              <select
                value={s.bankId}
                onChange={(e) => pickBank(index, e.target.value)}
                aria-label={`${index + 1}-fan: test bazasi`}
                className={cn(
                  field,
                  'min-w-[14rem] flex-1',
                  showProblems && !s.bankId && 'border-red-300',
                )}
              >
                <option value="">Test bazasini tanlang</option>
                {banks
                  .filter((b) => b.id === s.bankId || !taken.has(b.subjectId))
                  .map((b) => (
                    <option key={b.id} value={b.id}>
                      {b.subjectName} · {b.questionsCount} ta savol · {bankStateLabels[b.state] ?? b.state}
                    </option>
                  ))}
              </select>
              <label className="flex items-center gap-2 text-sm text-slate-500">
                Savollar
                <input
                  value={s.questionCount}
                  onChange={(e) => setCount(index, e.target.value)}
                  inputMode="numeric"
                  aria-label={`${index + 1}-fan: testdagi savollar soni`}
                  className={cn(field, 'w-20 text-center', showProblems && countBad && 'border-red-300')}
                />
              </label>
              <div className="flex items-center">
                <button
                  type="button"
                  onClick={() => emit(move(sections, index, -1))}
                  disabled={index === 0}
                  title="Yuqoriga"
                  aria-label="Yuqoriga"
                  className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 disabled:cursor-not-allowed disabled:text-slate-200"
                >
                  <ArrowUp className="h-4 w-4" />
                </button>
                <button
                  type="button"
                  onClick={() => emit(move(sections, index, 1))}
                  disabled={index === sections.length - 1}
                  title="Pastga"
                  aria-label="Pastga"
                  className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 disabled:cursor-not-allowed disabled:text-slate-200"
                >
                  <ArrowDown className="h-4 w-4" />
                </button>
                <button
                  type="button"
                  onClick={() => remove(index)}
                  title="Olib tashlash"
                  aria-label="Olib tashlash"
                  className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                >
                  <X className="h-4 w-4" />
                </button>
              </div>
            </div>

            {bank && (
              <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1 pl-7 text-xs text-slate-500">
                <span>
                  Ball: {bank.pointsPerCorrect === null ? '—' : formatPoints(bank.pointsPerCorrect)} / to'g'ri javob
                </span>
                <span>Vaqt: {bank.timeLimitMin === null ? '—' : `${bank.timeLimitMin} daqiqa`}</span>
                {bank.state !== 'ready' && (
                  <span className="inline-flex items-center gap-1 font-medium text-amber-700">
                    <AlertTriangle className="h-3.5 w-3.5" />
                    Baza {bankStateLabels[bank.state] ?? bank.state} — e'lon qilishdan oldin to'ldiring
                  </span>
                )}
                {count !== null && !Number.isNaN(count) && count > bank.questionsCount && (
                  <span className="inline-flex items-center gap-1 font-medium text-amber-700">
                    <AlertTriangle className="h-3.5 w-3.5" />
                    Bazada atigi {bank.questionsCount} ta savol bor
                  </span>
                )}
              </div>
            )}
          </div>
        )
      })}

      {canAdd && (
        <button
          type="button"
          onClick={() => emit([...sections, newSectionDraft()])}
          className="inline-flex items-center gap-1.5 rounded-lg px-2 py-1.5 text-sm font-medium text-brand-600 transition-colors hover:bg-brand-50"
        >
          <Plus className="h-4 w-4" /> Fan qo'shish
        </button>
      )}
    </div>
  )
}
