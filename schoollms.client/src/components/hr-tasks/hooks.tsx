import { useCallback, useEffect, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { Toast } from '@/components/ui/Toast'

/**
 * Keyset cursor stack — resets itself when the filter key changes (no effect, per
 * `react-hooks/set-state-in-effect`).
 */
export function useCursorStack(filterKey: string) {
  const [stack, setStack] = useState<{ key: string; cursors: (string | null)[] }>({ key: filterKey, cursors: [null] })
  const cursors = stack.key === filterKey ? stack.cursors : [null]
  const cursor = cursors[cursors.length - 1] ?? null
  return {
    cursor,
    hasPrev: cursors.length > 1,
    prev: () => setStack({ key: filterKey, cursors: cursors.length > 1 ? cursors.slice(0, -1) : cursors }),
    next: (nextCursor: string | null) => {
      if (nextCursor) setStack({ key: filterKey, cursors: [...cursors, nextCursor] })
    },
  }
}

/* ------------------------------------------------------------------ toast */

/** In-page green/red message (`components/ui/Toast`). */
export function useToast() {
  const [toast, setToast] = useState<{ message: string; tone: 'success' | 'error' } | null>(null)
  const close = useCallback(() => setToast(null), [])
  const success = useCallback((message: string) => setToast({ message, tone: 'success' }), [])
  const error = useCallback((message: string) => setToast({ message, tone: 'error' }), [])
  const node = (
    <Toast
      message={toast?.message ?? null}
      tone={toast?.tone ?? 'success'}
      onClose={close}
      duration={toast?.tone === 'error' ? 5000 : 3000}
    />
  )
  return { node, success, error }
}

/** Copy to clipboard; without the Clipboard API (plain HTTP) the text is shown in a prompt. */
export async function copyToClipboard(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text)
    return true
  } catch {
    window.prompt('Nusxalang', text)
    return false
  }
}

/** Debounced value for search inputs (no request per keystroke). */
export function useDebounced<T>(value: T, delay = 350): T {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(value), delay)
    return () => window.clearTimeout(timer)
  }, [value, delay])
  return debounced
}

/**
 * A "saved" message passed from another page via `navigate(path, { state: { flash } })`.
 * Shown once — the history `state` is cleared right away so a reload does not repeat it.
 */
export function useFlashToast() {
  const location = useLocation()
  const navigate = useNavigate()
  const flash = (location.state as { flash?: string } | null)?.flash ?? null
  const [message, setMessage] = useState<string | null>(flash)
  const close = useCallback(() => setMessage(null), [])

  useEffect(() => {
    if (flash) navigate(`${location.pathname}${location.search}`, { replace: true, state: null })
  }, [flash, location.pathname, location.search, navigate])

  return <Toast message={message} onClose={close} />
}
