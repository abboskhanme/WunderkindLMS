import { useCallback, useEffect, useRef, useState } from 'react'
import { hrErrorMessage } from '@/api/services/hrTasks'

interface LoadState<T> {
  key: string
  data?: T
  error?: string
  raw?: unknown
}

/**
 * Key-bound loading (the `components/lesson-analysis/useLoad.ts` pattern): refetches when
 * `key` changes, a stale response never lands on a new key. `key === null` — no request.
 *
 * While a new key loads, the OLD data stays available as `previous` so a list does not
 * blink on a filter change (the old `placeholderData` behaviour) — it is only dimmed.
 */
export function useLoad<T>(key: string | null, fn: () => Promise<T>) {
  const [state, setState] = useState<LoadState<T> | null>(null)
  const [nonce, setNonce] = useState(0)
  const fnRef = useRef(fn)

  useEffect(() => {
    fnRef.current = fn
  })

  const fullKey = key === null ? null : `${key}#${nonce}`

  useEffect(() => {
    if (fullKey === null) return
    let alive = true
    fnRef
      .current()
      .then((data) => {
        if (alive) setState({ key: fullKey, data })
      })
      .catch((err: unknown) => {
        if (alive) setState({ key: fullKey, error: hrErrorMessage(err), raw: err })
      })
    return () => {
      alive = false
    }
  }, [fullKey])

  const current = state && state.key === fullKey ? state : null

  const reload = useCallback(() => setNonce((n) => n + 1), [])

  const setData = useCallback((updater: (prev: T) => T) => {
    setState((prev) => (prev && prev.data !== undefined ? { ...prev, data: updater(prev.data) } : prev))
  }, [])

  return {
    data: current?.data ?? null,
    /** Until the current key answers — the previous key's data (shown dimmed). */
    previous: current ? null : (state?.data ?? null),
    error: current?.error ?? null,
    /** The raw error (for status/code checks). */
    rawError: current?.raw ?? null,
    loading: fullKey !== null && current === null,
    reload,
    setData,
  }
}
