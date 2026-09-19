import type { PeriodTime } from './periodTimes'

/* Dars raqami ustuni — izoh va manba uchun `periodTimes.ts` ga qarang.
   Alohida fayl: bu yerda faqat komponentlar turadi (fast refresh talabi). */

/** Jadval qatorining birinchi katagi: dars raqami va uning soati. */
export function PeriodCell({ period, time }: { period: number; time?: PeriodTime }) {
  return (
    <td className="px-2 py-1.5 align-top">
      <div className="mt-1.5 flex flex-col items-center gap-0.5">
        <span className="inline-flex h-6 w-6 items-center justify-center rounded-md bg-slate-100 text-xs font-semibold text-slate-500">
          {period}
        </span>
        {time && (
          <span className="whitespace-nowrap text-[10px] leading-none text-slate-400">
            {time.start}–{time.end}
          </span>
        )}
      </div>
    </td>
  )
}

/** Shu ustunning sarlavhasi — ikkala ekranda bir xil bo'lishi uchun. */
export function PeriodHead() {
  return <th className="w-[86px] px-2 py-2 text-center font-medium">Dars</th>
}
