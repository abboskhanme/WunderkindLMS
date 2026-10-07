/**
 * TAKLIF VA SHIKOYATLAR — the sender's side, shared by the parent and the teacher panels
 * (client, 2026-10-07):
 *   * "Mening murojaatlarim" — everything this account sent, with the status the school set
 *     (Qabul qilindi → Jarayonda → Hal qilindi), when it changed, and the answer;
 *   * "Yangi murojaat" — taklif or shikoyat, an optional department and photo.
 *
 * A full-screen sheet like the profile: Telegram's back button and × close it. The panel gives
 * it its own API (`list`, `departments`, `submit`) — the two panels keep separate API layers.
 */
import { useCallback, useEffect, useRef, useState } from 'react'
import { Camera, CheckCircle2, Clock, Lightbulb, MessageSquareWarning, Plus, X } from 'lucide-react'
import { Badge, Card, EmptyState, Hero } from './ui'
import { useAsync } from '../lib/useAsync'
import { dateTime } from '../lib/format'
import { haptic, hapticNotify, showBackButton } from '../lib/telegram'

const TONE = { new: 'brand', in_progress: 'neutral', resolved: 'success' }

export function FeedbackSheet({ api, onClose }) {
  const [mode, setMode] = useState('list')
  const list = useAsync(() => api.list(), [])

  const back = useCallback(() => (mode === 'new' ? setMode('list') : onClose()), [mode, onClose])
  useEffect(() => showBackButton(back), [back])

  return (
    <div role="dialog" aria-label="Taklif va shikoyatlar" className="fixed inset-0 z-40 overflow-y-auto overscroll-contain bg-ground">
      <div className="pb-[calc(24px+var(--tg-safe-bottom))]">
        <Hero
          title="Taklif va shikoyatlar"
          subtitle={mode === 'new' ? 'Yangi murojaat' : 'Mening murojaatlarim'}
          right={
            <button
              type="button"
              onClick={onClose}
              aria-label="Yopish"
              className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-brand-ink/10 text-brand-ink"
            >
              <X className="h-5 w-5" />
            </button>
          }
        />

        {mode === 'new' ? (
          <NewFeedback
            api={api}
            onSent={() => {
              setMode('list')
              list.reload()
            }}
          />
        ) : (
          <>
            <div className="px-4 pb-3">
              <button
                type="button"
                onClick={() => {
                  haptic('light')
                  setMode('new')
                }}
                className="flex w-full items-center justify-center gap-2 rounded-2xl bg-brand-ink py-3.5 text-[16px] font-semibold text-brand"
              >
                <Plus className="h-5 w-5" /> Yangi murojaat
              </button>
            </div>
            <MyItems query={list} />
          </>
        )}
      </div>
    </div>
  )
}

function MyItems({ query }) {
  if (query.loading && !query.data) return <p className="px-4 py-6 text-center text-[14px] text-slate-500">Yuklanmoqda…</p>
  if (query.error) return <p className="px-4 py-6 text-center text-[14px] text-red-600">{String(query.error)}</p>
  const rows = query.data ?? []
  if (rows.length === 0)
    return (
      <Card>
        <EmptyState
          icon={<Lightbulb className="h-9 w-9" />}
          title="Hali murojaat yo'q"
          note="Taklif yoki shikoyatingizni yuboring — holati va javobi shu yerda ko'rinadi."
        />
      </Card>
    )
  return rows.map((f) => <ItemCard key={f.id} f={f} />)
}

function ItemCard({ f }) {
  const complaint = f.type === 'complaint'
  return (
    <Card>
      <div className="space-y-2 px-4 py-4">
        <div className="flex flex-wrap items-center gap-2">
          <span
            className={
              'inline-flex items-center gap-1 rounded-full px-2.5 py-1 text-[12px] font-medium ' +
              (complaint ? 'bg-red-50 text-red-600' : 'bg-amber-50 text-amber-700')
            }
          >
            {complaint ? <MessageSquareWarning className="h-3.5 w-3.5" /> : <Lightbulb className="h-3.5 w-3.5" />}
            {complaint ? 'Shikoyat' : 'Taklif'}
          </span>
          <Badge tone={TONE[f.status] ?? 'neutral'}>{f.statusLabel}</Badge>
          {f.departmentName && <Badge>{f.departmentName}</Badge>}
        </div>
        <p className="whitespace-pre-line break-words text-[15px] text-slate-800">{f.text}</p>
        {f.answer && (
          <div className="rounded-xl bg-emerald-50 px-3 py-2 text-[14px] text-emerald-800">
            <span className="font-semibold">Javob:</span> {f.answer}
          </div>
        )}
        <ol className="space-y-1 pt-1">
          {(f.steps.length ? f.steps : [{ status: f.status, statusLabel: f.statusLabel, at: f.createdAt }]).map((s, i) => (
            <li key={i} className="flex items-center gap-2 text-[13px] text-slate-500">
              {s.status === 'resolved' ? (
                <CheckCircle2 className="h-4 w-4 text-emerald-600" />
              ) : (
                <Clock className="h-4 w-4 text-slate-400" />
              )}
              <span className="font-medium text-slate-700">{s.statusLabel}</span>
              <span>· {dateTime(s.at)}</span>
            </li>
          ))}
        </ol>
      </div>
    </Card>
  )
}

function NewFeedback({ api, onSent }) {
  const deps = useAsync(() => api.departments().catch(() => []), [])
  const [type, setType] = useState('suggestion')
  const [departmentId, setDepartmentId] = useState('')
  const [text, setText] = useState('')
  const [photo, setPhoto] = useState(null)
  const [sending, setSending] = useState(false)
  const [error, setError] = useState('')
  const fileRef = useRef(null)

  const send = async () => {
    if (!text.trim() || sending) return
    setSending(true)
    setError('')
    try {
      const fd = new FormData()
      fd.append('type', type)
      fd.append('text', text.trim())
      if (departmentId) fd.append('departmentId', departmentId)
      if (photo) fd.append('image', photo)
      await api.submit(fd)
      hapticNotify('success')
      onSent()
    } catch (e) {
      hapticNotify('error')
      setError(e?.message || "Yuborib bo'lmadi")
    } finally {
      setSending(false)
    }
  }

  return (
    <Card>
      <div className="space-y-4 px-4 py-4">
        <div className="flex gap-2">
          {[
            { value: 'suggestion', label: 'Taklif' },
            { value: 'complaint', label: 'Shikoyat' },
          ].map((o) => (
            <button
              key={o.value}
              type="button"
              onClick={() => setType(o.value)}
              className={
                'flex-1 rounded-xl py-2.5 text-[15px] font-semibold ' +
                (type === o.value ? 'bg-brand-ink text-brand' : 'bg-slate-100 text-slate-600')
              }
            >
              {o.label}
            </button>
          ))}
        </div>

        <label className="block">
          <span className="mb-1 block text-[13px] font-medium text-slate-500">Bo'lim (ixtiyoriy)</span>
          <select
            value={departmentId}
            onChange={(e) => setDepartmentId(e.target.value)}
            className="w-full rounded-xl border border-slate-200 bg-white px-3 py-3 text-[15px]"
          >
            <option value="">Bilmayman / umumiy</option>
            {(deps.data ?? []).map((d) => (
              <option key={d.id} value={d.id}>
                {d.name}
              </option>
            ))}
          </select>
        </label>

        <label className="block">
          <span className="mb-1 block text-[13px] font-medium text-slate-500">Matn</span>
          <textarea
            value={text}
            onChange={(e) => setText(e.target.value)}
            rows={5}
            maxLength={2000}
            placeholder={type === 'complaint' ? 'Nima boʻldi? Qachon, qayerda?' : 'Taklifingizni yozing'}
            className="w-full rounded-xl border border-slate-200 bg-white px-3 py-3 text-[15px]"
          />
        </label>

        <div className="flex items-center gap-3">
          <button
            type="button"
            onClick={() => fileRef.current?.click()}
            className="inline-flex items-center gap-2 rounded-xl bg-slate-100 px-3 py-2.5 text-[14px] font-semibold text-slate-700"
          >
            <Camera className="h-4 w-4" /> {photo ? 'Rasmni almashtirish' : "Rasm qo'shish"}
          </button>
          {photo && <span className="min-w-0 truncate text-[13px] text-slate-500">{photo.name}</span>}
          <input
            ref={fileRef}
            type="file"
            accept="image/*"
            className="hidden"
            onChange={(e) => setPhoto(e.target.files?.[0] ?? null)}
          />
        </div>

        {error && <p className="rounded-xl bg-red-50 px-3 py-2 text-[14px] text-red-600">{error}</p>}

        <button
          type="button"
          onClick={send}
          disabled={!text.trim() || sending}
          className="w-full rounded-2xl bg-brand-ink py-3.5 text-[16px] font-semibold text-brand disabled:opacity-40"
        >
          {sending ? 'Yuborilmoqda…' : 'Yuborish'}
        </button>
      </div>
    </Card>
  )
}
