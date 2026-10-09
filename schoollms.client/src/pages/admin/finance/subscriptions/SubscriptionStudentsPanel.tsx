/**
 * Karta yoki abonement qatori bosilganda ochiladigan o'quvchilar ro'yxati — butun maktab bo'yicha
 * (mijoz, 2026-10-09: "cardlar ham filter vazifasini bajarsin, ayniqsa aboniment biriktirilmagan
 * cardi"). Kartadagi son va shu ro'yxat serverda bitta qoidadan chiqadi.
 */
import { useEffect, useMemo, useRef, useState } from 'react'
import { FileSpreadsheet, X } from 'lucide-react'
import { useAsync } from '@/hooks/useAsync'
import {
  downloadSubscriptionAnalytics,
  getSubscriptionStudents,
  type SubscriptionFilter,
  type SubscriptionStudents,
} from '@/api/services/subscriptionAnalytics'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { formatMoney } from '@/lib/utils'
import { SubscriptionStudentsTable } from './SubscriptionStudentsTable'

export function SubscriptionStudentsPanel({
  filter,
  title,
  hint,
  from,
  to,
  search,
  onClose,
}: {
  filter: SubscriptionFilter
  title: string
  hint?: string
  from: string
  to: string
  search: string
  onClose: () => void
}) {
  const ref = useRef<HTMLDivElement>(null)
  const [query, setQuery] = useState('')
  const [exporting, setExporting] = useState(false)
  const [exportError, setExportError] = useState<string | null>(null)

  const { data, loading, error } = useAsync<SubscriptionStudents>(
    () => getSubscriptionStudents(filter, from, to),
    [filter, from, to],
  )

  // Yangi filtr tanlanganda ro'yxat ko'rinadigan joyga suriladi.
  useEffect(() => {
    ref.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }, [filter])

  const rows = useMemo(() => {
    const q = query.trim().toLowerCase()
    const all = data?.students ?? []
    return q ? all.filter((s) => s.fullName.toLowerCase().includes(q) || s.className.toLowerCase().includes(q)) : all
  }, [data, query])

  return (
    <div ref={ref} className="scroll-mt-4">
      <Card className="p-0 ring-2 ring-brand-200">
        <div className="flex flex-wrap items-start justify-between gap-3 px-5 pt-5">
          <div>
            <h2 className="font-semibold text-slate-800">
              {title}
              {data && <span className="ml-2 text-sm font-normal text-slate-400">{data.students.length} o'quvchi</span>}
            </h2>
            {hint && <p className="text-xs text-slate-400">{hint}</p>}
            {data && (
              <p className="mt-1 text-xs text-slate-500 tabular-nums">
                Hisoblangan {formatMoney(data.billed)} · to'langan{' '}
                <span className="text-emerald-700">{formatMoney(data.paid)}</span> · qarz{' '}
                <span className={data.debt > 0 ? 'text-red-600' : undefined}>{formatMoney(data.debt)}</span>
              </p>
            )}
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <input
              type="search"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Ism yoki sinf"
              aria-label="Ro'yxatdan qidirish"
              className="w-48 rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
            />
            <Button
              variant="secondary"
              disabled={exporting || !data}
              onClick={() => {
                setExporting(true)
                setExportError(null)
                downloadSubscriptionAnalytics({ from, to, filter })
                  .catch((e: unknown) => setExportError(e instanceof Error ? e.message : 'Xatolik'))
                  .finally(() => setExporting(false))
              }}
            >
              <FileSpreadsheet className="h-4 w-4" /> {exporting ? 'Tayyorlanmoqda…' : 'Excel'}
            </Button>
            <button
              type="button"
              onClick={onClose}
              aria-label="Ro'yxatni yopish"
              className="rounded-lg p-2 text-slate-400 hover:bg-slate-100 hover:text-slate-600"
            >
              <X className="h-4 w-4" />
            </button>
          </div>
        </div>
        {exportError && <p className="px-5 pt-2 text-sm text-red-600">{exportError}</p>}
        <div className="mt-3">
          {loading ? (
            <Loader label="Yuklanmoqda..." />
          ) : error ? (
            <p className="px-5 pb-6 text-sm text-red-600">{error}</p>
          ) : (
            data && (
              <SubscriptionStudentsTable rows={rows} plans={data.plans} from={from} to={to} search={search} showClass />
            )
          )}
        </div>
      </Card>
    </div>
  )
}
