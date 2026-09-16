import { useEffect, useMemo, useState } from 'react'
import {
  ArrowDownCircle,
  ArrowUpCircle,
  ChevronLeft,
  ChevronRight,
  ClipboardList,
  RotateCcw,
  Scale,
  Search,
} from 'lucide-react'
import type { DisciplineFeed, DisciplineFeedRow, DisciplineReason } from '@/types'
import { getDisciplineFeed, getDisciplineReasons } from '@/api/services/discipline'
import { cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { StatCard } from '@/components/ui/StatCard'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

const PAGE_SIZE = 50

const EMPTY: DisciplineFeed = {
  items: [],
  total: 0,
  page: 1,
  pageSize: PAGE_SIZE,
  plusCount: 0,
  minusCount: 0,
  pointsSum: 0,
  authors: [],
  classNames: [],
}

/** ISO sanani "DD.MM.YYYY HH:mm" ko'rinishida; jurnal yozuvida vaqt yo'q — faqat sana. */
function formatWhen(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  const dd = String(d.getDate()).padStart(2, '0')
  const mm = String(d.getMonth() + 1).padStart(2, '0')
  const day = `${dd}.${mm}.${d.getFullYear()}`
  if (!iso.includes('T')) return day
  return `${day} ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
}

/** "YYYY-MM-DD" — MAHALLIY sana bo'yicha (toISOString UTC beradi va kechqurun bir kun orqaga surardi). */
function isoDay(offsetDays = 0): string {
  const d = new Date()
  d.setDate(d.getDate() + offsetDays)
  const mm = String(d.getMonth() + 1).padStart(2, '0')
  const dd = String(d.getDate()).padStart(2, '0')
  return `${d.getFullYear()}-${mm}-${dd}`
}

interface Filters {
  from: string
  to: string
  className: string
  reasonId: string
  author: string
  sign: string
  source: string
  search: string
}

const INITIAL: Filters = {
  from: isoDay(-7),
  to: isoDay(),
  className: 'all',
  reasonId: 'all',
  author: 'all',
  sign: 'all',
  source: 'all',
  search: '',
}

export function HarakatlarPage() {
  const [feed, setFeed] = useState<DisciplineFeed>(EMPTY)
  const [reasons, setReasons] = useState<DisciplineReason[]>([])
  const [filters, setFilters] = useState<Filters>(INITIAL)
  const [searchInput, setSearchInput] = useState('')
  const [page, setPage] = useState(1)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    getDisciplineReasons().then(setReasons)
  }, [])

  // Qidiruv serverda bajariladi — har bosilgan harfga so'rov ketmasligi uchun kechiktiramiz.
  useEffect(() => {
    if (searchInput === filters.search) return
    const timer = setTimeout(() => {
      setPage(1)
      setFilters((prev) => ({ ...prev, search: searchInput }))
    }, 350)
    return () => clearTimeout(timer)
  }, [searchInput, filters.search])

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    getDisciplineFeed({
      from: filters.from || undefined,
      to: filters.to || undefined,
      className: filters.className === 'all' ? undefined : filters.className,
      reasonId: filters.reasonId === 'all' ? undefined : filters.reasonId,
      author: filters.author === 'all' ? undefined : filters.author,
      sign: filters.sign === 'all' ? undefined : (filters.sign as 'positive' | 'negative'),
      source: filters.source === 'all' ? undefined : (filters.source as 'manual' | 'attendance'),
      search: filters.search.trim() || undefined,
      page,
      pageSize: PAGE_SIZE,
    })
      .then((data) => {
        if (!cancelled) setFeed(data)
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [filters, page])

  // Filtr o'zgarsa — birinchi sahifaga qaytamiz, aks holda 3-sahifada "bo'sh" ko'rinardi.
  const set = (patch: Partial<Filters>) => {
    setPage(1)
    setFilters((prev) => ({ ...prev, ...patch }))
  }

  const setRange = (days: number) => set({ from: isoDay(-days), to: isoDay() })

  const reset = () => {
    setPage(1)
    setSearchInput('')
    setFilters(INITIAL)
  }

  const from = feed.total === 0 ? 0 : (feed.page - 1) * feed.pageSize + 1
  const to = Math.min(feed.page * feed.pageSize, feed.total)
  const lastPage = Math.max(1, Math.ceil(feed.total / feed.pageSize))

  const reasonOptions = useMemo(
    () => [...reasons].sort((a, b) => a.name.localeCompare(b.name)),
    [reasons],
  )

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Harakatlar</h1>
        <p className="text-sm text-slate-400">
          Maktab bo'ylab intizomiy yozuvlar — qo'lda kiritilgan ballar va jurnal davomatidan
          kelgan ballar bir ro'yxatda.
        </p>
      </div>

      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        <StatCard label="Yozuvlar" value={feed.total} icon={ClipboardList} />
        <StatCard
          label="Rag'bat"
          value={feed.plusCount}
          icon={ArrowUpCircle}
          iconBg="bg-emerald-50"
          iconColor="text-emerald-600"
        />
        <StatCard
          label="Jazo"
          value={feed.minusCount}
          icon={ArrowDownCircle}
          iconBg="bg-red-50"
          iconColor="text-red-600"
        />
        <StatCard
          label="Ball o'zgarishi"
          value={feed.pointsSum > 0 ? `+${feed.pointsSum}` : feed.pointsSum}
          icon={Scale}
          iconBg={feed.pointsSum < 0 ? 'bg-red-50' : 'bg-emerald-50'}
          iconColor={feed.pointsSum < 0 ? 'text-red-600' : 'text-emerald-600'}
          hint="Tanlangan davr bo'yicha"
        />
      </div>

      <Card className="p-0">
        {/* Filtrlar */}
        <div className="space-y-3 border-b border-slate-100 p-4">
          <div className="flex flex-wrap items-center gap-2">
            <input
              type="date"
              value={filters.from}
              onChange={(e) => set({ from: e.target.value })}
              className={control}
            />
            <span className="text-sm text-slate-400">—</span>
            <input
              type="date"
              value={filters.to}
              onChange={(e) => set({ to: e.target.value })}
              className={control}
            />
            <div className="flex items-center gap-1">
              {[
                { label: 'Bugun', days: 0 },
                { label: '7 kun', days: 7 },
                { label: '30 kun', days: 30 },
              ].map((r) => (
                <button
                  key={r.label}
                  type="button"
                  onClick={() => setRange(r.days)}
                  className="rounded-lg border border-slate-200 px-2.5 py-1.5 text-xs font-medium text-slate-600 transition-colors hover:bg-slate-50"
                >
                  {r.label}
                </button>
              ))}
            </div>
            <button
              type="button"
              onClick={reset}
              title="Filtrlarni tozalash"
              className="ml-auto inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-xs font-medium text-slate-500 transition-colors hover:bg-slate-100"
            >
              <RotateCcw className="h-3.5 w-3.5" />
              Tozalash
            </button>
          </div>

          <div className="flex flex-wrap items-center gap-3">
            <div className="relative min-w-[200px] flex-1">
              <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
              <input
                value={searchInput}
                onChange={(e) => setSearchInput(e.target.value)}
                placeholder="F.I.SH bo'yicha qidirish..."
                className={cn(control, 'w-full pl-9')}
              />
            </div>
            <select
              value={filters.className}
              onChange={(e) => set({ className: e.target.value })}
              className={control}
            >
              <option value="all">Barcha sinflar</option>
              {feed.classNames.map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>
            <select
              value={filters.reasonId}
              onChange={(e) => set({ reasonId: e.target.value })}
              className={control}
            >
              <option value="all">Barcha sabablar</option>
              {reasonOptions.map((r) => (
                <option key={r.id} value={r.id}>
                  {r.name} ({r.points > 0 ? `+${r.points}` : r.points})
                </option>
              ))}
            </select>
            <select
              value={filters.author}
              onChange={(e) => set({ author: e.target.value })}
              className={control}
            >
              <option value="all">Barcha xodimlar</option>
              {feed.authors.map((a) => (
                <option key={a} value={a}>
                  {a}
                </option>
              ))}
            </select>
            <select
              value={filters.sign}
              onChange={(e) => set({ sign: e.target.value })}
              className={control}
            >
              <option value="all">Rag'bat va jazo</option>
              <option value="positive">Faqat rag'bat (+)</option>
              <option value="negative">Faqat jazo (−)</option>
            </select>
            <select
              value={filters.source}
              onChange={(e) => set({ source: e.target.value })}
              className={control}
            >
              <option value="all">Barcha manbalar</option>
              <option value="manual">Qo'lda kiritilgan</option>
              <option value="attendance">Jurnal davomati</option>
            </select>
          </div>
        </div>

        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">Sana</th>
                  <th className="px-4 py-3">F.I.SH</th>
                  <th className="px-4 py-3">Sinf</th>
                  <th className="px-4 py-3">Sabab</th>
                  <th className="px-4 py-3 text-center">Ball</th>
                  <th className="px-4 py-3">Izoh</th>
                  <th className="px-4 py-3">Kim</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {feed.items.map((row: DisciplineFeedRow) => (
                  <tr key={row.id} className="hover:bg-slate-50/60">
                    <td className="whitespace-nowrap px-4 py-3 text-slate-500">
                      {formatWhen(row.createdAt)}
                    </td>
                    <td className="px-4 py-3 font-medium text-slate-800">{row.fullName}</td>
                    <td className="px-4 py-3">
                      <span className="rounded-md bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
                        {row.className}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-slate-700">{row.reasonName}</td>
                    <td className="px-4 py-3 text-center">
                      <span
                        className={cn(
                          'inline-block min-w-[40px] rounded-md px-2 py-0.5 text-sm font-bold',
                          row.points < 0 ? 'bg-red-50 text-red-600' : 'bg-emerald-50 text-emerald-600',
                        )}
                      >
                        {row.points > 0 ? `+${row.points}` : row.points}
                      </span>
                    </td>
                    <td className="max-w-[240px] truncate px-4 py-3 text-slate-500">
                      {row.note || '—'}
                    </td>
                    <td className="px-4 py-3">
                      {row.source === 'attendance' ? (
                        <span className="rounded-md bg-slate-100 px-2 py-0.5 text-[10px] font-medium text-slate-500">
                          jurnal
                        </span>
                      ) : (
                        <span className="text-slate-500">{row.createdBy || '—'}</span>
                      )}
                    </td>
                  </tr>
                ))}
                {feed.items.length === 0 && (
                  <tr>
                    <td colSpan={7} className="px-4 py-12 text-center text-slate-400">
                      Tanlangan davrda yozuv yo'q
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )}

        {/* Sahifalash */}
        <div className="flex items-center justify-between border-t border-slate-100 px-4 py-3">
          <p className="text-xs text-slate-400">
            {feed.total === 0 ? 'Yozuv yo\'q' : `${from}–${to} / ${feed.total} ta`}
          </p>
          <div className="flex items-center gap-1">
            <button
              type="button"
              disabled={feed.page <= 1 || loading}
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 disabled:cursor-not-allowed disabled:opacity-40"
            >
              <ChevronLeft className="h-4 w-4" />
            </button>
            <span className="min-w-[70px] text-center text-xs text-slate-500">
              {feed.page} / {lastPage}
            </span>
            <button
              type="button"
              disabled={feed.page >= lastPage || loading}
              onClick={() => setPage((p) => p + 1)}
              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 disabled:cursor-not-allowed disabled:opacity-40"
            >
              <ChevronRight className="h-4 w-4" />
            </button>
          </div>
        </div>
      </Card>
    </div>
  )
}
