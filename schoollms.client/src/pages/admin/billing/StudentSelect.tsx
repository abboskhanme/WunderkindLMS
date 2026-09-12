/**
 * O'quvchi tanlash: qidiruv maydoni + ro'yxat.
 *
 * Maktabda yuzlab o'quvchi bor, shuning uchun oddiy `<select>` yetmaydi —
 * ustiga filtr qo'yilgan. Erkin matn kiritish (datalist) ATAYLAB emas:
 * formaga id kerak, ism esa noyob emas.
 */
import { useMemo, useState } from 'react'
import { Search } from 'lucide-react'
import { Select } from '@/components/ui/Input'
import { filterStudents, studentLabel, type StudentOption } from './useStudents'

interface Props {
  options: StudentOption[]
  loading: boolean
  error: string | null
  value: string
  onChange: (studentId: string) => void
  label?: string
  required?: boolean
  disabled?: boolean
  /** Bo'sh variant matni (filtrlarda "Barcha o'quvchilar"). */
  emptyLabel?: string
}

export function StudentSelect({
  options,
  loading,
  error,
  value,
  onChange,
  label = "O'quvchi",
  required = false,
  disabled = false,
  emptyLabel = 'Tanlang...',
}: Props) {
  const [query, setQuery] = useState('')
  const filtered = useMemo(() => filterStudents(options, query), [options, query])

  // Tanlangan o'quvchi filtrdan tushib qolsa ham ro'yxatda qolsin — aks holda
  // qidiruvni yozgan zahoti tanlov jimgina yo'qoladi.
  const selected = options.find((o) => o.id === value)
  const visible =
    selected && !filtered.some((o) => o.id === selected.id) ? [selected, ...filtered] : filtered

  return (
    <div className="space-y-2">
      <div className="relative">
        <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
        <input
          type="search"
          value={query}
          disabled={disabled || loading}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="Ism yoki sinf bo'yicha qidirish"
          className="w-full rounded-lg border border-slate-200 py-2 pl-9 pr-3 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100 disabled:bg-slate-50"
        />
      </div>

      <Select
        label={label}
        required={required}
        value={value}
        disabled={disabled || loading}
        onChange={(e) => onChange(e.target.value)}
      >
        <option value="">
          {loading ? "O'quvchilar yuklanmoqda..." : emptyLabel}
        </option>
        {visible.map((o) => (
          <option key={o.id} value={o.id}>
            {studentLabel(o)}
          </option>
        ))}
      </Select>

      {error && <p className="text-xs text-red-600">{error}</p>}
      {!loading && !error && query.trim() !== '' && filtered.length === 0 && (
        <p className="text-xs text-slate-400">Qidiruvga mos o'quvchi topilmadi.</p>
      )}
    </div>
  )
}
