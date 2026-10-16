import { Plus, X } from 'lucide-react'
import type { ShopProduct } from '@/api/services/shop'
import { Button } from '@/components/ui/Button'
import { cn } from '@/lib/utils'
import { formatSum } from './format'
import type { SaleRow } from './saleMath'
import { MAX_QUANTITY, filledRows, newSaleRow, parseQuantity, saleTotal } from './saleMath'

/* ==========================================================================
   Mini-magazin sotuvi — Kirim shaklidagi mahsulot qatorlari (mijoz, 2026-10-15):
   "Kassa/Moliya kiritish vaqtida to'lov turiga (masalan: 'Kitob savdosi') bog'lab kirim
   qilish." Tanlangan Kirim turining faol mahsulotlari bo'lsa chiqadi; narx katalogdan,
   summa — qatorlar jami (kassir uni qo'lda o'zgartirmaydi). Server narxni o'zi qayta
   hisoblaydi va summa jamiga teng bo'lmasa rad etadi (`ShopSales.cs`).

   Qator ixtiyoriy: mahsulot tanlanmagan bo'sh qator e'tiborga olinmaydi, ya'ni shu tur bilan
   katalogda yo'q narsaga oddiy kirim ham yozish mumkin.
   ========================================================================== */

interface Props {
  /** Active products of the chosen Kirim type. */
  products: ShopProduct[]
  rows: SaleRow[]
  onChange: (rows: SaleRow[]) => void
  disabled?: boolean
}

export function SaleLines({ products, rows, onChange, disabled }: Props) {
  const update = (key: string, patch: Partial<SaleRow>) =>
    onChange(rows.map((r) => (r.key === key ? { ...r, ...patch } : r)))
  const remove = (key: string) => onChange(rows.filter((r) => r.key !== key))
  const total = saleTotal(rows, products)
  const chosen = filledRows(rows)
  const canAdd = rows.length < products.length

  return (
    <div className="mt-4">
      <span className="mb-1 block text-sm font-medium text-slate-600">Mahsulotlar</span>
      <div className="space-y-2">
        {rows.map((row) => {
          const product = products.find((p) => p.id === row.productId)
          const qty = parseQuantity(row.quantity)
          const qtyInvalid = row.productId !== '' && qty === null
          return (
            <div key={row.key} className="flex flex-wrap items-center gap-2 sm:flex-nowrap">
              <select
                value={row.productId}
                onChange={(e) => update(row.key, { productId: e.target.value })}
                disabled={disabled}
                aria-label="Mahsulot"
                className="min-w-0 flex-1 rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
              >
                <option value="">Mahsulot tanlang...</option>
                {products
                  // A product appears once per sale — the server refuses a second row of it.
                  .filter((p) => p.id === row.productId || !rows.some((r) => r.productId === p.id))
                  .map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.name} — {formatSum(p.price)}
                    </option>
                  ))}
              </select>
              <input
                value={row.quantity}
                onChange={(e) => update(row.key, { quantity: e.target.value.replace(/\D/g, '') })}
                disabled={disabled}
                inputMode="numeric"
                aria-label="Soni"
                title="Soni"
                className={cn(
                  'w-16 rounded-lg border px-2 py-2 text-right text-sm tabular-nums text-slate-800 outline-none',
                  qtyInvalid ? 'border-red-300 focus:border-red-400' : 'border-slate-200 focus:border-brand-400',
                )}
              />
              <span className="w-28 shrink-0 text-right text-sm tabular-nums text-slate-700">
                {product && qty !== null ? formatSum(Math.round(product.price * 100) * qty / 100) : '—'}
              </span>
              <button
                type="button"
                onClick={() => remove(row.key)}
                disabled={disabled}
                aria-label="Qatorni olib tashlash"
                title="Olib tashlash"
                className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600 disabled:opacity-40"
              >
                <X className="h-4 w-4" />
              </button>
            </div>
          )
        })}
      </div>

      <div className="mt-2 flex flex-wrap items-center justify-between gap-2">
        <Button variant="ghost" type="button" onClick={() => onChange([...rows, newSaleRow()])} disabled={disabled || !canAdd}>
          <Plus className="h-4 w-4" /> Mahsulot qo'shish
        </Button>
        {chosen.length > 0 && (
          <span className="text-sm text-slate-500">
            Jami:{' '}
            <span className="font-semibold tabular-nums text-slate-800">
              {total === null ? '—' : `${formatSum(total)} so'm`}
            </span>
          </span>
        )}
      </div>
      {chosen.some((r) => parseQuantity(r.quantity) === null) && (
        <p className="mt-1 text-xs text-red-600">Soni 1 dan {formatSum(MAX_QUANTITY)} gacha butun son bo'lishi kerak.</p>
      )}
    </div>
  )
}
