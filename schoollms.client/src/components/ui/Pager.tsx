import { ChevronLeft, ChevronRight } from 'lucide-react'
import { PAGE_SIZES } from '@/lib/pagination'

/**
 * Ro'yxat sahifalagichi — "1–20 / 57 ta", sahifa hajmi (20/50/100), ‹ › tugmalari.
 * Jurnal sinflari va bosh sahifadagi "Dars qoldirayotganlar" bir xil ko'rinishda
 * (code review, 2026-09-23: ikki nusxa bir-biridan ajralib ketmasin).
 */

export function Pager({
  total,
  page,
  lastPage,
  pageSize,
  onPage,
  onPageSize,
}: {
  total: number
  page: number
  lastPage: number
  pageSize: number
  onPage: (p: number) => void
  onPageSize: (n: number) => void
}) {
  if (total === 0) return null
  const btn =
    'rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 disabled:cursor-not-allowed disabled:opacity-40'
  return (
    <div className="flex flex-wrap items-center justify-between gap-2 border-t border-slate-100 px-4 py-3">
      <div className="flex items-center gap-2 text-xs text-slate-400">
        <span>
          {(page - 1) * pageSize + 1}–{Math.min(page * pageSize, total)} / {total} ta
        </span>
        <select
          value={pageSize}
          onChange={(e) => onPageSize(Number(e.target.value))}
          className="rounded-lg border border-slate-200 bg-white px-2 py-1 text-xs text-slate-600"
          aria-label="Sahifadagi qatorlar soni"
        >
          {PAGE_SIZES.map((n) => (
            <option key={n} value={n}>
              {n} tadan
            </option>
          ))}
        </select>
      </div>
      <div className="flex items-center gap-1">
        <button type="button" disabled={page <= 1} onClick={() => onPage(page - 1)} aria-label="Oldingi sahifa" className={btn}>
          <ChevronLeft className="h-4 w-4" />
        </button>
        <span className="min-w-[70px] text-center text-xs text-slate-500">
          {page} / {lastPage}
        </span>
        <button type="button" disabled={page >= lastPage} onClick={() => onPage(page + 1)} aria-label="Keyingi sahifa" className={btn}>
          <ChevronRight className="h-4 w-4" />
        </button>
      </div>
    </div>
  )
}
