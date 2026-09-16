import { useEffect, useState } from 'react'
import { getClasses } from '@/api/services/classes'
import { cn } from '@/lib/utils'

/**
 * Turniket hisobotlari uchun umumiy mayda qismlar (#11, #12, #13).
 * Jonli "Keldi-ketdi" sahifasiga TEGILMAGAN — u o'z ko'rinishida qoladi.
 */

/** Bugungi sana "yyyy-MM-dd" (brauzer mintaqasida). */
export const today = () => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/** N kun oldingi sana "yyyy-MM-dd". */
export const daysAgo = (n: number) => {
  const d = new Date()
  d.setDate(d.getDate() - n)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/** "2026-09-07" → "07.09.2026" (sana satri Date'ga o'girilmaydi — mintaqa surilmasin). */
export const shortDate = (iso: string) =>
  iso.length >= 10 ? `${iso.slice(8, 10)}.${iso.slice(5, 7)}.${iso.slice(0, 4)}` : iso || '—'

/** "2026-09-07T08:25:00" → "07.09.2026 08:25" */
export const syncLabel = (iso: string) =>
  iso && iso.length >= 16 ? `${shortDate(iso.slice(0, 10))} ${iso.slice(11, 16)}` : '—'

/** Sinflar ro'yxati (faqat nomlar) — filtr uchun. */
export function useClassNames(): string[] {
  const [names, setNames] = useState<string[]>([])
  useEffect(() => {
    getClasses()
      .then((cls) =>
        setNames(
          cls
            .filter((c) => !c.isArchived)
            .map((c) => c.name)
            .sort((a, b) => a.localeCompare(b)),
        ),
      )
      .catch(() => setNames([]))
  }, [])
  return names
}

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
    <input
      type="date"
      value={value}
      title={title}
      onChange={(e) => onChange(e.target.value)}
      className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 outline-none focus:border-brand-400"
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
