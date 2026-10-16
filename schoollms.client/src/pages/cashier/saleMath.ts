import type { ShopProduct } from '@/api/services/shop'

/* Sale-line arithmetic for the Kirim form (`SaleLines.tsx`) — a .ts file so the component file
   exports components only (react-refresh). docs/modules/shop.md. */

export interface SaleRow {
  key: string
  productId: string
  /** Raw text — "" means not typed yet. */
  quantity: string
}

/** Same cap as the server (`ShopSales.MaxQuantity`). */
export const MAX_QUANTITY = 10_000

export const newSaleRow = (): SaleRow => ({
  key: Math.random().toString(36).slice(2),
  productId: '',
  quantity: '1',
})

export function parseQuantity(text: string): number | null {
  if (!/^\d+$/.test(text.trim())) return null
  const n = Number(text)
  return n >= 1 && n <= MAX_QUANTITY ? n : null
}

/** Rows with a product chosen — the ones that are sent. */
export function filledRows(rows: SaleRow[]): SaleRow[] {
  return rows.filter((r) => r.productId !== '')
}

/**
 * Total in so'm, summed in tiyin so 0.1 + 0.2 never becomes 0.30000000000000004 — the server compares
 * to the tiyin. `null` while a chosen row has no valid quantity.
 */
export function saleTotal(rows: SaleRow[], products: ShopProduct[]): number | null {
  let tiyin = 0
  for (const row of filledRows(rows)) {
    const product = products.find((p) => p.id === row.productId)
    const qty = parseQuantity(row.quantity)
    if (!product || qty === null) return null
    tiyin += Math.round(product.price * 100) * qty
  }
  return tiyin / 100
}
