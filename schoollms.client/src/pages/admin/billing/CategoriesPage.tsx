/**
 * To'lov toifalari — maktab, avtobus, yotoqxona, ovqat va boshqalar (P1-17).
 *
 * Toifa — ma'lumotnoma, pul emas: bu yerda ledger yozuvi ham, ikki qavatli
 * nazorat ham yo'q. Shuning uchun sahifada tasdiq navbati yo'q.
 *
 * O'CHIRISH TUGMASI YO'Q va bo'lmaydi: toifaga obunalar, hisob-fakturalar
 * va daromad hisoblari bog'langan. Ishlatilmay qolgan toifa o'chirilmaydi —
 * FAOLSIZ qilinadi, shunda tarix joyida qoladi, yangi obunada esa u
 * tanlanmaydi.
 */
import { useCallback, useEffect, useState } from 'react'
import { Layers, Pencil, Plus } from 'lucide-react'
import type { FeeCategory } from '@/types'
import type { FeeCategoryInput } from '@/api/services/billingCatalog'
import {
  createFeeCategory,
  getFeeCategories,
  updateFeeCategory,
} from '@/api/services/billingCatalog'
import { billingErrorMessage } from '@/api/services/billingError'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { AsyncBlock, BillingGuard, IconBtn, Notice, StatusPill } from './BillingUi'
import { useBillingAccess } from './access'
import { CategoryFormModal } from './CategoryFormModal'

export function CategoriesPage() {
  return (
    <BillingGuard>
      <CategoriesView />
    </BillingGuard>
  )
}

function CategoriesView() {
  const { canManageSubscriptions } = useBillingAccess()

  const [rows, setRows] = useState<FeeCategory[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<FeeCategory | null>(null)
  const [saving, setSaving] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [pageNotice, setPageNotice] = useState<string | null>(null)

  const load = useCallback(() => {
    setLoading(true)
    setError(null)
    getFeeCategories()
      .then(setRows)
      .catch((e: unknown) => setError(billingErrorMessage(e, "Toifalarni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [])

  // eslint-disable-next-line react-hooks/set-state-in-effect -- sahifa ochilganda birinchi yuklash (loyihadagi umumiy naqsh)
  useEffect(() => load(), [load])

  const openCreate = () => {
    setEditing(null)
    setFormError(null)
    setFormOpen(true)
  }

  const openEdit = (row: FeeCategory) => {
    setEditing(row)
    setFormError(null)
    setFormOpen(true)
  }

  const handleSubmit = async (values: FeeCategoryInput) => {
    setSaving(true)
    setFormError(null)
    try {
      if (editing) {
        const updated = await updateFeeCategory(editing.id, values)
        setRows((prev) => prev.map((r) => (r.id === updated.id ? updated : r)))
        setPageNotice(`"${updated.name}" toifasi yangilandi.`)
      } else {
        const created = await createFeeCategory(values)
        setRows((prev) => [...prev, created].sort((a, b) => a.name.localeCompare(b.name)))
        setPageNotice(`"${created.name}" toifasi qo'shildi.`)
      }
      setFormOpen(false)
      setEditing(null)
    } catch (e: unknown) {
      setFormError(billingErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  const activeCount = rows.filter((r) => r.isActive).length

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">To'lov toifalari</h1>
          <p className="text-sm text-slate-400">
            Jami {rows.length} ta toifa · {activeCount} tasi faol
          </p>
        </div>
        {canManageSubscriptions && (
          <Button onClick={openCreate}>
            <Plus className="h-4 w-4" /> Yangi toifa
          </Button>
        )}
      </div>

      {pageNotice && <Notice tone="success">{pageNotice}</Notice>}

      <Card className="p-0">
        <AsyncBlock
          loading={loading}
          error={error}
          empty={rows.length === 0}
          emptyText="Hozircha birorta to'lov toifasi yo'q."
          emptyAction={
            canManageSubscriptions ? (
              <Button variant="secondary" onClick={openCreate}>
                <Plus className="h-4 w-4" /> Birinchi toifani qo'shish
              </Button>
            ) : undefined
          }
          onRetry={load}
        >
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">Toifa</th>
                  <th className="px-4 py-3">Kod</th>
                  <th className="px-4 py-3">Holat</th>
                  <th className="px-4 py-3 text-right">Amal</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((row) => (
                  <tr key={row.id} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3">
                      <span className="flex items-center gap-3">
                        <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-brand-50 text-brand-600">
                          <Layers className="h-4 w-4" />
                        </span>
                        <span className="font-medium text-slate-800">{row.name}</span>
                      </span>
                    </td>
                    <td className="px-4 py-3 font-mono text-xs text-slate-500">{row.code}</td>
                    <td className="px-4 py-3">
                      {row.isActive ? (
                        <StatusPill tone="success">Faol</StatusPill>
                      ) : (
                        <StatusPill>Faolsiz</StatusPill>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      <span className="flex justify-end">
                        {canManageSubscriptions ? (
                          <IconBtn
                            icon={Pencil}
                            title="Tahrirlash"
                            onClick={() => openEdit(row)}
                          />
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

      <p className="text-xs text-slate-400">
        Toifa o'chirilmaydi — unga obunalar va hisob-fakturalar bog'langan. Ishlatilmaydigan
        toifani "Faolsiz" qiling: tarix joyida qoladi, yangi obunada tanlanmaydi.
      </p>

      {canManageSubscriptions && (
        <CategoryFormModal
          open={formOpen}
          initial={editing}
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
