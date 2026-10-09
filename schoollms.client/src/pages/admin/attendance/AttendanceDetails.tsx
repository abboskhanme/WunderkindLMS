/**
 * Davomat analitikasining "kim?" qismi (mijoz, 2026-10-09: "qaysi o'quvchilar qaysi kuni qaysi
 * sababdan kelmagan haqidagi malumotlar bilish mumkin bo'lsin").
 *
 * Ikkita bo'lim, ikkalasi ham server bergan ro'yxatdan — sahifada hech narsa qayta hisoblanmaydi:
 *  - `StudentsTable` — har bir o'quvchi davr bo'yicha (o'quvchi-kun birligida, yuqoridagi kartalar
 *    bilan bir xil);
 *  - `EventsList` — oddiy "keldi" bo'lmagan har bir o'quvchi-kun: sana, holat, sabab, qaysi darslar.
 *
 * Filtr holati sahifada turadi (`AttendanceAnalyticsPage`): yuqoridagi kartalar, sabab yorliqlari va
 * o'quvchi qatori shu filtrni o'rnatib, shu bo'limga olib keladi.
 */
import { useState } from 'react'
import { Search, X } from 'lucide-react'
import type {
  AttendanceEvent,
  AttendanceEventStatus,
  AttendanceReasonRow,
  AttendanceStudentRow,
} from '@/api/services/attendanceAnalytics'
import { Card } from '@/components/ui/Card'
import { Pager } from '@/components/ui/Pager'
import { PAGE_SIZES } from '@/lib/pagination'
import { cn, formatDate } from '@/lib/utils'
import type { EventFilter, EventSelection } from './eventSelection'


const statusLabel: Record<AttendanceEventStatus, string> = {
  absent: 'Kelmadi',
  partial: 'Ayrim darslarga kelmadi',
  late: 'Kech keldi',
  unchecked: 'Belgilanmagan',
}

const statusTone: Record<AttendanceEventStatus, string> = {
  absent: 'bg-red-50 text-red-700',
  partial: 'bg-orange-50 text-orange-700',
  late: 'bg-sky-50 text-sky-700',
  unchecked: 'bg-amber-50 text-amber-700',
}

const pct = (v: number | null) => (v == null ? '—' : `${v.toFixed(1).replace('.0', '')}%`)

function matches(e: AttendanceEvent, filter: EventFilter): boolean {
  switch (filter) {
    case 'all':
      return true
    case 'excused':
      return e.status === 'absent' && !e.unexcused
    case 'unexcused':
      return e.status === 'absent' && e.unexcused
    default:
      return e.status === filter
  }
}

function usePaging(total: number) {
  const [pageSize, setPageSize] = useState<number>(PAGE_SIZES[0])
  const [page, setPage] = useState(1)
  const lastPage = Math.max(1, Math.ceil(total / pageSize))
  const current = Math.min(page, lastPage)
  return {
    pageSize,
    current,
    lastPage,
    slice: <T,>(rows: T[]) => rows.slice((current - 1) * pageSize, current * pageSize),
    setPage,
    setPageSize: (n: number) => {
      setPageSize(n)
      setPage(1)
    },
  }
}

/* ==========================================================================
   O'quvchilar bo'yicha
   ========================================================================== */

export function StudentsTable({
  rows,
  onPick,
}: {
  rows: AttendanceStudentRow[]
  /** O'quvchi qatori bosilganda — uning kunlari ro'yxatda ochiladi. */
  onPick: (studentId: string) => void
}) {
  const [query, setQuery] = useState('')
  const [order, setOrder] = useState<'name' | 'missed'>('name')
  const needle = query.trim().toLowerCase()
  const shown = rows
    .filter((r) => !needle || r.fullName.toLowerCase().includes(needle))
    .sort((a, b) =>
      order === 'missed'
        ? b.tally.absent - a.tally.absent || (b.tally.partial ?? 0) - (a.tally.partial ?? 0) || a.fullName.localeCompare(b.fullName)
        : 0,
    )
  const paging = usePaging(shown.length)

  return (
    <Card className="p-0">
      <div className="flex flex-wrap items-center justify-between gap-3 px-5 pt-5">
        <div>
          <h2 className="font-semibold text-slate-800">O'quvchilar bo'yicha</h2>
          <p className="text-xs text-slate-400">Qatorni bosing — o'quvchining kelmagan kunlari va sabablari ochiladi</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <SearchBox value={query} onChange={(v) => { setQuery(v); paging.setPage(1) }} />
          <div className="flex gap-1 rounded-lg bg-slate-100 p-1">
            {(
              [
                ['name', 'Sinf va ism'],
                ['missed', "Ko'p qoldirgan"],
              ] as const
            ).map(([key, label]) => (
              <button
                key={key}
                type="button"
                onClick={() => setOrder(key)}
                className={cn(
                  'rounded-md px-3 py-1 text-xs font-medium transition-colors',
                  order === key ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
                )}
              >
                {label}
              </button>
            ))}
          </div>
        </div>
      </div>
      {shown.length === 0 ? (
        <EmptyNote text={rows.length === 0 ? "Bu davrda o'tilgan dars yo'q." : "Bunday o'quvchi topilmadi."} />
      ) : (
        <div className="mt-3 overflow-x-auto">
          <table className="w-full min-w-[820px] text-left text-sm">
            <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
              <tr>
                <th className="px-4 py-3">O'quvchi</th>
                <th className="px-4 py-3">Sinf</th>
                <th className="px-4 py-3 text-center">Kun</th>
                <th className="px-4 py-3 text-center">Keldi</th>
                <th className="px-4 py-3 text-center">Sababli</th>
                <th className="px-4 py-3 text-center">Sababsiz</th>
                <th className="px-4 py-3 text-center">Ayrim darsga</th>
                <th className="px-4 py-3 text-center">Kech</th>
                <th className="px-4 py-3 text-center">Belgilanmagan</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 tabular-nums">
              {paging.slice(shown).map((r) => (
                <tr
                  key={r.studentId}
                  onClick={() => onPick(r.studentId)}
                  className="cursor-pointer hover:bg-slate-50/60"
                >
                  <td className="px-4 py-3 font-medium text-slate-800">{r.fullName}</td>
                  <td className="px-4 py-3 text-slate-500">{r.rowName}</td>
                  <td className="px-4 py-3 text-center text-slate-600">{r.tally.opportunities}</td>
                  <td className="px-4 py-3 text-center font-semibold text-emerald-600">{pct(r.tally.presentPct)}</td>
                  <td className="px-4 py-3 text-center text-slate-600">{r.tally.excused || '—'}</td>
                  <td className="px-4 py-3 text-center font-semibold text-red-600">{r.tally.unexcused || '—'}</td>
                  <td className="px-4 py-3 text-center text-orange-600">{r.tally.partial || '—'}</td>
                  <td className="px-4 py-3 text-center text-sky-700">{r.tally.late || '—'}</td>
                  <td className={cn('px-4 py-3 text-center', r.tally.unchecked ? 'text-amber-600' : 'text-slate-300')}>
                    {r.tally.unchecked || '—'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <Pager
            total={shown.length}
            page={paging.current}
            lastPage={paging.lastPage}
            pageSize={paging.pageSize}
            onPage={paging.setPage}
            onPageSize={paging.setPageSize}
          />
        </div>
      )}
    </Card>
  )
}

/* ==========================================================================
   Kim, qaysi kuni, nima sababdan
   ========================================================================== */

export function EventsList({
  events,
  reasons,
  truncated,
  selection,
  onSelect,
}: {
  events: AttendanceEvent[]
  reasons: AttendanceReasonRow[]
  truncated: boolean
  selection: EventSelection
  onSelect: (next: EventSelection) => void
}) {
  const [query, setQuery] = useState('')
  const reasonById = new Map(reasons.map((r) => [r.reasonId, r]))
  const needle = query.trim().toLowerCase()

  const scoped = events.filter(
    (e) =>
      (!selection.reasonId || e.reasonIds.includes(selection.reasonId)) &&
      (!selection.studentId || e.studentId === selection.studentId) &&
      (!needle || e.fullName.toLowerCase().includes(needle)),
  )
  const shown = scoped.filter((e) => matches(e, selection.filter))
  const paging = usePaging(shown.length)

  const chips: [EventFilter, string][] = [
    ['all', 'Hammasi'],
    ['absent', 'Kelmadi'],
    ['excused', 'Sababli'],
    ['unexcused', 'Sababsiz'],
    ['partial', 'Ayrim darslarga'],
    ['late', 'Kech keldi'],
    ['unchecked', 'Belgilanmagan'],
  ]
  const pickedStudent = selection.studentId ? events.find((e) => e.studentId === selection.studentId)?.fullName : null
  const pickedReason = selection.reasonId ? reasonById.get(selection.reasonId)?.name : null
  const set = (patch: Partial<EventSelection>) => {
    onSelect({ ...selection, ...patch })
    paging.setPage(1)
  }

  return (
    <Card className="p-0">
      <div className="space-y-3 px-5 pt-5">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="font-semibold text-slate-800">Kim, qaysi kuni, nima sababdan</h2>
            <p className="text-xs text-slate-400">Davr ichida kelmagan, ayrim darsga kelmagan, kech kelgan va belgilanmagan kunlar</p>
          </div>
          <SearchBox value={query} onChange={(v) => { setQuery(v); paging.setPage(1) }} />
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {chips.map(([key, label]) => (
            <button
              key={key}
              type="button"
              onClick={() => set({ filter: key })}
              className={cn(
                'rounded-full border px-3 py-1 text-xs font-medium transition-colors',
                selection.filter === key
                  ? 'border-brand-600 bg-brand-600 text-white'
                  : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50',
              )}
            >
              {label} <span className="opacity-70">{scoped.filter((e) => matches(e, key)).length}</span>
            </button>
          ))}
          {pickedReason && <ActiveChip label={`Sabab: ${pickedReason}`} onClear={() => set({ reasonId: null })} />}
          {pickedStudent && <ActiveChip label={pickedStudent} onClear={() => set({ studentId: null })} />}
        </div>
        {truncated && (
          <p className="text-xs text-amber-600">
            Ro'yxat juda uzun — eng yangi 5000 tasi ko'rsatilmoqda. Sinf yoki davrni toraytiring.
          </p>
        )}
      </div>
      {shown.length === 0 ? (
        <EmptyNote text="Tanlangan filtr bo'yicha yozuv yo'q." />
      ) : (
        <div className="mt-3 overflow-x-auto">
          <table className="w-full min-w-[820px] text-left text-sm">
            <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
              <tr>
                <th className="px-4 py-3">Sana</th>
                <th className="px-4 py-3">O'quvchi</th>
                <th className="px-4 py-3">Sinf</th>
                <th className="px-4 py-3">Holat</th>
                <th className="px-4 py-3">Sabab</th>
                <th className="px-4 py-3">Darslar</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {paging.slice(shown).map((e) => (
                <tr key={`${e.date}-${e.studentId}`} className="hover:bg-slate-50/60">
                  <td className="whitespace-nowrap px-4 py-3 tabular-nums text-slate-600">{formatDate(e.date)}</td>
                  <td className="px-4 py-3 font-medium text-slate-800">
                    <button
                      type="button"
                      onClick={() => set({ studentId: e.studentId })}
                      className="text-left hover:text-brand-600 hover:underline"
                      title="Shu o'quvchining kunlari"
                    >
                      {e.fullName}
                    </button>
                  </td>
                  <td className="px-4 py-3 text-slate-500">{e.rowName}</td>
                  <td className="px-4 py-3">
                    <span className={cn('whitespace-nowrap rounded-md px-2 py-0.5 text-xs font-medium', statusTone[e.status])}>
                      {statusLabel[e.status]}
                    </span>
                  </td>
                  <td className="px-4 py-3">
                    {e.reasonIds.length === 0 ? (
                      <span className="text-slate-300">—</span>
                    ) : (
                      <span className="flex flex-wrap gap-1">
                        {e.reasonIds.map((id) => {
                          const r = reasonById.get(id)
                          return (
                            <button
                              key={id}
                              type="button"
                              onClick={() => set({ reasonId: id })}
                              className={cn(
                                'rounded-md px-2 py-0.5 text-xs font-medium',
                                r?.isLate ? 'bg-sky-50 text-sky-700' : r?.unexcused ? 'bg-red-50 text-red-700' : 'bg-slate-100 text-slate-600',
                              )}
                            >
                              {r?.name ?? "O'chirilgan sabab"}
                            </button>
                          )
                        })}
                      </span>
                    )}
                  </td>
                  <td className="whitespace-nowrap px-4 py-3 tabular-nums text-slate-500">{lessonsText(e)}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <Pager
            total={shown.length}
            page={paging.current}
            lastPage={paging.lastPage}
            pageSize={paging.pageSize}
            onPage={paging.setPage}
            onPageSize={paging.setPageSize}
          />
        </div>
      )}
    </Card>
  )
}

/** "Kun bo'yi · 6 dars", "2, 3-darslar (6 dan)", "1-dars". */
function lessonsText(e: AttendanceEvent): string {
  if (e.status === 'unchecked') return `${e.heldLessons} dars belgilanmagan`
  if (e.status === 'absent') return `Kun bo'yi · ${e.heldLessons} dars`
  if (e.periods.length === 0) return '—'
  const list = `${e.periods.join(', ')}-dars${e.periods.length > 1 ? 'lar' : ''}`
  return e.status === 'partial' ? `${list} (${e.heldLessons} dan)` : list
}

function SearchBox({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  return (
    <div className="relative">
      <Search className="pointer-events-none absolute left-2.5 top-2 h-4 w-4 text-slate-400" />
      <input
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder="O'quvchi ismi..."
        className="w-56 rounded-lg border border-slate-200 bg-white py-1.5 pl-8 pr-3 text-sm outline-none focus:border-brand-400"
      />
    </div>
  )
}

function ActiveChip({ label, onClear }: { label: string; onClear: () => void }) {
  return (
    <span className="inline-flex items-center gap-1 rounded-full bg-brand-50 px-3 py-1 text-xs font-medium text-brand-700">
      {label}
      <button type="button" onClick={onClear} aria-label="Filtrni olib tashlash" className="rounded-full p-0.5 hover:bg-brand-100">
        <X className="h-3 w-3" />
      </button>
    </span>
  )
}

function EmptyNote({ text }: { text: string }) {
  return <p className="py-10 text-center text-sm text-slate-400">{text}</p>
}
