import {
  AlertTriangle,
  Ban,
  Printer,
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
import { NATIVE_CASH_KINDS, formatDateTime, formatSum, kindLabel, methodLabels, statusLabel } from './format'

interface Props {
  /** Usul kesimidagi yig'indi — QANCHA USUL BO'LSA, SHUNCHA kartochka (EduSchool skrinshotida 5 ta,
   *  qatorlar sayoz ekranga sig'may, YONGA aylanadi — sobit to'rttalik EMAS). */
  totalsByMethod: Partial<Record<PaymentMethod, number>>
  /** Davrdagi kirim / chiqim usul kesimida — kartochkada asosiy raqam kirim, ostida chiqim. */
  inByMethod?: Partial<Record<PaymentMethod, number>>
  outByMethod?: Partial<Record<PaymentMethod, number>>
  rows: CashBoxTransactionRow[]
  /** true — usul/turi/o'quvchi/sinf filtridan biri ishga tushgan: jadval torroq, yig'indilar EMAS. */
  narrowed?: boolean
  /** Whose money is on screen: the selected box, or every box (2026-10-13). */
  scope?: { boxName: string; allBoxes: boolean; onAllBoxesChange: (all: boolean) => void }
  loading: boolean
  error: string | null
  onRetry: () => void
  q: string
  onQChange: (q: string) => void
  selected: Set<string>
  onToggleSelect: (id: string) => void
  onToggleSelectAll: () => void
  onCancelRow: (row: CashBoxTransactionRow) => void
  /** Chek oynasini ochish — qator uchun bosiladigan qog'oz. */
  onReceiptRow: (row: CashBoxTransactionRow) => void
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
  inByMethod,
  outByMethod,
  rows,
  narrowed,
  scope,
  loading,
  error,
  onRetry,
  q,
  onQChange,
  selected,
  onToggleSelect,
  onToggleSelectAll,
  onCancelRow,
  onReceiptRow,
}: Props) {
  const allSelected = rows.length > 0 && rows.every((r) => selected.has(r.id))
  const methodEntries = Object.entries(totalsByMethod)

  return (
    // `min-w-0` — bu blok sahifadagi grid ustuni (`CashierPage`,
    // `lg:grid-cols-[...420px_1fr]`). Usiz jadvalning eng kichik eni ustunni
    // kengaytirib yuboradi va o'ng chekka (oxirgi karta, oxirgi ustun) ekrandan
    // chiqib ketadi; endi jadval O'Z ichida yonga suriladi (`DataTable`da
    // `overflow-x-auto` bor), kartalar esa ekranga sig'adi.
    <div className="min-w-0 space-y-4">
      {scope && (
        <div className="flex flex-wrap items-center justify-between gap-2 text-sm">
          <p className="min-w-0 truncate text-slate-500">
            {scope.allBoxes ? (
              <span className="font-medium text-slate-800">Barcha kassalar</span>
            ) : (
              <>
                <span className="font-medium text-slate-800">{scope.boxName}</span> kassasi
              </>
            )}{' '}
            — kirim va chiqim
          </p>
          <div className="flex rounded-lg bg-slate-100 p-0.5 text-xs font-medium">
            {[false, true].map((all) => (
              <button
                key={String(all)}
                type="button"
                onClick={() => scope.onAllBoxesChange(all)}
                aria-pressed={scope.allBoxes === all}
                className={cn(
                  'rounded-md px-3 py-1 transition-colors',
                  scope.allBoxes === all ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
                )}
              >
                {all ? 'Barcha kassalar' : 'Shu kassa'}
              </button>
            ))}
          </div>
        </div>
      )}

      {/* --- Usul kesimidagi yig'indi — kartalar eni bo'yicha teng taqsimlanadi
          (mijoz, 2026-09-18: "siqilib qolmasin"). `auto-fit` tufayli usullar
          soni nechta bo'lsa, shuncha ustun chiqadi va qator to'liq to'ladi. --- */}
      <div className="grid grid-cols-[repeat(auto-fit,minmax(120px,1fr))] gap-3">
        {/* Asosiy raqam — davrda shu usulda KIRGAN pul; ostida chiqqani (mijoz, 2026-10-05: kassir
            kunlik pulni buxgalterga ko'chirsa sof yig'indi 0/manfiy chiqib, "sana filtri ishlamayapti"
            deb o'qilardi). Eski javobda (kirim/chiqim yo'q) — sof yig'indi, avvalgidek. */}
        {methodEntries.map(([m, sum]) => {
          const method = m as PaymentMethod
          const income = inByMethod?.[method]
          const spent = outByMethod?.[method] ?? 0
          return (
            <Card key={m} className="p-3">
              <p className="text-xs font-medium uppercase leading-tight tracking-wide text-slate-400">
                {methodLabels[method] ?? m}
              </p>
              <p className="mt-1 text-base font-semibold tabular-nums text-slate-800">
                {formatSum(income ?? sum)}
              </p>
              {income !== undefined && spent > 0 && (
                <p className="mt-0.5 text-xs tabular-nums text-slate-400">chiqim: {formatSum(spent)}</p>
              )}
            </Card>
          )
        })}
      </div>
      {narrowed && (
        <p className="text-xs text-slate-400">
          * Yig'indilar tanlangan davr/kassa bo'yicha — usul, tranzaksiya turi, o'quvchi va sinf filtri
          faqat jadvalni torayadi.
        </p>
      )}

      {/* --- Kirim / chiqim yig'indisi --- */}
      {/* "Jami kirim" / "Jami chiqim" kartalari OLIB TASHLANDI (mijoz,
          2026-09-18): to'lov usuli kesimidagi kartalar ustida yana ikkita
          katta karta turardi va ekranning eni shunga ketardi. Raqamlarning
          o'zi yo'qolmadi — `inTotal`/`outTotal` hamon serverdan keladi. */}

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
            columns={ledgerColumns(
              allSelected,
              onToggleSelectAll,
              selected,
              onToggleSelect,
              onCancelRow,
              onReceiptRow,
            )}
            rows={rows}
            getRowId={(r) => r.id}
            loading={loading}
            rowClassName={(row) =>
              row.status === 'cancelled'
                ? 'bg-red-50/60'
                : row.status === 'pending'
                  ? 'bg-amber-50/60'
                  : 'hover:bg-slate-50/60'
            }
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
 * KIM — `row.who`. Kassaning O'Z amalida (kirim/chiqim/ko'chirish/ayirboshlash)
 * bu amalni bajargan kassir; o'quvchi to'lovi va qaytarimida esa O'QUVCHI ismi
 * (2026-09-22 dan beri ular ham shu jurnalda ko'rinadi).
 *
 * KASSIR — `row.cashierName` (2026-10-12): qatorni YOZGAN xodim, to'lov
 * qatorida ham (to'lovning kassiri). Eski javobda maydon bo'lmasa — avvalgidek
 * faqat kassaning o'z amallarida `row.who`.
 *
 * KASSA — qator qaysi kassada; ko'chirishda "yuborgan → qabul qiluvchi".
 */
function ledgerColumns(
  allSelected: boolean,
  onToggleSelectAll: () => void,
  selected: Set<string>,
  onToggleSelect: (id: string) => void,
  onCancelRow: (row: CashBoxTransactionRow) => void,
  onReceiptRow: (row: CashBoxTransactionRow) => void,
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
      // Sana + SOAT (`createdAt`, yozuv lahzasi): bitta kunda o'nlab qator
      // bo'ladi va kassir "qaysi biri meniki" ni faqat vaqt bilan ajratadi.
      // `row.date` esa kunlik filtr bilan bir xil manba — ikkalasi ham
      // serverda bitta lahzadan hisoblanadi.
      cell: (row) => <span className="text-slate-500">{formatDateTime(row.createdAt)}</span>,
    },
    {
      id: 'who',
      header: 'Kim',
      cell: (row) => <span className="text-slate-700">{row.who}</span>,
    },
    {
      id: 'box',
      header: 'Kassa',
      cell: (row) => (
        <span className="whitespace-nowrap text-slate-600">
          {row.boxName ?? '—'}
          {row.toBoxName && <span className="text-slate-400"> → {row.toBoxName}</span>}
        </span>
      ),
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
            row.status === 'cancelled' || row.status === 'returned'
              ? 'text-slate-400 line-through'
              : row.status === 'pending'
                ? 'text-amber-700'
                : 'text-slate-800',
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
      id: 'transactionType',
      header: 'Tranzaksiya turi',
      // Hozircha faqat Kirim qatorida to'ladi (`PlainIncomeForm.tsx`,
      // `CashBoxPayInRequest.TransactionTypeId`) — qolganlarida `—`.
      cell: (row) => (
        <span className="text-slate-600">
          {row.transactionTypeName ?? '—'}
          {row.recipientName && <span className="text-slate-400"> · {row.recipientName}</span>}
          {row.studentName && <span className="text-slate-400"> · {row.studentName}</span>}
          {/* Mini-magazin sale (2026-10-15): the products, in short; the receipt has the prices. */}
          {row.lines && row.lines.length > 0 && (
            <span
              className="block max-w-[16rem] truncate text-xs text-slate-400"
              title={row.lines.map((l) => `${l.productName} × ${l.quantity}`).join(', ')}
            >
              {row.lines.map((l) => `${l.productName} × ${l.quantity}`).join(', ')}
            </span>
          )}
        </span>
      ),
    },
    {
      id: 'status',
      header: 'Holati',
      cell: (row) => (
        <StatusPill
          tone={
            row.status === 'posted'
              ? 'success'
              : row.status === 'pending'
                ? 'warning'
                : row.status === 'returned'
                  ? 'neutral'
                  : 'danger'
          }
        >
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
      // Izoh: shu funksiya boshidagi hujjat.
      cell: (row) => (
        <span className="text-slate-600">
          {row.cashierName ?? (NATIVE_CASH_KINDS.includes(row.kind) ? row.who : '—')}
        </span>
      ),
    },
    {
      // IZOH va SABAB — EduSchool kassa ro'yxatida ham shu ikki ustun bor
      // (2026-09-18 da o'qildi). Ilgari kassir yozgan izoh ekranda UMUMAN
      // ko'rinmasdi (faqat audit jurnalida qolardi), bekor qilingan qatorning
      // sababi ham shunday edi. Uzun matn kesiladi, to'lig'i hoverda.
      id: 'note',
      header: 'Izoh',
      cell: (row) =>
        row.note ? (
          <span className="block max-w-[14rem] truncate text-slate-600" title={row.note}>
            {row.note}
          </span>
        ) : (
          <span className="text-slate-300">—</span>
        ),
    },
    {
      id: 'cancelReason',
      header: 'Sabab',
      cell: (row) =>
        row.cancelReason ? (
          <span className="block max-w-[14rem] truncate text-red-600" title={row.cancelReason}>
            {row.cancelReason}
          </span>
        ) : (
          <span className="text-slate-300">—</span>
        ),
    },
    {
      id: 'actions',
      header: 'Amal',
      alwaysVisible: true,
      headerClassName: 'text-right',
      cellClassName: 'text-right whitespace-nowrap',
      cell: (row) => (
        <span className="inline-flex items-center gap-1">
          {/* Chek — har qanday qator uchun, bekor qilingani uchun ham
              (o'sha qog'oz "nima uchun bekor qilindi" ni ham ko'rsatadi). */}
          <button
            type="button"
            title="Chek"
            aria-label="Chekni ochish"
            onClick={() => onReceiptRow(row)}
            className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600"
          >
            <Printer className="h-4 w-4" />
          </button>
          {/* Faqat kuchdagi qator: kutayotgan yoki qaytarilgan ko'chirish pulni
              qo'zg'atmagan — uni qabul qiluvchi qaytaradi (server ham rad etadi). */}
          {row.status === 'posted' && NATIVE_CASH_KINDS.includes(row.kind) && (
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
        </span>
      ),
    },
  ]
}
