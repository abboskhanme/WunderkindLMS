import { useEffect, useMemo, useRef, useState } from 'react'
import { Send, Megaphone, Users, AlertTriangle, Search, Check, ChevronRight } from 'lucide-react'
import type { Broadcast, MessageClass, TelegramParent, TelegramStatus } from '@/types'
import {
  getBroadcasts,
  getTelegramRegistrations,
  getTelegramStatus,
  sendBroadcast,
  type SendBroadcastReq,
} from '@/api/services/messages'
import {
  getGroupMembers,
  getGroups,
  type StudyGroupListItem,
  type StudyGroupMember,
} from '@/api/services/groups'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { useAuth } from '@/context/auth-context'
import { cn, formatDate, formatMoney } from '@/lib/utils'
import { messageTemplates as TEMPLATES, messageTokens as TOKENS } from '@/config/messageTemplates'
import { formatPhone } from '@/lib/phone'
import { escapeHtml, markupToHtml } from '@/lib/telegramMarkup'
import { FormatToolbar } from '@/components/ui/FormatToolbar'
import { RichText } from '@/components/ui/RichText'
import { BroadcastDetailModal } from './BroadcastDetailModal'
import { BroadcastImagePicker } from './BroadcastImagePicker'

type Scope = 'class' | 'group' | 'all' | 'selected'

/**
 * Preview uchun klient tomonida o'rinbosarlarni to'ldiradi (backend bilan bir xil mantiq): matn
 * avval formatlangan HTML'ga aylanadi, qiymatlar esa ekranlanib qo'yiladi — server ham shunday.
 */
function fill(text: string, p: TelegramParent): string {
  // Balans faqat moliya ruxsati bilan keladi. Ruxsat yo'q bo'lsa preview PUL
  // o'rniga chiziqcha ko'rsatadi — xabarning o'ziga esa haqiqiy summani server
  // qo'yadi, ya'ni yuborish buzilmaydi, faqat ko'rsatilmaydi.
  const debt = p.balance === null ? '—' : p.balance < 0 ? formatMoney(-p.balance) : "0 so'm"
  const v = (s: string) => escapeHtml(s).replace(/\$/g, '$$$$')
  return markupToHtml(text)
    .replace(/\{fish\}/gi, v(p.studentName))
    .replace(/\{sinf\}/gi, v(p.className))
    .replace(/\{qarzdorlik\}/gi, v(debt))
    .replace(/\{balans\}/gi, v(p.balance === null ? '—' : formatMoney(p.balance)))
    .replace(/\{ota[-_]ona\}/gi, v(p.parentName || 'Ota-ona'))
    .replace(/\{telefon\}/gi, v(formatPhone(p.phone)))
}

/** Ota-onalarga Telegram bot orqali e'lon yuborish: qamrov + andoza + o'rinbosarlar + tarix. */
export function BroadcastPanel({ classes }: { classes: MessageClass[] }) {
  // Balans moliya ruxsatiga bog'liq: serverda `null` bilan keladi, bu yerda esa
  // qarzdorlar filtri ham yashiriladi — aks holda filtr HAR DOIM bo'sh ro'yxat
  // berardi va foydalanuvchi buni nosozlik deb o'ylardi.
  const { user } = useAuth()
  const canSeeBalance = !user?.permissions || user.permissions.includes('finance')

  const [scope, setScope] = useState<Scope>('class')
  const [className, setClassName] = useState<string>(() => classes[0]?.name ?? '')
  const [onlyDebtors, setOnlyDebtors] = useState(false)
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set())
  const [text, setText] = useState('')
  /** E'lon rasmlari (`/uploads/...`) — 2026-10-24. */
  const [images, setImages] = useState<string[]>([])
  const [uploadingImages, setUploadingImages] = useState(false)
  const [search, setSearch] = useState('')

  // O'quv guruhlari (G-8): guruhni bir nechta sinf boqadi, shuning uchun
  // oluvchilar sinf nomi bilan emas, guruhning FAOL a'zoligi bilan topiladi.
  const [groups, setGroups] = useState<StudyGroupListItem[]>([])
  const [groupId, setGroupId] = useState('')
  const [groupMemberIds, setGroupMemberIds] = useState<Set<string>>(new Set())

  const [parents, setParents] = useState<TelegramParent[]>([])
  const [broadcasts, setBroadcasts] = useState<Broadcast[]>([])
  const [status, setStatus] = useState<TelegramStatus | null>(null)
  const [loading, setLoading] = useState(true)
  const [sending, setSending] = useState(false)
  const [result, setResult] = useState<string | null>(null)
  const [sendError, setSendError] = useState<string | null>(null)
  /** Batafsil ochilgan e'lon (2026-10-11: "kimga"). */
  const [detailId, setDetailId] = useState<string | null>(null)

  const textRef = useRef<HTMLTextAreaElement>(null)

  useEffect(() => {
    getTelegramStatus().then(setStatus)
    // Guruhlar ro'yxati `classes` ruxsatini talab qiladi — faqat `messages`
    // ruxsati bor xodimda 403 keladi va "Guruh bo'yicha" tugmasi umuman
    // ko'rinmaydi (ekran xato bermaydi).
    getGroups().then(setGroups).catch(() => setGroups([]))
    Promise.all([getBroadcasts(), getTelegramRegistrations()])
      .then(([b, p]) => {
        setBroadcasts(b)
        setParents(p)
      })
      .finally(() => setLoading(false))
  }, [])

  // Tanlangan guruhning FAOL a'zolari — oluvchilar shu ro'yxatdan chiqadi.
  useEffect(() => {
    let cancelled = false
    const load: Promise<StudyGroupMember[]> = groupId
      ? getGroupMembers(groupId)
      : Promise.resolve([])
    load
      .then((members) => {
        if (cancelled) return
        setGroupMemberIds(new Set(members.filter((m) => !m.leftOn).map((m) => m.studentId)))
      })
      .catch(() => {
        if (!cancelled) setGroupMemberIds(new Set())
      })
    return () => {
      cancelled = true
    }
  }, [groupId])

  // Qamrov bo'yicha qabul qiluvchilar (har bir yozuv = bitta o'quvchi/chat).
  const recipients = useMemo(() => {
    let list = parents
    if (scope === 'class') list = list.filter((p) => p.className === className)
    else if (scope === 'group') list = list.filter((p) => groupMemberIds.has(p.studentId))
    else if (scope === 'selected') list = list.filter((p) => selectedIds.has(p.studentId))
    if (onlyDebtors && scope !== 'selected') list = list.filter((p) => p.balance !== null && p.balance < 0)
    return list
  }, [parents, scope, className, groupMemberIds, selectedIds, onlyDebtors])

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
    if ((!t && images.length === 0) || sending || uploadingImages) return
    if (scope === 'class' && !className) return setResult('Sinf tanlang.')
    if (scope === 'group' && !groupId) return setResult('Guruh tanlang.')
    if (scope === 'selected' && selectedIds.size === 0) return setResult('Hech kim tanlanmadi.')
    setSending(true)
    setResult(null)
    setSendError(null)
    try {
      const req: SendBroadcastReq = {
        scope,
        className: scope === 'class' ? className : undefined,
        groupId: scope === 'group' ? groupId : undefined,
        onlyDebtors: scope !== 'selected' && onlyDebtors,
        studentIds: scope === 'selected' ? [...selectedIds] : undefined,
        text: t,
        imageUrls: images.length > 0 ? images : undefined,
      }
      const b = await sendBroadcast(req)
      setBroadcasts((prev) => [b, ...prev])
      setResult(
        b.recipientCount === 0
          ? "Mos keluvchi ro'yxatdagi ota-ona topilmadi — e'lon saqlandi, lekin hech kimga yuborilmadi."
          : `E'lon yuborildi: ${b.sentCount}/${b.recipientCount} ota-onaga yetkazildi` +
              (b.noTelegram ? ` · ${b.noTelegram} o'quvchining ota-onasi Telegramga ulanmagan.` : '.'),
      )
    } catch (err) {
      setSendError(
        (err as { response?: { data?: { message?: string } } })?.response?.data?.message ??
          "E'lonni yuborib bo'lmadi — qaytadan urinib ko'ring.",
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

  const preview = recipients[0] && (text.trim() || images.length > 0) ? fill(text, recipients[0]) : null
  // Telegram rasm izohi 1024 belgigacha: uzunroq matn rasmlardan keyin alohida xabar bo'lib boradi.
  const captionTooLong = images.length > 0 && !!preview && preview.replace(/<[^>]+>/g, '').length > 1000

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
          {groups.length > 0 && (
            <ScopeButton active={scope === 'group'} onClick={() => setScope('group')}>
              Guruh bo'yicha
            </ScopeButton>
          )}
          <ScopeButton active={scope === 'all'} onClick={() => setScope('all')}>
            Barcha ota-onalar
          </ScopeButton>
          <ScopeButton active={scope === 'selected'} onClick={() => setScope('selected')}>
            Tanlab
          </ScopeButton>

          {scope === 'group' && (
            <select
              value={groupId}
              onChange={(e) => setGroupId(e.target.value)}
              className="rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-sm text-slate-700 outline-none focus:border-brand-400"
            >
              <option value="">Guruh tanlang</option>
              {groups.map((g) => (
                <option key={g.id} value={g.id}>
                  {g.name} — {g.subjectName}
                </option>
              ))}
            </select>
          )}

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

          {scope !== 'selected' && canSeeBalance && (
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

          {/* Formatlash: **qalin**, __kursiv__ … — Telegram ilovasidagi belgilar (2026-10-11). */}
          <FormatToolbar textareaRef={textRef} value={text} onChange={setText} className="mb-1.5" />
          <textarea
            ref={textRef}
            className="h-36 w-full resize-none rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
            placeholder="E'lon matni — o'rinbosarlar har o'quvchiga moslab to'ldiriladi. Matnni belgilab, yuqoridagi tugmalar bilan formatlang."
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

          <BroadcastImagePicker value={images} onChange={setImages} onBusyChange={setUploadingImages} />

          {/* Preview */}
          {preview !== null && (
            <div className="mt-3 rounded-lg bg-slate-50 px-3 py-2">
              <p className="mb-1 text-xs font-medium text-slate-400">
                Namuna ({recipients[0].studentName}):
              </p>
              {images.length > 0 && (
                <div className="mb-2 flex flex-wrap gap-1">
                  {images.map((url, i) => (
                    <img key={`${url}-${i}`} src={url} alt="" className="h-14 w-14 rounded-md object-cover" />
                  ))}
                </div>
              )}
              <RichText
                html={`📢 <b>Maktab e'loni</b>${preview ? `\n\n${preview}` : ''}`}
                className="text-sm text-slate-700"
              />
              {captionTooLong && (
                <p className="mt-1 text-xs text-slate-400">
                  Matn uzun — Telegramda avval rasmlar, keyin matn alohida xabar bo'lib boradi.
                </p>
              )}
            </div>
          )}

          <div className="mt-3 flex items-center justify-between gap-3">
            {result && <p className="text-sm font-medium text-emerald-700">{result}</p>}
            {sendError && <p className="text-sm font-medium text-red-600">{sendError}</p>}
            <Button
              className="ml-auto"
              onClick={handleSend}
              disabled={
                (!text.trim() && images.length === 0) || uploadingImages || sending || recipients.length === 0
              }
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
                      {p.balance !== null && p.balance < 0 && (
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
                  <button
                    key={b.id}
                    type="button"
                    onClick={() => setDetailId(b.id)}
                    className="group block w-full rounded-lg border border-slate-100 p-3 text-left transition-colors hover:border-brand-200 hover:bg-brand-50/30"
                    title="Batafsil: kimga yetdi, kimga yetmadi"
                  >
                    <div className="mb-1 flex items-center gap-2">
                      <span className="rounded-md bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
                        {b.className}
                      </span>
                      <span className="ml-auto flex items-center gap-0.5 text-xs text-slate-400 group-hover:text-brand-600">
                        Batafsil <ChevronRight className="h-3.5 w-3.5" />
                      </span>
                    </div>
                    {b.imageUrls && b.imageUrls.length > 0 && (
                      <div className="mb-1.5 flex gap-1">
                        {b.imageUrls.slice(0, 4).map((url, i) => (
                          <img key={`${url}-${i}`} src={url} alt="" className="h-12 w-12 rounded-md object-cover" />
                        ))}
                        {b.imageUrls.length > 4 && (
                          <span className="flex h-12 w-12 items-center justify-center rounded-md bg-slate-100 text-xs font-medium text-slate-500">
                            +{b.imageUrls.length - 4}
                          </span>
                        )}
                      </div>
                    )}
                    <RichText markup={b.text} className="line-clamp-4 text-sm text-slate-700" />
                    <div className="mt-1.5 flex items-center justify-between gap-2 text-xs text-slate-400">
                      <span>
                        {formatDate(b.createdAt)} · {b.senderName}
                      </span>
                      <span>
                        {b.sentCount}/{b.recipientCount} yetkazildi
                        {b.noTelegram ? <span className="text-amber-600"> · {b.noTelegram} Telegramsiz</span> : null}
                      </span>
                    </div>
                  </button>
                ))}
              </div>
            )}
          </Card>
        </div>
      </div>
      <BroadcastDetailModal id={detailId} onClose={() => setDetailId(null)} />
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
