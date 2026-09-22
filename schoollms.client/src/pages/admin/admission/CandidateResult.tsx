/**
 * The result of one participation, on the candidate card
 * (`docs/modules/admission-and-testing.md` §3.1 screen 2, §6.3 `AttemptReviewDto`,
 * §7.4, §8.3; unit C2).
 *
 * ONE SOURCE. Score, per-subject bars, the attempt's device and IP and the
 * per-question review all come from `GET /api/admin/exams/participants/{pid}/review`
 * through `api/services/exams.ts` — the same call and the same types the
 * Natijalar register uses. Nothing here adds points up (§8.3: the server grades).
 *
 * THE ANSWER KEY IS ON THIS SCREEN (§7.7) — the review carries
 * `correctOptionId`, which is why the page is behind `admission`. It is
 * collapsed by default: an admissions officer opening the card in front of a
 * parent should not have the key on screen by accident.
 *
 * THREE ATTEMPT ACTIONS, labelled so nobody reaches for the wrong one (§7.4.5):
 *   · "Qurilma qulfini ochish"                — `admission`; keeps the answers;
 *   · "Hozir yakunlash va baholash"           — `exams`; grades what is there;
 *   · "Urinishni bekor qilish (natija o'chadi)" — `exams` AND `admission`.
 * Each asks for a reason (the page's `CandidateReasonModal`), which the
 * server writes to the audit log.
 */
import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import {
  AlertTriangle,
  Check,
  ChevronDown,
  ChevronUp,
  LockOpen,
  RefreshCw,
  RotateCcw,
  ShieldAlert,
} from 'lucide-react'
import {
  forceFinishAttempt,
  getAttemptReview,
  resetAttempt,
  type AttemptReview,
} from '@/api/services/exams'
import {
  candidateError,
  unlockDevice,
  type CandidateAction,
  type CandidateParticipation,
} from '@/api/services/candidates'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import { ScoreBar } from '../exams/ScoreBar'
import {
  DASH,
  ONLINE_EXAMS_ENABLED,
  finishReasonLabels,
  formatPercent,
  formatScore,
  formatWallClock,
} from '../exams/examLabels'
import type { CandidatePerms } from './CandidateHelpers'
import type { ReasonRequest } from './CandidateReasonModal'

interface Props {
  participation: CandidateParticipation
  perms: CandidatePerms
  /** Opens the page's reason prompt. */
  onAsk: (request: ReasonRequest) => void
  /** Something was written; the page toasts the sentence and re-reads the card. */
  onChanged: (message: string) => void
}

type Load = { status: 'loading' } | { status: 'error'; message: string } | { status: 'ready'; data: AttemptReview }

const LETTERS = 'ABCDEF'

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="grid grid-cols-[8.5rem_1fr] gap-3 py-1.5">
      <dt className="text-xs font-medium uppercase tracking-wide text-slate-400">{label}</dt>
      <dd className="break-words text-sm text-slate-700">{children}</dd>
    </div>
  )
}

/** A failed call becomes the sentence the reason modal shows under its textarea. */
async function orSentence(action: CandidateAction, call: () => Promise<unknown>): Promise<void> {
  try {
    await call()
  } catch (err) {
    throw new Error(candidateError(err, action).message, { cause: err })
  }
}

/**
 * Mounted with a `key` built from the participation's id, status and
 * `scoredAt`, so a change the card re-read reveals (graded, reset) reloads the
 * review by itself.
 */
export function CandidateResult(props: Props) {
  // Qog'ozda o'tgan imtihonda urinish (attempt) yo'q — ko'rib chiqish endpointi
  // faqat onlayn test uchun (B3, keyinga qoldirilgan). Natija qo'lda kiritilgan
  // ball bo'lib turadi.
  if (!ONLINE_EXAMS_ENABLED || props.participation.delivery !== 'online') {
    return <ManualCandidateResult participation={props.participation} />
  }
  return <OnlineCandidateResult {...props} />
}

function ManualCandidateResult({ participation }: { participation: CandidateParticipation }) {
  return (
    <div>
      <p className="text-2xl font-semibold text-slate-800">
        {formatScore(participation.totalPoints, participation.maxPoints)}
      </p>
      <p className="text-sm text-slate-500">
        {participation.percent === null ? DASH : formatPercent(participation.percent)} · qog'ozda o'tkazilgan,
        natija qo'lda kiritilgan
      </p>
      <ScoreBar value={participation.percent} max={100} className="mt-3" label="Umumiy natija foizi" />
    </div>
  )
}

function OnlineCandidateResult({ participation, perms, onAsk, onChanged }: Props) {
  const pid = participation.participantId
  const [load, setLoad] = useState<Load>({ status: 'loading' })
  const [token, setToken] = useState(0)
  const [showQuestions, setShowQuestions] = useState(false)

  useEffect(() => {
    let cancelled = false
    getAttemptReview(pid)
      .then((data) => {
        if (!cancelled) setLoad({ status: 'ready', data })
      })
      .catch((err: unknown) => {
        if (!cancelled) setLoad({ status: 'error', message: candidateError(err, 'review').message })
      })
    return () => {
      cancelled = true
    }
  }, [pid, token])

  const retry = () => {
    setLoad({ status: 'loading' })
    setToken((t) => t + 1)
  }

  if (load.status === 'loading') return <Loader label="Natija yuklanmoqda..." />

  if (load.status === 'error') {
    return (
      <div className="flex flex-col items-center gap-3 px-4 py-10 text-center">
        <div className="flex h-11 w-11 items-center justify-center rounded-xl bg-red-50 text-red-600">
          <AlertTriangle className="h-5 w-5" />
        </div>
        <div>
          <p className="font-medium text-slate-800">Natijani yuklab bo'lmadi</p>
          <p className="mt-1 max-w-md text-sm text-slate-500">{load.message}</p>
        </div>
        <Button variant="secondary" onClick={retry}>
          <RefreshCw className="h-4 w-4" /> Qayta urinish
        </Button>
      </div>
    )
  }

  const data = load.data
  const attempt = data.attempt
  const running = attempt !== null && attempt.finishedAt === null
  const subjectNames = new Map(data.perSubject.map((s) => [s.subjectId, s.name]))
  const questions = data.questions ? [...data.questions].sort((a, b) => a.order - b.order) : null

  const askUnlock = () =>
    onAsk({
      title: 'Qurilma qulfini ochish',
      description:
        "Nomzod testni boshqa qurilmadan davom ettira oladi. Berilgan javoblar saqlanib qoladi, vaqt to'xtamaydi.",
      confirmLabel: 'Qulfni ochish',
      run: async (reason) => {
        await orSentence('unlock', () => unlockDevice(pid, reason))
        onChanged("Qurilma qulfi ochildi — nomzod boshqa qurilmadan davom eta oladi")
      },
    })

  const askForceFinish = () =>
    onAsk({
      title: 'Hozir yakunlash va baholash',
      description:
        "Urinish shu paytdagi javoblar bilan yakunlanadi va baholanadi. Nomzod davom eta olmaydi.",
      confirmLabel: 'Yakunlash',
      run: async (reason) => {
        await orSentence('forceFinish', () => forceFinishAttempt(pid, reason))
        onChanged('Urinish yakunlandi va baholandi')
      },
    })

  const askReset = () =>
    onAsk({
      title: "Urinishni bekor qilish (natija o'chadi)",
      description:
        "Urinish, uning javoblari va bali butunlay o'chiriladi, havola bekor qilinadi. Nomzod qayta topshirishi uchun yangi havola chiqarish kerak bo'ladi.",
      confirmLabel: 'Urinishni bekor qilish',
      danger: true,
      run: async (reason) => {
        await orSentence('reset', () => resetAttempt(pid, reason))
        onChanged('Urinish bekor qilindi — nomzod testni qaytadan topshira oladi')
      },
    })

  const showUnlock = running && perms.invite
  const showForceFinish = running && perms.forceFinish
  const showReset = attempt !== null && perms.reset

  return (
    <div className="divide-y divide-slate-100">
      <section className="pb-4">
        {data.summary.totalPoints === null ? (
          <p className="text-sm text-slate-500">
            {running
              ? 'Nomzod hozir test topshirmoqda — ball yakunlangach chiqadi.'
              : "Ball hali yo'q."}
          </p>
        ) : (
          <>
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
            <ScoreBar value={data.summary.percent} max={100} className="mt-3" label="Umumiy natija foizi" />
          </>
        )}
      </section>

      {data.perSubject.length > 0 && (
        <section className="py-4">
          <h4 className="mb-3 text-sm font-semibold text-slate-800">Fanlar bo'yicha</h4>
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
        </section>
      )}

      {attempt && (
        <section className="py-4">
          <h4 className="mb-1 text-sm font-semibold text-slate-800">Urinish</h4>
          <dl className="divide-y divide-slate-50">
            <Field label="Boshlagan">{formatWallClock(attempt.startedAt)}</Field>
            <Field label="Yakunlagan">
              {attempt.finishedAt ? (
                formatWallClock(attempt.finishedAt)
              ) : (
                <span className="font-medium text-amber-700">Hali yakunlanmagan</span>
              )}
            </Field>
            <Field label="Qanday">
              {attempt.finishReason ? (finishReasonLabels[attempt.finishReason] ?? attempt.finishReason) : DASH}
            </Field>
            <Field label="Javob berilgan">{attempt.answeredCount} ta savol</Field>
            <Field label="Qurilma">{attempt.deviceLabel || DASH}</Field>
            <Field label="IP manzil">
              <span className="font-mono text-xs">{attempt.firstIp || DASH}</span>
            </Field>
          </dl>
          {attempt.abuseFlagged && (
            <p className="mt-2 flex items-center gap-2 rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-700">
              <ShieldAlert className="h-4 w-4 shrink-0" />
              Javoblar g'ayritabiiy ko'p yuborilgan — urinish belgilab qo'yilgan.
            </p>
          )}

          {(showUnlock || showForceFinish || showReset) && (
            <div className="mt-3 flex flex-wrap gap-2">
              {showUnlock && (
                <Button variant="secondary" onClick={askUnlock}>
                  <LockOpen className="h-4 w-4" /> Qurilma qulfini ochish
                </Button>
              )}
              {showForceFinish && (
                <Button variant="secondary" onClick={askForceFinish}>
                  <Check className="h-4 w-4" /> Hozir yakunlash va baholash
                </Button>
              )}
              {showReset && (
                <Button
                  variant="ghost"
                  onClick={askReset}
                  className="text-red-600 hover:bg-red-50 hover:text-red-700"
                >
                  <RotateCcw className="h-4 w-4" /> Urinishni bekor qilish (natija o'chadi)
                </Button>
              )}
            </div>
          )}
        </section>
      )}

      {questions && questions.length > 0 && (
        <section className="pt-4">
          <button
            type="button"
            onClick={() => setShowQuestions((v) => !v)}
            aria-expanded={showQuestions}
            className="inline-flex items-center gap-1.5 text-sm font-semibold text-slate-800 hover:text-brand-700"
          >
            {showQuestions ? <ChevronUp className="h-4 w-4" /> : <ChevronDown className="h-4 w-4" />}
            Savollar bo'yicha ({questions.length})
          </button>
          {!showQuestions && (
            <p className="mt-1 text-xs text-slate-400">
              To'g'ri javoblar bilan — nomzod yoki ota-ona oldida ochmang.
            </p>
          )}
          {showQuestions && (
            <ol className="mt-3 space-y-3">
              {questions.map((q, i) => {
                const newBlock = i === 0 || questions[i - 1].subjectId !== q.subjectId
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
                              {selected && <span className="shrink-0 text-xs font-medium">tanlagan</span>}
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
        </section>
      )}
    </div>
  )
}
