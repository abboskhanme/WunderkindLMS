/**
 * Mini App sessiyasi.
 *
 * OQIM: Telegram sahifani ochadi → `initData` (imzolangan xom satr) serverga
 * yuboriladi → server imzoni tekshiradi va JWT qaytaradi → shu token bilan
 * qolgan hamma endpoint chaqiriladi.
 *
 * Sahifadagi `initDataUnsafe` dan foydalanuvchi kimligi OLINMAYDI: uni har
 * kim o'zgartira oladi. Kimligini faqat server aytadi.
 *
 * BOTDAGI "🏫 Panelni ochish" TUGMASI (2026-09-28): u yerdan ochilganda `initData`
 * bo'lmaydi — o'rniga manzildagi shaxsiy kalit (`lib/panelKey.js`) almashtiriladi.
 */
import { createContext, useCallback, useContext, useEffect, useState } from 'react'
import { api, setUnauthorizedHandler, tokenStore } from './api'
import { bootstrap, initData, inTelegram } from './telegram'
import { panelKey } from './panelKey'

const SessionContext = createContext(null)

/** `idle` → `loading` → `ready` | `unlinked` | `outside` | `error` */
export function SessionProvider({ children }) {
  const [state, setState] = useState({ status: 'loading', user: null, error: null })

  const authenticate = useCallback(async () => {
    if (!inTelegram) {
      if (panelKey) {
        // Botdagi pastki tugma: imzo yo'q, lekin bot manzilga shaxsiy kalit qo'ygan.
        setState({ status: 'loading', user: null, error: null })
        try {
          const res = await api.post('/tg/auth/key', { key: panelKey })
          tokenStore.set(res.token)
          setState({ status: 'ready', user: res.user, error: null })
        } catch (e) {
          const stale = e.code === 'panel_key_invalid'
          setState({
            status: 'error',
            user: null,
            error: stale ? 'Tugma eskirgan. Botda /start ni bosing — yangi tugma keladi.' : e.message,
          })
        }
        return
      }
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
    // 401 — token eskirgan. FAQAT holatni "loading" ga qo'yish yetmaydi: hech
    // kim uni "ready" ga o'tkazmagani uchun ilova "Kirilmoqda…" da muzlab
    // qolardi. Telegram imzosi har doim qo'lda, ya'ni jimgina qayta kiramiz.
    setUnauthorizedHandler(() => {
      void authenticate()
    })
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

  /**
   * "Chiqish". Brauzerda — token o'chadi va login ekrani chiqadi. Telegram
   * ichida token o'chirishning o'zi yetmaydi: keyingi ochilishda imzo bo'yicha
   * yana avtomatik kiradi. Shuning uchun SHU Telegram akkaunt hisobdan uziladi
   * (imzo sarlavhada — ota chiqsa, onaning Telegram'i bog'langanicha qoladi)
   * va ilova login ekraniga qaytadi.
   */
  const logout = useCallback(async () => {
    if (inTelegram || panelKey) {
      // Qaysi Telegram chiqayotganini imzo yoki (pastki tugmadan ochilganda) kalit aytadi —
      // "hammasini uzish" emas: ota chiqsa, onaning Telegram'i bog'langanicha qoladi.
      const who = inTelegram ? { 'X-Telegram-Init-Data': initData } : { 'X-Telegram-Panel-Key': panelKey }
      try {
        await api.del('/tg/link', who)
      } catch (e) {
        // 404 — bog'lanish allaqachon yo'q: chiqish baribir bajarilgan.
        if (e.status !== 404) throw e
      }
    }
    tokenStore.clear()
    setState({ status: inTelegram ? 'unlinked' : 'outside', user: null, error: null })
  }, [])

  return (
    <SessionContext.Provider value={{ ...state, retry: authenticate, adoptSession, logout }}>
      {children}
    </SessionContext.Provider>
  )
}

export function useSession() {
  const ctx = useContext(SessionContext)
  if (!ctx) throw new Error('useSession faqat <SessionProvider> ichida ishlaydi')
  return ctx
}
