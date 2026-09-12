import type { InputHTMLAttributes } from 'react'
import { cn } from '@/lib/utils'
import { formatSum, parseSum, sanitizeSumInput } from './format'

type NativeProps = Omit<
  InputHTMLAttributes<HTMLInputElement>,
  'value' | 'onChange' | 'type' | 'inputMode'
>

interface MoneyInputProps extends NativeProps {
  /** Xom matn (raqamlar, ixtiyoriy nuqta). Bo'sh satr = hech narsa kiritilmagan. */
  value: string
  onValueChange: (raw: string) => void
  label?: string
  /** Maydon ostidagi kulrang izoh yoki qizil xato matni. */
  hint?: string
  invalid?: boolean
  className?: string
}

/**
 * Summa kiritish maydoni (P1-16).
 *
 * XOM MATNNI saqlaydi, sonni emas. Sabab: `number` holatida "0" bilan
 * "bo'sh" farqlanmaydi, `<input type="number">` esa brauzerga qarab
 * `1e5`, `--5`, `1,5` kabi qiymatlarni ham o'tkazib yuboradi. Kassa
 * ekranida "kassir hech narsa kiritmadi" — alohida holat: smenani yopish
 * tugmasi aynan shunga qarab o'chiq turadi (SPEC §4.2).
 *
 * Ko'rsatishda raqamlar guruhlanadi (`750 000`), holatda esa toza raqamlar
 * qoladi — ya'ni kursor sakramasin deb formatlash faqat maydon fokusdan
 * chiqqanda emas, har doim ko'rinadi, lekin qiymat `parseSum` uchun toza.
 */
export function MoneyInput({
  value,
  onValueChange,
  label,
  hint,
  invalid,
  className,
  disabled,
  ...rest
}: MoneyInputProps) {
  const parsed = parseSum(value)
  const display = parsed !== null && !value.endsWith('.') ? formatSum(parsed) : value

  return (
    <label className="block">
      {label && <span className="mb-1 block text-sm font-medium text-slate-600">{label}</span>}
      <input
        {...rest}
        type="text"
        inputMode="decimal"
        autoComplete="off"
        disabled={disabled}
        value={display}
        onChange={(e) => onValueChange(sanitizeSumInput(e.target.value))}
        className={cn(
          'w-full rounded-lg border px-3 py-2 text-right text-sm tabular-nums text-slate-800 outline-none transition-colors',
          'disabled:cursor-not-allowed disabled:bg-slate-50 disabled:text-slate-400',
          invalid
            ? 'border-red-300 focus:border-red-400 focus:ring-2 focus:ring-red-100'
            : 'border-slate-200 focus:border-brand-400 focus:ring-2 focus:ring-brand-100',
          className,
        )}
      />
      {hint && (
        <span className={cn('mt-1 block text-xs', invalid ? 'text-red-600' : 'text-slate-400')}>
          {hint}
        </span>
      )}
    </label>
  )
}
