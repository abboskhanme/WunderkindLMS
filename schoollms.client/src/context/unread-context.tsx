import { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import type { ChatMessage } from '@/types'
import { useAuth } from './auth-context'
import { connectChat, getAdminLastMessages } from '@/api/services/messages'
import { getTeacherLastMessages } from '@/api/services/teacher'

/* ---------- localStorage yordamchilari ---------- */

function storageKey(userId: string) {
  return `chat:lastRead:${userId}`
}

function loadLastRead(userId: string): Record<string, string> {
  try {
    return JSON.parse(localStorage.getItem(storageKey(userId)) ?? '{}') as Record<string, string>
  } catch {
    return {}
  }
}

function saveLastReadChannel(userId: string, channel: string, iso: string) {
  const map = loadLastRead(userId)
  map[channel] = iso
  localStorage.setItem(storageKey(userId), JSON.stringify(map))
}

/* ---------- Context turi ---------- */

interface UnreadContextValue {
  /** Yangi (o'qilmagan) xabari bor kanallar seti */
  unreadChannels: Set<string>
  /** Kanalni o'qilgan deb belgilash — badge ni o'chiradi va localStorage'ga yozadi */
  markRead: (channel: string) => void
  /** Real-time xabar olish uchun kanalga obuna bo'lish. Unsubscribe funksiyasini qaytaradi. */
  subscribe: (channel: string, cb: (msg: ChatMessage) => void) => () => void
  /** SignalR qayta ulanganda chaqiriladigan callback qo'shish. Tozalash funksiyasini qaytaradi. */
  onReconnect: (cb: () => void) => () => void
}

const UnreadContext = createContext<UnreadContextValue | null>(null)

export function useUnread(): UnreadContextValue {
  const ctx = useContext(UnreadContext)
  if (!ctx) throw new Error('useUnread must be inside UnreadProvider')
  return ctx
}

/**
 * Barcha autentifikatsiyalangan sahifalar uchun global chat context.
 *
 * Ishchi tartibi:
 * 1. Mount bo'lganda backenddan (rol bo'yicha) har kanal uchun oxirgi xabar vaqtini oladi.
 * 2. localStorage'dan foydalanuvchi oxirgi o'qigan vaqtlarini yuklaydi.
 * 3. lastMessageAt > lastRead → o'qilmagan (sidebar badge).
 * 4. SignalR orqali yangi xabarlar kuzatiladi — subscriber (ChatPanel) ochiq bo'lsa unga
 *    yo'naltiriladi, aks holda unreadChannels ga qo'shiladi.
 * 5. markRead(channel) — localStorage'ga yozadi va badgeni o'chiradi.
 */
export function UnreadProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth()
  const [unreadChannels, setUnreadChannels] = useState<Set<string>>(new Set())

  // userId ref — markRead barqaror callback bo'lishi uchun (useCallback deps yo'q)
  const userIdRef = useRef<string | null>(null)
  userIdRef.current = user?.id ?? null

  // Aktiv ChatPanel callbacklari (kanal → callback)
  const subscribersRef = useRef<Map<string, (msg: ChatMessage) => void>>(new Map())
  // Qayta ulanish tinglovchilari (ChatPanel re-fetch uchun)
  const reconnectListenersRef = useRef<Set<() => void>>(new Set())

  useEffect(() => {
    if (!user) return

    const userId = user.id
    const role = user.role
    let active = true

    // 1. Rol bo'yicha to'g'ri endpoint ni tanlaymiz
    const fetchLastMessages =
      role === 'admin' || role === 'superadmin' || role === 'staff'
        ? getAdminLastMessages
        : role === 'teacher'
          ? getTeacherLastMessages
          : null // student/parent uchun chat tarixi yo'q

    if (fetchLastMessages) {
      const lastRead = loadLastRead(userId)
      fetchLastMessages()
        .then((channelTimes) => {
          if (!active) return
          const unread = new Set<string>()
          for (const [channel, lastMsgAt] of Object.entries(channelTimes)) {
            // lastMsgAt: backend ISO vaqti; lastRead[channel]: localStorage ISO vaqti
            // ISO string leksikografik taqqoslash UTC uchun to'g'ri ishlaydi
            if (lastMsgAt && (!lastRead[channel] || lastMsgAt > lastRead[channel])) {
              unread.add(channel)
            }
          }
          setUnreadChannels(unread)
        })
        .catch(() => {})
    }

    // 2. Global SignalR ulanish (barcha kanallardan xabar qabul qiladi)
    const conn = connectChat((m) => {
      // Aktiv ChatPanel ga yo'naltiramiz (agar kanal ochiq bo'lsa)
      subscribersRef.current.get(m.className)?.(m)
      // Kanal hozir ko'rilmayotgan bo'lsa — o'qilmagan deb belgilaymiz
      if (!subscribersRef.current.has(m.className)) {
        setUnreadChannels((prev) => {
          if (prev.has(m.className)) return prev
          return new Set([...prev, m.className])
        })
      }
    })

    if (conn) {
      conn.onreconnected(() => {
        reconnectListenersRef.current.forEach((cb) => cb())
      })
      conn.start().catch(() => {})
    }

    return () => {
      active = false
      conn?.stop().catch(() => {})
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- user ob'ekti login/logout da o'zgaradi
  }, [user])

  const markRead = useCallback((channel: string) => {
    const userId = userIdRef.current
    if (userId) {
      // Hozirgi vaqtni saqlaydi — keyingi sessionda solishtirish uchun
      saveLastReadChannel(userId, channel, new Date().toISOString())
    }
    setUnreadChannels((prev) => {
      if (!prev.has(channel)) return prev
      const next = new Set(prev)
      next.delete(channel)
      return next
    })
  }, [])

  const subscribe = useCallback((channel: string, cb: (msg: ChatMessage) => void) => {
    subscribersRef.current.set(channel, cb)
    return () => {
      subscribersRef.current.delete(channel)
    }
  }, [])

  const onReconnect = useCallback((cb: () => void) => {
    reconnectListenersRef.current.add(cb)
    return () => {
      reconnectListenersRef.current.delete(cb)
    }
  }, [])

  return (
    <UnreadContext.Provider value={{ unreadChannels, markRead, subscribe, onReconnect }}>
      {children}
    </UnreadContext.Provider>
  )
}
