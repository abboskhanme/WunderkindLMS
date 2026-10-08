import { useEffect, useState } from 'react'
import { ChevronRight } from 'lucide-react'
import {
  getInvoicesByPlan,
  NO_PLAN,
  type InvoicePlanRow,
  type InvoiceRegisterFilters,
  type InvoicesByPlan,
} from '@/api/services/invoices'
import { billingErrorMessage } from '@/api/services/billingError'
import { Loader } from '@/components/ui/Loader'
import { cn, formatMoney } from '@/lib/utils'
import { Notice } from './BillingUi'

interface Props {
  /** The register's filter — the same numbers as the list, grouped. */
  query: InvoiceRegisterFilters
  /** A row was picked: show the list of that abonement. */
  onPick: (planId: string) => void
}

/**
 * "Abonementlar bo'yicha" (client, 2026-10-08: "qaysi abonement bo'yicha nechta student bor, qancha
 * to'lovlar bo'lgan"): one line per abonement for the chosen months — pupils, billed, discount, paid,
 * left (billed − discount − paid) and debtors. All money comes from the server; "Abonementsiz" collects months no abonement
 * covers. A line opens the register filtered to it.
 */
export function InvoicesByPlanTable({ query, onPick }: Props) {
  const [data, setData] = useState<InvoicesByPlan | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let cancelled = false
    // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr o'zgarganda qayta yuklash (maqsadli)
    setLoading(true)
    getInvoicesByPlan(query)
      .then((result) => {
        if (cancelled) return
        setData(result)
        setError(null)
      })
      .catch((e: unknown) => {
        if (cancelled) return
        setData(null)
        setError(billingErrorMessage(e, "Abonementlar bo'yicha hisobotni yuklab bo'lmadi"))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [query])

  if (loading) return <Loader label="Yuklanmoqda..." />
  if (error) return <div className="p-4"><Notice>{error}</Notice></div>
  if (!data || data.rows.length === 0) {
    return <p className="px-4 py-12 text-center text-sm text-slate-400">Bu filtr bo'yicha hisob-faktura yo'q</p>
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left text-sm">
        <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
          <tr>
            <th className="px-4 py-3">Abonement</th>
            <th className="px-4 py-3 text-right">Oylik narx</th>
            <th className="px-4 py-3 text-right">O'quvchilar</th>
            <th className="px-4 py-3 text-right">Hisoblangan</th>
            <th className="px-4 py-3 text-right">Chegirma</th>
            <th className="px-4 py-3 text-right">To'langan</th>
            <th className="px-4 py-3 text-right">Qoldiq</th>
            <th className="px-4 py-3 text-right">Qarzdorlar</th>
            <th className="w-8 px-2 py-3" />
          </tr>
        </thead>
        <tbody>
          {data.rows.map((row) => (
            <tr
              key={`${row.planId ?? 'none'}-${row.categoryName}`}
              onClick={() => onPick(row.planId ?? NO_PLAN)}
              className="cursor-pointer border-t border-slate-100 hover:bg-slate-50/70"
            >
              <td className="px-4 py-2.5">
                <p className={cn('font-medium', row.planId ? 'text-slate-800' : 'text-amber-700')}>
                  {row.planName}
                  {!row.planActive && <span className="ml-1.5 text-xs font-normal text-slate-400">(arxivda)</span>}
                </p>
                <p className="text-xs text-slate-400">{row.categoryName}</p>
              </td>
              <td className="whitespace-nowrap px-4 py-2.5 text-right text-slate-600">
                {row.monthlyAmount === null ? '—' : formatMoney(row.monthlyAmount)}
              </td>
              <td className="whitespace-nowrap px-4 py-2.5 text-right font-medium text-slate-800">{row.students}</td>
              <Money value={row.amount} />
              <Money value={row.discount} muted />
              <Money value={row.paid} tone="text-emerald-600" />
              <Money value={row.remaining} tone={row.remaining > 0 ? 'text-red-600' : 'text-slate-400'} />
              <td className="whitespace-nowrap px-4 py-2.5 text-right text-slate-600">{row.debtors}</td>
              <td className="px-2 py-2.5 text-slate-300">
                <ChevronRight className="h-4 w-4" />
              </td>
            </tr>
          ))}
        </tbody>
        <TotalRow row={data.total} />
      </table>
    </div>
  )
}

function Money({ value, tone, muted }: { value: number; tone?: string; muted?: boolean }) {
  return (
    <td className={cn('whitespace-nowrap px-4 py-2.5 text-right', tone ?? (muted ? 'text-slate-500' : 'text-slate-800'))}>
      {formatMoney(value)}
    </td>
  )
}

function TotalRow({ row }: { row: InvoicePlanRow }) {
  return (
    <tfoot>
      <tr className="border-t-2 border-slate-200 bg-slate-50 font-semibold text-slate-800">
        <td className="px-4 py-2.5">Jami</td>
        <td />
        <td className="whitespace-nowrap px-4 py-2.5 text-right">{row.students}</td>
        <Money value={row.amount} />
        <Money value={row.discount} muted />
        <Money value={row.paid} tone="text-emerald-600" />
        <Money value={row.remaining} tone={row.remaining > 0 ? 'text-red-600' : 'text-slate-400'} />
        <td className="whitespace-nowrap px-4 py-2.5 text-right">{row.debtors}</td>
        <td />
      </tr>
    </tfoot>
  )
}
