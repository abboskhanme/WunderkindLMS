/**
 * Avtomatik xabar shabloni — yaratish va tahrirlash (mijoz, 2026-10-26: "kelajakda qo'shimcha avtomatik
 * habar yaratish kerak bo'ladigan bo'lsa kod kiritmasdan…", keyin: "avtomatik xabarlar shablonini
 * yaratishni huddi shu e'lonlar kabi qilib ber").
 *
 * E'lon paneli (BroadcastPanel) bilan bir xil tuzilish: tepada "Kimga yuborish" (sinf / guruh / barcha
 * ota-onalar / tanlab, faqat qarzdorlar, oluvchilar soni), pastda matn — tayyor andozalar, formatlash,
 * o'rinbosarlar, rasm va namuna. Farqi: "Yuborish" o'rniga "Qachon yuboriladi" (bir marta / har kuni /
 * har hafta / har oy, vaqti) va "Saqlash". Yangi shablon O'CHIQ holda saqlanadi.
 */
import { useEffect, useMemo, useRef, useState } from 'react'
import { ArrowLeft, Check, Megaphone, Save, Search, Users } from 'lucide-react'
import type { TelegramParent } from '@/types'
import { getTelegramRegistrations } from '@/api/services/messages'
import { getGroups, getGroupMembers, type StudyGroupListItem } from '@/api/services/groups'
import {
  createAutoMessageRule,
  saveAutoMessageRule,
  type AutoMessageAudience,
  type AutoMessageClass,
  type AutoMessageRule,
  type AutoMessageSchedule,
} from '@/api/services/autoMessages'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input, Time24Input } from '@/components/ui/Input'
import { DatePicker } from '@/components/ui/DatePicker'
import { FormatToolbar } from '@/components/ui/FormatToolbar'
import { RichText } from '@/components/ui/RichText'
import { Switch } from '@/components/hr-tasks/ui'
import { useAuth } from '@/context/auth-context'
import { cn, formatMoney } from '@/lib/utils'
import { formatPhone } from '@/lib/phone'
import { escapeHtml, markupToHtml } from '@/lib/telegramMarkup'
import { messageTemplates as TEMPLATES, messageTokens as BROADCAST_TOKENS } from '@/config/messageTemplates'
import { BroadcastImagePicker } from './BroadcastImagePicker'
import { ScopeButton } from './BroadcastPanel'
import { SCHEDULES, WEEK_DAYS, errorText, isoDay } from './autoMessageLabels'

/** E'londagi o'rinbosarlar va avtomatik xabarning o'zinikilari (ism, yuborilgan kun va vaqt). */
const TOKENS = [
  ...BROADCAST_TOKENS.slice(0, 1),
  { token: '{ism}', label: 'Ism' },
  ...BROADCAST_TOKENS.slice(1),
  { token: '{sana}', label: 'Sana' },
  { token: '{vaqt}', label: 'Vaqt' },
]

/** Pupil's own name as the server takes it: the second word of "Familiya Ism Otasining ismi". */
function firstName(fullName: string): string {
  const parts = fullName.trim().split(/\s+/)
  return parts.length > 1 ? parts[1] : fullName.trim()
}

/** Namuna uchun o'rinbosarlarni to'ldiradi — server bilan bir xil qiymatlar (e'londagi kabi). */
function fill(text: string, p: TelegramParent, sendTime: string): string {
  const debt = p.balance === null ? '—' : p.balance < 0 ? formatMoney(-p.balance) : "0 so'm"
  const v = (s: string) => escapeHtml(s).replace(/\$/g, '$$$$')
  const today = new Date().toLocaleDateString('ru-RU', { timeZone: 'Asia/Tashkent' })
  return markupToHtml(text)
    .replace(/\{fish\}/gi, v(p.studentName))
    .replace(/\{ism\}/gi, v(firstName(p.studentName)))
    .replace(/\{sinf\}/gi, v(p.className))
    .replace(/\{qarzdorlik\}/gi, v(debt))
    .replace(/\{balans\}/gi, v(p.balance === null ? '—' : formatMoney(p.balance)))
    .replace(/\{ota[-_]ona\}/gi, v(p.parentName || 'Ota-ona'))
    .replace(/\{telefon\}/gi, v(formatPhone(p.phone)))
    .replace(/\{sana\}/gi, v(today))
    .replace(/\{vaqt\}/gi, v(sendTime))
}

export function AutoMessageTemplateEditor({
  rule,
  classes,
  onClose,
  onSaved,
}: {
  /** null — yangi shablon. */
  rule: AutoMessageRule | null
  classes: AutoMessageClass[]
  onClose: () => void
  onSaved: (rule: AutoMessageRule) => void
}) {
  const isNew = rule === null
  const { user } = useAuth()
  const canSeeBalance = !user?.permissions || user.permissions.includes('finance')

  // Kimga
  const [audience, setAudience] = useState<AutoMessageAudience>(rule?.audience ?? 'class')
  const [classId, setClassId] = useState(rule?.classIds?.[0] ?? classes[0]?.id ?? '')
  const [groupId, setGroupId] = useState(rule?.groupId ?? '')
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set(rule?.studentIds ?? []))
  const [onlyDebtors, setOnlyDebtors] = useState(rule?.onlyDebtors ?? false)
  const [search, setSearch] = useState('')

  // Matn
  const [title, setTitle] = useState(rule?.title ?? '')
  const [text, setText] = useState(rule?.text ?? '')
  const [images, setImages] = useState<string[]>(rule?.imageUrls ?? [])
  const [uploading, setUploading] = useState(false)

  // Qachon
  const [schedule, setSchedule] = useState<AutoMessageSchedule>(rule?.schedule ?? 'once')
  const [sendDate, setSendDate] = useState(rule?.sendDate ?? isoDay(-1))
  const [weekDays, setWeekDays] = useState<number[]>(rule?.weekDays?.length ? rule.weekDays : [1])
  const [day, setDay] = useState(rule?.dayOfMonth ?? 1)
  const [sendTime, setSendTime] = useState(rule?.sendTime ?? '10:00')
  const [approval, setApproval] = useState(rule?.requiresApproval ?? false)

  const [parents, setParents] = useState<TelegramParent[]>([])
  const [groups, setGroups] = useState<StudyGroupListItem[]>([])
  const [groupMemberIds, setGroupMemberIds] = useState<Set<string>>(new Set())
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const textRef = useRef<HTMLTextAreaElement>(null)

  useEffect(() => {
    getTelegramRegistrations().then(setParents).catch(() => setParents([]))
    // Guruhlar `classes` ruxsatini talab qiladi — bo'lmasa "Guruh bo'yicha" ko'rinmaydi (e'londagi kabi).
    getGroups().then(setGroups).catch(() => setGroups([]))
  }, [])

  useEffect(() => {
    if (!classId && classes[0]) setClassId(classes[0].id)
  }, [classes, classId])

  useEffect(() => {
    let cancelled = false
    ;(groupId ? getGroupMembers(groupId) : Promise.resolve([]))
      .then((members) => {
        if (!cancelled) setGroupMemberIds(new Set(members.filter((m) => !m.leftOn).map((m) => m.studentId)))
      })
      .catch(() => !cancelled && setGroupMemberIds(new Set()))
    return () => {
      cancelled = true
    }
  }, [groupId])

  const className = classes.find((c) => c.id === classId)?.name ?? ''

  // Hozir shu shablon kimga yetadi (ota-onasi Telegramda bo'lganlar) — e'londagi hisob bilan bir xil.
  const recipients = useMemo(() => {
    let list = parents
    if (audience === 'class') list = list.filter((p) => p.className === className)
    else if (audience === 'group') list = list.filter((p) => groupMemberIds.has(p.studentId))
    else if (audience === 'selected') list = list.filter((p) => selectedIds.has(p.studentId))
    if (onlyDebtors && audience !== 'selected') list = list.filter((p) => p.balance !== null && p.balance < 0)
    return list
  }, [parents, audience, className, groupMemberIds, selectedIds, onlyDebtors])

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

  const insertToken = (token: string) => {
    const el = textRef.current
    if (!el) return setText((t) => t + token)
    const start = el.selectionStart ?? text.length
    const end = el.selectionEnd ?? text.length
    setText(text.slice(0, start) + token + text.slice(end))
    requestAnimationFrame(() => {
      el.focus()
      el.setSelectionRange(start + token.length, start + token.length)
    })
  }

  const toggle = <T,>(list: T[], value: T) => (list.includes(value) ? list.filter((x) => x !== value) : [...list, value])
  const toggleSelected = (id: string) =>
    setSelectedIds((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const preview = recipients[0] && text.trim() ? fill(text, recipients[0], sendTime) : null

  const save = async () => {
    setError(null)
    if (!title.trim()) return setError('Shablon nomini yozing.')
    if (!text.trim()) return setError('Xabar matnini yozing.')
    if (audience === 'class' && !classId) return setError('Sinf tanlang.')
    if (audience === 'group' && !groupId) return setError('Guruh tanlang.')
    if (audience === 'selected' && selectedIds.size === 0) return setError('Hech kim tanlanmadi.')
    if (schedule === 'weekly' && weekDays.length === 0) return setError('Haftaning kamida bitta kunini tanlang.')
    setBusy(true)
    try {
      const body = {
        enabled: rule?.enabled ?? false,
        sendTime,
        dayOfMonth: day,
        minAmount: 0,
        text,
        imageUrls: images,
        requiresApproval: approval,
        title: title.trim(),
        schedule,
        sendDate: schedule === 'once' ? sendDate : null,
        weekDays: schedule === 'weekly' ? weekDays : [],
        audience,
        classIds: audience === 'class' ? [classId] : [],
        groupId: audience === 'group' ? groupId : null,
        studentIds: audience === 'selected' ? [...selectedIds] : [],
        onlyDebtors: audience !== 'selected' && onlyDebtors,
      }
      onSaved(isNew ? await createAutoMessageRule(body) : await saveAutoMessageRule(rule.key, body))
    } catch (e) {
      setError(errorText(e, "Saqlab bo'lmadi."))
    } finally {
      setBusy(false)
    }
  }

  const selectCls =
    'rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-sm text-slate-700 outline-none focus:border-brand-400'

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <Button variant="ghost" onClick={onClose} title="Orqaga">
          <ArrowLeft className="h-4 w-4" />
        </Button>
        <h2 className="font-semibold text-slate-800">{isNew ? 'Yangi avtomatik xabar shabloni' : `Shablon: ${rule.title}`}</h2>
      </div>

      {/* Kimga yuborish — e'londagi kabi */}
      <Card>
        <div className="mb-3 flex items-center gap-2">
          <Megaphone className="h-5 w-5 text-brand-600" />
          <p className="font-semibold text-slate-800">Kimga yuborish</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <ScopeButton active={audience === 'class'} onClick={() => setAudience('class')}>
            Sinf bo'yicha
          </ScopeButton>
          {groups.length > 0 && (
            <ScopeButton active={audience === 'group'} onClick={() => setAudience('group')}>
              Guruh bo'yicha
            </ScopeButton>
          )}
          <ScopeButton active={audience === 'all'} onClick={() => setAudience('all')}>
            Barcha ota-onalar
          </ScopeButton>
          <ScopeButton active={audience === 'selected'} onClick={() => setAudience('selected')}>
            Tanlab
          </ScopeButton>

          {audience === 'group' && (
            <select value={groupId} onChange={(e) => setGroupId(e.target.value)} className={selectCls}>
              <option value="">Guruh tanlang</option>
              {groups.map((g) => (
                <option key={g.id} value={g.id}>
                  {g.name} — {g.subjectName}
                </option>
              ))}
            </select>
          )}

          {audience === 'class' && (
            <select value={classId} onChange={(e) => setClassId(e.target.value)} className={selectCls}>
              {classes.length === 0 && <option value="">Sinf yo'q</option>}
              {classes.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </select>
          )}

          {audience !== 'selected' && canSeeBalance && (
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
            hozir {recipients.length} ta oluvchi
          </span>
        </div>
      </Card>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        {/* Xabar matni — e'londagi kabi */}
        <Card>
          <Input
            label="Shablon nomi"
            required
            maxLength={100}
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            placeholder="Masalan: Ota-onalar majlisi eslatmasi"
          />

          <p className="mb-2 mt-4 font-semibold text-slate-800">Xabar matni</p>
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

          <FormatToolbar textareaRef={textRef} value={text} onChange={setText} className="mb-1.5" />
          <textarea
            ref={textRef}
            className="h-36 w-full resize-none rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
            placeholder="Xabar matni — o'rinbosarlar har o'quvchiga moslab to'ldiriladi. Matnni belgilab, yuqoridagi tugmalar bilan formatlang."
            value={text}
            onChange={(e) => setText(e.target.value)}
          />

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

          <BroadcastImagePicker value={images} onChange={setImages} onBusyChange={setUploading} />

          {preview !== null && (
            <div className="mt-3 rounded-lg bg-slate-50 px-3 py-2">
              <p className="mb-1 text-xs font-medium text-slate-400">Namuna ({recipients[0].studentName}):</p>
              {images.length > 0 && (
                <div className="mb-2 flex flex-wrap gap-1">
                  {images.map((url, i) => (
                    <img key={`${url}-${i}`} src={url} alt="" className="h-14 w-14 rounded-md object-cover" />
                  ))}
                </div>
              )}
              <RichText html={preview} className="text-sm text-slate-700" />
            </div>
          )}

          <div className="mt-3 flex items-center justify-between gap-3">
            {error && <p className="text-sm font-medium text-red-600">{error}</p>}
            <div className="ml-auto flex gap-2">
              <Button variant="secondary" onClick={onClose} disabled={busy}>
                Bekor qilish
              </Button>
              <Button onClick={() => void save()} disabled={busy || uploading}>
                <Save className="h-4 w-4" /> {busy ? 'Saqlanmoqda…' : 'Saqlash'}
              </Button>
            </div>
          </div>
        </Card>

        <div className="space-y-4">
          {/* Qachon yuboriladi — e'londagi "Yuborish" o'rnida */}
          <Card>
            <p className="mb-3 font-semibold text-slate-800">Qachon yuboriladi</p>
            <div className="inline-flex rounded-xl bg-slate-100 p-1 text-sm font-medium">
              {SCHEDULES.map((s) => (
                <button
                  key={s.key}
                  type="button"
                  onClick={() => setSchedule(s.key)}
                  className={cn(
                    'whitespace-nowrap rounded-lg px-3 py-1.5 transition-colors',
                    schedule === s.key ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
                  )}
                >
                  {s.label}
                </button>
              ))}
            </div>

            <div className="mt-4 flex flex-wrap items-end gap-4">
              {schedule === 'once' && (
                <DatePicker label="Kuni" value={sendDate} onChange={setSendDate} min={isoDay(0)} className="w-40" />
              )}
              {schedule === 'weekly' && (
                <div>
                  <span className="mb-1 block text-sm font-medium text-slate-600">Kunlari</span>
                  <div className="flex gap-1">
                    {WEEK_DAYS.map((d) => (
                      <button
                        key={d.day}
                        type="button"
                        onClick={() => setWeekDays((list) => toggle(list, d.day))}
                        className={cn(
                          'h-9 w-10 rounded-lg border text-sm font-medium transition-colors',
                          weekDays.includes(d.day)
                            ? 'border-brand-500 bg-brand-50 text-brand-700'
                            : 'border-slate-200 text-slate-500 hover:bg-slate-50',
                        )}
                      >
                        {d.short}
                      </button>
                    ))}
                  </div>
                </div>
              )}
              {schedule === 'monthly' && (
                <div>
                  <span className="mb-1 block text-sm font-medium text-slate-600">Oyning kuni</span>
                  <select value={day} onChange={(e) => setDay(Number(e.target.value))} className={cn(selectCls, 'w-24')}>
                    {Array.from({ length: 28 }, (_, i) => i + 1).map((d) => (
                      <option key={d} value={d}>
                        {d}
                      </option>
                    ))}
                  </select>
                </div>
              )}
              <div>
                <span className="mb-1 block text-sm font-medium text-slate-600">Vaqti</span>
                <Time24Input value={sendTime} onChange={setSendTime} />
              </div>
              <label className="flex items-center gap-2 pb-2 text-sm text-slate-600">
                <Switch checked={approval} ariaLabel="Tasdiq bilan yuborish" onChange={setApproval} />
                Tasdiq bilan
              </label>
            </div>
            <p className="mt-3 text-xs text-slate-400">
              {isNew
                ? "Shablon o'chiq holda saqlanadi — ro'yxatdagi kartasida yoqing. "
                : ''}
              "Tasdiq bilan" yoqilsa, xabarlar tayyorlanadi va "Tarix va statistika"da tasdiqlangandan keyin ketadi.
              Oluvchilar har safar yuborish vaqtida qayta hisoblanadi.
            </p>
          </Card>

          {/* Tanlab — ota-ona ro'yxati (e'londagi kabi) */}
          {audience === 'selected' && (
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
                  <p className="py-6 text-center text-sm text-slate-400">Ro'yxatdan o'tgan ota-ona yo'q</p>
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
        </div>
      </div>
    </div>
  )
}
