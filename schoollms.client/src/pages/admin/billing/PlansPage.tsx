/**
 * Abonementlar — named, priced plans and how many students are on each
 * (docs/modules/billing-plans.md). Replaces "To'lov toifalari" in the Moliya settings menu;
 * a category is now the plan's TYPE, the plan carries the price.
 *
 * NO DELETE BUTTON: subscriptions and invoices point at a plan. A plan that is no longer
 * sold is ARCHIVED — it drops out of the subscription form, while the students already on
 * it keep their subscriptions. Restoring can hit `plan_name_taken` (names are unique among
 * ACTIVE plans only); the server's message is shown as is.
 */
import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Archive, ArchiveRestore, Pencil, Plus, Ticket, Users } from 'lucide-react'
import type { FeeCategory, SubscriptionPlan } from '@/types'
import type {
  SubscriptionPlanInput,
  SubscriptionPlanUpdate,
  SubscriptionPlanUpdateResult,
} from '@/api/services/billingCatalog'
import {
  createSubscriptionPlan,
  getFeeCategories,
  getSubscriptionPlans,
  updateSubscriptionPlan,
} from '@/api/services/billingCatalog'
import { billingErrorMessage } from '@/api/services/billingError'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Toast } from '@/components/ui/Toast'
import { formatMoney } from '@/lib/utils'
import { AsyncBlock, BillingGuard, IconBtn, Notice, StatusPill } from './BillingUi'
import { useBillingAccess } from './access'
import { PlanFormModal } from './PlanFormModal'
import { formatDayMonth, scheduleLabel } from './planUtils'

export function PlansPage() {
  return (
    <BillingGuard>
      <PlansView />
    </BillingGuard>
  )
}

/** The server's order: type (category) name, then plan name. */
function byTypeThenName(a: SubscriptionPlan, b: SubscriptionPlan): number {
  return a.categoryName.localeCompare(b.categoryName, 'uz') || a.name.localeCompare(b.name, 'uz')
}

function upsert(rows: SubscriptionPlan[], plan: SubscriptionPlan): SubscriptionPlan[] {
  const next = rows.some((r) => r.id === plan.id)
    ? rows.map((r) => (r.id === plan.id ? plan : r))
    : [...rows, plan]
  return next.sort(byTypeThenName)
}

function savedMessage({ plan, repriced, effectiveFrom }: SubscriptionPlanUpdateResult): string {
  if (repriced > 0 && effectiveFrom) {
    return `"${plan.name}" saqlandi: ${repriced} ta obuna ${formatDayMonth(effectiveFrom)} dan yangi narxga o'tadi.`
  }
  return `"${plan.name}" abonementi yangilandi.`
}

function PlansView() {
  const { canManageSubscriptions } = useBillingAccess()

  const [rows, setRows] = useState<SubscriptionPlan[]>([])
  const [categories, setCategories] = useState<FeeCategory[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<SubscriptionPlan | null>(null)
  const [saving, setSaving] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [togglingId, setTogglingId] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [toast, setToast] = useState<string | null>(null)
  const closeToast = useCallback(() => setToast(null), [])

  const load = useCallback(() => {
    setLoading(true)
    setError(null)
    Promise.all([getSubscriptionPlans(false), getFeeCategories()])
      .then(([plans, cats]) => {
        setRows(plans)
        setCategories(cats)
      })
      .catch((e: unknown) => setError(billingErrorMessage(e, "Abonementlarni yuklab bo'lmadi")))
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

  const openEdit = (row: SubscriptionPlan) => {
    setEditing(row)
    setFormError(null)
    setActionError(null)
    setFormOpen(true)
  }

  const closeForm = () => {
    setFormOpen(false)
    setEditing(null)
  }

  const handleCreate = async (values: SubscriptionPlanInput) => {
    setSaving(true)
    setFormError(null)
    try {
      const created = await createSubscriptionPlan(values)
      setRows((prev) => upsert(prev, created))
      closeForm()
      setToast(`"${created.name}" abonementi qo'shildi.`)
    } catch (e: unknown) {
      setFormError(billingErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  const handleUpdate = async (id: string, values: SubscriptionPlanUpdate) => {
    setSaving(true)
    setFormError(null)
    try {
      const result = await updateSubscriptionPlan(id, values)
      setRows((prev) => upsert(prev, result.plan))
      closeForm()
      setToast(savedMessage(result))
    } catch (e: unknown) {
      setFormError(billingErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  /** Archive / restore — the same PUT with only `isActive` flipped. */
  const toggleActive = async (row: SubscriptionPlan) => {
    const archiving = row.isActive
    if (
      archiving &&
      row.studentCount > 0 &&
      !window.confirm(
        `"${row.name}" arxivlanadi: yangi obunada tanlanmaydi, ${row.studentCount} ta o'quvchining obunasi esa davom etadi. Davom etasizmi?`,
      )
    ) {
      return
    }
    setTogglingId(row.id)
    setActionError(null)
    try {
      const { plan } = await updateSubscriptionPlan(row.id, {
        name: row.name,
        monthlyAmount: row.monthlyAmount,
        isActive: !archiving,
        // Always sent: the PUT treats null as "Butun oy", so the current schedule goes back as is.
        daysPerWeek: row.daysPerWeek ?? null,
      })
      setRows((prev) => upsert(prev, plan))
      setToast(archiving ? `"${plan.name}" arxivlandi.` : `"${plan.name}" qayta faollashtirildi.`)
    } catch (e: unknown) {
      setActionError(
        billingErrorMessage(e, archiving ? "Arxivlab bo'lmadi" : "Qayta faollashtirib bo'lmadi"),
      )
    } finally {
      setTogglingId(null)
    }
  }

  const activeCount = rows.filter((r) => r.isActive).length

  const actions = (row: SubscriptionPlan) =>
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

  const status = (row: SubscriptionPlan) =>
    row.isActive ? <StatusPill tone="success">Faol</StatusPill> : <StatusPill>Arxivda</StatusPill>

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Abonementlar</h1>
          <p className="text-sm text-slate-400">
            Jami {rows.length} ta abonement · {activeCount} tasi faol
          </p>
        </div>
        {canManageSubscriptions && (
          <Button onClick={openCreate}>
            <Plus className="h-4 w-4" /> Abonement
          </Button>
        )}
      </div>

      {actionError && <Notice>{actionError}</Notice>}

      <Card className="p-0">
        <AsyncBlock
          loading={loading}
          error={error}
          empty={rows.length === 0}
          emptyText="Hozircha birorta abonement yo'q."
          emptyAction={
            canManageSubscriptions ? (
              <Button variant="secondary" onClick={openCreate}>
                <Plus className="h-4 w-4" /> Birinchi abonementni qo'shish
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
                  <th className="px-4 py-3">№</th>
                  <th className="px-4 py-3">Nomi</th>
                  <th className="px-4 py-3">Turi</th>
                  <th className="px-4 py-3">Jadval</th>
                  <th className="px-4 py-3 text-right">Oylik narxi</th>
                  <th className="px-4 py-3 text-right">O'quvchilar</th>
                  <th className="px-4 py-3">Holati</th>
                  <th className="px-4 py-3 text-right">Amallar</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((row, idx) => (
                  <tr key={row.id} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3 text-slate-500">{idx + 1}</td>
                    {/* min width: a name like "5-11 sinflar 1 590 000" must not be squeezed into four lines */}
                    <td className="min-w-[16rem] px-4 py-3">
                      <span className="flex items-center gap-3">
                        <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-brand-50 text-brand-600">
                          <Ticket className="h-4 w-4" />
                        </span>
                        <span className="font-medium leading-snug text-slate-800">{row.name}</span>
                      </span>
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-slate-600">{row.categoryName}</td>
                    <td className="whitespace-nowrap px-4 py-3 text-slate-600">
                      {scheduleLabel(row.daysPerWeek)}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-right tabular-nums text-slate-800">
                      {formatMoney(row.monthlyAmount)}
                    </td>
                    <td className="px-4 py-3 text-right tabular-nums">
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
                    </td>
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
                  <Ticket className="h-4 w-4" />
                </span>
                <div className="min-w-0 flex-1">
                  <div className="flex items-start justify-between gap-2">
                    <p className="break-words font-medium text-slate-800">{row.name}</p>
                    {status(row)}
                  </div>
                  <p className="mt-0.5 text-xs text-slate-400">
                    {row.categoryName} · {scheduleLabel(row.daysPerWeek)}
                  </p>
                  <div className="mt-2 flex items-center justify-between gap-2">
                    <div className="text-sm">
                      <p className="font-medium tabular-nums text-slate-800">
                        {formatMoney(row.monthlyAmount)}
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

      <p className="text-xs text-slate-400">
        Abonement o'chirilmaydi — unga obunalar bog'langan. Sotilmay qolganini arxivlang: yangi
        obunada tanlanmaydi, undagi o'quvchilarning obunasi esa davom etadi. Abonement turlari
        (O'qish, Avtobus, Yotoqxona, …){' '}
        <Link
          to="/admin/billing/settings?tab=categories"
          className="font-medium text-brand-600 hover:text-brand-700"
        >
          To'lov toifalari
        </Link>{' '}
        sahifasida.
      </p>

      {canManageSubscriptions && (
        <PlanFormModal
          open={formOpen}
          initial={editing}
          categories={categories}
          plans={rows}
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
