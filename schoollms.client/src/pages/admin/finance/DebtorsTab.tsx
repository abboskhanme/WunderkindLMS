/**
 * Qarzdorlar — TOIFA KESIMIDA (P1-18).
 *
 * Manba: `GET /api/admin/finance/debtors`. Qarz har safar `invoices` va
 * `payment_allocations` dan hisoblanadi — o'quvchi qatoridagi saqlangan
 * qoldiqdan EMAS (P1-13 qoidasi). Server jami qarz bo'yicha kamayish
 * tartibida qaytaradi va bu tartib bu yerda BUZILMAYDI.
 *
 * ASOSIY TALAB: maktab / avtobus / yotoqxona qarzi ARALASHMAYDI. Har toifa
 * o'z ustuniga tushadi va yuqorida o'z yig'indisi bor — direktorga "avtobus
 * pulini kim to'lamadi" degan savol bitta ustunda ko'rinadi.
 */
import { useMemo, useState } from 'react'
import { AlertTriangle, Download, Users, Wallet } from 'lucide-react'
import { useAsync } from '@/hooks/useAsync'
import { getDebtors } from '@/api/services/financeReports'
import type { DebtorRow } from '@/types'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { StatCard } from '@/components/ui/StatCard'
import { cn, exportToCsv, formatMoney } from '@/lib/utils'
import { ReportState } from './ReportState'
import { CollectionRateCard } from './CollectionRateCard'
import { formatMonthLabel } from './reportLabels'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

/** Toifalarning ekrandagi tartibi — mijoz shu ketma-ketlikda o'ylaydi. */
const categoryOrder = ['tuition', 'bus', 'dormitory', 'meals', 'other']

interface CategoryColumn {
  code: string
  name: string
}

/** Jadval ustunlari: qaysi toifada haqiqatan qarz bor — o'sha ustun chiqadi. */
function categoryColumns(rows: DebtorRow[]): CategoryColumn[] {
  const found = new Map<string, string>()
  for (const row of rows) {
    for (const c of row.byCategory) {
      if (!found.has(c.categoryCode)) found.set(c.categoryCode, c.categoryName)
    }
  }
  return [...found.entries()]
    .map(([code, name]) => ({ code, name }))
    .sort((a, b) => {
      const ai = categoryOrder.indexOf(a.code)
      const bi = categoryOrder.indexOf(b.code)
      if (ai !== -1 && bi !== -1) return ai - bi
      if (ai !== -1) return -1
      if (bi !== -1) return 1
      return a.name.localeCompare(b.name)
    })
}

function debtOf(row: DebtorRow, code: string): number {
  return row.byCategory.find((c) => c.categoryCode === code)?.debt ?? 0
}

export function DebtorsTab() {
  const [onlyOverdue, setOnlyOverdue] = useState(false)
  // Sukut bo'yicha YOQIQ: maktabdan ketgan o'quvchining qarzi ham qarz
  // (FinanceReportQueries.DebtorReportQuery izohi). Uni yashirish —
  // direktorning ongli qarori bo'lishi kerak, sukut emas.
  const [includeArchived, setIncludeArchived] = useState(true)
  const [className, setClassName] = useState('')
  const [search, setSearch] = useState('')

  const { data, loading, error, refetch } = useAsync(
    () => getDebtors({ onlyOverdue, includeArchived }),
    [onlyOverdue, includeArchived],
  )

  const rows = useMemo(() => data ?? [], [data])

  const classes = useMemo(
    () => [...new Set(rows.map((r) => r.className))].sort((a, b) => a.localeCompare(b)),
    [rows],
  )

  const visible = useMemo(() => {
    const q = search.trim().toLowerCase()
    return rows.filter(
      (r) =>
        (!className || r.className === className) &&
        (!q || r.fullName.toLowerCase().includes(q) || r.parentPhone.includes(q)),
    )
  }, [rows, className, search])

  const columns = useMemo(() => categoryColumns(visible), [visible])

  // Ro'yxat yig'indilari — ekranda ko'rinib turgan qatorlar bo'yicha.
  // Har bir qator summasi serverdan tayyor keladi, bu yerda faqat qo'shiladi.
  const totals = useMemo(() => {
    const byCategory = new Map<string, number>()
    let debt = 0
    let overdueDebt = 0
    for (const r of visible) {
      debt += r.debt
      if (r.daysOverdue > 0) overdueDebt += r.debt
      for (const c of r.byCategory) {
        byCategory.set(c.categoryCode, (byCategory.get(c.categoryCode) ?? 0) + c.debt)
      }
    }
    return { debt, overdueDebt, byCategory, count: visible.length }
  }, [visible])

  const handleExport = () => {
    exportToCsv(
      'qarzdorlar.csv',
      [
        "O'quvchi",
        'Sinf',
        'Telefon',
        ...columns.map((c) => c.name),
        'Jami qarz',
        'Kechikish (kun)',
        'Eng eski oy',
      ],
      visible.map((r) => [
        r.fullName,
        r.className,
        r.parentPhone,
        ...columns.map((c) => String(debtOf(r, c.code))),
        String(r.debt),
        String(r.daysOverdue),
        r.oldestUnpaidMonth ? formatMonthLabel(r.oldestUnpaidMonth) : '',
      ]),
    )
  }

  return (
    <div className="space-y-6">
      <Card className="flex flex-wrap items-center gap-3 p-4">
        <input
          type="search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="O'quvchi yoki telefon"
          className={cn(control, 'min-w-56')}
        />
        <select
          value={className}
          onChange={(e) => setClassName(e.target.value)}
          className={control}
        >
          <option value="">Barcha sinflar</option>
          {classes.map((c) => (
            <option key={c} value={c}>
              {c}
            </option>
          ))}
        </select>
        <label className="flex items-center gap-2 text-sm text-slate-600">
          <input
            type="checkbox"
            checked={onlyOverdue}
            onChange={(e) => setOnlyOverdue(e.target.checked)}
            className="h-4 w-4 rounded border-slate-300 text-brand-600"
          />
          Faqat muddati o'tganlar
        </label>
        <label className="flex items-center gap-2 text-sm text-slate-600">
          <input
            type="checkbox"
            checked={includeArchived}
            onChange={(e) => setIncludeArchived(e.target.checked)}
            className="h-4 w-4 rounded border-slate-300 text-brand-600"
          />
          Arxivdagilar ham
        </label>
        <div className="ml-auto">
          <Button variant="secondary" onClick={handleExport} disabled={visible.length === 0}>
            <Download className="h-4 w-4" /> CSV
          </Button>
        </div>
      </Card>

      <ReportState
        loading={loading}
        error={error}
        isEmpty={visible.length === 0}
        emptyTitle="Qarzdor yo'q"
        emptyHint="Tanlangan filtr bo'yicha qarzi bor o'quvchi topilmadi."
        onRetry={refetch}
      >
        <div className="space-y-6">
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <StatCard
              label="Jami qarz"
              value={formatMoney(totals.debt)}
              icon={Wallet}
              iconBg="bg-red-50"
              iconColor="text-red-600"
            />
            <StatCard
              label="Qarzdorlar"
              value={String(totals.count)}
              icon={Users}
              iconBg="bg-brand-50"
              iconColor="text-brand-600"
              hint="Ekrandagi filtr bo'yicha"
            />
            <StatCard
              label="Muddati o'tgan qarz"
              value={formatMoney(totals.overdueDebt)}
              icon={AlertTriangle}
              iconBg="bg-amber-50"
              iconColor="text-amber-600"
              hint="To'lov muddati sozlamasi bo'yicha"
            />
          </div>

          {/* Toifalar kesimi — maktab / avtobus / yotoqxona ALOHIDA */}
          <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
            {columns.map((c) => (
              <Card key={c.code} className="p-4">
                <p className="text-xs font-medium uppercase tracking-wide text-slate-400">
                  {c.name}
                </p>
                <p className="mt-1 text-lg font-semibold text-slate-800">
                  {formatMoney(totals.byCategory.get(c.code) ?? 0)}
                </p>
              </Card>
            ))}
          </div>

          {/* Yig'ilish darajasi — "qancha hisoblandi, qanchasi keldi" savoli. */}
          <CollectionRateCard />

          <Card className="p-0">
            <div className="border-b border-slate-100 p-4">
              <h2 className="font-semibold text-slate-800">Qarzdorlar ro'yxati</h2>
              <p className="text-sm text-slate-400">
                Jami qarz bo'yicha kamayish tartibida · qizil qator — muddati o'tgan
              </p>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-4 py-3">O'quvchi</th>
                    <th className="px-4 py-3">Sinf</th>
                    <th className="px-4 py-3">Telefon</th>
                    {columns.map((c) => (
                      <th key={c.code} className="px-4 py-3 text-right">
                        {c.name}
                      </th>
                    ))}
                    <th className="px-4 py-3 text-right">Jami qarz</th>
                    <th className="px-4 py-3 text-right">Kechikish</th>
                    <th className="px-4 py-3">Eng eski oy</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {visible.map((r) => (
                    <tr
                      key={r.studentId}
                      className={cn(
                        'hover:bg-slate-50/60',
                        r.daysOverdue > 0 && 'bg-red-50/50 hover:bg-red-50',
                      )}
                    >
                      <td className="px-4 py-3 font-medium text-slate-800">{r.fullName}</td>
                      <td className="px-4 py-3">
                        <span className="rounded-md bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
                          {r.className}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-slate-500">{r.parentPhone || '—'}</td>
                      {columns.map((c) => {
                        const value = debtOf(r, c.code)
                        return (
                          <td
                            key={c.code}
                            className={cn(
                              'px-4 py-3 text-right',
                              value > 0
                                ? 'text-slate-700'
                                : value < 0
                                  ? 'text-emerald-600'
                                  : 'text-slate-300',
                            )}
                          >
                            {value === 0 ? '—' : formatMoney(value)}
                          </td>
                        )
                      })}
                      <td className="px-4 py-3 text-right font-semibold text-red-600">
                        {formatMoney(r.debt)}
                      </td>
                      <td className="px-4 py-3 text-right">
                        {r.daysOverdue > 0 ? (
                          <span className="rounded-md bg-red-100 px-2 py-0.5 text-xs font-semibold text-red-700">
                            {r.daysOverdue} kun
                          </span>
                        ) : (
                          <span className="text-xs text-slate-400">Muddatida</span>
                        )}
                      </td>
                      <td className="px-4 py-3 text-slate-500">
                        {r.oldestUnpaidMonth ? formatMonthLabel(r.oldestUnpaidMonth) : '—'}
                      </td>
                    </tr>
                  ))}
                </tbody>
                <tfoot>
                  <tr className="border-t border-slate-200 bg-slate-50/60 text-sm font-semibold">
                    <td className="px-4 py-3 text-slate-700" colSpan={3}>
                      Jami — {totals.count} ta o'quvchi
                    </td>
                    {columns.map((c) => (
                      <td key={c.code} className="px-4 py-3 text-right text-slate-700">
                        {formatMoney(totals.byCategory.get(c.code) ?? 0)}
                      </td>
                    ))}
                    <td className="px-4 py-3 text-right text-red-700">
                      {formatMoney(totals.debt)}
                    </td>
                    <td className="px-4 py-3" colSpan={2} />
                  </tr>
                </tfoot>
              </table>
            </div>
          </Card>
        </div>
      </ReportState>
    </div>
  )
}
