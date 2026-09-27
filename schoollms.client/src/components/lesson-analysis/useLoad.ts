import { useCallback, useEffect, useRef, useState } from 'react'
import { laErrorMessage } from '@/api/services/lessonAnalysis'

interface LoadState<T> {
  key: string
  data?: T
  error?: string
}

/**
 * Kalitga bog'langan yuklash: `key` o'zgarsa qayta so'raydi, eski javob yangi kalitga
 * hech qachon tushmaydi (poyga yo'q). `key === null` — so'ramaydi.
 *
 * Holat effekt ichida sinxron tozalanmaydi (react-hooks `set-state-in-effect`):
 * "yuklanmoqda" — joriy kalitga javob hali kelmaganining o'zi.
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
        if (alive) setState({ key: fullKey, error: laErrorMessage(err) })
      })
    return () => {
      alive = false
    }
  }, [fullKey])

  const current = state && state.key === fullKey ? state : null

  const reload = useCallback(() => setNonce((n) => n + 1), [])

  /** Mahalliy yangilash (masalan saqlangan javob bilan) — qayta so'rovsiz. */
  const setData = useCallback(
    (updater: (prev: T) => T) => {
      setState((prev) => (prev && prev.data !== undefined ? { ...prev, data: updater(prev.data) } : prev))
    },
    [],
  )

  return {
    data: current?.data ?? null,
    error: current?.error ?? null,
    loading: fullKey !== null && current === null,
    reload,
    setData,
  }
}
