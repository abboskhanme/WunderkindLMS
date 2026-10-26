/**
 * Avtomatik xabar shabloni — yaratish va sozlash (mijoz, 2026-10-26: "kelajakda qo'shimcha avtomatik
 * habar yaratish kerak bo'ladigan bo'lsa kod kiritmasdan faqat shu yerni o'zida shablon yaratish").
 * Shablon faqat taqvim bo'yicha ketadi — bir marta, har kuni, haftaning tanlangan kunlari yoki har oy —
 * barcha o'quvchilarning yoki tanlangan sinflarning ota-onalariga. Yangi shablon O'CHIQ holda yaratiladi.
 * Matn, rasm, vaqt va "Tasdiq bilan" keyin kartaning o'zida tahrirlanadi.
 */
import { useRef, useState } from 'react'
import {
  createAutoMessageRule,
  saveAutoMessageRule,
  type AutoMessageClass,
  type AutoMessageRule,
  type AutoMessageSchedule,
} from '@/api/services/autoMessages'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select, Time24Input } from '@/components/ui/Input'
import { DatePicker } from '@/components/ui/DatePicker'
import { FormatToolbar } from '@/components/ui/FormatToolbar'
import { cn } from '@/lib/utils'
import { SCHEDULES, WEEK_DAYS, errorText, isoDay } from './autoMessageLabels'

const TEMPLATE_TOKENS = [
  { token: '{fish}', label: 'F.I.Sh.' },
  { token: '{ism}', label: 'Ism' },
  { token: '{sinf}', label: 'Sinf' },
  { token: '{ota_ona}', label: 'Ota-ona' },
  { token: '{sana}', label: 'Sana' },
  { token: '{vaqt}', label: 'Vaqt' },
]

export function AutoMessageTemplateModal({
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
  const [title, setTitle] = useState(rule?.title ?? '')
  const [schedule, setSchedule] = useState<AutoMessageSchedule>(rule?.schedule ?? 'once')
  const [sendDate, setSendDate] = useState(rule?.sendDate ?? isoDay(-1))
  const [weekDays, setWeekDays] = useState<number[]>(rule?.weekDays?.length ? rule.weekDays : [1])
  const [day, setDay] = useState(rule?.dayOfMonth ?? 1)
  const [sendTime, setSendTime] = useState(rule?.sendTime ?? '10:00')
  const [allPupils, setAllPupils] = useState(!rule?.classIds?.length)
  const [classIds, setClassIds] = useState<string[]>(rule?.classIds ?? [])
  const [text, setText] = useState(rule?.text ?? '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const textRef = useRef<HTMLTextAreaElement>(null)

  const toggle = <T,>(list: T[], value: T) => (list.includes(value) ? list.filter((x) => x !== value) : [...list, value])

  const submit = async () => {
    setError(null)
    if (!title.trim()) return setError('Shablon nomini yozing.')
    if (schedule === 'weekly' && weekDays.length === 0) return setError('Haftaning kamida bitta kunini tanlang.')
    if (!allPupils && classIds.length === 0) return setError('Kamida bitta sinf tanlang yoki "Barcha o\'quvchilar"ni belgilang.')
    if (isNew && !text.trim()) return setError('Xabar matnini yozing.')
    setBusy(true)
    try {
      const body = {
        enabled: rule?.enabled ?? false,
        sendTime: isNew ? sendTime : rule.sendTime,
        dayOfMonth: day,
        minAmount: 0,
        text: isNew ? text : rule.text,
        title: title.trim(),
        schedule,
        sendDate: schedule === 'once' ? sendDate : null,
        weekDays: schedule === 'weekly' ? weekDays : [],
        classIds: allPupils ? [] : classIds,
      }
      onSaved(isNew ? await createAutoMessageRule(body) : await saveAutoMessageRule(rule.key, body))
    } catch (e) {
      setError(errorText(e, "Saqlab bo'lmadi."))
    } finally {
      setBusy(false)
    }
  }

  const insertToken = (token: string) => {
    const start = textRef.current?.selectionStart ?? text.length
    const end = textRef.current?.selectionEnd ?? text.length
    setText(text.slice(0, start) + token + text.slice(end))
    requestAnimationFrame(() => {
      textRef.current?.focus()
      textRef.current?.setSelectionRange(start + token.length, start + token.length)
    })
  }

  return (
    <Modal
      open
      onClose={onClose}
      size="lg"
      title={isNew ? 'Yangi avtomatik xabar shabloni' : 'Shablonni sozlash'}
      footer={
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={() => void submit()} disabled={busy}>
            {busy ? 'Saqlanmoqda…' : isNew ? 'Yaratish' : 'Saqlash'}
          </Button>
        </div>
      }
    >
      <div className="space-y-5">
        <Input
          label="Nomi"
          required
          maxLength={100}
          value={title}
          onChange={(e) => setTitle(e.target.value)}
          placeholder="Masalan: Ota-onalar majlisi eslatmasi"
        />

        <div>
          <span className="mb-1.5 block text-sm font-medium text-slate-600">Qachon yuboriladi</span>
          <div className="inline-flex rounded-xl bg-slate-100 p-1 text-sm font-medium">
            {SCHEDULES.map((s) => (
              <button
                key={s.key}
                type="button"
                onClick={() => setSchedule(s.key)}
                className={cn(
                  'rounded-lg px-4 py-1.5 transition-colors',
                  schedule === s.key ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
                )}
              >
                {s.label}
              </button>
            ))}
          </div>

          <div className="mt-3 flex flex-wrap items-end gap-4">
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
              <Select label="Oyning kuni" value={String(day)} onChange={(e) => setDay(Number(e.target.value))} className="w-24">
                {Array.from({ length: 28 }, (_, i) => i + 1).map((d) => (
                  <option key={d} value={d}>
                    {d}
                  </option>
                ))}
              </Select>
            )}
            {isNew && (
              <div>
                <span className="mb-1 block text-sm font-medium text-slate-600">Vaqti</span>
                <Time24Input value={sendTime} onChange={setSendTime} />
              </div>
            )}
          </div>
        </div>

        <div>
          <span className="mb-1.5 block text-sm font-medium text-slate-600">Kimga</span>
          <div className="flex flex-wrap gap-4 text-sm text-slate-700">
            <label className="flex items-center gap-2">
              <input type="radio" checked={allPupils} onChange={() => setAllPupils(true)} />
              Barcha o'quvchilarning ota-onalariga
            </label>
            <label className="flex items-center gap-2">
              <input type="radio" checked={!allPupils} onChange={() => setAllPupils(false)} />
              Tanlangan sinflarga
            </label>
          </div>
          {!allPupils && (
            <div className="mt-2 flex max-h-48 flex-wrap gap-1.5 overflow-y-auto rounded-lg border border-slate-200 p-2">
              {classes.length === 0 && <p className="text-xs text-slate-400">Sinflar yo'q.</p>}
              {classes.map((c) => (
                <button
                  key={c.id}
                  type="button"
                  onClick={() => setClassIds((list) => toggle(list, c.id))}
                  className={cn(
                    'rounded-md border px-2.5 py-1 text-xs font-medium transition-colors',
                    classIds.includes(c.id)
                      ? 'border-brand-500 bg-brand-50 text-brand-700'
                      : 'border-slate-200 text-slate-600 hover:bg-slate-50',
                  )}
                >
                  {c.name}
                </button>
              ))}
            </div>
          )}
        </div>

        {isNew && (
          <div>
            <span className="mb-1.5 block text-sm font-medium text-slate-600">Xabar matni</span>
            <FormatToolbar textareaRef={textRef} value={text} onChange={setText} className="mb-1.5" />
            <textarea
              ref={textRef}
              value={text}
              onChange={(e) => setText(e.target.value)}
              className="h-32 w-full resize-y rounded-lg border border-slate-200 px-3 py-2 text-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
            />
            <div className="mt-1.5 flex flex-wrap gap-1.5">
              {TEMPLATE_TOKENS.map((t) => (
                <button
                  key={t.token}
                  type="button"
                  onClick={() => insertToken(t.token)}
                  className="rounded-md bg-slate-100 px-2 py-1 text-xs font-medium text-slate-600 transition-colors hover:bg-brand-50 hover:text-brand-700"
                >
                  {t.label} <span className="font-normal text-slate-400">{t.token}</span>
                </button>
              ))}
            </div>
            <p className="mt-2 text-xs text-slate-400">
              Shablon o'chiq holda yaratiladi. Rasm, "Tasdiq bilan" va namunani kartada sozlab, keyin yoqing.
            </p>
          </div>
        )}

        {error && <p className="text-sm text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}
