/**
 * Mahsulotlar — mini-magazin katalogi (mijoz, 2026-10-15): "Mahsulotlar va kitob savdosi (Ombor /
 * Mini-magazin). Maktab ichidagi qo'shimcha savdolar (kitoblar, formalar, to'garak aksessuarlari)
 * hisobini yuritish uchun bo'lim yaratilsin." `BillingSettingsPage.tsx` ning kataloglar markazida
 * ochiladi (`/admin/billing/settings?tab=products`) — `navigation.ts` Moliya menyusiga yangi yozuv
 * qo'shishni taqiqlaydi.
 *
 * SOTUV BU YERDA EMAS: kassir Kassa → Kirim shaklida turni tanlaydi, shu turdagi mahsulotlar qator
 * bo'lib chiqadi (`CashierPage.tsx`, `IncomeForm`). Bu sahifa — faqat nom, tur, narx va kirim turi.
 *
 * SOTILGAN MAHSULOT — O'CHIRISH TUGMASI YO'Q: uning sotuv qatorlari unga ishora qiladi, server 409
 * (`product_in_use`) qaytarardi. O'rniga "Faol" belgisi olinadi — `TransactionTypesPage.tsx` dagi
 * "seed qilingan tur" bilan bir xil falsafa.
 */
import { useCallback, useEffect, useState } from 'react'
import { Lock, Pencil, Plus, Trash2 } from 'lucide-react'
import type { ShopProduct, ShopProductInput } from '@/api/services/shop'
import { createShopProduct, deleteShopProduct, getShopProducts, shopKindLabel, updateShopProduct } from '@/api/services/shop'
import type { TransactionType } from '@/api/services/transactionTypes'
import { getTransactionTypes } from '@/api/services/transactionTypes'
import { billingErrorMessage } from '@/api/services/billingError'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { formatMoney } from '@/lib/utils'
import { AsyncBlock, BillingGuard, IconBtn, Notice, StatusPill } from './BillingUi'
import { useBillingAccess } from './access'
import { ShopProductFormModal } from './ShopProductFormModal'

export function ShopProductsPage() {
  return (
    <BillingGuard>
      <ShopProductsView />
    </BillingGuard>
  )
}

function ShopProductsView() {
  const { canManageShopProducts } = useBillingAccess()

  const [rows, setRows] = useState<ShopProduct[]>([])
  const [types, setTypes] = useState<TransactionType[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<ShopProduct | null>(null)
  const [saving, setSaving] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [pageNotice, setPageNotice] = useState<string | null>(null)
  const [deletingId, setDeletingId] = useState<string | null>(null)

  const load = useCallback(() => {
    setLoading(true)
    setError(null)
    Promise.all([getShopProducts(), getTransactionTypes('in')])
      .then(([products, inTypes]) => {
        setRows(products)
        setTypes(inTypes)
      })
      .catch((e: unknown) => setError(billingErrorMessage(e, "Mahsulotlarni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [])

  // eslint-disable-next-line react-hooks/set-state-in-effect -- sahifa ochilganda birinchi yuklash (loyihadagi umumiy naqsh)
  useEffect(() => load(), [load])

  /** Active Kirim types, plus the edited product's own type even if it was switched off since. */
  const typeChoices = types.filter((t) => t.isActive || t.id === editing?.transactionTypeId)

  const openCreate = () => {
    setEditing(null)
    setFormError(null)
    setFormOpen(true)
  }

  const openEdit = (row: ShopProduct) => {
    setEditing(row)
    setFormError(null)
    setFormOpen(true)
  }

  const sortRows = (list: ShopProduct[]) =>
    [...list].sort((a, b) => a.position - b.position || a.name.localeCompare(b.name))

  const handleSubmit = async (values: ShopProductInput) => {
    setSaving(true)
    setFormError(null)
    try {
      if (editing) {
        const updated = await updateShopProduct(editing.id, values)
        setRows((prev) => sortRows(prev.map((r) => (r.id === updated.id ? updated : r))))
        setPageNotice(`"${updated.name}" yangilandi.`)
      } else {
        const created = await createShopProduct(values)
        setRows((prev) => sortRows([...prev, created]))
        setPageNotice(`"${created.name}" qo'shildi.`)
      }
      setFormOpen(false)
      setEditing(null)
    } catch (e: unknown) {
      setFormError(billingErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  const handleDelete = async (row: ShopProduct) => {
    if (!window.confirm(`"${row.name}" o'chiriladi. Davom etasizmi?`)) return
    setDeletingId(row.id)
    setPageNotice(null)
    try {
      await deleteShopProduct(row.id)
      setRows((prev) => prev.filter((r) => r.id !== row.id))
      setPageNotice(`"${row.name}" o'chirildi.`)
    } catch (e: unknown) {
      setError(billingErrorMessage(e, "O'chirib bo'lmadi"))
    } finally {
      setDeletingId(null)
    }
  }

  const activeCount = rows.filter((r) => r.isActive).length

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Mahsulotlar</h1>
          <p className="text-sm text-slate-400">
            Kitob, forma va to'garak aksessuarlari. Kassada Kirim turi tanlanganda shu turdagi mahsulotlar chiqadi,
            narx shu yerdan olinadi. Jami {rows.length} ta · {activeCount} tasi faol.
          </p>
        </div>
        {canManageShopProducts && (
          <Button onClick={openCreate}>
            <Plus className="h-4 w-4" /> Yangi mahsulot
          </Button>
        )}
      </div>

      {pageNotice && <Notice tone="success">{pageNotice}</Notice>}

      <Card className="p-0">
        <AsyncBlock
          loading={loading}
          error={error}
          empty={rows.length === 0}
          emptyText="Hozircha birorta mahsulot yo'q."
          emptyAction={
            canManageShopProducts ? (
              <Button variant="secondary" onClick={openCreate}>
                <Plus className="h-4 w-4" /> Birinchi mahsulotni qo'shish
              </Button>
            ) : undefined
          }
          onRetry={load}
        >
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">№</th>
                  <th className="px-4 py-3">Nomi</th>
                  <th className="px-4 py-3">Turi</th>
                  <th className="px-4 py-3 text-right">Narx</th>
                  <th className="px-4 py-3">Kirim turi</th>
                  <th className="px-4 py-3">Holat</th>
                  <th className="px-4 py-3 text-right">Amallar</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((row, idx) => (
                  <tr key={row.id} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3 text-slate-500">{idx + 1}</td>
                    <td className="px-4 py-3">
                      <span className="font-medium text-slate-800">{row.name}</span>
                    </td>
                    <td className="px-4 py-3 text-slate-600">{shopKindLabel(row.kind)}</td>
                    <td className="whitespace-nowrap px-4 py-3 text-right font-medium tabular-nums text-slate-800">
                      {formatMoney(row.price)}
                    </td>
                    <td className="px-4 py-3 text-slate-600">{row.transactionTypeName}</td>
                    <td className="px-4 py-3">
                      {row.isActive ? <StatusPill tone="success">Faol</StatusPill> : <StatusPill>Faolsiz</StatusPill>}
                    </td>
                    <td className="px-4 py-3">
                      <span className="flex justify-end gap-1">
                        {canManageShopProducts ? (
                          <>
                            <IconBtn icon={Pencil} title="Tahrirlash" onClick={() => openEdit(row)} />
                            {row.isSold ? (
                              <span
                                title="Sotilgan mahsulot — o'chirib bo'lmaydi, faolsizlantirish mumkin"
                                className="flex h-8 w-8 items-center justify-center text-slate-300"
                              >
                                <Lock className="h-4 w-4" />
                              </span>
                            ) : (
                              <IconBtn
                                icon={Trash2}
                                title="O'chirish"
                                tone="danger"
                                disabled={deletingId === row.id}
                                onClick={() => void handleDelete(row)}
                              />
                            )}
                          </>
                        ) : (
                          <span className="text-xs text-slate-300">—</span>
                        )}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </AsyncBlock>
      </Card>

      {canManageShopProducts && (
        <ShopProductFormModal
          open={formOpen}
          initial={editing}
          types={typeChoices}
          busy={saving}
          error={formError}
          onClose={() => {
            setFormOpen(false)
            setEditing(null)
          }}
          onSubmit={handleSubmit}
        />
      )}
    </div>
  )
}
