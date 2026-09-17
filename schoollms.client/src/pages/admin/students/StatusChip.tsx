import { cn } from '@/lib/utils'

/**
 * O'quvchi holati nishoni (§2.3, S-5).
 *
 * RANG INLINE STYLE BILAN — Tailwind sinf nomlari bilan emas. Rangni maktab
 * o'zi kiritadi (`#RRGGBB`), ya'ni uni oldindan sinfga aylantirib bo'lmaydi:
 * Tailwind faqat kodda YOZIB QO'YILGAN sinflarni bundle'ga qo'shadi va
 * `bg-[${color}]` ko'rinishidagi dinamik sinf jimgina yo'qoladi.
 *
 * Fon — rangning 18% shaffofligi, matn esa to'liq rang: shunda har qanday
 * foydalanuvchi tanlagan rangda ham yozuv o'qiladi.
 */

interface StatusChipProps {
  name: string
  color?: string | null
  className?: string
}

/** `#RRGGBB` ni `rgba(r, g, b, alpha)` ga o'giradi. Shakli buzuq bo'lsa null. */
function rgba(hex: string | null | undefined, alpha: number): string | null {
  if (!hex || !/^#[0-9a-fA-F]{6}$/.test(hex)) return null
  const r = parseInt(hex.slice(1, 3), 16)
  const g = parseInt(hex.slice(3, 5), 16)
  const b = parseInt(hex.slice(5, 7), 16)
  return `rgba(${r}, ${g}, ${b}, ${alpha})`
}

export function StatusChip({ name, color, className }: StatusChipProps) {
  const background = rgba(color, 0.18)

  return (
    <span
      className={cn(
        'inline-flex max-w-[12rem] items-center truncate rounded-md px-2 py-0.5 text-xs font-medium',
        background ? '' : 'bg-slate-100 text-slate-600',
        className,
      )}
      style={background ? { backgroundColor: background, color: color ?? undefined } : undefined}
      title={name}
    >
      {name}
    </span>
  )
}
