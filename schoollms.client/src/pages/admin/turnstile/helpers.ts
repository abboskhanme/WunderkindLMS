import { useEffect, useState } from 'react'
import { getClasses } from '@/api/services/classes'

/**
 * Turniket hisobotlarining sana yordamchilari va sinf ro'yxati hooki (#11, #12, #13).
 *
 * Komponentlardan AJRATILGAN fayl: `react-refresh/only-export-components` qoidasi
 * bitta faylda ham komponent, ham oddiy funksiya eksport qilinishiga ruxsat
 * bermaydi (fast refresh buziladi). `shared.tsx` — faqat komponentlar.
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
