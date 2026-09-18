import {
  AlertTriangle,
  ArrowDownCircle,
  ArrowUpCircle,
  Ban,
  Receipt,
  RefreshCw,
  Search,
} from 'lucide-react'
import type { PaymentMethod } from '@/types'
import type { CashBoxTransactionRow } from '@/api/services/cashBoxes'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { StatusPill } from '@/pages/admin/billing/BillingUi'
import { cn } from '@/lib/utils'
import { DataTable } from '@/components/table/DataTable'
import type { DataTableColumn } from '@/components/table/DataTable'
import { formatDateTime, formatSum, kindLabel, methodLabels, statusLabel } from './format'

interface Props {
  /** Usul kesimidagi yig'indi — QANCHA USUL BO'LSA, SHUNCHA kartochka (EduSchool skrinshotida 5 ta,
   *  qatorlar sayoz ekranga sig'may, YONGA aylanadi — sobit to'rttalik EMAS). */
  totalsByMethod: Partial<Record<PaymentMethod, number>>
  inTotal: number
  outTotal: number
  rows: CashBoxTransactionRow[]
  /** true — usul/turi/o'quvchi/sinf filtridan biri ishga tushgan: jadval torroq, yig'indilar EMAS. */
  narrowed?: boolean
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
 * O'ng ustun — usul kesimidagi yig'indi, kirim/chiqim, chek qidiruvi va
 * tranzaksiyalar jadvali (EduSchool tuzilishi, mijoz sxemasi).
 *
 * Pulni bu yerda HECH KIM hisoblamaydi: yig'indilar ham, jadval qatorlari
 * ham to'g'ridan-to'g'ri `GET /admin/cash-boxes/transactions` javobidan
 * chiziladi. `rows` endi `CashierPage`da usul/turi/o'quvchi/sinf bo'yicha
 * MIJOZ TOMONDA filtrlangan bo'lishi mumkin — shuning uchun `narrowed`
 * bo'lsa, yig'indilar davr/kassa bo'yicha (jadvaldan KATTAROQ) ekanini
 * eslatuvchi izoh chiqadi, aks holda kassir "nega yig'indi qatorlarga mos
 * kelmayapti" deb o'ylab qoladi.
 */
export function CashLedger({
  totalsByMethod,
  inTotal,
  outTotal,
  rows,
  narrowed,
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
  const methodEntries = Object.entries(totalsByMethod)

  return (
    <div className="space-y-4">
      {/* --- Usul kesimidagi yig'indi — nechta bo'lsa, shuncha; sig'masa yonga suriladi --- */}
      <div className="flex gap-3 overflow-x-auto pb-1">
        {methodEntries.map(([m, sum]) => (
          <Card key={m} className="w-36 shrink-0 p-3">
            <p className="truncate text-xs font-medium uppercase tracking-wide text-slate-400">
              {methodLabels[m as PaymentMethod] ?? m}
            </p>
            <p className="mt-1 text-base font-semibold tabular-nums text-slate-800">{formatSum(sum)}</p>
          </Card>
        ))}
      </div>
      {narrowed && (
        <p className="text-xs text-slate-400">
          * Yig'indilar tanlangan davr/kassa bo'yicha — usul, tranzaksiya turi, o'quvchi va sinf filtri
          faqat jadvalni torayadi.
        </p>
      )}

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
        {error ? (
          <div className="flex flex-col items-center gap-3 px-4 py-10 text-center">
            <AlertTriangle className="h-7 w-7 text-red-400" />
            <p className="text-sm text-red-600">{error}</p>
            <Button variant="secondary" onClick={onRetry}>
              <RefreshCw className="h-4 w-4" /> Qayta urinish
            </Button>
          </div>
        ) : (
          // `DataTable` — X-1 (`components/table/DataTable.tsx`): yuklanish, bo'sh
          // holat va ustunlarni yashirish/tartiblash/qadash (o'ng yuqoridagi
          // "Ustunlar" tugmasi) shu komponentning o'zida — bu yerda qayta
          // yozilmaydi.
          <DataTable
            pageKey="cashier.ledger"
            columns={ledgerColumns(allSelected, onToggleSelectAll, selected, onToggleSelect, onCancelRow)}
            rows={rows}
            getRowId={(r) => r.id}
            loading={loading}
            rowClassName={(row) => (row.status === 'cancelled' ? 'bg-red-50/60' : 'hover:bg-slate-50/60')}
            emptyMessage="Tanlangan davrda tranzaksiya yo'q."
          />
        )}
      </Card>
    </div>
  )
}

/**
 * Ustunlar — EduSchool skrinshotidagi to'liq ro'yxat: checkbox · № · SANA ·
 * KIM · SHARTNOMA RAQAM · MIQDOR · TRANZAKSIYA · HOLATI · TO'LOV USULI ·
 * KASSIR.
 *
 * KIM va KASSIR — bitta manbadan (`row.who`): backend `CashBoxTransactionRowDto.Who`
 * "kim yozgan"ni, ya'ni AMALNI BAJARGAN kassirni qaytaradi
 * (`CashBoxService.cs` izohi), o'quvchi/to'lovchi ISMI esa qatorda umuman
 * yo'q (faqat `contractNo` bor). Shuning uchun ikkala ustun bir xil qiymatni
 * ko'rsatadi — bu FABRIKATSIYA emas, mavjud yagona "kim" maydoni; agar mijoz
 * "Kim" ustuni to'lovchi (o'quvchi) ismini ko'rsatishini xohlasa, bu
 * backend'ga o'quvchi ismini qatorga qo'shishni talab qiladi (hisobotda
 * aytilgan).
 */
function ledgerColumns(
  allSelected: boolean,
  onToggleSelectAll: () => void,
  selected: Set<string>,
  onToggleSelect: (id: string) => void,
  onCancelRow: (row: CashBoxTransactionRow) => void,
): DataTableColumn<CashBoxTransactionRow>[] {
  return [
    {
      id: 'select',
      alwaysVisible: true,
      headerClassName: 'w-10',
      header: (
        <input
          type="checkbox"
          checked={allSelected}
          onChange={onToggleSelectAll}
          aria-label="Hammasini tanlash"
          className="h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-400"
        />
      ),
      cell: (row) => (
        <input
          type="checkbox"
          checked={selected.has(row.id)}
          onChange={() => onToggleSelect(row.id)}
          aria-label={`№${row.no} tanlash`}
          className="h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-400"
        />
      ),
    },
    {
      id: 'no',
      header: '№',
      cell: (row) => <span className="text-slate-500">{row.no}</span>,
    },
    {
      id: 'date',
      header: 'Sana',
      cellClassName: 'whitespace-nowrap',
      cell: (row) => <span className="text-slate-500">{formatDateTime(row.date)}</span>,
    },
    {
      id: 'who',
      header: 'Kim',
      cell: (row) => <span className="text-slate-700">{row.who}</span>,
    },
    {
      id: 'contract',
      header: 'Shartnoma raqami',
      cell: (row) =>
        row.contractNo ? (
          <span className="inline-flex items-center gap-1 text-slate-500">
            <Receipt className="h-3.5 w-3.5 text-slate-300" />
            {row.contractNo}
          </span>
        ) : (
          <span className="text-slate-500">—</span>
        ),
    },
    {
      id: 'amount',
      header: 'Miqdor',
      headerClassName: 'text-right',
      cellClassName: 'text-right',
      cell: (row) => (
        <span
          className={cn(
            'font-medium tabular-nums',
            row.status === 'cancelled' ? 'text-slate-400 line-through' : 'text-slate-800',
          )}
        >
          {formatSum(row.amount)}
        </span>
      ),
    },
    {
      id: 'kind',
      header: 'Tranzaksiya',
      cell: (row) => <span className="text-slate-600">{kindLabel(row.kind)}</span>,
    },
    {
      id: 'status',
      header: 'Holati',
      cell: (row) => (
        <StatusPill tone={row.status === 'cancelled' ? 'danger' : 'success'}>
          {statusLabel(row.status)}
        </StatusPill>
      ),
    },
    {
      id: 'method',
      header: "To'lov usuli",
      cell: (row) => <span className="text-slate-600">{methodLabels[row.method] ?? row.method}</span>,
    },
    {
      id: 'cashier',
      header: 'Kassir',
      // `row.who` bilan bir xil manba — izoh: shu funksiya boshidagi hujjat.
      cell: (row) => <span className="text-slate-600">{row.who}</span>,
    },
    {
      id: 'actions',
      header: 'Amal',
      alwaysVisible: true,
      headerClassName: 'text-right',
      cellClassName: 'text-right',
      cell: (row) =>
        row.status !== 'cancelled' && (
          <button
            type="button"
            title="Tranzaksiyani bekor qilish"
            aria-label="Tranzaksiyani bekor qilish"
            onClick={() => onCancelRow(row)}
            className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
          >
            <Ban className="h-4 w-4" />
          </button>
        ),
    },
  ]
}
