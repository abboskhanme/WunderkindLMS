import { useEffect, useRef, useState } from 'react'
import { ArrowLeft, Paperclip, Mic, Send, MessageSquare } from 'lucide-react'
import Avatar from '../components/Avatar'
import { AsyncView } from '../components/State'
import EmptyState, { EmptyIllustration } from '../components/EmptyState'
import { useFetch, useSession } from '../lib/session'
import { api } from '../lib/api'

const ROLE = {
  student: { color: '#0EA5E9', label: "O'quvchi" },
  parent: { color: '#F59E0B', label: 'Ota-ona' },
  admin: { color: '#7C3AED', label: 'Admin' },
  teacher: { color: 'var(--primary)', label: "O'qituvchi" },
}

const STAFF_NAMES = ['Xodimlar', 'Xodimlar guruhi']

// ISO → HH:mm.
function formatTime(iso) {
  if (!iso) return ''
  const d = new Date(iso)
  if (isNaN(d.getTime())) return ''
  return d.toLocaleTimeString('uz', { hour: '2-digit', minute: '2-digit' })
}

// Chat conversation — header, message bubbles (left/right), input bar.
export default function ChatConversationScreen({ params, onBack }) {
  const className = params?.className || ''
  const { user } = useSession()
  const myId = user?.id
  const isStaff = STAFF_NAMES.includes(className)

  const messagesQ = useFetch(() => api.chat(className), [className])
  const { data, setData } = messagesQ
  const messages = Array.isArray(data) ? data : []

  const [text, setText] = useState('')
  const [sending, setSending] = useState(false)
  const scrollRef = useRef(null)
  const bottomRef = useRef(null)

  const scrollToBottom = () => {
    const el = scrollRef.current
    if (el) el.scrollTop = el.scrollHeight
  }

  // Yangi xabarlar kelganda pastga aylantirish.
  useEffect(() => {
    scrollToBottom()
  }, [messages.length])

  // Yengil polling — har ~8s yangi xabarlarni qo'shib boramiz (id bo'yicha dedupe).
  useEffect(() => {
    if (!className) return
    const id = setInterval(async () => {
      const since = messagesQ.data?.length ? messagesQ.data[messagesQ.data.length - 1].createdAt : undefined
      try {
        const fresh = await api.chat(className, since)
        if (!Array.isArray(fresh) || fresh.length === 0) return
        setData((prev) => {
          const list = Array.isArray(prev) ? prev : []
          const ids = new Set(list.map((m) => m.id))
          const add = fresh.filter((m) => !ids.has(m.id))
          return add.length ? [...list, ...add] : list
        })
      } catch {
        /* polling xatosi — sukutda o'tkazamiz */
      }
    }, 8000)
    return () => clearInterval(id)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [className])

  const send = async () => {
    const value = text.trim()
    if (!value || sending) return
    setSending(true)
    try {
      const created = await api.sendChat(className, value)
      setData((prev) => {
        const list = Array.isArray(prev) ? prev : []
        if (created && list.some((m) => m.id === created.id)) return list
        return created ? [...list, created] : list
      })
      setText('')
      requestAnimationFrame(scrollToBottom)
    } catch {
      /* yuborish xatosi — input saqlanadi */
    } finally {
      setSending(false)
    }
  }

  const onKeyDown = (e) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      send()
    }
  }

  const avatarText = isStaff ? 'XD' : className.slice(0, 2).toUpperCase()

  return (
    <div className="h-full flex flex-col bg-bg-alt">
      {/* Header */}
      <div className="flex items-center gap-2.5 px-3 py-2 bg-surface border-b border-border">
        {onBack && (
          <button onClick={onBack} className="w-10 h-10 rounded-xl bg-surface2 flex items-center justify-center text-text">
            <ArrowLeft size={20} />
          </button>
        )}
        <div
          className="w-9 h-9 rounded-[10px] flex items-center justify-center text-white text-[12px] font-extrabold"
          style={{ background: isStaff ? 'linear-gradient(135deg,#7C3AED,#C026D3)' : 'linear-gradient(135deg,#14B8A6,#0F766E)' }}
        >
          {avatarText}
        </div>
        <div className="flex-1">
          <p className="text-[15px] font-bold text-text">{className}</p>
          <div className="flex items-center gap-1 text-[11px] text-muted">
            <span className="w-1.5 h-1.5 rounded-full bg-success" /> Online
          </div>
        </div>
      </div>

      {/* Messages */}
      <AsyncView
        query={messagesQ}
        empty={
          <EmptyState
            icon={<EmptyIllustration><MessageSquare size={32} /></EmptyIllustration>}
            title="Xabarlar yo'q"
            subtitle="Birinchi xabarni yozib suhbatni boshlang."
          />
        }
      >
        <div ref={scrollRef} className="flex-1 overflow-y-auto no-scrollbar px-3 py-4 space-y-1">
          {messages.map((m, i) => {
            const isMe = m.senderUserId === myId
            const prev = messages[i - 1]
            const showAvatar = !isMe && (!prev || prev.senderUserId !== m.senderUserId)
            const role = ROLE[m.senderRole] || ROLE.teacher
            return (
              <div key={m.id} className={['flex items-end gap-2', isMe ? 'justify-end' : 'justify-start'].join(' ')}>
                {!isMe && <div className="w-8">{showAvatar && <Avatar name={m.senderName} size={32} />}</div>}
                <div className={['max-w-[78%] flex flex-col', isMe ? 'items-end' : 'items-start'].join(' ')}>
                  {!isMe && showAvatar && (
                    <div className="flex items-center gap-1.5 pl-2 pb-0.5">
                      <span className="text-[11px] font-bold" style={{ color: role.color }}>{m.senderName}</span>
                      <span className="px-1.5 py-px rounded-sm text-[9px] font-bold tracking-wide" style={{ background: `${role.color}26`, color: role.color }}>
                        {role.label.toUpperCase()}
                      </span>
                    </div>
                  )}
                  <div
                    className={['px-3 py-2 text-[14px] leading-snug', isMe ? 'bg-primary text-white' : 'bg-surface border border-border text-text'].join(' ')}
                    style={{ borderRadius: 18, borderBottomRightRadius: isMe ? 4 : 18, borderBottomLeftRadius: isMe ? 18 : 4 }}
                  >
                    {m.text}
                    <span className={['ml-1.5 text-[10px] font-mono', isMe ? 'text-white/70' : 'text-faint'].join(' ')}>{formatTime(m.createdAt)}</span>
                  </div>
                </div>
              </div>
            )
          })}
          <div ref={bottomRef} />
        </div>
      </AsyncView>

      {/* Input */}
      <div className="flex items-end gap-2 px-3 py-2 bg-surface border-t border-border">
        <div className="w-10 h-10 rounded-xl bg-surface2 flex items-center justify-center text-text shrink-0">
          <Paperclip size={20} />
        </div>
        <div className="flex-1 flex items-center gap-1 px-4 py-1.5 rounded-[22px] bg-surface2 border border-border/50">
          <input
            value={text}
            onChange={(e) => setText(e.target.value)}
            onKeyDown={onKeyDown}
            placeholder="Xabar yozing..."
            className="flex-1 bg-transparent outline-none text-[14px] text-text placeholder:text-faint"
          />
          <Mic size={18} className="text-faint" />
        </div>
        <button
          onClick={send}
          disabled={sending || !text.trim()}
          className={['w-11 h-11 rounded-full flex items-center justify-center text-white shrink-0 transition-colors', text.trim() && !sending ? 'bg-primary' : 'bg-surface3'].join(' ')}
        >
          <Send size={18} />
        </button>
      </div>
    </div>
  )
}
