/**
 * Dars qoldirayotgan o'quvchilar — bosh sahifadan Davomat analitikasiga ko'chdi (mijoz, 2026-10-11:
 * "davomat analitikasiga o'tkazilsin ... sabablarini yuklab olish va batafsil malumot qismi").
 *
 * Faqat SABABSIZ qoldirilgan kunlar sanaladi (bosh sahifadagi bilan bir xil qoida — serverda
 * `AttendanceAnalytics.IsUnexcusedReason`). "Batafsil" — shu o'quvchining har bir kuni va sababi
 * pastdagi "Kim, qaysi kuni" ro'yxatida.
 */
import { useMemo, useState } from 'react'
import { ChevronRight } from 'lucide-react'
import type { AttendanceStudentRow } from '@/api/services/attendanceAnalytics'
import { Card } from '@/components/ui/Card'
import { Pager } from '@/components/ui/Pager'
import { PAGE_SIZES } from '@/lib/pagination'
import { cn, formatDate } from '@/lib/utils'

const MISS_FILTERS = [
  { key: 'all', label: 'Barchasi', min: 1 },
  { key: '3', label: '3+ sababsiz', min: 3 },
  { key: '5', label: '5+ sababsiz', min: 5 },
] as const

export function AbsenteesCard({
  rows,
  days,
  onPick,
}: {
  rows: AttendanceStudentRow[]
  /** Davrdagi kunlar soni — davrga sig'maydigan chegara ("5+" 3 kunlik davrda) ko'rsatilmaydi. */
  days: number
  onPick: (studentId: string) => void
}) {
  const [filter, setFilter] = useState<(typeof MISS_FILTERS)[number]['key']>('all')
  const [pageSize, setPageSize] = useState<number>(PAGE_SIZES[0])
  const [page, setPage] = useState(1)

  const filters = MISS_FILTERS.filter((f) => f.min <= Math.max(days, 1))
  const min = (filters.find((f) => f.key === filter) ?? MISS_FILTERS[0]).min
  const missing = useMemo(
    () =>
      rows
        .filter((r) => r.tally.unexcused > 0)
        .sort(
          (a, b) =>
            b.tally.unexcused - a.tally.unexcused ||
            b.tally.absent - a.tally.absent ||
            a.fullName.localeCompare(b.fullName),
        ),
    [rows],
  )
  const shown = missing.filter((r) => r.tally.unexcused >= min)
  const lastPage = Math.max(1, Math.ceil(shown.length / pageSize))
  const current = Math.min(page, lastPage)
  const pageRows = shown.slice((current - 1) * pageSize, current * pageSize)

  return (
    <Card className="p-0">
      <div className="flex flex-wrap items-center justify-between gap-3 px-5 pt-5">
        <div>
          <h2 className="font-semibold text-slate-800">Dars qoldirayotgan o'quvchilar</h2>
          <p className="text-xs text-slate-400">
            Faqat sababsiz qoldirilgan kunlar. Qatorni bosing — har bir kun va sababi.
          </p>
        </div>
        {filters.length > 1 && (
          <div className="flex gap-1 rounded-xl bg-slate-100 p-1">
            {filters.map((f) => (
              <button
                key={f.key}
                type="button"
                onClick={() => {
                  setFilter(f.key)
                  setPage(1)
                }}
                className={cn(
                  'rounded-lg px-3 py-1.5 text-xs font-medium transition-colors',
                  min === f.min ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500',
                )}
              >
                {f.label}
                <span className="ml-1 text-slate-400">{missing.filter((r) => r.tally.unexcused >= f.min).length}</span>
              </button>
            ))}
          </div>
        )}
      </div>

      {shown.length === 0 ? (
        <p className="px-5 py-8 text-center text-sm text-slate-400">
          {missing.length === 0 ? 'Bu davrda sababsiz qoldirish qayd etilmagan.' : "Bu chegaradan oshgan o'quvchi yo'q."}
        </p>
      ) : (
        <>
          <div className="mt-3 overflow-x-auto">
            <table className="w-full min-w-[640px] table-fixed text-sm">
              <colgroup>
                <col className="w-[34%]" />
                <col className="w-[16%]" />
                <col className="w-[14%]" />
                <col className="w-[14%]" />
                <col className="w-[16%]" />
                <col className="w-[6%]" />
              </colgroup>
              <thead className="whitespace-nowrap bg-slate-50 text-left text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-5 py-2.5 font-medium">O'quvchi</th>
                  <th className="px-3 py-2.5 font-medium">Sinf</th>
                  <th className="px-3 py-2.5 font-medium">Sababsiz</th>
                  <th className="px-3 py-2.5 font-medium">Jami kelmagan</th>
                  <th className="px-3 py-2.5 font-medium">Oxirgi kelgan</th>
                  <th />
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 tabular-nums">
                {pageRows.map((r) => (
                  <tr
                    key={r.studentId}
                    onClick={() => onPick(r.studentId)}
                    className="group cursor-pointer hover:bg-brand-50/40"
                    title="Har bir kun va sababi"
                  >
                    <td className="px-5 py-2.5 text-slate-800">
                      <span className="block truncate font-medium" title={r.fullName}>
                        {r.fullName}
                      </span>
                    </td>
                    <td className="truncate px-3 py-2.5 text-slate-500">{r.rowName || '—'}</td>
                    <td className="px-3 py-2.5 font-semibold text-red-600">{r.tally.unexcused} kun</td>
                    <td className="px-3 py-2.5 text-slate-600">{r.tally.absent} kun</td>
                    <td className="px-3 py-2.5 text-slate-500">{r.lastPresent ? formatDate(r.lastPresent) : '—'}</td>
                    <td className="pr-4 text-slate-300 group-hover:text-brand-500">
                      <ChevronRight className="h-4 w-4" />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pager
            total={shown.length}
            page={current}
            lastPage={lastPage}
            pageSize={pageSize}
            onPage={setPage}
            onPageSize={(n) => {
              setPageSize(n)
              setPage(1)
            }}
          />
        </>
      )}
    </Card>
  )
}
