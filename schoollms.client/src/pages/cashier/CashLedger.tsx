import {
  AlertTriangle,
  ArrowDownCircle,
  ArrowUpCircle,
  Ban,
  Inbox,
  Receipt,
  RefreshCw,
  Search,
} from 'lucide-react'
import type { PaymentMethod } from '@/types'
import type { CashBoxTransactionRow } from '@/api/services/cashBoxes'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { StatusPill } from '@/pages/admin/billing/BillingUi'
import { cn } from '@/lib/utils'
import { formatDateTime, formatSum, kindLabel, methodLabels, statusLabel } from './format'

const METHODS: PaymentMethod[] = ['cash', 'card', 'transfer', 'online']

interface Props {
  totalsByMethod: Partial<Record<PaymentMethod, number>>
  inTotal: number
  outTotal: number
  rows: CashBoxTransactionRow[]
  loading: boolean
  error: string | null
  onRetry: () => void
  q: string
  onQChange: (q: string) => void
  selected: Set<string>
  onToggleSelect: (id: string) => void
  onToggleSelectAll: () => void
  onCancelRow: (row: CashBoxTransactionRow) => void
}

/**
 * O'ng ustun — to'rtta usul yig'indisi, kirim/chiqim, chek qidiruvi va
 * tranzaksiyalar jadvali (EduSchool tuzilishi, mijoz sxemasi).
 *
 * Pulni bu yerda HECH KIM hisoblamaydi: to'rttala yig'indi ham, jadval
 * qatorlari ham to'g'ridan-to'g'ri `GET /admin/cash-boxes/transactions`
 * javobidan chiziladi.
 */
export function CashLedger({
  totalsByMethod,
  inTotal,
  outTotal,
  rows,
  loading,
  error,
  onRetry,
  q,
  onQChange,
  selected,
  onToggleSelect,
  onToggleSelectAll,
  onCancelRow,
}: Props) {
  const allSelected = rows.length > 0 && rows.every((r) => selected.has(r.id))

  return (
    <div className="space-y-4">
      {/* --- To'rtta usul yig'indisi --- */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        {METHODS.map((m) => (
          <Card key={m} className="p-3">
            <p className="text-xs font-medium uppercase tracking-wide text-slate-400">
              {methodLabels[m]}
            </p>
            <p className="mt-1 text-base font-semibold tabular-nums text-slate-800">
              {formatSum(totalsByMethod[m] ?? 0)}
            </p>
          </Card>
        ))}
      </div>

      {/* --- Kirim / chiqim yig'indisi --- */}
      <div className="grid grid-cols-2 gap-3">
        <Card className="flex items-center gap-3 p-3">
          <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-emerald-50 text-emerald-600">
            <ArrowDownCircle className="h-5 w-5" />
          </span>
          <div className="min-w-0">
            <p className="text-xs text-slate-400">Jami kirim</p>
            <p className="truncate text-base font-semibold tabular-nums text-emerald-700">
              {formatSum(inTotal)}
            </p>
          </div>
        </Card>
        <Card className="flex items-center gap-3 p-3">
          <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-red-50 text-red-600">
            <ArrowUpCircle className="h-5 w-5" />
          </span>
          <div className="min-w-0">
            <p className="text-xs text-slate-400">Jami chiqim</p>
            <p className="truncate text-base font-semibold tabular-nums text-red-700">
              {formatSum(outTotal)}
            </p>
          </div>
        </Card>
      </div>

      {/* --- Chek/shartnoma raqami bo'yicha qidiruv --- */}
      <div className="relative">
        <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
        <input
          value={q}
          onChange={(e) => onQChange(e.target.value)}
          placeholder="Chek yoki shartnoma raqami, F.I.Sh. bo'yicha qidirish"
          autoComplete="off"
          className="w-full rounded-lg border border-slate-200 bg-white py-2 pl-9 pr-3 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
        />
      </div>

      {/* --- Jadval --- */}
      <Card className="p-0">
        {loading && <Loader className="py-10" label="Yuklanmoqda..." />}

        {!loading && error && (
          <div className="flex flex-col items-center gap-3 px-4 py-10 text-center">
            <AlertTriangle className="h-7 w-7 text-red-400" />
            <p className="text-sm text-red-600">{error}</p>
            <Button variant="secondary" onClick={onRetry}>
              <RefreshCw className="h-4 w-4" /> Qayta urinish
            </Button>
          </div>
        )}

        {!loading && !error && rows.length === 0 && (
          <div className="flex flex-col items-center gap-2 px-4 py-10 text-center">
            <Inbox className="h-7 w-7 text-slate-300" />
            <p className="text-sm text-slate-500">Tanlangan davrda tranzaksiya yo'q.</p>
          </div>
        )}

        {!loading && !error && rows.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="w-10 px-4 py-3">
                    <input
                      type="checkbox"
                      checked={allSelected}
                      onChange={onToggleSelectAll}
                      aria-label="Hammasini tanlash"
                      className="h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-400"
                    />
                  </th>
                  <th className="px-4 py-3">№</th>
                  <th className="px-4 py-3">Sana</th>
                  <th className="px-4 py-3">Kim</th>
                  <th className="px-4 py-3">Shartnoma raqami</th>
                  <th className="px-4 py-3 text-right">Miqdor</th>
                  <th className="px-4 py-3">Tranzaksiya</th>
                  <th className="px-4 py-3">Holati</th>
                  <th className="px-4 py-3 text-right">Amal</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((row) => {
                  const cancelled = row.status === 'cancelled'
                  return (
                    <tr key={row.id} className={cn(cancelled ? 'bg-red-50/60' : 'hover:bg-slate-50/60')}>
                      <td className="px-4 py-3">
                        <input
                          type="checkbox"
                          checked={selected.has(row.id)}
                          onChange={() => onToggleSelect(row.id)}
                          aria-label={`№${row.no} tanlash`}
                          className="h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-400"
                        />
                      </td>
                      <td className="px-4 py-3 text-slate-500">{row.no}</td>
                      <td className="whitespace-nowrap px-4 py-3 text-slate-500">
                        {formatDateTime(row.date)}
                      </td>
                      <td className="px-4 py-3 text-slate-700">{row.who}</td>
                      <td className="px-4 py-3 text-slate-500">
                        {row.contractNo ? (
                          <span className="inline-flex items-center gap-1">
                            <Receipt className="h-3.5 w-3.5 text-slate-300" />
                            {row.contractNo}
                          </span>
                        ) : (
                          '—'
                        )}
                      </td>
                      <td
                        className={cn(
                          'px-4 py-3 text-right font-medium tabular-nums',
                          cancelled ? 'text-slate-400 line-through' : 'text-slate-800',
                        )}
                      >
                        {formatSum(row.amount)}
                      </td>
                      <td className="px-4 py-3 text-slate-600">
                        {kindLabel(row.kind)}
                        <span className="ml-1.5 text-xs text-slate-400">
                          ({methodLabels[row.method] ?? row.method})
                        </span>
                      </td>
                      <td className="px-4 py-3">
                        <StatusPill tone={cancelled ? 'danger' : 'success'}>
                          {statusLabel(row.status)}
                        </StatusPill>
                      </td>
                      <td className="px-4 py-3 text-right">
                        {!cancelled && (
                          <button
                            type="button"
                            title="Tranzaksiyani bekor qilish"
                            aria-label="Tranzaksiyani bekor qilish"
                            onClick={() => onCancelRow(row)}
                            className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                          >
                            <Ban className="h-4 w-4" />
                          </button>
                        )}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </div>
  )
}
