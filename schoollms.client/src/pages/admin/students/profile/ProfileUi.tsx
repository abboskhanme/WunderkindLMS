import type { LucideIcon } from 'lucide-react'
import type { ReactNode } from 'react'
import { AlertTriangle } from 'lucide-react'
import { Card } from '@/components/ui/Card'

/**
 * O'quvchi kartochkasining umumiy bo'laklari — docs/modules/students-parity.md
 * §2.3 (S-10).
 *
 * Faqat KOMPONENT eksport qilinadi (yordamchi funksiya emas): bitta fayldan
 * ham komponent, ham yordamchi chiqarish lint qoidasi bilan taqiqlangan.
 */

/** Sarlavhali oq karta — kartochkadagi har bir bo'lim shu ko'rinishda. */
export function ProfileSection({
  title,
  icon: Icon,
  action,
  children,
}: {
  title: string
  icon: LucideIcon
  /** O'ng chetdagi tugma yoki filtr (ixtiyoriy). */
  action?: ReactNode
  children: ReactNode
}) {
  return (
    <Card>
      <div className="mb-4 flex flex-wrap items-center gap-2">
        <Icon className="h-5 w-5 text-brand-600" />
        <h2 className="font-semibold text-slate-800">{title}</h2>
        {action && <div className="ml-auto">{action}</div>}
      </div>
      {children}
    </Card>
  )
}

/** Bo'sh holat — "hali yo'q" yozuvi bir xil ko'rinsin. */
export function ProfileEmpty({ children }: { children: ReactNode }) {
  return <p className="py-8 text-center text-sm text-slate-400">{children}</p>
}

/**
 * Xato holati. Ruxsat yo'qligi (403) ENDSIZ AYLANUVCHI DOIRA emas, o'qiladigan
 * yozuv bo'lishi kerak — §4 dagi "money rule" shuni talab qiladi.
 */
export function ProfileError({ message }: { message: string }) {
  return (
    <div className="flex items-start gap-2 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
      <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
      <span>{message}</span>
    </div>
  )
}
