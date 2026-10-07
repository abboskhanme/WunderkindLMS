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
import { forgetPanelKey, panelKey } from './panelKey'
import { linkTicket } from './linkTicket'

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
          // Bog'langan akkaunt arxivlangan: server bog'lanishni uzib, chipta berdi — boshqa akkaunt bilan kiriladi.
          if (e.code === 'not_linked') {
            linkTicket.set(e.body?.ticket)
            forgetPanelKey()
            setState({ status: 'unlinked', user: null, error: e.message })
            return
          }
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
        linkTicket.set(e.body?.ticket)
        setState({ status: 'unlinked', user: null, error: e.message || null })
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
    // Telegram ichidamizmi — imzo bo'yicha yoki botdagi tugma kaliti bo'yicha (kalit pastda unutiladi).
    const fromTelegram = inTelegram || Boolean(panelKey)
    if (fromTelegram) {
      // Qaysi Telegram chiqayotganini imzo yoki (pastki tugmadan ochilganda) kalit aytadi —
      // "hammasini uzish" emas: ota chiqsa, onaning Telegram'i bog'langanicha qoladi.
      const who = inTelegram ? { 'X-Telegram-Init-Data': initData } : { 'X-Telegram-Panel-Key': panelKey }
      try {
        const res = await api.del('/tg/link', who)
        linkTicket.set(res?.ticket)
      } catch (e) {
        // 404 — bog'lanish allaqachon yo'q: chiqish baribir bajarilgan.
        if (e.status !== 404) throw e
        linkTicket.set(e.body?.ticket)
      }
    }
    tokenStore.clear()
    forgetPanelKey()
    // Telegram ichida — har doim Telegram'ning login ekrani (2026-10-07). Avval tugmadan ochilganda
    // brauzer login ekrani chiqardi: yangi akkaunt Telegram'ga bog'lanmas, keyingi ochilishda esa
    // eski tugma "eskirgan" deb qolardi. Server chiqishda bog'lash chiptasini beradi — imzosiz ham ulanadi.
    setState({ status: fromTelegram ? 'unlinked' : 'outside', user: null, error: null })
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
