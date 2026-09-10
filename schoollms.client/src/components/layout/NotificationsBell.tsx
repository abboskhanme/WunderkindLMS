import { useCallback, useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  Bell,
  Cake,
  CheckCheck,
  Lightbulb,
  MessageSquare,
  TriangleAlert,
  UserRoundCheck,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import type { NotificationItem, NotificationKind } from '@/types'
import { getNotifications, markNotificationsRead } from '@/api/services/notifications'
import { cn } from '@/lib/utils'

/** Ro'yxat qancha vaqtda bir yangilanadi (ms). */
const POLL_MS = 60_000

/** Har tur uchun ikonka va rang. */
const kindStyle: Record<NotificationKind, { icon: LucideIcon; wrap: string }> = {
  suggestion: { icon: Lightbulb, wrap: 'bg-amber-50 text-amber-600' },
  complaint: { icon: TriangleAlert, wrap: 'bg-red-50 text-red-600' },
  pickup: { icon: UserRoundCheck, wrap: 'bg-emerald-50 text-emerald-600' },
  chat: { icon: MessageSquare, wrap: 'bg-blue-50 text-blue-600' },
  birthday: { icon: Cake, wrap: 'bg-violet-50 text-violet-600' },
}

/** "3 daqiqa oldin", "2 kun oldin" ko'rinishidagi nisbiy vaqt. */
function timeAgo(iso: string): string {
  const then = new Date(iso).getTime()
  if (Number.isNaN(then)) return ''
  const min = Math.floor((Date.now() - then) / 60_000)
  if (min < 1) return 'hozirgina'
  if (min < 60) return `${min} daqiqa oldin`
  const hours = Math.floor(min / 60)
  if (hours < 24) return `${hours} soat oldin`
  const days = Math.floor(hours / 24)
  if (days < 7) return `${days} kun oldin`
  return new Date(then).toLocaleDateString('uz-UZ')
}

export function NotificationsBell() {
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)
  const [items, setItems] = useState<NotificationItem[]>([])
  const [unread, setUnread] = useState(0)
  const [loading, setLoading] = useState(false)
  const panelRef = useRef<HTMLDivElement>(null)

  const load = useCallback(async () => {
    try {
      const res = await getNotifications()
      setItems(res.items)
      setUnread(res.unreadCount)
    } catch {
      // Tarmoq xatosi — nishon o'zgarmaydi, keyingi urinishda yangilanadi.
    }
  }, [])

  // Boshlang'ich yuklash + davriy yangilash + oynaga qaytilganda yangilash.
  useEffect(() => {
    void load()
    const timer = window.setInterval(() => void load(), POLL_MS)
    const onFocus = () => void load()
    window.addEventListener('focus', onFocus)
    return () => {
      window.clearInterval(timer)
      window.removeEventListener('focus', onFocus)
    }
  }, [load])

  // Tashqariga bosilganda yoki Escape bosilganda panelni yopamiz.
  useEffect(() => {
    if (!open) return
    const onClick = (e: MouseEvent) => {
      if (panelRef.current && !panelRef.current.contains(e.target as Node)) setOpen(false)
    }
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false)
    }
    document.addEventListener('mousedown', onClick)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onClick)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  const toggle = () => {
    const next = !open
    setOpen(next)
    if (next) void load()
  }

  const markAllRead = async () => {
    setLoading(true)
    try {
      await markNotificationsRead()
      setItems((prev) => prev.map((i) => ({ ...i, isNew: false })))
      setUnread(0)
    } finally {
      setLoading(false)
    }
  }

  const openItem = async (item: NotificationItem) => {
    setOpen(false)
    navigate(item.link)
    // Bosilgan element ko'rilgan hisoblanadi — nishonni ham tozalaymiz.
    if (unread > 0) {
      try {
        await markNotificationsRead()
        setItems((prev) => prev.map((i) => ({ ...i, isNew: false })))
        setUnread(0)
      } catch {
        // Belgilash bo'lmasa ham o'tish amalga oshdi — keyingi yangilanishda tiklanadi.
      }
    }
  }

  return (
    <div className="relative" ref={panelRef}>
      <button
        onClick={toggle}
        className="relative rounded-lg p-2 text-slate-500 transition-colors hover:bg-slate-100"
        title="Bildirishnomalar"
        aria-haspopup="dialog"
        aria-expanded={open}
      >
        <Bell className="h-5 w-5" />
        {unread > 0 && (
          <span className="absolute -right-0.5 -top-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-red-500 px-1 text-[10px] font-semibold leading-none text-white">
            {unread > 9 ? '9+' : unread}
          </span>
        )}
      </button>

      {open && (
        <div
          role="dialog"
          aria-label="Bildirishnomalar"
          className="absolute right-0 top-full z-40 mt-2 w-[22rem] max-w-[calc(100vw-2rem)] overflow-hidden rounded-xl border border-slate-200 bg-white shadow-lg"
        >
          <div className="flex items-center justify-between border-b border-slate-100 px-4 py-3">
            <p className="text-sm font-semibold text-slate-700">
              Bildirishnomalar
              {unread > 0 && <span className="ml-1.5 text-xs font-normal text-slate-400">{unread} yangi</span>}
            </p>
            {unread > 0 && (
              <button
                onClick={markAllRead}
                disabled={loading}
                className="flex items-center gap-1 rounded-md px-1.5 py-1 text-xs text-brand-600 transition-colors hover:bg-brand-50 disabled:opacity-50"
                title="Hammasini o'qilgan deb belgilash"
              >
                <CheckCheck className="h-3.5 w-3.5" />
                O'qildi
              </button>
            )}
          </div>

          <div className="max-h-96 overflow-y-auto">
            {items.length === 0 ? (
              <p className="px-4 py-8 text-center text-sm text-slate-400">
                Hozircha bildirishnoma yo'q
              </p>
            ) : (
              items.map((item) => {
                const { icon: Icon, wrap } = kindStyle[item.kind] ?? kindStyle.chat
                return (
                  <button
                    key={item.id}
                    onClick={() => void openItem(item)}
                    className={cn(
                      'flex w-full items-start gap-3 border-b border-slate-50 px-4 py-3 text-left transition-colors last:border-b-0 hover:bg-slate-50',
                      item.isNew && 'bg-brand-50/40',
                    )}
                  >
                    <span className={cn('mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-lg', wrap)}>
                      <Icon className="h-4 w-4" />
                    </span>
                    <span className="min-w-0 flex-1">
                      <span className="flex items-center gap-1.5">
                        <span className="truncate text-sm font-medium text-slate-700">{item.title}</span>
                        {item.isNew && <span className="h-1.5 w-1.5 shrink-0 rounded-full bg-brand-500" />}
                      </span>
                      <span className="mt-0.5 block break-words text-xs text-slate-500">{item.text}</span>
                      <span className="mt-1 block text-[11px] text-slate-400">{timeAgo(item.createdAt)}</span>
                    </span>
                  </button>
                )
              })
            )}
          </div>
        </div>
      )}
    </div>
  )
}
