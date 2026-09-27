import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { ChevronLeft, ChevronRight, Inbox } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { DatePicker } from '@/components/ui/DatePicker'
import { Time24Input } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { cn } from '@/lib/utils'
import { HR_WEEKDAYS } from '@/types/hrTasks'
import {
  HR_PERIOD_BUTTONS,
  HR_PERIOD_LABEL,
  hrFormatPercent,
  hrWeekdayLabel,
  hrWeekdayShort,
  type HrPeriodPreset,
  type HrTone,
} from './lib'

/**
 * Shared building blocks of the HR task pages — in OUR admin panel style
 * (`components/ui/*`, the `lesson-analysis/ui.tsx` pattern), not the old app's theme.
 */

export const inputCls =
  'w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100 disabled:bg-slate-50 disabled:text-slate-400'

export const primaryLinkCls =
  'inline-flex items-center justify-center gap-2 rounded-lg px-4 py-2 text-sm font-medium transition-colors bg-brand-600 text-white hover:bg-brand-700'
export const secondaryLinkCls =
  'inline-flex items-center justify-center gap-2 rounded-lg px-4 py-2 text-sm font-medium transition-colors border border-slate-200 bg-white text-slate-700 hover:bg-slate-50'

/* ------------------------------------------------------------------ header */

export function PageHeader({
  title,
  description,
  actions,
}: {
  title: string
  description?: ReactNode
  actions?: ReactNode
}) {
  return (
    <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
      <div className="min-w-0">
        <h1 className="text-xl font-semibold text-slate-800">{title}</h1>
        {description && <p className="text-sm text-slate-400">{description}</p>}
      </div>
      {actions && <div className="flex flex-col gap-2 sm:flex-row sm:flex-wrap sm:justify-end [&>*]:w-full sm:[&>*]:w-auto">{actions}</div>}
    </div>
  )
}

export function SectionTitle({ children, hint }: { children: ReactNode; hint?: ReactNode }) {
  return (
    <div className="mb-3">
      <h2 className="font-semibold text-slate-800">{children}</h2>
      {hint && <p className="text-sm text-slate-400">{hint}</p>}
    </div>
  )
}

/* ------------------------------------------------------------------- badge */

const TONE_BADGE: Record<HrTone, string> = {
  info: 'bg-brand-50 text-brand-700',
  brand: 'bg-brand-50 text-brand-700',
  success: 'bg-emerald-50 text-emerald-700',
  warning: 'bg-amber-50 text-amber-700',
  danger: 'bg-red-50 text-red-600',
  neutral: 'bg-slate-100 text-slate-600',
}

const TONE_DOT: Record<HrTone, string> = {
  info: 'bg-brand-500',
  brand: 'bg-brand-500',
  success: 'bg-emerald-500',
  warning: 'bg-amber-500',
  danger: 'bg-red-500',
  neutral: 'bg-slate-400',
}

export function Badge({
  tone = 'neutral',
  dot,
  children,
  className,
}: {
  tone?: HrTone
  dot?: boolean
  children: ReactNode
  className?: string
}) {
  return (
    <span
      className={cn(
        'inline-flex max-w-full items-center gap-1.5 whitespace-nowrap rounded-md px-2 py-0.5 text-xs font-medium',
        TONE_BADGE[tone],
        className,
      )}
    >
      {dot && <span aria-hidden className={cn('h-1.5 w-1.5 shrink-0 rounded-full', TONE_DOT[tone])} />}
      <span className="truncate">{children}</span>
    </span>
  )
}

/* ---------------------------------------------------------------- progress */

/** 80%+ green, 50%+ amber, otherwise red (the old `ProgressBar`). */
export function ProgressBar({
  percent,
  showValue,
  caption,
  className,
}: {
  percent: number
  showValue?: boolean
  caption?: string
  className?: string
}) {
  const safe = Number.isFinite(percent) ? Math.min(Math.max(percent, 0), 100) : 0
  const bar = safe >= 80 ? 'bg-emerald-500' : safe >= 50 ? 'bg-amber-500' : 'bg-red-500'
  return (
    <div className={cn('min-w-0', className)}>
      <div
        role="progressbar"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={Math.round(safe)}
        className="h-1.5 w-full overflow-hidden rounded-full bg-slate-100"
      >
        <div className={cn('h-full rounded-full transition-[width]', bar)} style={{ width: `${safe}%` }} />
      </div>
      {(caption || showValue) && (
        <div className="mt-1 flex items-center justify-between gap-2 text-xs tabular-nums text-slate-500">
          <span className="truncate">{caption}</span>
          {showValue && <span className="shrink-0">{hrFormatPercent(safe)}</span>}
        </div>
      )}
    </div>
  )
}

/* ------------------------------------------------------------------- stats */

const TONE_VALUE: Record<HrTone, string> = {
  neutral: 'text-slate-800',
  info: 'text-brand-700',
  brand: 'text-brand-700',
  success: 'text-emerald-600',
  warning: 'text-amber-600',
  danger: 'text-red-600',
}

export function StatTile({
  label,
  value,
  hint,
  icon,
  tone = 'neutral',
  loading,
  errored,
  to,
}: {
  label: string
  value: ReactNode
  hint?: ReactNode
  icon?: ReactNode
  tone?: HrTone
  loading?: boolean
  errored?: boolean
  to?: string
}) {
  const body = (
    <Card className={cn('h-full min-w-0 p-4', to && 'transition group-hover:border-brand-300 group-hover:shadow-md')}>
      <div className="flex items-start justify-between gap-3">
        <p className="text-xs font-medium uppercase tracking-wide text-slate-400">{label}</p>
        {icon && <span className="text-slate-300">{icon}</span>}
      </div>
      {loading ? (
        <div className="mt-2 h-7 w-16 animate-pulse rounded-md bg-slate-100" />
      ) : (
        <div className={cn('mt-2 text-2xl font-semibold leading-none tabular-nums', errored ? 'text-slate-300' : TONE_VALUE[tone])}>
          {errored ? '—' : value}
        </div>
      )}
      <p className="mt-1.5 min-h-[1rem] truncate text-xs text-slate-400">
        {loading ? null : errored ? 'Ma’lumot olinmadi' : hint}
      </p>
    </Card>
  )
  if (!to) return body
  return (
    <Link to={to} className="group block rounded-2xl focus:outline-none focus-visible:ring-2 focus-visible:ring-brand-400">
      {body}
    </Link>
  )
}

/* --------------------------------------------------------- empty / notice */

export function EmptyState({ title, hint, action, icon }: { title: string; hint?: ReactNode; action?: ReactNode; icon?: ReactNode }) {
  return (
    <div className="flex flex-col items-center px-6 py-12 text-center">
      <span className="mb-3 grid h-12 w-12 place-items-center rounded-full bg-slate-100 text-slate-400">
        {icon ?? <Inbox className="h-5 w-5" />}
      </span>
      <p className="font-semibold text-slate-700">{title}</p>
      {hint && <p className="mt-1 max-w-md text-sm text-slate-400">{hint}</p>}
      {action && <div className="mt-4">{action}</div>}
    </div>
  )
}

export function Notice({ kind = 'error', children }: { kind?: 'error' | 'info' | 'success' | 'warning'; children: ReactNode }) {
  const tone = {
    error: 'border-red-200 bg-red-50 text-red-700',
    info: 'border-brand-100 bg-brand-50 text-brand-800',
    success: 'border-emerald-200 bg-emerald-50 text-emerald-800',
    warning: 'border-amber-200 bg-amber-50 text-amber-800',
  }[kind]
  return <div className={cn('rounded-xl border px-4 py-3 text-sm leading-snug', tone)}>{children}</div>
}

export function Skeleton({ className }: { className?: string }) {
  return <div className={cn('animate-pulse rounded-lg bg-slate-100', className)} />
}

/* ------------------------------------------------------------------ fields */

export function Field({
  label,
  required,
  hint,
  error,
  children,
  className,
}: {
  label?: string
  required?: boolean
  hint?: ReactNode
  error?: string | null
  children: ReactNode
  className?: string
}) {
  return (
    <div className={cn('block min-w-0', className)}>
      {label && (
        <span className="mb-1 block text-sm font-medium text-slate-600">
          {label}
          {required && <span className="text-red-500"> *</span>}
        </span>
      )}
      {children}
      {error ? (
        <p className="mt-1 text-xs text-red-600" role="alert">
          {error}
        </p>
      ) : hint ? (
        <p className="mt-1 text-xs text-slate-400">{hint}</p>
      ) : null}
    </div>
  )
}

/** On/off switch (the `LaCriteriaPage` style). */
export function Switch({
  checked,
  onChange,
  label,
  hint,
  disabled,
  ariaLabel,
}: {
  checked: boolean
  onChange: (next: boolean) => void
  label?: ReactNode
  hint?: ReactNode
  disabled?: boolean
  ariaLabel?: string
}) {
  const knob = (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={ariaLabel}
      disabled={disabled}
      onClick={(e) => {
        e.stopPropagation()
        onChange(!checked)
      }}
      className={cn(
        'relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors disabled:opacity-50',
        checked ? 'bg-emerald-500' : 'bg-slate-300',
      )}
    >
      <span
        className={cn(
          'inline-block h-5 w-5 rounded-full bg-white shadow transition-transform',
          checked ? 'translate-x-5' : 'translate-x-0.5',
        )}
      />
    </button>
  )
  if (!label) return knob
  return (
    <div className="flex items-start gap-3">
      <div className="pt-0.5">{knob}</div>
      <div className="min-w-0">
        <p className="text-sm font-medium text-slate-700">{label}</p>
        {hint && <p className="text-xs text-slate-400">{hint}</p>}
      </div>
    </div>
  )
}

/** Date + time (Tashkent) in one field. */
export function DateTimeField({
  label,
  required,
  date,
  time,
  onDate,
  onTime,
  error,
  hint,
  disabled,
}: {
  label: string
  required?: boolean
  date: string
  time: string
  onDate: (value: string) => void
  onTime: (value: string) => void
  error?: string | null
  hint?: ReactNode
  disabled?: boolean
}) {
  return (
    <Field label={label} required={required} error={error} hint={hint}>
      <div className="flex flex-wrap items-center gap-2">
        <div className="min-w-[10rem] flex-1">
          <DatePicker value={date} onChange={onDate} disabled={disabled} invalid={Boolean(error)} ariaLabel={`${label}: sana`} />
        </div>
        <Time24Input value={time} onChange={onTime} disabled={disabled} />
      </div>
    </Field>
  )
}

/** Both dates in one cell ("from — to"). `w-full` + wrap — DatePicker's default `w-40` twice overflowed a quarter-width filter cell; when both do not fit at 8.75rem each, the second date moves to its own line instead of being cut. */
export function DateRangeField({
  label,
  dateFrom,
  dateTo,
  onFrom,
  onTo,
  className,
}: {
  label: string
  dateFrom: string
  dateTo: string
  onFrom: (value: string) => void
  onTo: (value: string) => void
  className?: string
}) {
  return (
    <Field label={label} className={className}>
      <div className="flex flex-wrap items-center gap-2">
        <div className="min-w-[8.75rem] flex-1">
          <DatePicker className="w-full" value={dateFrom} onChange={onFrom} max={dateTo || undefined} ariaLabel={`${label}: dan`} />
        </div>
        <span className="hidden shrink-0 text-xs text-slate-400 2xl:inline" aria-hidden>
          —
        </span>
        <div className="min-w-[8.75rem] flex-1">
          <DatePicker className="w-full" value={dateTo} onChange={onTo} min={dateFrom || undefined} ariaLabel={`${label}: gacha`} />
        </div>
      </div>
    </Field>
  )
}

/* --------------------------------------------------------- quick buttons */

const chipBase = 'shrink-0 rounded-full border text-sm font-medium transition-colors'
const chipOn = 'border-brand-600 bg-brand-600 text-white'
const chipOff = 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50'

/** "Bugun · 3 kun · 1 hafta · 15 kun · 1 oy" — one row, scrolls sideways on phones. */
export function PeriodTabs({
  value,
  active,
  onChange,
}: {
  value: HrPeriodPreset
  active: boolean
  onChange: (preset: HrPeriodPreset) => void
}) {
  return (
    <div role="group" aria-label="Davr" className="-mx-1 flex gap-2 overflow-x-auto px-1 pb-1 [scrollbar-width:none]">
      {HR_PERIOD_BUTTONS.map((preset) => {
        const selected = active && preset === value
        return (
          <button
            key={preset}
            type="button"
            aria-pressed={selected}
            onClick={() => onChange(preset)}
            className={cn(chipBase, 'h-9 px-4', selected ? chipOn : chipOff)}
          >
            {HR_PERIOD_LABEL[preset]}
          </button>
        )
      })}
    </div>
  )
}

/** Multi-select chips (status filter, reminder offsets). */
export function ToggleChips<T extends string | number>({
  options,
  value,
  onChange,
  ariaLabel,
}: {
  options: { value: T; label: string; ariaLabel?: string }[]
  value: readonly T[]
  onChange: (next: T[]) => void
  ariaLabel?: string
}) {
  return (
    <div role="group" aria-label={ariaLabel} className="flex flex-wrap gap-2">
      {options.map((option) => {
        const selected = value.includes(option.value)
        return (
          <button
            key={String(option.value)}
            type="button"
            aria-pressed={selected}
            aria-label={option.ariaLabel}
            onClick={() =>
              onChange(selected ? value.filter((v) => v !== option.value) : [...value, option.value])
            }
            className={cn(chipBase, 'h-8 px-3 text-xs', selected ? 'border-brand-400 bg-brand-50 text-brand-700' : chipOff)}
          >
            {option.label}
          </button>
        )
      })}
    </div>
  )
}

/** Weekdays — 7 buttons (1 = Monday). */
export function WeekdayToggles({
  label,
  value,
  onChange,
  error,
  hint,
  disabled,
}: {
  label: string
  value: number[]
  onChange: (next: number[]) => void
  error?: string | null
  hint?: ReactNode
  disabled?: boolean
}) {
  function toggle(day: number): void {
    if (disabled) return
    onChange(value.includes(day) ? value.filter((d) => d !== day) : [...value, day].sort((a, b) => a - b))
  }
  return (
    <Field label={label} error={error} hint={hint}>
      <div className="flex flex-wrap gap-2">
        {HR_WEEKDAYS.map((day) => {
          const selected = value.includes(day)
          return (
            <button
              key={day}
              type="button"
              disabled={disabled}
              aria-pressed={selected}
              aria-label={hrWeekdayLabel(day)}
              onClick={() => toggle(day)}
              className={cn(
                'h-10 min-w-11 rounded-xl border px-3 text-sm font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-50',
                selected ? chipOn : chipOff,
              )}
            >
              {hrWeekdayShort(day)}
            </button>
          )
        })}
      </div>
    </Field>
  )
}

/* ------------------------------------------------------------- pagination */

/** Keyset pagination: "Oldingi / Keyingi". */
export function CursorPager({
  summary,
  hasPrev,
  hasMore,
  loading,
  onPrev,
  onNext,
}: {
  summary: string
  hasPrev: boolean
  hasMore: boolean
  loading?: boolean
  onPrev: () => void
  onNext: () => void
}) {
  const btn =
    'inline-flex h-10 items-center gap-1 rounded-lg border border-slate-200 bg-white px-3 text-sm text-slate-600 transition-colors hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-40'
  return (
    <div className="flex flex-wrap items-center justify-between gap-2 px-1 py-2">
      <p className="text-xs text-slate-400">{summary}</p>
      <div className="flex items-center gap-2">
        <button type="button" className={btn} disabled={!hasPrev || loading} onClick={onPrev}>
          <ChevronLeft className="h-4 w-4" />
          Oldingi
        </button>
        <button type="button" className={btn} disabled={!hasMore || loading} onClick={onNext}>
          Keyingi
          <ChevronRight className="h-4 w-4" />
        </button>
      </div>
    </div>
  )
}

/* ----------------------------------------------------------- confirmation */

export function ConfirmModal({
  open,
  title,
  description,
  confirmLabel,
  cancelLabel = 'Bekor qilish',
  danger,
  busy,
  onConfirm,
  onClose,
}: {
  open: boolean
  title: string
  description: ReactNode
  confirmLabel: string
  cancelLabel?: string
  danger?: boolean
  busy?: boolean
  onConfirm: () => void
  onClose: () => void
}) {
  return (
    <Modal
      open={open}
      onClose={onClose}
      title={title}
      size="sm"
      footer={
        <div className="flex w-full flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            {cancelLabel}
          </Button>
          <Button variant={danger ? 'danger' : 'primary'} onClick={onConfirm} disabled={busy}>
            {busy ? 'Bajarilmoqda…' : confirmLabel}
          </Button>
        </div>
      }
    >
      <div className="text-sm text-slate-600">{description}</div>
    </Modal>
  )
}
