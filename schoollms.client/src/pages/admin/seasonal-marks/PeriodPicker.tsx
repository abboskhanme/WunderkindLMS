import type { PeriodKind } from '@/api/services/seasonalMarks'
import { MonthPicker } from '@/components/ui/DatePicker'
import { cn } from '@/lib/utils'
import {
  PERIOD_KINDS,
  QUARTERS,
  controlClass,
  periodKindLabels,
  yearOptions,
  type PeriodSelection,
} from './periods'

interface SegmentedProps<T extends string | number> {
  options: readonly T[]
  value: T
  label: (option: T) => string
  onChange: (option: T) => void
  disabled?: boolean
  ariaLabel: string
}

/** The pill switch the journal uses for its quarters. */
export function Segmented<T extends string | number>({
  options,
  value,
  label,
  onChange,
  disabled,
  ariaLabel,
}: SegmentedProps<T>) {
  return (
    <div role="radiogroup" aria-label={ariaLabel} className="flex w-fit gap-1 rounded-lg bg-slate-100 p-1">
      {options.map((option) => (
        <button
          key={option}
          type="button"
          role="radio"
          aria-checked={option === value}
          disabled={disabled}
          onClick={() => onChange(option)}
          className={cn(
            'whitespace-nowrap rounded-md px-3 py-1.5 text-sm font-medium transition-colors disabled:cursor-not-allowed',
            option === value
              ? 'bg-white text-brand-700 shadow-sm'
              : 'text-slate-500 hover:text-slate-700',
          )}
        >
          {label(option)}
        </button>
      ))}
    </div>
  )
}

interface PeriodPickerProps {
  value: PeriodSelection
  onChange: (next: PeriodSelection) => void
  disabled?: boolean
}

/**
 * Tur → davr, always complete (the entry grid, the pivot and the coverage
 * report all need one exact period). Monthly uses the shared `MonthPicker`,
 * which carries the year with it; quarterly and yearly pick the year from a
 * plain list.
 */
export function PeriodPicker({ value, onChange, disabled }: PeriodPickerProps) {
  const monthValue = `${value.year}-${String(value.month).padStart(2, '0')}`

  return (
    <div className="flex flex-wrap items-center gap-3">
      <Segmented<PeriodKind>
        options={PERIOD_KINDS}
        value={value.kind}
        label={(k) => periodKindLabels[k]}
        onChange={(kind) => onChange({ ...value, kind })}
        disabled={disabled}
        ariaLabel="Baholash turi"
      />

      {value.kind === 'monthly' ? (
        <MonthPicker
          value={monthValue}
          onChange={(v) => {
            const [y, m] = v.split('-').map(Number)
            if (y && m) onChange({ ...value, year: y, month: m })
          }}
          disabled={disabled}
          ariaLabel="Oy"
          title="Oy"
        />
      ) : (
        <select
          value={value.year}
          onChange={(e) => onChange({ ...value, year: Number(e.target.value) })}
          disabled={disabled}
          aria-label="Yil"
          className={controlClass}
        >
          {yearOptions(value.year).map((y) => (
            <option key={y} value={y}>
              {y}-yil
            </option>
          ))}
        </select>
      )}

      {value.kind === 'quarterly' && (
        <Segmented<number>
          options={QUARTERS}
          value={value.quarter}
          label={(q) => `${q}-chorak`}
          onChange={(quarter) => onChange({ ...value, quarter })}
          disabled={disabled}
          ariaLabel="Chorak"
        />
      )}
    </div>
  )
}
