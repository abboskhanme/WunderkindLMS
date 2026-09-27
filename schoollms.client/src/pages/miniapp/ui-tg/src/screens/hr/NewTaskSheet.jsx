/**
 * "+ O'zimga topshiriq" oynasi (eski `components/NewTaskSheet.tsx`, `POST /hr/me/tasks`).
 *
 * Muddat MAHALLIY mintaqada yig'iladi va siljish bilan yuboriladi
 * (`2026-09-02T18:30:00+05:00`). Soat bo'sh qolsa — kun oxiri 23:59.
 * O'tgan paytga topshiriq qo'shib bo'lmaydi (sana VA soat tekshiriladi); qurilma
 * soatiga faqat u server bilan bir kunda turganda ishoniladi.
 */
import { useState } from 'react'
import { hrApi, hrErrorMessage } from '../../lib/hrApi'
import { addDays, localIsoAt, todayIn } from '../../lib/hrFormat'
import { hapticNotify, hapticSelect } from '../../lib/telegram'
import { BottomSheet, Btn, FieldLabel, Hint, INPUT_CLASS, Notice } from './shared'
import { t } from './texts'

const MAX_TITLE = 200
const DUE_HOUR = 23
const DUE_MINUTE = 59
const DEFAULT_TIME = `${DUE_HOUR}:${DUE_MINUTE}`
const TIME_RE = /^(\d{1,2}):(\d{2})$/

function parseTime(value) {
  const match = TIME_RE.exec(String(value).trim())
  if (!match) return { hours: DUE_HOUR, minutes: DUE_MINUTE }
  const hours = Number(match[1])
  const minutes = Number(match[2])
  if (hours > 23 || minutes > 59) return { hours: DUE_HOUR, minutes: DUE_MINUTE }
  return { hours, minutes }
}

function isPastMoment(dueAt, today, timezone) {
  if (todayIn(timezone) !== today) return false
  const due = new Date(dueAt).getTime()
  return Number.isFinite(due) && due < Date.now()
}

function DueChip({ active, disabled, onClick, children }) {
  return (
    <button
      type="button"
      aria-pressed={active}
      disabled={disabled}
      onClick={onClick}
      className={
        'rounded-xl px-3.5 py-2 text-[14px] font-semibold transition disabled:opacity-50 ' +
        (active ? 'bg-brand text-brand-ink' : 'bg-slate-100 text-slate-600')
      }
    >
      {children}
    </button>
  )
}

export function NewTaskSheet({ today, timezone, onCreated, onClose }) {
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [due, setDue] = useState(today)
  const [time, setTime] = useState(DEFAULT_TIME)
  const [customOpen, setCustomOpen] = useState(false)
  const [error, setError] = useState(null)
  const [saving, setSaving] = useState(false)

  const tomorrow = addDays(today, 1)
  const trimmedTitle = title.trim()

  const submit = async () => {
    if (saving) return
    if (trimmedTitle.length === 0) {
      setError(t('new.title_required'))
      hapticNotify('warning')
      return
    }
    if (trimmedTitle.length > MAX_TITLE) {
      setError(t('new.title_too_long'))
      hapticNotify('warning')
      return
    }
    if (!due || due < today) {
      setError(t('new.due_past'))
      hapticNotify('warning')
      return
    }
    const { hours, minutes } = parseTime(time)
    const dueAt = localIsoAt(due, hours, minutes, timezone)
    if (due === today && isPastMoment(dueAt, today, timezone)) {
      setError(t('new.due_past_time'))
      hapticNotify('warning')
      return
    }

    setError(null)
    setSaving(true)
    try {
      const created = await hrApi.createTask({
        title: trimmedTitle,
        description: description.trim().length > 0 ? description.trim() : null,
        dueAt,
      })
      hapticNotify('success')
      onCreated(created)
    } catch (e) {
      setError(hrErrorMessage(e))
      hapticNotify('error')
      setSaving(false)
    }
  }

  const pickDue = (value, custom) => {
    hapticSelect()
    setDue(value)
    setCustomOpen(custom)
    setError(null)
  }

  return (
    <BottomSheet
      title={t('new.title')}
      busy={saving}
      onClose={onClose}
      footer={
        <Btn variant="primary" block loading={saving} disabled={trimmedTitle.length === 0} onClick={() => void submit()}>
          {saving ? t('common.sending') : t('new.submit')}
        </Btn>
      }
    >
      {error !== null && (
        <Notice tone="danger" onDismiss={() => setError(null)} className="mt-1">
          {error}
        </Notice>
      )}

      <FieldLabel htmlFor="hr-new-title">{t('new.title_label')}</FieldLabel>
      <input
        id="hr-new-title"
        maxLength={MAX_TITLE}
        enterKeyHint="next"
        placeholder={t('new.title_placeholder')}
        value={title}
        disabled={saving}
        onChange={(e) => {
          setTitle(e.target.value)
          setError(null)
        }}
        className={INPUT_CLASS}
      />

      <FieldLabel htmlFor="hr-new-description">{t('new.description_label')}</FieldLabel>
      <textarea
        id="hr-new-description"
        rows={3}
        maxLength={2000}
        placeholder={t('new.description_placeholder', { optional: t('common.optional') })}
        value={description}
        disabled={saving}
        onChange={(e) => setDescription(e.target.value)}
        className={INPUT_CLASS + ' resize-none leading-snug'}
      />

      <FieldLabel>{t('new.due_label')}</FieldLabel>
      <div className="flex flex-wrap gap-2">
        <DueChip active={!customOpen && due === today} disabled={saving} onClick={() => pickDue(today, false)}>
          {t('date.today')}
        </DueChip>
        <DueChip active={!customOpen && due === tomorrow} disabled={saving} onClick={() => pickDue(tomorrow, false)}>
          {t('date.tomorrow')}
        </DueChip>
        <DueChip active={customOpen} disabled={saving} onClick={() => pickDue(due, true)}>
          {t('new.due_pick')}
        </DueChip>
      </div>
      {customOpen && (
        <input
          type="date"
          value={due}
          min={today}
          disabled={saving}
          aria-label={t('new.due_label')}
          onChange={(e) => {
            setDue(e.target.value)
            setError(null)
          }}
          className={INPUT_CLASS + ' mt-2 min-h-[44px]'}
        />
      )}

      {/* Soat ALOHIDA qator: sana chiplari bilan bir qatorda ikkalasi ham torayardi. */}
      <div className="mt-3 flex items-center gap-3">
        <label htmlFor="hr-new-time" className="shrink-0 text-[13px] font-semibold text-slate-500">
          {t('new.due_time_label')}
        </label>
        <input
          id="hr-new-time"
          type="time"
          step={60}
          value={time}
          disabled={saving}
          onChange={(e) => {
            setTime(e.target.value)
            setError(null)
          }}
          className={INPUT_CLASS + ' min-h-[44px] flex-1'}
        />
      </div>
      <Hint>{t('new.due_time_hint')}</Hint>
    </BottomSheet>
  )
}
