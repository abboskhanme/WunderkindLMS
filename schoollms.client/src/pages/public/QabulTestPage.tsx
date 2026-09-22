/**
 * Public entrance test — `/qabul-test/:token`
 * (`docs/modules/admission-and-testing.md` §7, unit C6).
 *
 * The only unauthenticated write in the admission module. A candidate opens it
 * from a link the school sent through Telegram, usually on a phone, sits the test
 * once, and sees their score.
 *
 * The page is a state machine driven by the server (§7.3). Every screen is the
 * answer to one question — "what does `state` say?" — and the client holds no
 * state of its own worth keeping: nothing is written to localStorage or
 * sessionStorage, so a reload mid-test is simply another `state` call that
 * returns the same paper with the saved picks still selected.
 *
 *   loading       → skeleton
 *   (404 et al.)  → one "link not found" card, the same for unknown, revoked,
 *                   expired and cancelled (§7.5 — no enumeration)
 *   not_assigned  → "no active test yet", with a refresh
 *   lobby         → rules + mandatory agreement + Start          (QabulTestLobby)
 *   in_progress   → the paper, timer, autosave, finish          (QabulTestPaper)
 *   blocked       → "open it on the device you started on"      (QabulTestDeviceLock)
 *   finished      → the score the server returns                (QabulTestResult)
 *
 * Renders outside `AppLayout` and outside `ProtectedRoute`, and never touches the
 * shared `api` client (§7.8) — see `api/publicExamClient.ts`.
 */
import { useEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { useParams } from 'react-router-dom'
import { AlertTriangle, CalendarClock, FileSearch, Loader2, RefreshCw } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { cn } from '@/lib/utils'
import {
  LINK_NOT_FOUND_MESSAGE,
  examClock,
  fetchExamState,
  retryableMessage,
  startExam,
} from '@/api/publicExamClient'
import type {
  ExamClock,
  PublicCandidate,
  PublicExamCall,
  PublicExamFailure,
  PublicExamInfo,
  PublicExamState,
  PublicPaper,
  PublicResult,
} from '@/api/publicExamClient'
import { DeviceLock } from './QabulTestDeviceLock'
import { Lobby } from './QabulTestLobby'
import { Paper } from './QabulTestPaper'
import type { PaperOutcome } from './QabulTestPaper'
import { ResultView } from './QabulTestResult'

type View =
  | { kind: 'loading' }
  | { kind: 'not_found' }
  | { kind: 'failed'; message: string }
  | { kind: 'not_assigned'; candidate: PublicCandidate | null }
  | { kind: 'lobby'; candidate: PublicCandidate | null; exam: PublicExamInfo }
  | {
      kind: 'in_progress'
      candidate: PublicCandidate | null
      paper: PublicPaper
      clock: ExamClock
      /** Remounts the paper whenever the server hands over a fresh copy. */
      session: number
    }
  | { kind: 'blocked'; deviceLabel: string | null }
  | { kind: 'finished'; candidate: PublicCandidate | null; result: PublicResult }

const UNEXPECTED_MESSAGE = "Sahifani ochib bo'lmadi. Birozdan so'ng qayta urinib ko'ring."

/** A failed call, reduced to the screen it deserves (§7.5). */
function failureView(failure: PublicExamFailure): View {
  switch (failure.kind) {
    case 'not_found':
      return { kind: 'not_found' }
    case 'blocked':
      return { kind: 'blocked', deviceLabel: failure.deviceLabel }
    case 'rate_limited':
    case 'unavailable':
      return { kind: 'failed', message: retryableMessage(failure) }
    case 'time_up':
    case 'not_in_progress':
      // Neither is a `state` answer; re-asking is the only sensible move and the
      // retry card offers exactly that without looping on its own.
      return { kind: 'failed', message: UNEXPECTED_MESSAGE }
  }
}

/** Runs outside render: the in-progress clock reads `Date.now()`. */
function viewFromState(res: PublicExamCall<PublicExamState>, session: number): View {
  if (res.kind !== 'ok') return failureView(res)
  const data = res.data
  switch (data.state) {
    case 'lobby':
      return { kind: 'lobby', candidate: data.candidate, exam: data.exam }
    case 'in_progress':
      return {
        kind: 'in_progress',
        candidate: data.candidate,
        paper: data.paper,
        clock: examClock(data.paper),
        session,
      }
    case 'finished':
      return { kind: 'finished', candidate: data.candidate, result: data.result }
    case 'blocked':
      return { kind: 'blocked', deviceLabel: data.deviceLabel }
    case 'not_assigned':
      return { kind: 'not_assigned', candidate: data.candidate }
  }
}

// ---------------------------------------------------------------------------
//  Shell and state cards — same frame as the public survey page
// ---------------------------------------------------------------------------

function PageShell({ wide = false, children }: { wide?: boolean; children: ReactNode }) {
  return (
    <div className="min-h-screen bg-slate-100 px-4 py-8">
      <div className={cn('mx-auto w-full space-y-4', wide ? 'max-w-2xl' : 'max-w-md')}>
        <div className="flex items-center justify-center gap-3">
          {/* Same logo tile as the login page — the school's own yellow. */}
          <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-[#FFD006]">
            <img src="/logo.png" alt="" className="h-7 w-7 object-contain" />
          </div>
          <span className="text-base font-semibold text-slate-800">Wunderkind International School</span>
        </div>
        {children}
      </div>
    </div>
  )
}

interface StateCardProps {
  icon: ReactNode
  title: string
  text: string
  eyebrow?: string
  children?: ReactNode
}

function StateCard({ icon, title, text, eyebrow, children }: StateCardProps) {
  return (
    <Card className="flex flex-col items-center gap-3 py-8 text-center">
      {icon}
      {eyebrow && <p className="break-words text-sm font-medium text-slate-500">{eyebrow}</p>}
      <h1 className="text-lg font-semibold text-slate-800">{title}</h1>
      <p className="text-base text-slate-500">{text}</p>
      {children}
    </Card>
  )
}

function RetryButton({ onRetry, busy = false, label = 'Qayta urinish' }: { onRetry: () => void; busy?: boolean; label?: string }) {
  return (
    <Button variant="secondary" onClick={onRetry} disabled={busy} className="h-11 px-5">
      {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <RefreshCw className="h-4 w-4" />}
      {label}
    </Button>
  )
}

function SkeletonCard() {
  return (
    <Card className="space-y-4">
      <div className="h-4 w-24 animate-pulse rounded bg-slate-100" />
      <div className="h-6 w-2/3 animate-pulse rounded bg-slate-100" />
      <div className="grid grid-cols-3 gap-2">
        {[0, 1, 2].map((cell) => (
          <div key={cell} className="h-20 animate-pulse rounded-xl bg-slate-100" />
        ))}
      </div>
      <div className="space-y-3 pt-2">
        {[0, 1, 2, 3].map((row) => (
          <div key={row} className="h-12 w-full animate-pulse rounded-xl bg-slate-100" />
        ))}
      </div>
      <span className="sr-only">Yuklanmoqda…</span>
    </Card>
  )
}

// ---------------------------------------------------------------------------
//  Page
// ---------------------------------------------------------------------------

/**
 * Keyed by token so a different link in the same tab starts from a clean slate —
 * nothing from one candidate's session can leak into another's.
 */
export function QabulTestPage() {
  const { token = '' } = useParams<{ token: string }>()
  const trimmed = token.trim()
  return <QabulTestSession key={trimmed} token={trimmed} />
}

function QabulTestSession({ token }: { token: string }) {
  // An empty token is not worth a request; it gets the same card as a bad one.
  const [view, setView] = useState<View>(() => (token ? { kind: 'loading' } : { kind: 'not_found' }))
  const [refreshing, setRefreshing] = useState(false)
  const [starting, setStarting] = useState(false)
  const [startError, setStartError] = useState<string | null>(null)

  /** Only the newest `state` answer may land; older ones are dropped. */
  const seqRef = useRef(0)
  const startingRef = useRef(false)

  useEffect(() => {
    if (!token) return
    let active = true
    const seq = ++seqRef.current
    void fetchExamState(token).then((res) => {
      if (active && seq === seqRef.current) setView(viewFromState(res, seq))
    })
    return () => {
      active = false
    }
  }, [token])

  /**
   * Ask the server again. `quiet` keeps the current card on screen with a
   * spinner on its button (refresh, re-check); otherwise the skeleton shows.
   */
  const reload = (quiet: boolean) => {
    const seq = ++seqRef.current
    if (quiet) setRefreshing(true)
    else setView({ kind: 'loading' })
    void fetchExamState(token).then((res) => {
      if (seq !== seqRef.current) return
      setRefreshing(false)
      setView(viewFromState(res, seq))
    })
  }

  const handleStart = async () => {
    if (startingRef.current || view.kind !== 'lobby') return
    const candidate = view.candidate
    startingRef.current = true
    setStarting(true)
    setStartError(null)

    const res = await startExam(token)
    startingRef.current = false
    setStarting(false)

    switch (res.kind) {
      case 'ok': {
        const seq = ++seqRef.current
        const outcome = res.data
        setView(
          outcome.kind === 'paper'
            ? {
                kind: 'in_progress',
                candidate,
                paper: outcome.paper,
                clock: examClock(outcome.paper),
                session: seq,
              }
            : { kind: 'finished', candidate, result: outcome.result },
        )
        return
      }
      case 'rate_limited':
      case 'unavailable':
        // `start` is idempotent (§7.3): pressing again cannot draw a second paper.
        setStartError(retryableMessage(res))
        return
      case 'time_up':
      case 'not_in_progress':
        reload(false)
        return
      case 'blocked':
      case 'not_found':
        setView(failureView(res))
        return
    }
  }

  /**
   * The paper may call this from a save loop started renders ago, so it reads
   * no render-time value: `view` only through the functional update.
   */
  const handlePaperOutcome = (outcome: PaperOutcome) => {
    switch (outcome.kind) {
      case 'finished':
        setView((prev) => ({
          kind: 'finished',
          candidate: prev.kind === 'in_progress' ? prev.candidate : null,
          result: outcome.result,
        }))
        window.scrollTo({ top: 0 })
        return
      case 'blocked':
        setView({ kind: 'blocked', deviceLabel: outcome.deviceLabel })
        window.scrollTo({ top: 0 })
        return
      case 'not_found':
        setView({ kind: 'not_found' })
        window.scrollTo({ top: 0 })
        return
      case 'refetch':
        reload(false)
        return
    }
  }

  switch (view.kind) {
    case 'loading':
      return (
        <PageShell>
          <SkeletonCard />
        </PageShell>
      )

    case 'not_found':
      return (
        <PageShell>
          <StateCard
            icon={<FileSearch className="h-10 w-10 text-slate-300" />}
            title={LINK_NOT_FOUND_MESSAGE}
            text="Havolani to'liq ochganingizni tekshiring. Yangi havola kerak bo'lsa, maktab bilan bog'laning."
          />
        </PageShell>
      )

    case 'failed':
      return (
        <PageShell>
          <StateCard
            icon={<AlertTriangle className="h-10 w-10 text-rose-400" />}
            title="Sahifani ochib bo'lmadi"
            text={view.message}
          >
            <RetryButton onRetry={() => reload(false)} />
          </StateCard>
        </PageShell>
      )

    case 'not_assigned':
      return (
        <PageShell>
          <StateCard
            icon={<CalendarClock className="h-10 w-10 text-slate-300" />}
            eyebrow={view.candidate?.fullName}
            title="Test hali ochilmagan"
            text="Siz uchun hozircha faol test yo'q. Test vaqti haqida maktab bilan bog'laning."
          >
            <RetryButton onRetry={() => reload(true)} busy={refreshing} label="Yangilash" />
          </StateCard>
        </PageShell>
      )

    case 'lobby':
      return (
        <PageShell>
          <Lobby
            candidate={view.candidate}
            exam={view.exam}
            starting={starting}
            error={startError}
            onStart={() => void handleStart()}
          />
        </PageShell>
      )

    case 'in_progress':
      return (
        <PageShell wide>
          <Paper
            key={view.session}
            token={token}
            paper={view.paper}
            clock={view.clock}
            onOutcome={handlePaperOutcome}
          />
        </PageShell>
      )

    case 'blocked':
      return (
        <PageShell>
          <DeviceLock deviceLabel={view.deviceLabel} checking={refreshing} onRecheck={() => reload(true)} />
        </PageShell>
      )

    case 'finished':
      return (
        <PageShell wide={view.result.questions !== null}>
          <ResultView candidate={view.candidate} result={view.result} />
        </PageShell>
      )
  }
}
