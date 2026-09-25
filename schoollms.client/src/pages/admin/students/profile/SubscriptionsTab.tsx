import { useCallback, useEffect, useMemo, useState } from 'react'
import { CalendarOff, Pencil, Plus } from 'lucide-react'
import type { FeeCategory } from '@/types'
import type {
  SubscriptionInput,
  SubscriptionRecord,
  SubscriptionUpdate,
} from '@/api/services/billingCatalog'
import {
  createSubscription,
  endSubscription,
  getFeeCategories,
  getSubscriptions,
  updateSubscription,
} from '@/api/services/billingCatalog'
import { billingErrorMessage } from '@/api/services/billingError'
import { voidInvoice } from '@/api/services/invoices'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { formatMoney } from '@/lib/utils'
import { AsyncBlock, IconBtn, Notice, StatusPill } from '@/pages/admin/billing/BillingUi'
import { EndSubscriptionModal } from '@/pages/admin/billing/EndSubscriptionModal'
import { SubscriptionFormModal } from '@/pages/admin/billing/SubscriptionFormModal'
import type { StudentOption } from '@/pages/admin/billing/useStudents'

interface Props {
  studentId: string
  studentName: string
  className: string
  /** Obuna o'zgargach "To'lovlar" tab'i yangidan yuklanishi uchun. */
  onChanged?: () => void
}

/**
 * O'quvchi kartochkasining "Abonementlar" tab'i — shu bolaning obunalari
 * (ovqat, avtobus, yotoqxona, ...) va ularni boshqarish: ochish, narx /
 * tafsilotni tahrirlash, yopish.
 *
 * `billing/SubscriptionsPage` ning bitta o'quvchiga toraytirilgani: o'sha
 * API, o'sha modallar, o'sha yopish tartibi (avval tanlangan kelajak oylar
 * `voidInvoice` bilan ketma-ket bekor qilinadi, keyin obuna yopiladi).
 * Ruxsat serverda (`Roles.FinanceStaff`); tab'ni sahifa faqat moliya
 * rollariga ko'rsatadi.
 */
export function SubscriptionsTab({ studentId, studentName, className, onChanged }: Props) {
  const [rows, setRows] = useState<SubscriptionRecord[]>([])
  const [categories, setCategories] = useState<FeeCategory[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [activeOnly, setActiveOnly] = useState(false)

  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<SubscriptionRecord | null>(null)
  const [ending, setEnding] = useState<SubscriptionRecord | null>(null)
  const [busy, setBusy] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  // Forma o'quvchi tanlovini kutadi — bu yerda u bitta va oldindan tanlangan.
  const self = useMemo<StudentOption[]>(
    () => [{ id: studentId, fullName: studentName, className, search: '' }],
    [studentId, studentName, className],
  )

  const load = useCallback(() => {
    setLoading(true)
    setError(null)
    Promise.all([getSubscriptions({ studentId, activeOnly }), getFeeCategories()])
      .then(([subs, cats]) => {
        setRows(
          [...subs].sort(
            (a, b) =>
              Number(b.isActive) - Number(a.isActive) ||
              a.categoryName.localeCompare(b.categoryName, 'uz'),
          ),
        )
        setCategories(cats)
      })
      .catch((e: unknown) => setError(billingErrorMessage(e, "Abonementlarni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [studentId, activeOnly])

  // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr o'zgarganda qayta yuklash (SubscriptionsPage bilan bir xil naqsh)
  useEffect(() => load(), [load])

  const changed = () => {
    load()
    onChanged?.()
  }

  const openCreate = () => {
    setEditing(null)
    setActionError(null)
    setFormOpen(true)
  }

  const openEdit = (row: SubscriptionRecord) => {
    setEditing(row)
    setActionError(null)
    setFormOpen(true)
  }

  const handleCreate = async (values: SubscriptionInput) => {
    setBusy(true)
    setActionError(null)
    try {
      const created = await createSubscription(values)
      setFormOpen(false)
      setNotice(`"${created.categoryName}" abonementi ochildi.`)
      changed()
    } catch (e: unknown) {
      setActionError(billingErrorMessage(e, "Abonementni ochib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const handleUpdate = async (id: string, values: SubscriptionUpdate) => {
    setBusy(true)
    setActionError(null)
    try {
      const updated = await updateSubscription(id, values)
      setFormOpen(false)
      setEditing(null)
      setNotice(`"${updated.categoryName}" abonementi yangilandi.`)
      changed()
    } catch (e: unknown) {
      setActionError(billingErrorMessage(e, "Abonementni saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  /** `SubscriptionsPage.handleEnd` bilan bir xil: biror bekor qilish rad etilsa — to'xtaydi. */
  const handleEnd = async (id: string, endsOn: string, voidInvoiceIds: string[], reason: string) => {
    setBusy(true)
    setActionError(null)
    try {
      for (const invoiceId of voidInvoiceIds) {
        await voidInvoice(invoiceId, reason)
      }
      const ended = await endSubscription(id, endsOn)
      setEnding(null)
      const voidedNote =
        voidInvoiceIds.length > 0 ? ` (${voidInvoiceIds.length} ta kelajak oy bekor qilindi)` : ''
      setNotice(`"${ended.categoryName}" abonementi ${endsOn} sanasida yopildi${voidedNote}.`)
      changed()
    } catch (e: unknown) {
      setActionError(billingErrorMessage(e, "Abonementni yopib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="space-y-4">
      {notice && <Notice tone="success">{notice}</Notice>}
      {actionError && !formOpen && !ending && <Notice>{actionError}</Notice>}

      <Card className="p-0">
        <header className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 px-5 py-3">
          <div>
            <p className="font-medium text-slate-800">Abonementlar</p>
            <p className="text-xs text-slate-400">{rows.length} ta</p>
          </div>
          <div className="flex flex-wrap items-center gap-3">
            <label className="flex cursor-pointer items-center gap-2 text-sm text-slate-600">
              <input
                type="checkbox"
                checked={activeOnly}
                onChange={(e) => setActiveOnly(e.target.checked)}
                className="h-4 w-4 rounded border-slate-300 accent-brand-600"
              />
              Faqat amaldagilar
            </label>
            <Button onClick={openCreate}>
              <Plus className="h-4 w-4" /> Abonement qo'shish
            </Button>
          </div>
        </header>

        <AsyncBlock
          loading={loading}
          error={error}
          empty={rows.length === 0}
          emptyText={activeOnly ? "Amaldagi abonement yo'q." : "Bu o'quvchida hali abonement yo'q."}
          onRetry={load}
        >
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-2">Toifa</th>
                  <th className="px-4 py-2 text-right">Oylik summa</th>
                  <th className="px-4 py-2">Tafsilot</th>
                  <th className="px-4 py-2">Boshlanish</th>
                  <th className="px-4 py-2">Tugash</th>
                  <th className="px-4 py-2">Holat</th>
                  <th className="px-4 py-2 text-right">Amal</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((row) => (
                  <tr key={row.id} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3 font-medium text-slate-800">{row.categoryName}</td>
                    <td className="px-4 py-3 text-right tabular-nums text-slate-800">
                      {formatMoney(row.monthlyAmount)}
                    </td>
                    <td className="px-4 py-3 text-slate-600">{row.detail || '—'}</td>
                    <td className="whitespace-nowrap px-4 py-3 text-slate-600">{row.startsOn}</td>
                    <td className="whitespace-nowrap px-4 py-3 text-slate-600">{row.endsOn ?? 'muddatsiz'}</td>
                    <td className="px-4 py-3">
                      {row.isActive ? (
                        <StatusPill tone="success">Amalda</StatusPill>
                      ) : (
                        <StatusPill>Yopilgan</StatusPill>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      <span className="flex justify-end gap-0.5">
                        <IconBtn icon={Pencil} title="Narx / tafsilotni tahrirlash" onClick={() => openEdit(row)} />
                        {row.isActive && (
                          <IconBtn
                            icon={CalendarOff}
                            title="Abonementni yopish"
                            tone="danger"
                            onClick={() => {
                              setActionError(null)
                              setEnding(row)
                            }}
                          />
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

      <SubscriptionFormModal
        open={formOpen}
        initial={editing}
        categories={categories}
        students={self}
        studentsLoading={false}
        studentsError={null}
        presetStudentId={studentId}
        lockStudent
        busy={busy}
        error={actionError}
        onClose={() => {
          setFormOpen(false)
          setEditing(null)
        }}
        onCreate={handleCreate}
        onUpdate={handleUpdate}
      />
      <EndSubscriptionModal
        open={ending !== null}
        subscription={ending}
        busy={busy}
        error={actionError}
        onClose={() => setEnding(null)}
        onConfirm={handleEnd}
      />
    </div>
  )
}
