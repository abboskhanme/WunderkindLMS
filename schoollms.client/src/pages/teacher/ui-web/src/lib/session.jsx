import { createContext, useContext, useState, useEffect, useCallback, useRef } from 'react'
import { api, tokenStore, userStore, setUnauthorizedHandler } from './api'
import { autoInitPush, disablePush } from './push'

const SessionContext = createContext(null)

export function SessionProvider({ children }) {
  const [token, setToken] = useState(() => tokenStore.get())
  const [user, setUser] = useState(() => userStore.get())

  // 401 — token tugadi: holatni tozalaymiz, App login ekraniga qaytadi.
  useEffect(() => {
    setUnauthorizedHandler(() => {
      setToken(null)
      setUser(null)
    })
  }, [])

  const login = useCallback(async (email, password) => {
    const res = await api.login(email, password)
    if (res.user && res.user.role !== 'teacher') {
      throw new Error('Bu ilova faqat o\'qituvchilar uchun')
    }
    tokenStore.set(res.token)
    userStore.set(res.user)
    setToken(res.token)
    setUser(res.user)
    autoInitPush() // ruxsat oldindan berilgan bo'lsa tokenni yangilaydi (so'ramaydi)
    return res.user
  }, [])

  const logout = useCallback(async () => {
    try { await disablePush() } catch { /* ignore */ }
    tokenStore.clear()
    setToken(null)
    setUser(null)
  }, [])

  // Sahifa ochilganda allaqachon kirilgan bo'lsa (token saqlangan) — push tokenni jimgina yangilaymiz.
  useEffect(() => {
    if (tokenStore.get()) autoInitPush()
  }, [])

  // Profilni yangilab, user'ni boyitamiz (fanlar, sinf rahbarligi, ruxsatlar, rasm).
  const refreshProfile = useCallback(async () => {
    try {
      const p = await api.profile()
      const merged = { ...userStore.get(), ...p, fullName: p.fullName, avatarUrl: p.photoUrl ?? null }
      userStore.set(merged)
      setUser(merged)
      return p
    } catch {
      return null
    }
  }, [])

  const value = { token, user, isAuthed: !!token, login, logout, refreshProfile, setUser }
  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>
}

export function useSession() {
  const ctx = useContext(SessionContext)
  if (!ctx) throw new Error('useSession must be used within SessionProvider')
  return ctx
}

/**
 * Ma'lumot olish hook'i — loading / error / reload bilan.
 *   const { data, loading, error, reload } = useFetch(() => api.schedule(q, w), [q, w])
 * `fn` har render'da yangi bo'lishi mumkin — deps o'zgarganda qayta chaqiriladi.
 */
export function useFetch(fn, deps = []) {
  const [data, setData] = useState(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)
  const fnRef = useRef(fn)
  fnRef.current = fn

  const reload = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const res = await fnRef.current()
      setData(res)
    } catch (e) {
      setError(e)
    } finally {
      setLoading(false)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps)

  useEffect(() => {
    let alive = true
    setLoading(true)
    setError(null)
    fnRef
      .current()
      .then((res) => alive && setData(res))
      .catch((e) => alive && setError(e))
      .finally(() => alive && setLoading(false))
    return () => {
      alive = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps)

  return { data, loading, error, reload, setData }
}
