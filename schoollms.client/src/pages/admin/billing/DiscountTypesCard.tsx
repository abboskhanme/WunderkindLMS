/**
 * "Chegirma turlari" — the discount catalog (docs/modules/billing-plans.md §8). Sits at the top
 * of the Chegirmalar page, above the hand-written discount requests and their approval queue.
 *
 * A catalog type is attached to a student from the student's profile WITHOUT approval (client
 * choice); hand-written discounts below still wait for the director.
 *
 * NO DELETE BUTTON: attached discounts point at a type. A type that is no longer given is
 * ARCHIVED — it cannot be attached any more, the students who already have it keep it.
 * Restoring can hit `discount_type_name_taken` (names are unique among ACTIVE types only);
 * the server's message is shown as is.
 */
import { useCallback, useEffect, useState } from 'react'
import { Archive, ArchiveRestore, BadgePercent, Pencil, Plus, Users } from 'lucide-react'
import type { DiscountType, FeeCategory } from '@/types'
import type {
  DiscountTypeInput,
  DiscountTypeUpdate,
  DiscountTypeUpdateResult,
} from '@/api/services/billingCatalog'
import {
  createDiscountType,
  getDiscountTypes,
  getFeeCategories,
  updateDiscountType,
} from '@/api/services/billingCatalog'
import { billingErrorMessage } from '@/api/services/billingError'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Toast } from '@/components/ui/Toast'
import { AsyncBlock, IconBtn, Notice, StatusPill } from './BillingUi'
import { useBillingAccess } from './access'
import { DiscountTypeFormModal } from './DiscountTypeFormModal'
import { discountValueLabel } from './discountUtils'

/** Active first, then by name. */
function byActiveThenName(a: DiscountType, b: DiscountType): number {
  return Number(b.isActive) - Number(a.isActive) || a.name.localeCompare(b.name, 'uz')
}

function upsert(rows: DiscountType[], type: DiscountType): DiscountType[] {
  const next = rows.some((r) => r.id === type.id)
    ? rows.map((r) => (r.id === type.id ? type : r))
    : [...rows, type]
  return next.sort(byActiveThenName)
}

function savedMessage({ discountType, updated }: DiscountTypeUpdateResult): string {
  if (updated > 0) {
    return `"${discountType.name}" saqlandi: ${updated} ta o'quvchining chegirmasi keyingi oydan yangi qiymatga o'tadi.`
  }
  return `"${discountType.name}" chegirma turi yangilandi.`
}

const scopeLabel = (row: DiscountType) => row.categoryName ?? 'Barcha abonementlar'

export function DiscountTypesCard() {
  const { canManageSubscriptions } = useBillingAccess()

  const [rows, setRows] = useState<DiscountType[]>([])
  const [categories, setCategories] = useState<FeeCategory[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<DiscountType | null>(null)
  const [saving, setSaving] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [togglingId, setTogglingId] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [toast, setToast] = useState<string | null>(null)
  const closeToast = useCallback(() => setToast(null), [])

  const load = useCallback(() => {
    setLoading(true)
    setError(null)
    Promise.all([getDiscountTypes(false), getFeeCategories()])
      .then(([types, cats]) => {
        setRows([...types].sort(byActiveThenName))
        setCategories(cats)
      })
      .catch((e: unknown) => setError(billingErrorMessage(e, "Chegirma turlarini yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [])

  // eslint-disable-next-line react-hooks/set-state-in-effect -- initial load on mount (project-wide pattern)
  useEffect(() => load(), [load])

  const openCreate = () => {
    setEditing(null)
    setFormError(null)
    setActionError(null)
    setFormOpen(true)
  }

  const openEdit = (row: DiscountType) => {
    setEditing(row)
    setFormError(null)
    setActionError(null)
    setFormOpen(true)
  }

  const closeForm = () => {
    setFormOpen(false)
    setEditing(null)
  }

  const handleCreate = async (values: DiscountTypeInput) => {
    setSaving(true)
    setFormError(null)
    try {
      const created = await createDiscountType(values)
      setRows((prev) => upsert(prev, created))
      closeForm()
      setToast(`"${created.name}" chegirma turi qo'shildi.`)
    } catch (e: unknown) {
      setFormError(billingErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  const handleUpdate = async (id: string, values: DiscountTypeUpdate) => {
    setSaving(true)
    setFormError(null)
    try {
      const result = await updateDiscountType(id, values)
      setRows((prev) => upsert(prev, result.discountType))
      closeForm()
      setToast(savedMessage(result))
    } catch (e: unknown) {
      setFormError(billingErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  /** Archive / restore — the same PUT with only `isActive` flipped. */
  const toggleActive = async (row: DiscountType) => {
    const archiving = row.isActive
    if (
      archiving &&
      row.studentCount > 0 &&
      !window.confirm(
        `"${row.name}" arxivlanadi: endi hech kimga biriktirilmaydi, ${row.studentCount} ta o'quvchining chegirmasi esa davom etadi. Davom etasizmi?`,
      )
    ) {
      return
    }
    setTogglingId(row.id)
    setActionError(null)
    try {
      const { discountType } = await updateDiscountType(row.id, {
        name: row.name,
        percent: row.percent,
        amount: row.amount,
        isActive: !archiving,
      })
      setRows((prev) => upsert(prev, discountType))
      setToast(
        archiving
          ? `"${discountType.name}" arxivlandi.`
          : `"${discountType.name}" qayta faollashtirildi.`,
      )
    } catch (e: unknown) {
      setActionError(
        billingErrorMessage(e, archiving ? "Arxivlab bo'lmadi" : "Qayta faollashtirib bo'lmadi"),
      )
    } finally {
      setTogglingId(null)
    }
  }

  const activeCount = rows.filter((r) => r.isActive).length

  const actions = (row: DiscountType) =>
    canManageSubscriptions ? (
      <span className="flex justify-end gap-0.5">
        <IconBtn icon={Pencil} title="Tahrirlash" onClick={() => openEdit(row)} />
        {row.isActive ? (
          <IconBtn
            icon={Archive}
            title="Arxivlash"
            disabled={togglingId === row.id}
            onClick={() => void toggleActive(row)}
          />
        ) : (
          <IconBtn
            icon={ArchiveRestore}
            title="Tiklash"
            disabled={togglingId === row.id}
            onClick={() => void toggleActive(row)}
          />
        )}
      </span>
    ) : (
      <span className="flex justify-end text-xs text-slate-300">—</span>
    )

  const status = (row: DiscountType) =>
    row.isActive ? <StatusPill tone="success">Faol</StatusPill> : <StatusPill>Arxivda</StatusPill>

  const students = (row: DiscountType) => (
    <span
      className={
        row.studentCount > 0
          ? 'inline-flex items-center gap-1.5 text-slate-700'
          : 'inline-flex items-center gap-1.5 text-slate-300'
      }
    >
      <Users className="h-3.5 w-3.5" />
      {row.studentCount}
    </span>
  )

  return (
    <div className="space-y-3">
      {actionError && <Notice>{actionError}</Notice>}

      <Card className="p-0">
        <header className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 px-5 py-3">
          <div>
            <p className="font-medium text-slate-800">Chegirma turlari</p>
            <p className="text-xs text-slate-400">
              {loading || error
                ? "Nomi va miqdori belgilangan chegirmalar — o'quvchiga tasdiqsiz biriktiriladi."
                : `${rows.length} ta tur · ${activeCount} tasi faol · o'quvchiga tasdiqsiz biriktiriladi`}
            </p>
          </div>
          {canManageSubscriptions && (
            <Button onClick={openCreate}>
              <Plus className="h-4 w-4" /> Chegirma turi
            </Button>
          )}
        </header>

        <AsyncBlock
          loading={loading}
          error={error}
          empty={rows.length === 0}
          emptyText="Hozircha birorta chegirma turi yo'q."
          emptyAction={
            canManageSubscriptions ? (
              <Button variant="secondary" onClick={openCreate}>
                <Plus className="h-4 w-4" /> Birinchi chegirma turini qo'shish
              </Button>
            ) : undefined
          }
          onRetry={load}
        >
          {/* Desktop: table */}
          <div className="hidden overflow-x-auto sm:block">
            <table className="w-full text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">Nomi</th>
                  <th className="px-4 py-3">Summa</th>
                  <th className="px-4 py-3">Qaysi abonementga</th>
                  <th className="px-4 py-3 text-right">Bog'langan o'quvchilar</th>
                  <th className="px-4 py-3">Holati</th>
                  <th className="px-4 py-3 text-right">Amallar</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((row) => (
                  <tr key={row.id} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3">
                      <span className="flex items-center gap-3">
                        <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-brand-50 text-brand-600">
                          <BadgePercent className="h-4 w-4" />
                        </span>
                        <span className="font-medium text-slate-800">{row.name}</span>
                      </span>
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 font-medium tabular-nums text-slate-800">
                      {discountValueLabel(row.percent, row.amount)}
                    </td>
                    <td className="px-4 py-3 text-slate-600">{scopeLabel(row)}</td>
                    <td className="px-4 py-3 text-right tabular-nums">{students(row)}</td>
                    <td className="px-4 py-3">{status(row)}</td>
                    <td className="px-4 py-3">{actions(row)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* Mobile: cards */}
          <ul className="divide-y divide-slate-100 sm:hidden">
            {rows.map((row) => (
              <li key={row.id} className="flex items-start gap-3 px-4 py-3">
                <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-brand-50 text-brand-600">
                  <BadgePercent className="h-4 w-4" />
                </span>
                <div className="min-w-0 flex-1">
                  <div className="flex items-start justify-between gap-2">
                    <p className="break-words font-medium text-slate-800">{row.name}</p>
                    {status(row)}
                  </div>
                  <p className="mt-0.5 text-xs text-slate-400">{scopeLabel(row)}</p>
                  <div className="mt-2 flex items-center justify-between gap-2">
                    <div className="text-sm">
                      <p className="font-medium tabular-nums text-slate-800">
                        {discountValueLabel(row.percent, row.amount)}
                      </p>
                      <p className="inline-flex items-center gap-1 text-xs text-slate-500">
                        <Users className="h-3.5 w-3.5" /> {row.studentCount} ta o'quvchi
                      </p>
                    </div>
                    {actions(row)}
                  </div>
                </div>
              </li>
            ))}
          </ul>
        </AsyncBlock>
      </Card>

      {canManageSubscriptions && (
        <DiscountTypeFormModal
          open={formOpen}
          initial={editing}
          categories={categories}
          types={rows}
          busy={saving}
          error={formError}
          onClose={closeForm}
          onCreate={handleCreate}
          onUpdate={handleUpdate}
        />
      )}

      <Toast message={toast} onClose={closeToast} duration={6000} />
    </div>
  )
}
