import { useEffect, useMemo, useRef, useState } from 'react'
import { Send, Megaphone, Users, AlertTriangle, Search, Check } from 'lucide-react'
import type { Broadcast, MessageClass, TelegramParent, TelegramStatus } from '@/types'
import {
  getBroadcasts,
  getTelegramRegistrations,
  getTelegramStatus,
  sendBroadcast,
  type SendBroadcastReq,
} from '@/api/services/messages'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { cn, formatDate, formatMoney } from '@/lib/utils'
import { messageTemplates as TEMPLATES, messageTokens as TOKENS } from '@/config/messageTemplates'

type Scope = 'class' | 'all' | 'selected'

/** Preview uchun klient tomonida o'rinbosarlarni to'ldiradi (backend bilan bir xil mantiq). */
function fill(text: string, p: TelegramParent): string {
  const debt = p.balance < 0 ? formatMoney(-p.balance) : "0 so'm"
  return text
    .replace(/\{fish\}/gi, p.studentName)
    .replace(/\{sinf\}/gi, p.className)
    .replace(/\{qarzdorlik\}/gi, debt)
    .replace(/\{balans\}/gi, formatMoney(p.balance))
    .replace(/\{ota[-_]ona\}/gi, p.parentName || 'Ota-ona')
    .replace(/\{telefon\}/gi, p.phone)
}

/** Ota-onalarga Telegram bot orqali e'lon yuborish: qamrov + andoza + o'rinbosarlar + tarix. */
export function BroadcastPanel({ classes }: { classes: MessageClass[] }) {
  const [scope, setScope] = useState<Scope>('class')
  const [className, setClassName] = useState<string>(() => classes[0]?.name ?? '')
  const [onlyDebtors, setOnlyDebtors] = useState(false)
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set())
  const [text, setText] = useState('')
  const [search, setSearch] = useState('')

  const [parents, setParents] = useState<TelegramParent[]>([])
  const [broadcasts, setBroadcasts] = useState<Broadcast[]>([])
  const [status, setStatus] = useState<TelegramStatus | null>(null)
  const [loading, setLoading] = useState(true)
  const [sending, setSending] = useState(false)
  const [result, setResult] = useState<string | null>(null)

  const textRef = useRef<HTMLTextAreaElement>(null)

  useEffect(() => {
    getTelegramStatus().then(setStatus)
    Promise.all([getBroadcasts(), getTelegramRegistrations()])
      .then(([b, p]) => {
        setBroadcasts(b)
        setParents(p)
      })
      .finally(() => setLoading(false))
  }, [])

  // Qamrov bo'yicha qabul qiluvchilar (har bir yozuv = bitta o'quvchi/chat).
  const recipients = useMemo(() => {
    let list = parents
    if (scope === 'class') list = list.filter((p) => p.className === className)
    else if (scope === 'selected') list = list.filter((p) => selectedIds.has(p.studentId))
    if (onlyDebtors && scope !== 'selected') list = list.filter((p) => p.balance < 0)
    return list
  }, [parents, scope, className, selectedIds, onlyDebtors])

  const insertToken = (token: string) => {
    const el = textRef.current
    if (!el) {
      setText((t) => t + token)
      return
    }
    const start = el.selectionStart ?? text.length
    const end = el.selectionEnd ?? text.length
    const next = text.slice(0, start) + token + text.slice(end)
    setText(next)
    // Kursorni qo'yilgan token oxiriga qaytaramiz
    requestAnimationFrame(() => {
      el.focus()
      const pos = start + token.length
      el.setSelectionRange(pos, pos)
    })
  }

  const toggleSelected = (id: string) =>
    setSelectedIds((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const handleSend = async () => {
    const t = text.trim()
    if (!t || sending) return
    if (scope === 'class' && !className) return setResult('Sinf tanlang.')
    if (scope === 'selected' && selectedIds.size === 0) return setResult('Hech kim tanlanmadi.')
    setSending(true)
    setResult(null)
    try {
      const req: SendBroadcastReq = {
        scope,
        className: scope === 'class' ? className : undefined,
        onlyDebtors: scope !== 'selected' && onlyDebtors,
        studentIds: scope === 'selected' ? [...selectedIds] : undefined,
        text: t,
      }
      const b = await sendBroadcast(req)
      setBroadcasts((prev) => [b, ...prev])
      setResult(
        b.recipientCount === 0
          ? "Mos keluvchi ro'yxatdagi ota-ona topilmadi — e'lon saqlandi, lekin hech kimga yuborilmadi."
          : `E'lon yuborildi: ${b.sentCount}/${b.recipientCount} ota-onaga yetkazildi.`,
      )
    } finally {
      setSending(false)
    }
  }

  const filteredParents = useMemo(() => {
    const q = search.trim().toLowerCase()
    if (!q) return parents
    return parents.filter(
      (p) =>
        p.studentName.toLowerCase().includes(q) ||
        p.className.toLowerCase().includes(q) ||
        (p.parentName ?? '').toLowerCase().includes(q),
    )
  }, [parents, search])

  const preview = recipients[0] ? fill(text, recipients[0]) : null

  return (
    <div className="space-y-4">
      {status && !status.configured && (
        <div className="flex items-start gap-2 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <p>
            Telegram bot hali sozlanmagan. E'lon saqlanadi, lekin yuborilmaydi. Sozlash —
            "Sozlamalar → Telegram bot" bo'limida.
          </p>
        </div>
      )}

      {/* Qamrov */}
      <Card>
        <div className="mb-3 flex items-center gap-2">
          <Megaphone className="h-5 w-5 text-brand-600" />
          <p className="font-semibold text-slate-800">Kimga yuborish</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <ScopeButton active={scope === 'class'} onClick={() => setScope('class')}>
            Sinf bo'yicha
          </ScopeButton>
          <ScopeButton active={scope === 'all'} onClick={() => setScope('all')}>
            Barcha ota-onalar
          </ScopeButton>
          <ScopeButton active={scope === 'selected'} onClick={() => setScope('selected')}>
            Tanlab
          </ScopeButton>

          {scope === 'class' && (
            <select
              value={className}
              onChange={(e) => setClassName(e.target.value)}
              className="rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-sm text-slate-700 outline-none focus:border-brand-400"
            >
              {classes.length === 0 && <option value="">Sinf yo'q</option>}
              {classes.map((c) => (
                <option key={c.name} value={c.name}>
                  {c.name}
                </option>
              ))}
            </select>
          )}

          {scope !== 'selected' && (
            <label className="inline-flex cursor-pointer items-center gap-1.5 rounded-lg border border-slate-200 px-3 py-1.5 text-sm text-slate-600">
              <input
                type="checkbox"
                checked={onlyDebtors}
                onChange={(e) => setOnlyDebtors(e.target.checked)}
                className="h-4 w-4 accent-brand-600"
              />
              Faqat qarzdorlar
            </label>
          )}

          <span className="ml-auto inline-flex items-center gap-1.5 text-sm font-medium text-brand-700">
            <Users className="h-4 w-4" />
            {recipients.length} ta oluvchi
          </span>
        </div>
      </Card>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        {/* Matn yozish */}
        <Card>
          <p className="mb-2 font-semibold text-slate-800">E'lon matni</p>

          {/* Andozalar */}
          <div className="mb-2 flex flex-wrap gap-1.5">
            {TEMPLATES.map((t) => (
              <button
                key={t.label}
                type="button"
                onClick={() => setText(t.text)}
                className="rounded-full border border-slate-200 px-2.5 py-1 text-xs text-slate-600 transition-colors hover:bg-slate-50"
              >
                {t.label}
              </button>
            ))}
            {text && (
              <button
                type="button"
                onClick={() => setText('')}
                className="rounded-full border border-slate-200 px-2.5 py-1 text-xs text-slate-400 transition-colors hover:bg-slate-50"
              >
                Tozalash
              </button>
            )}
          </div>

          <textarea
            ref={textRef}
            className="h-36 w-full resize-none rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
            placeholder="E'lon matni — o'rinbosarlar har o'quvchiga moslab to'ldiriladi"
            value={text}
            onChange={(e) => setText(e.target.value)}
          />

          {/* O'rinbosarlar */}
          <div className="mt-2">
            <p className="mb-1 text-xs text-slate-400">O'rinbosar qo'shish (har o'quvchiga moslanadi):</p>
            <div className="flex flex-wrap gap-1.5">
              {TOKENS.map((t) => (
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
          </div>

          {/* Preview */}
          {preview && (
            <div className="mt-3 rounded-lg bg-slate-50 px-3 py-2">
              <p className="mb-1 text-xs font-medium text-slate-400">
                Namuna ({recipients[0].studentName}):
              </p>
              <p className="whitespace-pre-wrap break-words text-sm text-slate-700">
                📢 Maktab e'loni{'\n\n'}
                {preview}
              </p>
            </div>
          )}

          <div className="mt-3 flex items-center justify-between gap-3">
            {result && <p className="text-sm font-medium text-emerald-700">{result}</p>}
            <Button
              className="ml-auto"
              onClick={handleSend}
              disabled={!text.trim() || sending || recipients.length === 0}
            >
              <Send className="h-4 w-4" /> {sending ? 'Yuborilmoqda...' : 'Yuborish'}
            </Button>
          </div>
        </Card>

        <div className="space-y-4">
          {/* Tanlab — ota-ona ro'yxati */}
          {scope === 'selected' && (
            <Card>
              <p className="mb-2 font-semibold text-slate-800">Oluvchilarni tanlang</p>
              <div className="relative mb-2">
                <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
                <input
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  placeholder="O'quvchi, sinf yoki ota-ona..."
                  className="w-full rounded-lg border border-slate-200 py-2 pl-9 pr-3 text-sm outline-none focus:border-brand-400"
                />
              </div>
              <div className="max-h-72 space-y-1 overflow-y-auto">
                {filteredParents.length === 0 && (
                  <p className="py-6 text-center text-sm text-slate-400">
                    Ro'yxatdan o'tgan ota-ona yo'q
                  </p>
                )}
                {filteredParents.map((p) => {
                  const active = selectedIds.has(p.studentId)
                  return (
                    <button
                      key={`${p.studentId}-${p.chatId}`}
                      type="button"
                      onClick={() => toggleSelected(p.studentId)}
                      className={cn(
                        'flex w-full items-center gap-2 rounded-lg border px-3 py-2 text-left text-sm transition-colors',
                        active
                          ? 'border-brand-300 bg-brand-50'
                          : 'border-slate-100 hover:bg-slate-50',
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
                        <span className="font-medium text-slate-700">{p.studentName}</span>
                        <span className="text-xs text-slate-400">
                          {' '}
                          · {p.className || '—'} · {p.parentName || 'Ota-ona'}
                        </span>
                      </span>
                      {p.balance < 0 && (
                        <span className="shrink-0 rounded-full bg-red-50 px-2 py-0.5 text-xs font-medium text-red-600">
                          qarz
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
            </Card>
          )}

          {/* Tarix */}
          <Card>
            <p className="mb-3 font-semibold text-slate-800">Yuborilgan e'lonlar</p>
            {loading ? (
              <Loader label="Yuklanmoqda..." />
            ) : (
              <div className="max-h-96 space-y-3 overflow-y-auto">
                {broadcasts.length === 0 && (
                  <p className="py-6 text-center text-sm text-slate-400">Hali e'lon yuborilmagan</p>
                )}
                {broadcasts.map((b) => (
                  <div key={b.id} className="rounded-lg border border-slate-100 p-3">
                    <div className="mb-1 flex items-center gap-2">
                      <span className="rounded-md bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
                        {b.className}
                      </span>
                    </div>
                    <p className="whitespace-pre-wrap break-words text-sm text-slate-700">{b.text}</p>
                    <div className="mt-1.5 flex items-center justify-between text-xs text-slate-400">
                      <span>{formatDate(b.createdAt)}</span>
                      <span>
                        {b.sentCount}/{b.recipientCount} yetkazildi
                      </span>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </Card>
        </div>
      </div>
    </div>
  )
}

function ScopeButton({
  active,
  onClick,
  children,
}: {
  active: boolean
  onClick: () => void
  children: React.ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'rounded-lg px-3 py-1.5 text-sm font-medium transition-colors',
        active ? 'bg-brand-600 text-white' : 'border border-slate-200 text-slate-600 hover:bg-slate-50',
      )}
    >
      {children}
    </button>
  )
}
