/**
 * XABARLAR — kanallar ro'yxati va suhbat.
 *
 * Kanal = sinf guruhi (ota-onalar + o'quvchilar) yoki xodimlar guruhi. Server
 * hozircha "o'qildi" holatini saqlamaydi, shuning uchun o'qilmaganlar
 * qurilmadagi belgidan (`lib/chatSeen.js`) hisoblanadi va yangi xabarlar
 * `?since=` bilan olinadi — bu bir vaqtning o'zida ham sonini, ham oxirgi
 * xabar matnini beradi.
 */
import { useEffect, useMemo, useRef, useState } from 'react'
import { MessageSquare, Send, Users } from 'lucide-react'
import {
  Badge, Card, EmptyState, ErrorState, Hero, Loader, Row, Screen,
} from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { teacherApi } from '../../lib/teacherApi'
import { markSeen, readSeen } from '../../lib/chatSeen'
import { dayMonth } from '../../lib/format'
import { haptic, showBackButton } from '../../lib/telegram'
import {
  AsyncBlock, STAFF_CHANNEL, channelBadge, channelSummaries, channelTitle,
} from './shared'

const ROLE_LABEL = {
  teacher: "O'qituvchi",
  parent: 'Ota-ona',
  student: "O'quvchi",
  admin: 'Administrator',
  superadmin: 'Administrator',
}

/** ISO → "HH:mm" (server vaqti mintaqasiz keladi, ya'ni mahalliy deb o'qiladi). */
function timeOf(iso) {
  const d = iso ? new Date(iso) : null
  if (!d || Number.isNaN(d.getTime())) return ''
  return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
}

/** Ro'yxatdagi qisqa vaqt: bugun bo'lsa soat, aks holda sana. */
function shortWhen(iso) {
  const d = iso ? new Date(iso) : null
  if (!d || Number.isNaN(d.getTime())) return ''
  return d.toDateString() === new Date().toDateString() ? timeOf(iso) : dayMonth(iso)
}

export function MessagesTab({ profile, user }) {
  const [openChannel, setOpenChannel] = useState(null)

  useEffect(() => {
    if (!openChannel) return undefined
    return showBackButton(() => setOpenChannel(null))
  }, [openChannel])

  if (openChannel) {
    return (
      <Conversation
        channel={openChannel}
        profile={profile}
        userId={user?.id ?? user?.userId ?? null}
        onBack={() => setOpenChannel(null)}
      />
    )
  }
  return <ChannelList onOpen={setOpenChannel} />
}

/* ------------------------------------------------------------ kanallar */

function ChannelList({ onOpen }) {
  const q = useAsync(async () => {
    const [names, lastMap] = await Promise.all([
      teacherApi.chatChannels(),
      teacherApi.chatLastMessages(),
    ])
    const rows = await channelSummaries(names, lastMap, readSeen())
    rows.sort((a, b) => (b.lastAt || '').localeCompare(a.lastAt || ''))
    return rows
  }, [])

  const unreadTotal = (q.data || []).reduce((n, r) => n + r.unread, 0)

  return (
    <Screen>
      <Hero
        title="Xabarlar"
        subtitle={
          q.data
            ? unreadTotal > 0
              ? `${unreadTotal} ta yangi xabar`
              : "Yangi xabar yo'q"
            : 'Sinf va xodimlar guruhlari'
        }
      />
      <AsyncBlock query={q} loadingLabel="Guruhlar yuklanmoqda…">
        {(rows) => (
          <>
            <Card title="Guruhlar" action={<Badge tone="neutral">{rows.length} ta</Badge>}>
              {rows.length === 0 ? (
                <EmptyState
                  icon={<MessageSquare className="h-9 w-9" />}
                  title="Guruh yo'q"
                  note="Siz hali biror sinfga biriktirilmagansiz, shuning uchun yozishadigan guruh ham yo'q."
                />
              ) : (
                rows.map((r) => <ChannelRow key={r.name} row={r} onOpen={onOpen} />)
              )}
            </Card>
            <div className="h-4" />
          </>
        )}
      </AsyncBlock>
    </Screen>
  )
}

function ChannelRow({ row, onOpen }) {
  const staff = row.name === STAFF_CHANNEL
  const subtitle = row.preview
    ? `${row.author ? `${row.author}: ` : ''}${row.preview}`
    : row.lastAt
      ? "Xabarlar bor — ochib ko'ring"
      : "Hali xabar yo'q"

  return (
    <Row
      lead={
        <div
          className={
            'flex h-11 w-11 shrink-0 items-center justify-center rounded-xl text-[13px] font-bold ' +
            (staff ? 'bg-brand-ink text-brand' : 'bg-brand/25 text-brand-ink')
          }
        >
          {staff ? <Users className="h-5 w-5" /> : channelBadge(row.name)}
        </div>
      }
      title={channelTitle(row.name)}
      subtitle={subtitle}
      right={
        <div className="flex shrink-0 flex-col items-end gap-1">
          <span className="text-[11px] text-slate-400">{shortWhen(row.lastAt)}</span>
          {row.unread > 0 && (
            <Badge tone="brand">{row.unread > 99 ? '99+' : row.unread}</Badge>
          )}
        </div>
      }
      onClick={() => {
        haptic()
        onOpen(row.name)
      }}
    />
  )
}

/* -------------------------------------------------------------- suhbat */

function Conversation({ channel, profile, userId, onBack }) {
  const q = useAsync(() => teacherApi.chatMessages(channel), [channel])
  const [extra, setExtra] = useState([])
  const [text, setText] = useState('')
  const [sending, setSending] = useState(false)
  const [sendError, setSendError] = useState(null)
  // O'z xabarlarimizni ajratish uchun: sessiyada userId bo'lmasa, birinchi
  // yuborilgan xabarning javobidan bilib olamiz.
  const [myUserId, setMyUserId] = useState(userId)
  const bottomRef = useRef(null)

  const messages = useMemo(() => {
    const base = q.data || []
    if (extra.length === 0) return base
    const ids = new Set(base.map((m) => m.id))
    return [...base, ...extra.filter((m) => !ids.has(m.id))]
  }, [q.data, extra])

  const last = messages.length ? messages[messages.length - 1] : null

  // Kanalni o'qilgan deb belgilaymiz — ro'yxatdagi hisob shundan yuradi.
  useEffect(() => {
    if (last) markSeen(channel, last.createdAt, last.text, last.senderName)
  }, [channel, last])

  // Yangi xabar kelganda pastga tushamiz. `scrollIntoView` eski WebView'larda
  // bo'lmasligi mumkin — bo'lmasa shunchaki aylantirmaymiz.
  useEffect(() => {
    const el = bottomRef.current
    if (typeof el?.scrollIntoView === 'function') el.scrollIntoView({ block: 'end' })
  }, [messages.length])

  // Yengil polling — SignalR o'rniga (Mini App qisqa ochiladi, 8 soniya yetarli).
  useEffect(() => {
    const id = setInterval(async () => {
      const since = last?.createdAt
      if (!since) return
      try {
        const fresh = await teacherApi.chatMessages(channel, since)
        if (fresh.length) setExtra((prev) => [...prev, ...fresh])
      } catch {
        /* polling xatosi — jim o'tamiz, keyingi urinishda tuzaladi */
      }
    }, 8000)
    return () => clearInterval(id)
  }, [channel, last])

  const isMine = (m) =>
    myUserId
      ? m.senderUserId === myUserId
      : m.senderRole === 'teacher' && m.senderName === profile.fullName

  const send = async () => {
    const value = text.trim()
    if (!value || sending) return
    setSending(true)
    setSendError(null)
    try {
      const created = await teacherApi.sendChatMessage(channel, value)
      if (created) {
        setExtra((prev) => [...prev, created])
        if (!myUserId) setMyUserId(created.senderUserId)
      }
      setText('')
      haptic()
    } catch (e) {
      setSendError(e?.message || 'Xabar yuborilmadi')
    } finally {
      setSending(false)
    }
  }

  const staff = channel === STAFF_CHANNEL

  return (
    <Screen>
      <Hero
        title={channelTitle(channel)}
        subtitle={staff ? "Barcha o'qituvchi va administratorlar" : "Ota-onalar va o'quvchilar"}
        right={
          <button
            type="button"
            onClick={onBack}
            className="h-9 rounded-full bg-brand-ink px-3 text-[13px] font-semibold text-brand"
          >
            Guruhlar
          </button>
        }
      />

      {q.loading && q.data === null ? (
        <Loader label="Xabarlar yuklanmoqda…" />
      ) : q.error ? (
        <ErrorState message={q.error} onRetry={q.reload} />
      ) : messages.length === 0 ? (
        <Card>
          <EmptyState
            icon={<MessageSquare className="h-9 w-9" />}
            title="Hali xabar yo'q"
            note="Bu guruhda hech kim yozmagan. Birinchi xabarni siz yozing — u barcha ota-onalarga ko'rinadi."
          />
        </Card>
      ) : (
        <div className="px-3 pt-3">
          {messages.map((m, i) => (
            <Bubble
              key={m.id}
              message={m}
              mine={isMine(m)}
              showDay={i === 0 || !sameDay(messages[i - 1].createdAt, m.createdAt)}
              showAuthor={
                !isMine(m) && (i === 0 || messages[i - 1].senderUserId !== m.senderUserId)
              }
            />
          ))}
        </div>
      )}

      {sendError && <p className="mx-6 mt-2 text-[13px] font-medium text-red-600">{sendError}</p>}

      {/* Yozish paneli tab panelining ustida turadi. */}
      <div className="h-24" />
      <div ref={bottomRef} />
      <div className="fixed inset-x-0 bottom-[calc(76px+var(--tg-safe-bottom))] z-10 flex items-center gap-2 border-t border-slate-200 bg-white px-3 py-2.5">
        <input
          value={text}
          onChange={(e) => setText(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              e.preventDefault()
              void send()
            }
          }}
          placeholder="Xabar yozing…"
          className="min-w-0 flex-1 rounded-2xl bg-slate-100 px-4 py-2.5 text-[15px] outline-none placeholder:text-slate-400"
        />
        <button
          type="button"
          onClick={send}
          disabled={sending || !text.trim()}
          aria-label="Yuborish"
          className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-brand text-brand-ink disabled:bg-slate-200 disabled:text-slate-400"
        >
          <Send className="h-5 w-5" />
        </button>
      </div>
    </Screen>
  )
}

function sameDay(a, b) {
  return String(a).slice(0, 10) === String(b).slice(0, 10)
}

function Bubble({ message, mine, showDay, showAuthor }) {
  return (
    <>
      {showDay && (
        <div className="my-3 flex justify-center">
          <span className="rounded-full bg-white px-3 py-1 text-[11px] font-semibold text-slate-400">
            {dayMonth(message.createdAt)}
          </span>
        </div>
      )}
      <div className={'mb-1.5 flex ' + (mine ? 'justify-end' : 'justify-start')}>
        <div className="max-w-[80%]">
          {showAuthor && (
            <p className="pb-0.5 pl-2 text-[11px] font-semibold text-slate-500">
              {message.senderName}
              <span className="ml-1 font-normal text-slate-400">
                {ROLE_LABEL[message.senderRole] || message.senderRole}
              </span>
            </p>
          )}
          <div
            className={
              'rounded-2xl px-3.5 py-2 text-[15px] leading-snug ' +
              (mine ? 'bg-brand text-brand-ink' : 'bg-white text-brand-ink')
            }
          >
            {message.text}
            <span className={'ml-2 text-[11px] ' + (mine ? 'text-brand-ink/50' : 'text-slate-400')}>
              {timeOf(message.createdAt)}
            </span>
          </div>
        </div>
      </div>
    </>
  )
}
