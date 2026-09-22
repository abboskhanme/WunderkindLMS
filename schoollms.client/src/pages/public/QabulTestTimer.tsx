/**
 * Countdown for the public entrance test (§7.8).
 *
 * Display only. The server owns the deadline and grades on it whatever this
 * component shows; the parent hands in a deadline already translated onto this
 * browser's clock (`examClock` in `api/publicExamClient.ts`) and re-translates it
 * on every `answer` response. The one thing the timer *does* is say "time is up"
 * — once — so the page can call `finish` itself (§7.1 fact 5).
 */
import { useEffect, useEffectEvent, useRef, useState } from 'react'
import { Clock } from 'lucide-react'
import { cn } from '@/lib/utils'

interface TimerProps {
  /** Local epoch ms at which the server's deadline falls. */
  deadlineMs: number
  /** Local epoch ms at which `deadlineMs` was computed — seeds the first frame. */
  syncedAt: number
  onExpire: () => void
}

/** Half a second keeps the display within a blink of the second boundary. */
const TICK_MS = 500

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

function formatRemaining(ms: number): string {
  const total = Math.ceil(ms / 1000)
  const hours = Math.floor(total / 3600)
  const minutes = Math.floor((total % 3600) / 60)
  const seconds = total % 60
  return hours > 0 ? `${hours}:${pad(minutes)}:${pad(seconds)}` : `${pad(minutes)}:${pad(seconds)}`
}

export function Timer({ deadlineMs, syncedAt, onExpire }: TimerProps) {
  const [now, setNow] = useState(syncedAt)
  const expiredRef = useRef(false)
  const expire = useEffectEvent(() => onExpire())

  useEffect(() => {
    const tick = () => {
      const current = Date.now()
      setNow(current)
      if (current >= deadlineMs && !expiredRef.current) {
        expiredRef.current = true
        expire()
      }
    }
    const id = window.setInterval(tick, TICK_MS)
    // Mobile browsers throttle intervals in a background tab; catch up the
    // moment the candidate comes back instead of on the next throttled tick.
    const onVisible = () => {
      if (document.visibilityState === 'visible') tick()
    }
    document.addEventListener('visibilitychange', onVisible)
    return () => {
      window.clearInterval(id)
      document.removeEventListener('visibilitychange', onVisible)
    }
  }, [deadlineMs])

  // A re-sync moves `syncedAt` forward before the next tick moves `now`.
  const remainingMs = Math.max(0, deadlineMs - Math.max(now, syncedAt))
  const lastMinute = remainingMs <= 60_000
  const lastFive = remainingMs <= 5 * 60_000

  return (
    <div
      role="timer"
      aria-label="Qolgan vaqt"
      className={cn(
        'flex items-center gap-1.5 rounded-full px-3 py-1.5 text-base font-semibold tabular-nums transition-colors',
        lastMinute
          ? 'bg-rose-50 text-rose-600'
          : lastFive
            ? 'bg-amber-50 text-amber-700'
            : 'bg-slate-100 text-slate-800',
      )}
    >
      <Clock className={cn('h-4 w-4', lastMinute && remainingMs > 0 && 'animate-pulse')} />
      {formatRemaining(remainingMs)}
    </div>
  )
}
