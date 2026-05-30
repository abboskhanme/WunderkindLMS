import { useEffect, useRef, useState } from 'react'
import { Send } from 'lucide-react'
import type { ChatMessage } from '@/types'
import { useAuth } from '@/context/auth-context'
import { useUnread } from '@/context/unread-context'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { roleLabels } from '@/config/navigation'
import { cn } from '@/lib/utils'

interface Props {
  className: string
  /** Xabarlarni olish (admin yoki o'qituvchi servisidan) */
  fetchMessages: (className: string, since?: string) => Promise<ChatMessage[]>
  /** Xabar yuborish */
  sendMessage: (className: string, text: string) => Promise<ChatMessage>
  /** Panel sarlavhasi (berilmasa — "{className} — guruh chati") */
  title?: string
  /** Sarlavha ostidagi izoh (a'zolar haqida) */
  subtitle?: string
}

/**
 * Bitta sinfning guruh chati: real-time (SignalR) xabarlar + yozish maydoni.
 * SignalR ulanishi UnreadProvider orqali global tarzda boshqariladi — alohida ulanish ochilmaydi.
 */
export function ChatPanel({ className, fetchMessages, sendMessage, title, subtitle }: Props) {
  const { user } = useAuth()
  const { markRead, subscribe, onReconnect } = useUnread()
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [text, setText] = useState('')
  const [loading, setLoading] = useState(true)
  const [sending, setSending] = useState(false)
  const bottomRef = useRef<HTMLDivElement>(null)
  const classRef = useRef(className)

  useEffect(() => {
    classRef.current = className
  }, [className])

  // Sinf o'zgarsa — xabarlarni qayta yuklaymiz.
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- sinf almashganda chatni qayta yuklash (maqsadli)
    setLoading(true)
    fetchMessages(className)
      .then(setMessages)
      .finally(() => setLoading(false))
    // eslint-disable-next-line react-hooks/exhaustive-deps -- fetchMessages barqaror deb hisoblanadi
  }, [className])

  // Kanalga obuna bo'lamiz + o'qilgan deb belgilaymiz (badgeni o'chiradi).
  useEffect(() => {
    markRead(className)
    return subscribe(className, (m) => {
      setMessages((prev) => (prev.some((x) => x.id === m.id) ? prev : [...prev, m]))
    })
  }, [className, markRead, subscribe])

  // SignalR qayta ulanganda xabarlarni re-fetch qilamiz.
  useEffect(() => {
    return onReconnect(() => {
      fetchMessages(classRef.current)
        .then(setMessages)
        .catch(() => {})
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps -- fetchMessages barqaror deb hisoblanadi
  }, [onReconnect])

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages])

  const handleSend = async (e: React.FormEvent) => {
    e.preventDefault()
    const t = text.trim()
    if (!t || sending) return
    setSending(true)
    try {
      const m = await sendMessage(className, t)
      setText('')
      setMessages((prev) => (prev.some((x) => x.id === m.id) ? prev : [...prev, m]))
    } finally {
      setSending(false)
    }
  }

  return (
    <Card className="flex h-[70vh] flex-col p-0">
      <div className="border-b border-slate-100 px-4 py-3">
        <p className="font-semibold text-slate-800">{title ?? `${className} — guruh chati`}</p>
        <p className="text-xs text-slate-400">
          {subtitle ?? "O'quvchilar, dars beruvchi o'qituvchilar va admin"}
        </p>
      </div>

      <div className="flex-1 space-y-3 overflow-y-auto px-4 py-4">
        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : messages.length === 0 ? (
          <p className="py-12 text-center text-sm text-slate-400">
            Hozircha xabar yo'q. Birinchi bo'lib yozing.
          </p>
        ) : (
          messages.map((m) => {
            const mine = m.senderUserId === user?.id
            return (
              <div key={m.id} className={cn('flex', mine ? 'justify-end' : 'justify-start')}>
                <div
                  className={cn(
                    'max-w-[75%] rounded-2xl px-3 py-2 text-sm',
                    mine ? 'bg-brand-600 text-white' : 'bg-slate-100 text-slate-800',
                  )}
                >
                  {!mine && (
                    <div className="mb-0.5 flex items-center gap-1.5">
                      <span className="text-xs font-semibold text-slate-700">{m.senderName}</span>
                      <span className="rounded bg-slate-200 px-1 text-[10px] text-slate-500">
                        {roleLabels[m.senderRole]}
                      </span>
                    </div>
                  )}
                  <p className="whitespace-pre-wrap break-words">{m.text}</p>
                  <div
                    className={cn(
                      'mt-0.5 text-right text-[10px]',
                      mine ? 'text-brand-100' : 'text-slate-400',
                    )}
                  >
                    {formatTime(m.createdAt)}
                  </div>
                </div>
              </div>
            )
          })
        )}
        <div ref={bottomRef} />
      </div>

      <form onSubmit={handleSend} className="flex items-center gap-2 border-t border-slate-100 p-3">
        <input
          className="flex-1 rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
          placeholder="Xabar yozing..."
          value={text}
          onChange={(e) => setText(e.target.value)}
        />
        <button
          type="submit"
          disabled={!text.trim() || sending}
          className="inline-flex h-10 w-10 items-center justify-center rounded-lg bg-brand-600 text-white transition-colors hover:bg-brand-700 disabled:opacity-50"
          title="Yuborish"
        >
          <Send className="h-4 w-4" />
        </button>
      </form>
    </Card>
  )
}

function formatTime(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return ''
  return d.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })
}
