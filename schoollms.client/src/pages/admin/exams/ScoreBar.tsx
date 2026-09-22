import { cn } from '@/lib/utils'

interface Props {
  /** Server-computed points (or percent). Missing → an empty track. */
  value: number | null | undefined
  max: number | null | undefined
  className?: string
  label?: string
}

/**
 * A thin horizontal bar for a server-computed ratio — per-subject points or a
 * percent. A native `<progress>` styled with Tailwind's pseudo-element
 * variants: accessible by default, and no inline `style` for the width.
 */
export function ScoreBar({ value, max, className, label }: Props) {
  const safeMax = max && max > 0 ? max : 1
  const safeValue = value && value > 0 ? Math.min(value, safeMax) : 0
  return (
    <progress
      value={safeValue}
      max={safeMax}
      aria-label={label}
      className={cn(
        'h-1.5 w-full appearance-none overflow-hidden rounded-full bg-slate-100',
        '[&::-webkit-progress-bar]:rounded-full [&::-webkit-progress-bar]:bg-slate-100',
        '[&::-webkit-progress-value]:rounded-full [&::-webkit-progress-value]:bg-brand-500',
        '[&::-moz-progress-bar]:rounded-full [&::-moz-progress-bar]:bg-brand-500',
        className,
      )}
    />
  )
}
