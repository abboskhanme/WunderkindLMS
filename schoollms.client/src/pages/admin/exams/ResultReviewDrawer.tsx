/**
 * One result in full — `GET /exams/participants/{pid}/review`
 * (§6.3 `AttemptReviewDto`; unit C3). A right-hand drawer, the
 * `SubmissionDetailDrawer` pattern: the filtered register stays visible
 * behind it.
 *
 * For a paper exam it is the per-subject breakdown. For an online exam it
 * adds the attempt (device, IP, why it ended) and the paper itself WITH the
 * answer key — this endpoint is admin-only for exactly that reason (§7.7).
 *
 * TWO ADMIN ACTIONS, online only (§8.3, §3.7):
 *   · "Yakunlash" — grade an abandoned attempt now (`force-finish`), `exams`;
 *   · "Urinishni bekor qilish" — destroy the attempt and its score so the
 *     candidate can sit again (`reset-attempt`), `exams` AND `admission`.
 * Both need a written reason; it goes to the audit log.
 */
import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { AlertTriangle, Check, Loader2, RefreshCw, ShieldAlert, X } from 'lucide-react'
import {
  examsErrorMessage,
  forceFinishAttempt,
  getAttemptReview,
  resetAttempt,
  type AttemptReview,
  type ResultRow,
} from '@/api/services/exams'
import { Button } from '@/components/ui/Button'
import { Textarea } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import { ScoreBar } from './ScoreBar'
import {
  DASH,
  finishReasonLabels,
  formatDay,
  formatPercent,
  formatScore,
  formatWallClock,
  participantStatusLabel,
  participantStatusTone,
} from './examLabels'

interface Props {
  /** The row the user clicked; the drawer opens with it while the review loads. */
  row: ResultRow
  /** Holds `exams`. */
  canForceFinish: boolean
  /** Holds `exams` and `admission`. */
  canReset: boolean
  onClose: () => void
  /** Something was written; the parent toasts and re-reads its list. */
  onChanged: (message: string) => void
}

type LoadState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; data: AttemptReview }

type Action = 'forceFinish' | 'reset'

const LETTERS = 'ABCDEF'

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="grid grid-cols-[9rem_1fr] gap-3 py-2">
      <dt className="text-xs font-medium uppercase tracking-wide text-slate-400">{label}</dt>
      <dd className="break-words text-sm text-slate-700">{children}</dd>
    </div>
  )
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="border-t border-slate-100 px-5 py-4 first:border-t-0">
      <h3 className="mb-2 text-sm font-semibold text-slate-800">{title}</h3>
      {children}
    </section>
  )
}

export function ResultReviewDrawer({ row, canForceFinish, canReset, onClose, onChanged }: Props) {
  // Mounted with `key={row.participantId}`: the initial state is the loading
  // state and the effect only writes from its callbacks.
  const [state, setState] = useState<LoadState>({ status: 'loading' })
  const [attempt, setAttempt] = useState(0)

  const [action, setAction] = useState<Action | null>(null)
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    getAttemptReview(row.participantId)
      .then((data) => {
        if (!cancelled) setState({ status: 'ready', data })
      })
      .catch((err: unknown) => {
        if (!cancelled) setState({ status: 'error', message: examsErrorMessage(err, 'review.load') })
      })
    return () => {
      cancelled = true
    }
  }, [row.participantId, attempt])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && !busy && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose, busy])

  const retry = () => {
    setState({ status: 'loading' })
    setAttempt((n) => n + 1)
  }

  const openAction = (next: Action) => {
    setAction(next)
    setReason('')
    setActionError(null)
  }

  const runAction = async () => {
    if (!action || !reason.trim() || busy) return
    setBusy(true)
    setActionError(null)
    try {
      if (action === 'forceFinish') {
        const data = await forceFinishAttempt(row.participantId, reason.trim())
        setState({ status: 'ready', data })
        setAction(null)
        onChanged('Urinish yakunlandi va baholandi')
      } else {
        await resetAttempt(row.participantId, reason.trim())
        onChanged("Urinish bekor qilindi — nomzod testni qaytadan topshira oladi")
        onClose()
      }
    } catch (err) {
      setActionError(examsErrorMessage(err, action === 'forceFinish' ? 'review.forceFinish' : 'review.reset'))
    } finally {
      setBusy(false)
    }
  }

  const data = state.status === 'ready' ? state.data : null
  const live = data?.attempt ?? null
  const showForceFinish = canForceFinish && live !== null && live.finishedAt === null
  const showReset = canReset && live !== null

  const subjectNames = new Map((data?.perSubject ?? []).map((s) => [s.subjectId, s.name]))

  return (
    <div className="fixed inset-0 z-50 flex justify-end">
      <div className="absolute inset-0 bg-slate-900/40 backdrop-blur-sm" onClick={busy ? undefined : onClose} />

      <aside
        role="dialog"
        aria-modal="true"
        aria-label="Natija tafsilotlari"
        className="relative z-10 flex h-full w-full max-w-xl flex-col overflow-y-auto border-l border-slate-200 bg-white shadow-xl"
      >
        <header className="sticky top-0 z-10 flex items-start justify-between gap-3 border-b border-slate-100 bg-white px-5 py-4">
          <div className="min-w-0">
            <h2 className="truncate font-semibold text-slate-800">{row.fullName}</h2>
            <p className="mt-0.5 flex flex-wrap items-center gap-2 text-xs text-slate-400">
              <span
                className={cn(
                  'inline-flex items-center rounded-full px-2 py-0.5 font-medium',
                  participantStatusTone(row.status),
                )}
              >
                {participantStatusLabel(row.status)}
              </span>
              <span className="truncate">{row.examTitle}</span>
              <span>{formatDay(row.examDate)}</span>
            </p>
          </div>
          <button
            type="button"
            onClick={onClose}
            disabled={busy}
            aria-label="Yopish"
            className="rounded-lg p-1 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600 disabled:opacity-40"
          >
            <X className="h-5 w-5" />
          </button>
        </header>

        {state.status === 'loading' ? (
          <Loader label="Yuklanmoqda..." />
        ) : state.status === 'error' ? (
          <div className="flex flex-1 flex-col items-center justify-center gap-3 px-6 py-12 text-center">
            <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-red-50 text-red-600">
              <AlertTriangle className="h-6 w-6" />
            </div>
            <div>
              <p className="font-medium text-slate-800">Natijani ochib bo'lmadi</p>
              <p className="mt-1 text-sm text-slate-500">{state.message}</p>
            </div>
            <Button variant="secondary" onClick={retry}>
              <RefreshCw className="h-4 w-4" /> Qayta urinish
            </Button>
          </div>
        ) : data ? (
          <div className="pb-6">
            <Section title="Natija">
              <div className="flex flex-wrap items-end justify-between gap-3">
                <div>
                  <p className="text-2xl font-semibold text-slate-800">
                    {formatScore(data.summary.totalPoints, data.summary.maxPoints)}
                  </p>
                  <p className="text-sm text-slate-400">
                    ball
                    {data.summary.correctCount !== null &&
                      data.summary.totalCount !== null &&
                      ` · ${data.summary.correctCount} / ${data.summary.totalCount} to'g'ri javob`}
                  </p>
                </div>
                <p className="text-xl font-semibold text-brand-700">{formatPercent(data.summary.percent)}</p>
              </div>
              <ScoreBar
                value={data.summary.percent}
                max={100}
                className="mt-3"
                label="Umumiy natija foizi"
              />
            </Section>

            <Section title="Fanlar bo'yicha">
              {data.perSubject.length === 0 ? (
                <p className="text-sm text-slate-400">Fanlar bo'yicha ball hali yo'q.</p>
              ) : (
                <ul className="space-y-3">
                  {data.perSubject.map((s) => (
                    <li key={s.subjectId}>
                      <div className="mb-1 flex items-baseline justify-between gap-3 text-sm">
                        <span className="truncate text-slate-700">{s.name}</span>
                        <span className="whitespace-nowrap text-slate-500">
                          {formatScore(s.points, s.maxPoints)}
                          {s.correct !== null && s.total !== null && (
                            <span className="ml-2 text-xs text-slate-400">
                              ({s.correct}/{s.total})
                            </span>
                          )}
                        </span>
                      </div>
                      <ScoreBar value={s.points} max={s.maxPoints} label={s.name} />
                    </li>
                  ))}
                </ul>
              )}
            </Section>

            {live && (
              <Section title="Urinish">
                <dl className="divide-y divide-slate-50">
                  <Field label="Boshlagan">{formatWallClock(live.startedAt)}</Field>
                  <Field label="Yakunlagan">
                    {live.finishedAt ? formatWallClock(live.finishedAt) : (
                      <span className="font-medium text-amber-700">Hali yakunlanmagan</span>
                    )}
                  </Field>
                  <Field label="Qanday">
                    {live.finishReason ? finishReasonLabels[live.finishReason] ?? live.finishReason : DASH}
                  </Field>
                  <Field label="Javob berilgan">{live.answeredCount} ta savol</Field>
                  <Field label="Qurilma">{live.deviceLabel || DASH}</Field>
                  <Field label="IP manzil">
                    <span className="font-mono text-xs">{live.firstIp || DASH}</span>
                  </Field>
                </dl>
                {live.abuseFlagged && (
                  <p className="mt-2 flex items-center gap-2 rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-700">
                    <ShieldAlert className="h-4 w-4 shrink-0" />
                    Javoblar g'ayritabiiy tez yuborilgan — urinish belgilab qo'yilgan.
                  </p>
                )}
              </Section>
            )}

            {(showForceFinish || showReset) && (
              <Section title="Amallar">
                {action === null ? (
                  <div className="flex flex-wrap gap-2">
                    {showForceFinish && (
                      <Button variant="secondary" onClick={() => openAction('forceFinish')}>
                        <Check className="h-4 w-4" /> Hozir yakunlash va baholash
                      </Button>
                    )}
                    {showReset && (
                      <Button variant="secondary" onClick={() => openAction('reset')}>
                        <RefreshCw className="h-4 w-4" /> Urinishni bekor qilish
                      </Button>
                    )}
                  </div>
                ) : (
                  <div className="space-y-3 rounded-xl border border-slate-100 bg-slate-50/60 p-3 text-sm">
                    <p className="text-slate-600">
                      {action === 'forceFinish'
                        ? "Urinish shu paytdagi javoblar bilan yakunlanadi va baholanadi. Nomzod davom eta olmaydi."
                        : "Urinish, uning javoblari va bali butunlay o'chiriladi, havola bekor qilinadi. Nomzod testni qaytadan topshirishi uchun yangi havola chiqarish kerak bo'ladi."}
                    </p>
                    <Textarea
                      label="Sababi"
                      required
                      rows={2}
                      maxLength={500}
                      autoFocus
                      value={reason}
                      onChange={(e) => setReason(e.target.value)}
                    />
                    {actionError && (
                      <p className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-red-700">
                        {actionError}
                      </p>
                    )}
                    <div className="flex justify-end gap-2">
                      <Button variant="secondary" onClick={() => setAction(null)} disabled={busy}>
                        Ortga
                      </Button>
                      <Button
                        variant={action === 'reset' ? 'danger' : 'primary'}
                        onClick={() => void runAction()}
                        disabled={!reason.trim() || busy}
                      >
                        {busy && <Loader2 className="h-4 w-4 animate-spin" />}
                        {action === 'forceFinish' ? 'Yakunlash' : 'Bekor qilish'}
                      </Button>
                    </div>
                  </div>
                )}
              </Section>
            )}

            {data.questions && (
              <Section title={`Savollar (${data.questions.length})`}>
                {data.questions.length === 0 ? (
                  <p className="text-sm text-slate-400">Savollar yo'q.</p>
                ) : (
                  <ol className="space-y-4">
                    {[...data.questions]
                      .sort((a, b) => a.order - b.order)
                      .map((q, i, all) => {
                        const newBlock = i === 0 || all[i - 1].subjectId !== q.subjectId
                        return (
                          <li key={q.id}>
                            {newBlock && (
                              <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">
                                {subjectNames.get(q.subjectId) ?? DASH}
                              </p>
                            )}
                            <div
                              className={cn(
                                'rounded-xl border p-3',
                                q.isCorrect ? 'border-emerald-100' : 'border-red-100',
                              )}
                            >
                              <p className="text-sm text-slate-800">
                                <span className="mr-1.5 font-semibold text-slate-400">{i + 1}.</span>
                                {q.text}
                              </p>
                              {q.imageUrl && (
                                <img
                                  src={q.imageUrl}
                                  alt=""
                                  loading="lazy"
                                  className="mt-2 max-h-48 rounded-lg border border-slate-100"
                                />
                              )}
                              <ul className="mt-2 space-y-1">
                                {q.options.map((o, oi) => {
                                  const selected = o.id === q.selectedOptionId
                                  const correct = o.id === q.correctOptionId
                                  return (
                                    <li
                                      key={o.id}
                                      className={cn(
                                        'flex items-start gap-2 rounded-lg px-2 py-1 text-sm',
                                        correct && 'bg-emerald-50 text-emerald-800',
                                        selected && !correct && 'bg-red-50 text-red-700',
                                        !selected && !correct && 'text-slate-600',
                                      )}
                                    >
                                      <span className="w-4 shrink-0 font-semibold">{LETTERS[oi] ?? oi + 1}</span>
                                      <span className="flex-1">{o.text}</span>
                                      {selected && (
                                        <span className="shrink-0 text-xs font-medium">tanlagan</span>
                                      )}
                                    </li>
                                  )
                                })}
                              </ul>
                              {q.selectedOptionId === null && (
                                <p className="mt-1 text-xs text-slate-400">Javob berilmagan</p>
                              )}
                            </div>
                          </li>
                        )
                      })}
                  </ol>
                )}
              </Section>
            )}
          </div>
        ) : null}
      </aside>
    </div>
  )
}
