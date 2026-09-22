/**
 * `in_progress` (§7.3) — the paper.
 *
 * What the server fixes and this screen only reflects:
 * - the question set and its order (materialised at `start`, §5.11), so a reload
 *   shows the same paper with every saved pick still selected;
 * - the deadline (§7.1 fact 4). The timer is display-only and re-synced from
 *   every `answer` response.
 *
 * What this screen owns:
 * - one `answer` per click, painted first and sent second (§7.8). Picks for the
 *   same question are sent strictly one after another, so the last one the
 *   candidate tapped is the last one the server stores even on a network that
 *   reorders requests;
 * - `finish`, exactly once: confirmed on the manual path, unasked on the timer
 *   path (§7.1 fact 10), and never before every in-flight pick has landed.
 *
 * Nothing about the paper is written to storage. Resume after a reload is the
 * server's `state` answer, not a local copy.
 */
import { useEffect, useId, useRef, useState } from 'react'
import { AlertTriangle, Check, Loader2, RefreshCw } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Modal } from '@/components/ui/Modal'
import { cn } from '@/lib/utils'
import { examClock, finishExam, retryableMessage, saveExamAnswer } from '@/api/publicExamClient'
import type {
  ExamClock,
  PublicExamFailure,
  PublicExamQuestion,
  PublicPaper,
  PublicResult,
} from '@/api/publicExamClient'
import { Timer } from './QabulTestTimer'

/** What the paper hands back to the page when it can no longer continue. */
export type PaperOutcome =
  | { kind: 'finished'; result: PublicResult }
  | { kind: 'blocked'; deviceLabel: string | null }
  | { kind: 'not_found' }
  | { kind: 'refetch' }

interface PaperProps {
  token: string
  paper: PublicPaper
  clock: ExamClock
  onOutcome: (outcome: PaperOutcome) => void
}

type FinishPath = 'manual' | 'timer'

/** Absent key = saved (or never touched). */
type SaveStatus = 'saving' | 'failed'

type FinishPhase =
  | { kind: 'idle' }
  | { kind: 'confirm' }
  | { kind: 'finishing'; path: FinishPath }
  | { kind: 'failed'; path: FinishPath; message: string }
  /** Timer path only: the result is in, the time-up dialog waits for a tap. */
  | { kind: 'ready'; result: PublicResult }

const OPTION_LETTERS = 'ABCDEFGH'

interface SubjectBlock {
  key: string
  name: string
  items: Array<{ question: PublicExamQuestion; number: number }>
}

/**
 * Consecutive runs of one subject become one block — the same walk EduSchool
 * does (§8.2): a new block starts whenever `subjectId` changes.
 */
function groupBySubject(paper: PublicPaper): SubjectBlock[] {
  const names = new Map(paper.subjects.map((s) => [s.id, s.name]))
  const blocks: SubjectBlock[] = []
  let currentSubjectId: string | null = null
  for (let index = 0; index < paper.questions.length; index++) {
    const question = paper.questions[index]
    const item = { question, number: index + 1 }
    const last = blocks[blocks.length - 1]
    if (last && currentSubjectId === question.subjectId) {
      last.items.push(item)
      continue
    }
    currentSubjectId = question.subjectId
    blocks.push({
      key: `${question.subjectId}-${index}`,
      name: names.get(question.subjectId) ?? 'Fan',
      items: [item],
    })
  }
  return blocks
}

function initialSelection(paper: PublicPaper): Record<string, string | null> {
  const selection: Record<string, string | null> = {}
  for (const question of paper.questions) selection[question.id] = question.selectedOptionId
  return selection
}

/** Event-handler use only — reads the wall clock. */
function deadlinePassed(clock: ExamClock): boolean {
  return Date.now() >= clock.deadlineMs
}

function without<T>(record: Record<string, T>, key: string): Record<string, T> {
  if (!(key in record)) return record
  const next = { ...record }
  delete next[key]
  return next
}

// ---------------------------------------------------------------------------
//  Pieces
// ---------------------------------------------------------------------------

interface SaveIndicatorProps {
  saving: boolean
  failed: boolean
  answered: number
  onRetry: () => void
}

function SaveIndicator({ saving, failed, answered, onRetry }: SaveIndicatorProps) {
  if (saving) {
    return (
      <span className="flex items-center gap-1.5 text-sm text-slate-500">
        <Loader2 className="h-4 w-4 animate-spin" />
        Saqlanmoqda…
      </span>
    )
  }
  if (failed) {
    return (
      <button
        type="button"
        onClick={onRetry}
        className="flex items-center gap-1.5 rounded-full px-2 py-1 text-sm font-medium text-rose-600 transition-colors hover:bg-rose-50"
      >
        <AlertTriangle className="h-4 w-4" />
        Saqlanmadi
      </button>
    )
  }
  if (answered === 0) return <span className="text-sm text-slate-400">Javob tanlang</span>
  return (
    <span className="flex items-center gap-1.5 text-sm text-emerald-600">
      <Check className="h-4 w-4" />
      Saqlandi
    </span>
  )
}

interface QuestionCardProps {
  question: PublicExamQuestion
  number: number
  selectedOptionId: string | null
  status: SaveStatus | undefined
  locked: boolean
  onChoose: (questionId: string, optionId: string) => void
}

function QuestionCard({ question, number, selectedOptionId, status, locked, onChoose }: QuestionCardProps) {
  const [imageBroken, setImageBroken] = useState(false)
  const legendId = useId()

  return (
    <Card className="p-4">
      <fieldset className="min-w-0" aria-labelledby={legendId}>
        <div className="flex items-start gap-3">
          <span
            className={cn(
              'flex h-7 min-w-7 shrink-0 items-center justify-center rounded-full px-1.5 text-sm font-semibold',
              selectedOptionId ? 'bg-brand-600 text-white' : 'bg-slate-100 text-slate-500',
            )}
          >
            {number}
          </span>
          <p id={legendId} className="min-w-0 flex-1 whitespace-pre-line break-words pt-0.5 text-base text-slate-800">
            {question.text}
          </p>
        </div>

        {question.imageUrl && !imageBroken && (
          <img
            src={question.imageUrl}
            alt=""
            loading="lazy"
            onError={() => setImageBroken(true)}
            className="mt-3 max-h-72 max-w-full rounded-xl border border-slate-100 object-contain"
          />
        )}

        <div className="mt-3 space-y-2">
          {question.options.map((option, index) => {
            const selected = option.id === selectedOptionId
            return (
              <label
                key={option.id}
                className={cn(
                  'flex min-h-12 items-start gap-3 rounded-xl border px-3.5 py-2.5 text-base transition-colors has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-brand-200',
                  selected
                    ? 'border-brand-500 bg-brand-50 text-brand-800'
                    : 'border-slate-200 bg-white text-slate-700',
                  locked ? 'cursor-default' : 'cursor-pointer',
                  !locked && !selected && 'hover:bg-slate-50',
                )}
              >
                <input
                  type="radio"
                  name={`q-${question.id}`}
                  value={option.id}
                  checked={selected}
                  disabled={locked}
                  onChange={() => onChoose(question.id, option.id)}
                  className="sr-only"
                />
                <span
                  className={cn(
                    'flex h-7 w-7 shrink-0 items-center justify-center rounded-full border text-sm font-semibold',
                    selected ? 'border-brand-600 bg-brand-600 text-white' : 'border-slate-300 text-slate-500',
                  )}
                >
                  {OPTION_LETTERS[index] ?? index + 1}
                </span>
                <span className="min-w-0 flex-1 break-words pt-0.5">{option.text}</span>
              </label>
            )
          })}
        </div>

        {status === 'saving' && (
          <p className="mt-2 flex items-center gap-1.5 text-sm text-slate-400">
            <Loader2 className="h-3.5 w-3.5 animate-spin" />
            Saqlanmoqda…
          </p>
        )}
        {status === 'failed' && (
          <p className="mt-2 flex items-center gap-1.5 text-sm text-rose-600">
            <AlertTriangle className="h-3.5 w-3.5" />
            Javob saqlanmadi
          </p>
        )}
      </fieldset>
    </Card>
  )
}

// ---------------------------------------------------------------------------
//  Paper
// ---------------------------------------------------------------------------

export function Paper({ token, paper, clock: initialClock, onOutcome }: PaperProps) {
  const [selected, setSelected] = useState(() => initialSelection(paper))
  const [saveStatus, setSaveStatus] = useState<Record<string, SaveStatus>>({})
  const [saveError, setSaveError] = useState<string | null>(null)
  const [clock, setClock] = useState(initialClock)
  const [finish, setFinish] = useState<FinishPhase>({ kind: 'idle' })

  /** One running save loop per question; `finish` waits for all of them. */
  const inFlightRef = useRef(new Map<string, Promise<void>>())
  /** The newest pick made while that question's save was still in flight. */
  const queuedRef = useRef(new Map<string, string>())
  /** The §7.8 guard: the interval fires again before `finish` returns. */
  const finishStartedRef = useRef(false)
  /** Set once an outcome has gone to the page; everything after is ignored. */
  const endedRef = useRef(false)

  const blockIdPrefix = useId()
  const blocks = groupBySubject(paper)
  const total = paper.questions.length
  const answered = paper.questions.reduce((count, q) => (selected[q.id] ? count + 1 : count), 0)
  const statuses = Object.values(saveStatus)
  const saving = statuses.includes('saving')
  const failed = statuses.includes('failed')
  const locked = finish.kind === 'finishing' || finish.kind === 'failed' || finish.kind === 'ready'

  const emit = (outcome: PaperOutcome) => {
    if (endedRef.current) return
    endedRef.current = true
    onOutcome(outcome)
  }

  // Leaving while a pick is unsent is the one way to lose it — say so.
  const unsafeToLeave = saving || failed || finish.kind === 'finishing'
  useEffect(() => {
    if (!unsafeToLeave) return
    const onBeforeUnload = (event: BeforeUnloadEvent) => {
      event.preventDefault()
      event.returnValue = ''
    }
    window.addEventListener('beforeunload', onBeforeUnload)
    return () => window.removeEventListener('beforeunload', onBeforeUnload)
  }, [unsafeToLeave])

  // -- finish ---------------------------------------------------------------

  const runFinish = async (path: FinishPath) => {
    setFinish({ kind: 'finishing', path })
    // A pick still on the wire must reach the server before `finish` does.
    while (inFlightRef.current.size > 0) {
      await Promise.allSettled([...inFlightRef.current.values()])
    }
    if (endedRef.current) return

    const res = await finishExam(token)
    if (endedRef.current) return
    switch (res.kind) {
      case 'ok':
        if (path === 'manual') emit({ kind: 'finished', result: res.data })
        else setFinish({ kind: 'ready', result: res.data })
        return
      case 'blocked':
        // Past the deadline a missing cookie is far likelier to be its expiry
        // than a second device — let `state` say which.
        emit(path === 'timer' ? { kind: 'refetch' } : { kind: 'blocked', deviceLabel: res.deviceLabel })
        return
      case 'not_found':
        emit({ kind: 'not_found' })
        return
      case 'time_up':
      case 'not_in_progress':
        emit({ kind: 'refetch' })
        return
      case 'rate_limited':
      case 'unavailable':
        setFinish({ kind: 'failed', path, message: retryableMessage(res) })
        return
    }
  }

  const beginFinish = (path: FinishPath) => {
    // Timer, a late `time_up` and the confirm button can all arrive here; the
    // first one wins and the rest are already covered by its dialog.
    if (finishStartedRef.current || endedRef.current) return
    finishStartedRef.current = true
    // One last attempt at any pick that failed to send; `runFinish` waits for it.
    if (path === 'manual') resendFailed()
    void runFinish(path)
  }

  /** Manual path only: the candidate backs out of a finish that failed to send. */
  const cancelFinish = () => {
    finishStartedRef.current = false
    // The timer fires once; if the deadline passed while this dialog was open,
    // backing out goes straight to the timer path instead of an open paper.
    if (deadlinePassed(clock)) {
      beginFinish('timer')
      return
    }
    setFinish({ kind: 'idle' })
  }

  // -- answers --------------------------------------------------------------

  const onAnswerFailure = (questionId: string, res: PublicExamFailure) => {
    switch (res.kind) {
      case 'time_up':
        // The server has already finished and graded the attempt (§7.5).
        setSaveStatus((prev) => without(prev, questionId))
        beginFinish('timer')
        return
      case 'not_in_progress':
        emit({ kind: 'refetch' })
        return
      case 'blocked':
        emit({ kind: 'blocked', deviceLabel: res.deviceLabel })
        return
      case 'not_found':
        emit({ kind: 'not_found' })
        return
      case 'rate_limited':
      case 'unavailable':
        setSaveStatus((prev) => ({ ...prev, [questionId]: 'failed' }))
        setSaveError(retryableMessage(res))
        return
    }
  }

  const sendAnswer = (questionId: string, optionId: string) => {
    if (inFlightRef.current.has(questionId)) {
      queuedRef.current.set(questionId, optionId)
      return
    }
    setSaveStatus((prev) => ({ ...prev, [questionId]: 'saving' }))

    const job = (async () => {
      try {
        let next: string | undefined = optionId
        while (next !== undefined) {
          const res = await saveExamAnswer(token, questionId, next)
          if (endedRef.current) return
          if (res.kind !== 'ok') {
            // The newest pick stays painted; "Qayta yuborish" sends it.
            queuedRef.current.delete(questionId)
            onAnswerFailure(questionId, res)
            return
          }
          setClock(examClock(res.data))
          next = queuedRef.current.get(questionId)
          queuedRef.current.delete(questionId)
        }
        setSaveStatus((prev) => without(prev, questionId))
      } finally {
        // Synchronously with the loop's exit: a pick made after this point
        // starts a new loop instead of queueing behind one that has ended.
        inFlightRef.current.delete(questionId)
      }
    })()

    // The loop always awaits before it can finish, so this lands first.
    inFlightRef.current.set(questionId, job)
  }

  const choose = (questionId: string, optionId: string) => {
    if (finishStartedRef.current || endedRef.current) return
    setSelected((prev) => ({ ...prev, [questionId]: optionId }))
    sendAnswer(questionId, optionId)
  }

  const resendFailed = () => {
    setSaveError(null)
    for (const [questionId, status] of Object.entries(saveStatus)) {
      const optionId = selected[questionId]
      if (status === 'failed' && optionId) sendAnswer(questionId, optionId)
    }
  }

  /** The candidate's own retry; once finishing has begun, the paper is closed. */
  const retryFailed = () => {
    if (finishStartedRef.current || endedRef.current) return
    resendFailed()
  }

  const scrollToBlock = (index: number) => {
    document.getElementById(`${blockIdPrefix}-${index}`)?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }

  // -- render ---------------------------------------------------------------

  const unanswered = total - answered
  const manualDialogOpen =
    finish.kind === 'confirm' ||
    ((finish.kind === 'finishing' || finish.kind === 'failed') && finish.path === 'manual')
  const timeUpDialogOpen =
    finish.kind === 'ready' ||
    ((finish.kind === 'finishing' || finish.kind === 'failed') && finish.path === 'timer')

  return (
    <div className="space-y-4">
      {/* Sticky, translucent, one row — it has to fit a 360 px phone. */}
      <div className="sticky top-0 z-20 -mx-4 border-b border-slate-200/70 bg-slate-100/85 px-4 py-2.5 backdrop-blur-md">
        <div className="flex items-center justify-between gap-2">
          <Timer
            deadlineMs={clock.deadlineMs}
            syncedAt={clock.syncedAt}
            onExpire={() => beginFinish('timer')}
          />
          <span className="text-sm font-medium text-slate-600 tabular-nums">
            {answered}/{total}
          </span>
          <SaveIndicator saving={saving} failed={failed} answered={answered} onRetry={retryFailed} />
        </div>
      </div>

      {failed && !saving && (
        <div
          role="alert"
          className="flex flex-col gap-2 rounded-xl bg-rose-50 px-3.5 py-3 text-sm text-rose-700 sm:flex-row sm:items-center sm:justify-between"
        >
          <span>{saveError ?? "Ba'zi javoblar saqlanmadi."} Tanlangan javob hali serverga yetmadi.</span>
          <Button variant="secondary" onClick={retryFailed} disabled={locked} className="shrink-0">
            <RefreshCw className="h-4 w-4" />
            Qayta yuborish
          </Button>
        </div>
      )}

      {blocks.length > 1 && (
        <div className="flex flex-wrap gap-2">
          {blocks.map((block, index) => {
            const done = block.items.filter((item) => selected[item.question.id]).length
            return (
              <button
                key={block.key}
                type="button"
                onClick={() => scrollToBlock(index)}
                className="rounded-full border border-slate-200 bg-white px-3 py-1.5 text-sm text-slate-600 transition-colors hover:bg-slate-50"
              >
                {block.name}{' '}
                <span className="font-semibold text-slate-800 tabular-nums">
                  {done}/{block.items.length}
                </span>
              </button>
            )
          })}
        </div>
      )}

      {blocks.map((block, index) => {
        const done = block.items.filter((item) => selected[item.question.id]).length
        return (
          <section key={block.key} id={`${blockIdPrefix}-${index}`} className="scroll-mt-20 space-y-3">
            <div className="flex items-baseline justify-between gap-3 px-1 pt-2">
              <h2 className="min-w-0 break-words text-base font-semibold text-slate-800">{block.name}</h2>
              <span className="shrink-0 text-sm text-slate-500 tabular-nums">
                {done}/{block.items.length}
              </span>
            </div>
            {block.items.map(({ question, number }) => (
              <QuestionCard
                key={question.id}
                question={question}
                number={number}
                selectedOptionId={selected[question.id] ?? null}
                status={saveStatus[question.id]}
                locked={locked}
                onChoose={choose}
              />
            ))}
          </section>
        )
      })}

      <Card className="space-y-3 text-center">
        <p className="text-base text-slate-600">
          Javob berilgan: <span className="font-semibold text-slate-800 tabular-nums">{answered}/{total}</span>
        </p>
        <Button
          onClick={() => setFinish({ kind: 'confirm' })}
          disabled={locked}
          className="h-12 w-full text-base"
        >
          Testni yakunlash
        </Button>
      </Card>

      <Modal
        open={manualDialogOpen}
        onClose={() => {
          if (finish.kind === 'confirm') setFinish({ kind: 'idle' })
          else if (finish.kind === 'failed') cancelFinish()
        }}
        title="Testni yakunlash"
        size="sm"
        footer={
          finish.kind === 'failed' ? (
            <>
              <Button variant="secondary" onClick={cancelFinish}>
                Bekor qilish
              </Button>
              <Button onClick={() => void runFinish('manual')}>
                <RefreshCw className="h-4 w-4" />
                Qayta urinish
              </Button>
            </>
          ) : (
            <>
              <Button
                variant="secondary"
                onClick={() => setFinish({ kind: 'idle' })}
                disabled={finish.kind === 'finishing'}
              >
                Davom etish
              </Button>
              <Button onClick={() => beginFinish('manual')} disabled={finish.kind === 'finishing'}>
                {finish.kind === 'finishing' && <Loader2 className="h-4 w-4 animate-spin" />}
                {finish.kind === 'finishing' ? 'Yakunlanmoqda…' : 'Yakunlash'}
              </Button>
            </>
          )
        }
      >
        <div className="space-y-3 text-base text-slate-600">
          <p>Yakunlagandan so'ng javoblarni o'zgartirib bo'lmaydi.</p>
          <p>
            Javob berilgan:{' '}
            <span className="font-semibold text-slate-800 tabular-nums">
              {answered}/{total}
            </span>
          </p>
          {unanswered > 0 && (
            <p className="rounded-xl bg-amber-50 px-3.5 py-2.5 text-sm text-amber-700">
              {unanswered} ta savol javobsiz qoldi.
            </p>
          )}
          {finish.kind === 'failed' && (
            <p role="alert" className="rounded-xl bg-rose-50 px-3.5 py-2.5 text-sm text-rose-600">
              {finish.message}
            </p>
          )}
        </div>
      </Modal>

      <Modal
        open={timeUpDialogOpen}
        onClose={() => {
          if (finish.kind === 'ready') emit({ kind: 'finished', result: finish.result })
        }}
        title="Vaqt tugadi"
        size="sm"
        footer={
          finish.kind === 'failed' ? (
            <Button onClick={() => void runFinish('timer')}>
              <RefreshCw className="h-4 w-4" />
              Qayta urinish
            </Button>
          ) : (
            <Button
              onClick={() => {
                if (finish.kind === 'ready') emit({ kind: 'finished', result: finish.result })
              }}
              disabled={finish.kind !== 'ready'}
            >
              {finish.kind !== 'ready' && <Loader2 className="h-4 w-4 animate-spin" />}
              Natijani ko'rish
            </Button>
          )
        }
      >
        <div className="space-y-3 text-base text-slate-600">
          <p>Ajratilgan vaqt tugadi. Test yakunlandi — tanlagan javoblaringiz hisobga olinadi.</p>
          {finish.kind === 'finishing' && (
            <p className="flex items-center gap-2 text-sm text-slate-500">
              <Loader2 className="h-4 w-4 animate-spin" />
              Natija hisoblanmoqda…
            </p>
          )}
          {finish.kind === 'failed' && (
            <p role="alert" className="rounded-xl bg-rose-50 px-3.5 py-2.5 text-sm text-rose-600">
              {finish.message}
            </p>
          )}
        </div>
      </Modal>
    </div>
  )
}
