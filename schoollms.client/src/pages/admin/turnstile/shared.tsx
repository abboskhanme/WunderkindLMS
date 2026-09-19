import { cn } from '@/lib/utils'
import { DatePicker } from '@/components/ui/DatePicker'

/**
 * Turniket hisobotlarining umumiy KOMPONENTLARI (#11, #12, #13).
 * Sana yordamchilari va `useClassNames` — `./helpers`da (fast refresh qoidasi).
 * Jonli "Keldi-ketdi" sahifasiga TEGILMAGAN — u o'z ko'rinishida qoladi.
 */

interface ClassFilterProps {
  value: string
  onChange: (v: string) => void
  classes: string[]
}

/** "Barcha sinflar" + sinf tanlash. Bo'sh qiymat = barcha. */
export function ClassFilter({ value, onChange, classes }: ClassFilterProps) {
  return (
    <select
      value={value}
      onChange={(e) => onChange(e.target.value)}
      className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 outline-none focus:border-brand-400"
    >
      <option value="">Barcha sinflar</option>
      {classes.map((c) => (
        <option key={c} value={c}>
          {c}
        </option>
      ))}
    </select>
  )
}

interface DateInputProps {
  value: string
  onChange: (v: string) => void
  title?: string
}

export function DateInput({ value, onChange, title }: DateInputProps) {
  return (
    <DatePicker
      value={value}
      title={title}
      onChange={(value: string) => onChange(value)}
      className="w-40"
    />
  )
}

interface TileProps {
  label: string
  value: string | number
  hint?: string
  tone?: 'neutral' | 'good' | 'warn' | 'bad'
}

const tones: Record<NonNullable<TileProps['tone']>, string> = {
  neutral: 'text-slate-800',
  good: 'text-emerald-600',
  warn: 'text-amber-600',
  bad: 'text-rose-600',
}

/** Bitta raqamli katakcha — hisobot sarlavhasidagi qator uchun. */
export function Tile({ label, value, hint, tone = 'neutral' }: TileProps) {
  return (
    <div className="rounded-xl border border-slate-100 bg-slate-50/60 px-4 py-3">
      <div className={cn('text-2xl font-bold', tones[tone])}>{value}</div>
      <div className="text-xs text-slate-400">{label}</div>
      {hint && <div className="mt-0.5 text-[11px] text-slate-300">{hint}</div>}
    </div>
  )
}

/** Sarlavha + izoh — uch sahifada ham bir xil. */
export function PageHead({ title, hint }: { title: string; hint: string }) {
  return (
    <div>
      <h1 className="text-xl font-semibold text-slate-800">{title}</h1>
      <p className="text-sm text-slate-400">{hint}</p>
    </div>
  )
}
