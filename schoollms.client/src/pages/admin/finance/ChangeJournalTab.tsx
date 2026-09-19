/**
 * P&L 2.0 — F6.03 o'zgarishlar jurnali ("nega reja shu oy siljidi").
 *
 * Manba: `GET /api/admin/finance/pnl/expectation/changes` — to'liq qoida
 * `FinanceReportQueries.RevenueExpectationChanges.cs` da. Bu yerda faqat
 * ko'rsatish: raqam, tur va saralash SERVERDAN keladi.
 */
import { useState } from 'react'
import { ChevronLeft, ChevronRight, Download } from 'lucide-react'
import { useAsync } from '@/hooks/useAsync'
import { getChangeJournal, type ChangeJournalKind } from '@/api/services/financeReports'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { cn, exportToCsv } from '@/lib/utils'
import { changeKindClass, changeKindLabel, changeKindLabels, formatSignedMoney, signClass } from './reportLabels'
import { ReportState } from './ReportState'
import { formatDate } from '@/lib/utils'

interface Props {
  /** "YYYY-MM" */
  month: string
}

const PAGE_SIZE = 25

const kindOptions: { value: ChangeJournalKind | ''; label: string }[] = [
  { value: '', label: 'Hammasi' },
  ...(Object.keys(changeKindLabels) as ChangeJournalKind[]).map((k) => ({ value: k, label: changeKindLabels[k] })),
]

function netEffectText(value: number | null): string {
  return value === null ? '—' : formatSignedMoney(value)
}

export function ChangeJournalTab({ month }: Props) {
  const [kind, setKind] = useState<ChangeJournalKind | ''>('')
  const [page, setPage] = useState(1)

  const { data, loading, error, refetch } = useAsync(
    () => getChangeJournal(month, kind || undefined, page, PAGE_SIZE),
    [month, kind, page],
  )

  const lastPage = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1

  const handleExport = () => {
    if (!data) return
    exportToCsv(
      `pnl-2.0-jurnal_${month}.csv`,
      ['Sana', 'Turi', "O'quvchi", 'Sinf', 'Toifa', "Ta'sir", 'Izoh', 'Kim'],
      data.rows.map((r) => [
        r.date,
        changeKindLabel(r.kind),
        r.studentName,
        r.className,
        r.categoryName ?? '',
        netEffectText(r.netEffect),
        r.note,
        r.author,
      ]),
    )
  }

  return (
    <div className="space-y-4">
      <Card className="flex flex-wrap items-center gap-3 p-4">
        <span className="text-sm font-medium text-slate-600">Tur:</span>
        <select
          value={kind}
          onChange={(e) => {
            setKind(e.target.value as ChangeJournalKind | '')
            setPage(1)
          }}
          className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
        >
          {kindOptions.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
        <div className="ml-auto">
          <Button variant="secondary" onClick={handleExport} disabled={!data || data.rows.length === 0}>
            <Download className="h-4 w-4" /> CSV
          </Button>
        </div>
      </Card>

      <ReportState
        loading={loading}
        error={error}
        isEmpty={!!data && data.rows.length === 0}
        emptyTitle="Bu oyda o'zgarish yo'q"
        emptyHint="Boshqa oyni yoki turni tanlang."
        onRetry={refetch}
      >
        {data && (
          <Card className="p-0">
            <div className="overflow-x-auto">
              <table className="w-full min-w-[56rem] text-left text-sm">
                <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-4 py-3">Sana</th>
                    <th className="px-4 py-3">Turi</th>
                    <th className="px-4 py-3">O'quvchi</th>
                    <th className="px-4 py-3">Sinf</th>
                    <th className="px-4 py-3">Toifa</th>
                    <th className="px-4 py-3 text-right">Ta'sir</th>
                    <th className="px-4 py-3">Izoh</th>
                    <th className="px-4 py-3">Kim</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {data.rows.map((r, i) => (
                    <tr key={`${r.date}-${r.kind}-${r.studentId}-${i}`} className="hover:bg-slate-50/60 align-top">
                      <td className="whitespace-nowrap px-4 py-2.5 text-slate-500">{formatDate(r.date)}</td>
                      <td className="px-4 py-2.5">
                        <span
                          className={cn(
                            'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
                            changeKindClass(r.kind),
                          )}
                        >
                          {changeKindLabel(r.kind)}
                        </span>
                      </td>
                      <td className="px-4 py-2.5 text-slate-700">{r.studentName}</td>
                      <td className="px-4 py-2.5 text-slate-500">{r.className}</td>
                      <td className="px-4 py-2.5 text-slate-500">{r.categoryName ?? '—'}</td>
                      <td className={cn('px-4 py-2.5 text-right font-medium', signClass(r.netEffect ?? 0))}>
                        {netEffectText(r.netEffect)}
                      </td>
                      <td className="max-w-sm px-4 py-2.5 text-slate-600">{r.note}</td>
                      <td className="whitespace-nowrap px-4 py-2.5 text-slate-500">{r.author}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="flex items-center justify-between border-t border-slate-100 px-4 py-3">
              <p className="text-xs text-slate-400">
                {data.total === 0 ? "Yozuv yo'q" : `${data.rows.length} / ${data.total} ta`}
              </p>
              <div className="flex items-center gap-1">
                <button
                  type="button"
                  disabled={page <= 1 || loading}
                  onClick={() => setPage((p) => Math.max(1, p - 1))}
                  className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 disabled:cursor-not-allowed disabled:opacity-40"
                >
                  <ChevronLeft className="h-4 w-4" />
                </button>
                <span className="min-w-[70px] text-center text-xs text-slate-500">
                  {data.page} / {lastPage}
                </span>
                <button
                  type="button"
                  disabled={page >= lastPage || loading}
                  onClick={() => setPage((p) => p + 1)}
                  className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 disabled:cursor-not-allowed disabled:opacity-40"
                >
                  <ChevronRight className="h-4 w-4" />
                </button>
              </div>
            </div>
          </Card>
        )}
      </ReportState>
    </div>
  )
}
