/**
 * The three bank settings (§5.1) as one row of inputs — shared by the
 * "Baza qo'shish" modal and the settings strip on the bank screen, so both
 * accept exactly the same text and show the same hints.
 */
import { Input } from '@/components/ui/Input'
import { cn } from '@/lib/utils'
import { SETTINGS_LABELS, type SettingsDraft, type SettingsField } from './BankHelpers'

interface Props {
  draft: SettingsDraft
  onChange: (next: SettingsDraft) => void
  disabled?: boolean
  /** The field the last parse rejected — outlined red. */
  invalidField?: SettingsField | null
  className?: string
}

const HINTS: Record<SettingsField, string> = {
  questionsPerTest: 'Bitta testga nechta savol tushadi',
  timeLimitMin: '1–600 daqiqa',
  pointsPerCorrect: 'Masalan: 1 yoki 1,5',
}

const PLACEHOLDERS: Record<SettingsField, string> = {
  questionsPerTest: 'masalan: 20',
  timeLimitMin: 'masalan: 45',
  pointsPerCorrect: 'masalan: 1',
}

const FIELDS: readonly SettingsField[] = ['questionsPerTest', 'timeLimitMin', 'pointsPerCorrect']

export function BankSettingsFields({ draft, onChange, disabled, invalidField, className }: Props) {
  return (
    <div className={cn('grid gap-3 sm:grid-cols-3', className)}>
      {FIELDS.map((field) => (
        <div key={field}>
          <Input
            label={SETTINGS_LABELS[field]}
            inputMode={field === 'pointsPerCorrect' ? 'decimal' : 'numeric'}
            autoComplete="off"
            placeholder={PLACEHOLDERS[field]}
            value={draft[field]}
            disabled={disabled}
            onChange={(e) => onChange({ ...draft, [field]: e.target.value })}
            className={cn(
              'disabled:bg-slate-50 disabled:text-slate-500',
              invalidField === field && 'border-red-300 focus:border-red-400 focus:ring-red-100',
            )}
          />
          <p className="mt-1 text-xs text-slate-400">{HINTS[field]}</p>
        </div>
      ))}
    </div>
  )
}
