import { useCallback, useEffect, useState } from 'react'

/**
 * Bitta so'rov uchun holat: `{ data, loading, error, reload }`.
 *
 * Har ekran o'z `try/catch` ini yozmasligi uchun. `deps` o'zgarsa so'rov
 * qaytadan yuboriladi; eskirgan javob YOZILMAYDI — foydalanuvchi farzandni
 * tez almashtirsa, birinchi so'rovning kech kelgan javobi ikkinchisining
 * ustiga chiqib ketmasligi kerak.
 */
export function useAsync(fn, deps = []) {
  const [state, setState] = useState({ data: null, loading: true, error: null })
  const [tick, setTick] = useState(0)

  const reload = useCallback(() => setTick((n) => n + 1), [])

  useEffect(() => {
    let alive = true
    setState((s) => ({ ...s, loading: true, error: null }))
    Promise.resolve()
      .then(fn)
      .then((data) => alive && setState({ data, loading: false, error: null }))
      .catch((e) => alive && setState({ data: null, loading: false, error: e?.message || 'Xatolik' }))
    return () => {
      alive = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, tick])

  return { ...state, reload }
}
