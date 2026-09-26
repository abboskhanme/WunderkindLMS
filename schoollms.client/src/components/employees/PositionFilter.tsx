import {
  TEACHER_FILTER,
  TEACHER_POSITION,
  positionFilterLabel,
  staffPositionFilter,
  type PositionOptions,
} from '@/lib/employees'
import { cn } from '@/lib/utils'

interface Props {
  value: string
  onChange: (value: string) => void
  positions: PositionOptions
  className?: string
}

/** "Lavozim" select: Hammasi / O'qituvchi / each staff position (lib/employees.ts). */
export function PositionFilter({ value, onChange, positions, className }: Props) {
  // A value from the URL may name a position nobody holds yet — keep it selectable.
  const known =
    !value ||
    (value === TEACHER_FILTER && positions.hasTeachers) ||
    positions.staffPositions.some((p) => staffPositionFilter(p) === value)

  return (
    <select
      aria-label="Lavozim"
      value={value}
      onChange={(e) => onChange(e.target.value)}
      className={cn(
        'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400',
        className,
      )}
    >
      <option value="">Barcha lavozimlar</option>
      {(positions.hasTeachers || value === TEACHER_FILTER) && (
        <option value={TEACHER_FILTER}>{TEACHER_POSITION}</option>
      )}
      {positions.staffPositions.map((p) => (
        <option key={p} value={staffPositionFilter(p)}>
          {p}
        </option>
      ))}
      {!known && value !== TEACHER_FILTER && <option value={value}>{positionFilterLabel(value)}</option>}
    </select>
  )
}
