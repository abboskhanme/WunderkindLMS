import { useCallback, useEffect, useState } from 'react'

interface AsyncState<T> {
  data: T | null
  loading: boolean
  error: string | null
  refetch: () => void
}

/**
 * API chaqiruvlari uchun universal hook.
 * loading / error / data holatlarini boshqaradi.
 */
export function useAsync<T>(fn: () => Promise<T>, deps: unknown[] = []): AsyncState<T> {
  const [data, setData] = useState<T | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const run = useCallback(() => {
    let active = true
    setLoading(true)
    setError(null)
    fn()
      .then((res) => active && setData(res))
      .catch((e: unknown) => {
        if (active) {
          const msg = e instanceof Error ? e.message : 'Xatolik yuz berdi'
          setError(msg)
        }
      })
      .finally(() => active && setLoading(false))
    return () => {
      active = false
    }
    // eslint-disable-next-line react-hooks/use-memo, react-hooks/exhaustive-deps -- generik hook: deps qiymati chaqiruvchidan keladi, array literal emas (maqsadli)
  }, deps)

  useEffect(() => run(), [run])

  return { data, loading, error, refetch: run }
}
