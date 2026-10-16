/**
 * Mini-magazin (client, 2026-10-15): "Mahsulotlar va kitob savdosi" — the product catalogue
 * (Moliya sozlamalari → "Mahsulotlar") and "Savdo hisoboti" (Kassa page). docs/modules/shop.md.
 *
 * `ShopProductsController` — `/api/admin/finance/products`. The sale itself is NOT here: it is the
 * cash box's own Kirim with `lines` (`cashBoxIn` in `cashBoxes.ts`). Errors are not wrapped here;
 * pages read them with `billingErrorMessage` / `financeErrorMessage`, like the neighbours.
 */
import type { PaymentMethod } from '@/types'
import { api } from '../client'

const BASE = '/admin/finance/products'

/** The client's "turi" — server `ShopProductKind`. */
export type ShopProductKind = 'book' | 'uniform' | 'club' | 'other'

export const shopProductKinds: ShopProductKind[] = ['book', 'uniform', 'club', 'other']

/** Same words as the server's export (`ShopSalesReport.KindLabel`). */
export const shopProductKindLabels: Record<ShopProductKind, string> = {
  book: 'Kitob',
  uniform: 'Forma',
  club: "To'garak aksessuari",
  other: 'Boshqa',
}

export function shopKindLabel(kind: string): string {
  return shopProductKindLabels[kind as ShopProductKind] ?? kind
}

export interface ShopProduct {
  id: string
  name: string
  kind: ShopProductKind
  price: number
  /** The Kirim type it is sold under — its P&L account is the sale's account. */
  transactionTypeId: string
  transactionTypeName: string
  isActive: boolean
  position: number
  /** Sold at least once — can be switched off, not deleted. */
  isSold: boolean
}

export interface ShopProductInput {
  name: string
  kind: ShopProductKind
  price: number
  transactionTypeId: string
  isActive: boolean
  position: number
}

export type ShopProductPatch = Partial<ShopProductInput>

export async function getShopProducts(
  params: { activeOnly?: boolean; transactionTypeId?: string } = {},
): Promise<ShopProduct[]> {
  const { data } = await api.get<ShopProduct[]>(BASE, { params })
  return data
}

export async function createShopProduct(input: ShopProductInput): Promise<ShopProduct> {
  const { data } = await api.post<ShopProduct>(BASE, input)
  return data
}

export async function updateShopProduct(id: string, patch: ShopProductPatch): Promise<ShopProduct> {
  const { data } = await api.put<ShopProduct>(`${BASE}/${id}`, patch)
  return data
}

/** Never-sold products only — a sold one answers 409 `product_in_use`. */
export async function deleteShopProduct(id: string): Promise<void> {
  await api.delete(`${BASE}/${id}`)
}

/* ========================================================================
   Savdo hisoboti
   ======================================================================== */

/** One product line as sold (name and price at the moment of sale). */
export interface ShopSaleLine {
  productId: string
  productName: string
  quantity: number
  unitPrice: number
  lineTotal: number
}

export interface ShopSalesProductRow {
  productId: string
  name: string
  kind: string
  quantity: number
  amount: number
}

export interface ShopSalesGroupRow {
  key: string
  name: string
  /** Receipts with at least one line in the group. */
  sales: number
  quantity: number
  amount: number
}

export interface ShopSaleRow {
  transactionId: string
  createdAt: string
  boxName: string
  cashierName: string
  studentName: string | null
  transactionTypeName: string | null
  method: PaymentMethod
  amount: number
  lines: ShopSaleLine[]
}

export interface ShopSalesReport {
  from: string
  to: string
  salesCount: number
  quantity: number
  amount: number
  byProduct: ShopSalesProductRow[]
  byKind: ShopSalesGroupRow[]
  byType: ShopSalesGroupRow[]
  byCashier: ShopSalesGroupRow[]
  /** Newest first; capped on the server — `salesTruncated` says so. Totals count everything. */
  sales: ShopSaleRow[]
  salesTruncated: boolean
}

export interface ShopSalesFilters {
  /** "YYYY-MM-DD" */
  from?: string
  /** "YYYY-MM-DD" */
  to?: string
  boxId?: string
  studentId?: string
}

export async function getShopSalesReport(filters: ShopSalesFilters, signal?: AbortSignal): Promise<ShopSalesReport> {
  const { data } = await api.get<ShopSalesReport>(`${BASE}/sales`, { params: filters, signal })
  return data
}

/** The same report as .xlsx (server-built, one sheet per table). */
export async function exportShopSalesReport(filters: ShopSalesFilters): Promise<void> {
  const res = await api.get(`${BASE}/sales/export`, { params: filters, responseType: 'blob' })
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  const cd = (res.headers['content-disposition'] as string | undefined) ?? ''
  const m = cd.match(/filename="?([^";]+)"?/)
  a.download = m?.[1] ?? `savdo-hisoboti_${filters.from ?? ''}_${filters.to ?? ''}.xlsx`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}
