/**
 * Mini App sessiyasi.
 *
 * OQIM: Telegram sahifani ochadi → `initData` (imzolangan xom satr) serverga
 * yuboriladi → server imzoni tekshiradi va JWT qaytaradi → shu token bilan
 * qolgan hamma endpoint chaqiriladi.
 *
 * Sahifadagi `initDataUnsafe` dan foydalanuvchi kimligi OLINMAYDI: uni har
 * kim o'zgartira oladi. Kimligini faqat server aytadi.
 */
import { createContext, useCallback, useContext, useEffect, useState } from 'react'
import { api, setUnauthorizedHandler, tokenStore } from './api'
import { bootstrap, initData, inTelegram } from './telegram'

const SessionContext = createContext(null)

/** `idle` → `loading` → `ready` | `unlinked` | `outside` | `error` */
export function SessionProvider({ children }) {
  const [state, setState] = useState({ status: 'loading', user: null, error: null })

  const authenticate = useCallback(async () => {
    if (!inTelegram) {
      // Brauzerda ochilgan: imzo yo'q, ya'ni kim ekanini isbotlab bo'lmaydi.
      setState({ status: 'outside', user: null, error: null })
      return
    }
    setState({ status: 'loading', user: null, error: null })
    try {
      const res = await api.post('/tg/auth', { initData })
      tokenStore.set(res.token)
      setState({ status: 'ready', user: res.user, error: null })
    } catch (e) {
      // Server "bu Telegram hisobi hech kimga bog'lanmagan" deganda — bu xato
      // emas, bu OQIM. Foydalanuvchiga ulash ekrani ko'rsatiladi.
      if (e.status === 409 || e.code === 'not_linked') {
        setState({ status: 'unlinked', user: null, error: null })
        return
      }
      setState({ status: 'error', user: null, error: e.message })
    }
  }, [])

  useEffect(() => {
    bootstrap()
    setUnauthorizedHandler(() => setState({ status: 'loading', user: null, error: null }))
    void authenticate()
  }, [authenticate])

  /**
   * Brauzerdagi login muvaffaqiyatli bo'lganda chaqiriladi. Token allaqachon
   * saqlangan; bu yerda faqat kim ekanini aniqlab, ilovani ochamiz.
   */
  const adoptSession = useCallback(async (user) => {
    if (user) {
      setState({ status: 'ready', user, error: null })
      return
    }
    try {
      setState({ status: 'ready', user: await api.get('/auth/me'), error: null })
    } catch (e) {
      setState({ status: 'error', user: null, error: e.message })
    }
  }, [])

  return (
    <SessionContext.Provider value={{ ...state, retry: authenticate, adoptSession }}>
      {children}
    </SessionContext.Provider>
  )
}

export function useSession() {
  const ctx = useContext(SessionContext)
  if (!ctx) throw new Error('useSession faqat <SessionProvider> ichida ishlaydi')
  return ctx
}
