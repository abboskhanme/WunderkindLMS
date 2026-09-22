import { useCallback, useEffect, useRef, useState } from 'react'

interface Settled<T> {
  key: string
  nonce: number
  data: T | null
  error: unknown
}

export interface Remote<T> {
  /**
   * Data for the current key. While a `reload()` of the SAME key is in flight
   * the previous data stays here (`refreshing` is true), so a table does not
   * blink into a spinner after every save.
   */
  data: T | null
  /** The raw failure of the last request for this key, if it failed. */
  error: unknown
  /** True until the first answer for the current key arrives. */
  loading: boolean
  /** A reload of data that is already on screen. */
  refreshing: boolean
  reload: () => void
  /** Local edit of the data on screen (e.g. one row after an inline save). */
  mutate: (update: (data: T) => T) => void
}

/**
 * One GET keyed by a string. `key === null` means "not enough is chosen yet —
 * do not ask". State is only set from the promise callbacks, never
 * synchronously inside the effect, and an answer for a stale key is dropped.
 */
export function useRemote<T>(key: string | null, load: () => Promise<T>): Remote<T> {
  const [settled, setSettled] = useState<Settled<T> | null>(null)
  const [nonce, setNonce] = useState(0)

  const loadRef = useRef(load)
  useEffect(() => {
    loadRef.current = load
  })

  useEffect(() => {
    if (key === null) return
    let active = true
    loadRef.current().then(
      (data) => {
        if (active) setSettled({ key, nonce, data, error: null })
      },
      (error: unknown) => {
        if (active) setSettled({ key, nonce, data: null, error })
      },
    )
    return () => {
      active = false
    }
  }, [key, nonce])

  const sameKey = key !== null && settled !== null && settled.key === key
  const fresh = sameKey && settled.nonce === nonce
  const data = sameKey ? settled.data : null

  const reload = useCallback(() => setNonce((n) => n + 1), [])
  const mutate = useCallback(
    (update: (data: T) => T) =>
      setSettled((prev) => (prev && prev.data !== null ? { ...prev, data: update(prev.data) } : prev)),
    [],
  )

  return {
    data,
    error: fresh ? settled.error : null,
    loading: key !== null && !fresh && data === null,
    refreshing: key !== null && !fresh && data !== null,
    reload,
    mutate,
  }
}
