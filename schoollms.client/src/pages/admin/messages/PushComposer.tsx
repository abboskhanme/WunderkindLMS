import { useEffect, useMemo, useRef, useState } from 'react'
import { Send, Bell, AlertTriangle, Search, Check } from 'lucide-react'
import type { MessageClass, PushMessage, PushRecipient } from '@/types'
import {
  getPushStatus,
  getPushMessages,
  getPushRecipients,
  sendPush,
  type SendPushReq,
} from '@/api/services/messages'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Input } from '@/components/ui/Input'
import { cn, formatDate } from '@/lib/utils'
import { messageTemplates, messageTokens } from '@/config/messageTemplates'

type Audience = 'parents' | 'teachers' | 'selected'

/** Ilovaga (Firebase push) bildirishnoma yuborish: qabul qiluvchi + sarlavha/matn + tarix. */
export function PushComposer({ classes }: { classes: MessageClass[] }) {
  const [audience, setAudience] = useState<Audience>('parents')
  const [className, setClassName] = useState('')
  const [title, setTitle] = useState('')
  const [body, setBody] = useState('')

  const [configured, setConfigured] = useState(true)
  const [history, setHistory] = useState<PushMessage[]>([])
  const [loading, setLoading] = useState(true)
  const [sending, setSending] = useState(false)
  const [result, setResult] = useState<string | null>(null)

  // "Tanlab" rejim uchun
  const [recipients, setRecipients] = useState<PushRecipient[]>([])
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set())
  const [search, setSearch] = useState('')
  const recipientsLoaded = useRef(false)
  const bodyRef = useRef<HTMLTextAreaElement>(null)

  // Tayyor andoza tanlash: sarlavha = nom, matn = andoza (o'rinbosarlar bilan).
  const applyTemplate = (label: string, text: string) => {
    setTitle(label)
    setBody(text)
  }

  // O'rinbosarni matn (body) kursoriga qo'yadi.
  const insertToken = (token: string) => {
    const el = bodyRef.current
    if (!el) {
      setBody((b) => b + token)
      return
    }
    const start = el.selectionStart ?? body.length
    const end = el.selectionEnd ?? body.length
    setBody(body.slice(0, start) + token + body.slice(end))
    requestAnimationFrame(() => {
      el.focus()
      const pos = start + token.length
      el.setSelectionRange(pos, pos)
    })
  }

  useEffect(() => {
    getPushStatus().then((s) => setConfigured(s.configured))
    getPushMessages()
      .then(setHistory)
      .finally(() => setLoading(false))
  }, [])

  // Oluvchilar ro'yxatini birinchi marta "Tanlab" tanlanganda yuklaymiz.
  useEffect(() => {
    if (audience === 'selected' && !recipientsLoaded.current) {
      recipientsLoaded.current = true
      getPushRecipients().then(setRecipients)
    }
  }, [audience])

  const filteredRecipients = useMemo(() => {
    const q = search.trim().toLowerCase()
    if (!q) return recipients
    return recipients.filter(
      (r) => r.name.toLowerCase().includes(q) || r.detail.toLowerCase().includes(q),
    )
  }, [recipients, search])

  const toggleRecipient = (id: string) =>
    setSelectedIds((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const handleSend = async () => {
    if ((!title.trim() && !body.trim()) || sending) return
    if (audience === 'selected' && selectedIds.size === 0) {
      setResult('Hech kim tanlanmadi.')
      return
    }
    setSending(true)
    setResult(null)
    try {
      const req: SendPushReq = {
        audience,
        className: audience === 'parents' && className ? className : undefined,
        userIds: audience === 'selected' ? [...selectedIds] : undefined,
        title: title.trim(),
        body: body.trim(),
      }
      const p = await sendPush(req)
      setHistory((prev) => [p, ...prev])
      setTitle('')
      setBody('')
      setResult(
        p.recipientCount === 0
          ? "Qurilma topilmadi — hech kimga yuborilmadi (foydalanuvchilar ilovaga kirib qurilma ulashishi kerak)."
          : `Push yuborildi: ${p.sentCount}/${p.recipientCount} qurilmaga.`,
      )
    } finally {
      setSending(false)
    }
  }

  const control =
    'rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-sm text-slate-700 outline-none focus:border-brand-400'

  return (
    <div className="space-y-4">
      {!configured && (
        <div className="flex items-start gap-2 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <p>
            Firebase push hali sozlanmagan. Bildirishnoma saqlanadi, lekin yuborilmaydi. Sozlash —
            "Sozlamalar → Push (Firebase)" bo'limida.
          </p>
        </div>
      )}

      <Card>
        <div className="mb-3 flex items-center gap-2">
          <Bell className="h-5 w-5 text-brand-600" />
          <p className="font-semibold text-slate-800">Kimga yuborish</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <div className="flex w-fit gap-1 rounded-lg bg-slate-100 p-1">
            <button
              type="button"
              onClick={() => setAudience('parents')}
              className={cn(
                'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                audience === 'parents' ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500 hover:text-slate-700',
              )}
            >
              Ota-onalar
            </button>
            <button
              type="button"
              onClick={() => setAudience('teachers')}
              className={cn(
                'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                audience === 'teachers' ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500 hover:text-slate-700',
              )}
            >
              O'qituvchilar
            </button>
            <button
              type="button"
              onClick={() => setAudience('selected')}
              className={cn(
                'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                audience === 'selected' ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500 hover:text-slate-700',
              )}
            >
              Tanlab
            </button>
          </div>

          {audience === 'parents' && (
            <select value={className} onChange={(e) => setClassName(e.target.value)} className={control}>
              <option value="">Barcha sinflar</option>
              {classes.map((c) => (
                <option key={c.name} value={c.name}>
                  {c.name}
                </option>
              ))}
            </select>
          )}
          {audience === 'selected' && (
            <span className="text-sm font-medium text-brand-700">{selectedIds.size} ta tanlandi</span>
          )}
        </div>

        {/* Tanlab — oluvchilar ro'yxati */}
        {audience === 'selected' && (
          <div className="mt-3 border-t border-slate-100 pt-3">
            <div className="relative mb-2">
              <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
              <input
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Ism, sinf yoki o'qituvchi..."
                className="w-full rounded-lg border border-slate-200 py-2 pl-9 pr-3 text-sm outline-none focus:border-brand-400"
              />
            </div>
            <div className="max-h-72 space-y-1 overflow-y-auto">
              {filteredRecipients.length === 0 && (
                <p className="py-6 text-center text-sm text-slate-400">Oluvchi topilmadi</p>
              )}
              {filteredRecipients.map((r) => {
                const active = selectedIds.has(r.userId)
                return (
                  <button
                    key={r.userId}
                    type="button"
                    onClick={() => toggleRecipient(r.userId)}
                    className={cn(
                      'flex w-full items-center gap-2 rounded-lg border px-3 py-2 text-left text-sm transition-colors',
                      active ? 'border-brand-300 bg-brand-50' : 'border-slate-100 hover:bg-slate-50',
                    )}
                  >
                    <span
                      className={cn(
                        'flex h-4 w-4 shrink-0 items-center justify-center rounded border',
                        active ? 'border-brand-500 bg-brand-500 text-white' : 'border-slate-300',
                      )}
                    >
                      {active && <Check className="h-3 w-3" />}
                    </span>
                    <span className="flex-1">
                      <span className="font-medium text-slate-700">{r.name}</span>
                      <span className="text-xs text-slate-400">
                        {' '}· {r.group}
                        {r.detail ? ` · ${r.detail}` : ''}
                      </span>
                    </span>
                    {!r.hasDevice && (
                      <span className="shrink-0 rounded-full bg-amber-50 px-2 py-0.5 text-[10px] font-medium text-amber-600">
                        qurilma yo'q
                      </span>
                    )}
                  </button>
                )
              })}
            </div>
            {selectedIds.size > 0 && (
              <button
                type="button"
                onClick={() => setSelectedIds(new Set())}
                className="mt-2 text-xs text-slate-500 hover:text-slate-700"
              >
                Tanlovni tozalash ({selectedIds.size})
              </button>
            )}
          </div>
        )}
      </Card>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <Card className="space-y-3">
          <p className="font-semibold text-slate-800">Bildirishnoma</p>

          {/* Tayyor andozalar */}
          <div className="flex flex-wrap gap-1.5">
            {messageTemplates.map((t) => (
              <button
                key={t.label}
                type="button"
                onClick={() => applyTemplate(t.label, t.text)}
                className="rounded-full border border-slate-200 px-2.5 py-1 text-xs text-slate-600 transition-colors hover:bg-slate-50"
              >
                {t.label}
              </button>
            ))}
            {(title || body) && (
              <button
                type="button"
                onClick={() => {
                  setTitle('')
                  setBody('')
                }}
                className="rounded-full border border-slate-200 px-2.5 py-1 text-xs text-slate-400 transition-colors hover:bg-slate-50"
              >
                Tozalash
              </button>
            )}
          </div>

          <Input label="Sarlavha" value={title} onChange={(e) => setTitle(e.target.value)} placeholder="masalan: Yig'ilish" />
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-600">Matn</label>
            <textarea
              ref={bodyRef}
              className="h-28 w-full resize-none rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
              placeholder="Bildirishnoma matni"
              value={body}
              onChange={(e) => setBody(e.target.value)}
            />
            {/* O'rinbosarlar */}
            <div className="mt-2 flex flex-wrap gap-1.5">
              {messageTokens.map((t) => (
                <button
                  key={t.token}
                  type="button"
                  onClick={() => insertToken(t.token)}
                  className="rounded-md bg-slate-100 px-2 py-1 text-xs font-medium text-slate-600 transition-colors hover:bg-brand-50 hover:text-brand-700"
                  title={t.token}
                >
                  {t.label}
                </button>
              ))}
            </div>
            <p className="mt-1 text-xs text-slate-400">
              O'rinbosarlar har o'quvchiga moslab to'ldiriladi (ota-onalarga). O'qituvchilarga faqat {'{fish}'} ishlaydi.
            </p>
          </div>
          <div className="flex items-center justify-between gap-3">
            {result && <p className="text-sm font-medium text-emerald-700">{result}</p>}
            <Button className="ml-auto" onClick={handleSend} disabled={(!title.trim() && !body.trim()) || sending}>
              <Send className="h-4 w-4" /> {sending ? 'Yuborilmoqda...' : 'Yuborish'}
            </Button>
          </div>
        </Card>

        <Card>
          <p className="mb-3 font-semibold text-slate-800">Yuborilgan push'lar</p>
          {loading ? (
            <Loader label="Yuklanmoqda..." />
          ) : (
            <div className="max-h-96 space-y-3 overflow-y-auto">
              {history.length === 0 && (
                <p className="py-6 text-center text-sm text-slate-400">Hali push yuborilmagan</p>
              )}
              {history.map((p) => (
                <div key={p.id} className="rounded-lg border border-slate-100 p-3">
                  <div className="mb-1 flex items-center gap-2">
                    <span className="rounded-md bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
                      {p.audience}
                    </span>
                  </div>
                  {p.title && <p className="text-sm font-semibold text-slate-800">{p.title}</p>}
                  {p.body && <p className="whitespace-pre-wrap break-words text-sm text-slate-700">{p.body}</p>}
                  <div className="mt-1.5 flex items-center justify-between text-xs text-slate-400">
                    <span>{formatDate(p.createdAt)}</span>
                    <span>{p.sentCount}/{p.recipientCount} yuborildi</span>
                  </div>
                </div>
              ))}
            </div>
          )}
        </Card>
      </div>
    </div>
  )
}
