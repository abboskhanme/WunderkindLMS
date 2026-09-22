import type { ReactNode } from 'react'
import type { LucideIcon } from 'lucide-react'
import { AlertTriangle, ChevronLeft, ChevronRight, RefreshCw } from 'lucide-react'
import { Button } from '@/components/ui/Button'

/**
 * The error, empty and pager blocks of the four Mavsumiy baholash screens —
 * the same shapes `SubmissionsPage` uses, so these screens look like the
 * rest of the admin panel.
 */

interface ErrorStateProps {
  title: string
  message: string
  onRetry?: () => void
}

export function ErrorState({ title, message, onRetry }: ErrorStateProps) {
  return (
    <div className="flex flex-col items-center gap-3 px-4 py-14 text-center">
      <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-red-50 text-red-600">
        <AlertTriangle className="h-6 w-6" />
      </div>
      <div>
        <p className="font-medium text-slate-800">{title}</p>
        <p className="mt-1 max-w-md text-sm text-slate-500">{message}</p>
      </div>
      {onRetry && (
        <Button variant="secondary" onClick={onRetry}>
          <RefreshCw className="h-4 w-4" /> Qayta urinish
        </Button>
      )}
    </div>
  )
}

interface EmptyStateProps {
  icon: LucideIcon
  title: string
  hint?: ReactNode
  action?: ReactNode
}

export function EmptyState({ icon: Icon, title, hint, action }: EmptyStateProps) {
  return (
    <div className="flex flex-col items-center gap-2 px-4 py-14 text-center">
      <Icon className="h-8 w-8 text-slate-300" />
      <p className="font-medium text-slate-600">{title}</p>
      {hint && <p className="max-w-md text-sm text-slate-400">{hint}</p>}
      {action && <div className="mt-1">{action}</div>}
    </div>
  )
}

interface PagerProps {
  page: number
  limit: number
  total: number
  disabled?: boolean
  onPage: (page: number) => void
}

export function Pager({ page, limit, total, disabled, onPage }: PagerProps) {
  const lastPage = Math.max(1, Math.ceil(total / limit))
  const first = total === 0 ? 0 : (page - 1) * limit + 1
  const last = Math.min(page * limit, total)
  const btn =
    'rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 disabled:cursor-not-allowed disabled:opacity-40'
  return (
    <div className="flex items-center justify-between border-t border-slate-100 px-4 py-3">
      <p className="text-xs text-slate-400">
        {total === 0 ? "Yozuv yo'q" : `${first}–${last} / ${total} ta`}
      </p>
      <div className="flex items-center gap-1">
        <button
          type="button"
          disabled={page <= 1 || disabled}
          onClick={() => onPage(Math.max(1, page - 1))}
          aria-label="Oldingi sahifa"
          className={btn}
        >
          <ChevronLeft className="h-4 w-4" />
        </button>
        <span className="min-w-[70px] text-center text-xs text-slate-500">
          {page} / {lastPage}
        </span>
        <button
          type="button"
          disabled={page >= lastPage || disabled}
          onClick={() => onPage(page + 1)}
          aria-label="Keyingi sahifa"
          className={btn}
        >
          <ChevronRight className="h-4 w-4" />
        </button>
      </div>
    </div>
  )
}

/** Red strip for a failure that does not replace the whole screen (export, save). */
export function InlineError({ message }: { message: string }) {
  return (
    <p className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
      {message}
    </p>
  )
}
