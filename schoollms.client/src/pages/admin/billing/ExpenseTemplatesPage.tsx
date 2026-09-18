/**
 * Rejalashtirilgan chiqim shablonlari — F6.01 (finance-parity.md §2.6.3, §2.6.1
 * `planned` tab). `BillingSettingsPage.tsx` ning kataloglar markazida beshinchi
 * bo'lim sifatida ochiladi (`/admin/billing/settings?tab=expense-templates`).
 *
 * BU YERDA HAQIQIY CHIQIM YOZILMAYDI. Shablon faqat (1) direktorga har oyning
 * belgilangan kunida Telegram eslatmasi yuboradi
 * (`ExpenseTemplateReminderService`) va (2) P&L 2.0'ning `planned` tabida
 * "rejalashtirilgan" qator sifatida ko'rinadi. Haqiqiy xarajat hamon
 * "Chiqimlar" ekranida (`ExpensesPage.tsx`), qo'lda, ikki qavatli nazorat
 * bilan yoziladi.
 *
 * TO'LIQ CRUD, TO'LOV TOIFALARIDAN FARQLI: shablon HAQIQATAN o'chiriladi
 * (server ham xuddi shunday — `expense_templates_guards.sql`). `CategoriesPage`
 * dagi "o'chirish yo'q, faqat faolsizlantirish" qoidasi bu yerga tegishli
 * emas: bu yerda obuna ham, hisob-faktura ham, daromad hisobi ham bog'lanmagan
 * — shablonni o'chirish hech qanday tarixni yo'qotmaydi.
 */
import { useCallback, useEffect, useState } from 'react'
import { CalendarClock, Pencil, Trash2, Plus } from 'lucide-react'
import type { ExpenseTemplate, ExpenseTemplateInput } from '@/api/services/expenseTemplates'
import {
  createExpenseTemplate,
  deleteExpenseTemplate,
  getExpenseTemplates,
  updateExpenseTemplate,
} from '@/api/services/expenseTemplates'
import { billingErrorMessage } from '@/api/services/billingError'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { formatMoney } from '@/lib/utils'
import { AsyncBlock, BillingGuard, IconBtn, Notice, StatusPill } from './BillingUi'
import { useBillingAccess } from './access'
import { ExpenseTemplateFormModal } from './ExpenseTemplateFormModal'

export function ExpenseTemplatesPage() {
  return (
    <BillingGuard>
      <ExpenseTemplatesView />
    </BillingGuard>
  )
}

function ExpenseTemplatesView() {
  const { canManageExpenseTemplates } = useBillingAccess()

  const [rows, setRows] = useState<ExpenseTemplate[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<ExpenseTemplate | null>(null)
  const [saving, setSaving] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [pageNotice, setPageNotice] = useState<string | null>(null)
  const [deletingId, setDeletingId] = useState<string | null>(null)

  const load = useCallback(() => {
    setLoading(true)
    setError(null)
    getExpenseTemplates()
      .then(setRows)
      .catch((e: unknown) => setError(billingErrorMessage(e, "Shablonlarni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [])

  // eslint-disable-next-line react-hooks/set-state-in-effect -- sahifa ochilganda birinchi yuklash (loyihadagi umumiy naqsh)
  useEffect(() => load(), [load])

  const openCreate = () => {
    setEditing(null)
    setFormError(null)
    setFormOpen(true)
  }

  const openEdit = (row: ExpenseTemplate) => {
    setEditing(row)
    setFormError(null)
    setFormOpen(true)
  }

  const handleSubmit = async (values: ExpenseTemplateInput) => {
    setSaving(true)
    setFormError(null)
    try {
      if (editing) {
        const updated = await updateExpenseTemplate(editing.id, values)
        setRows((prev) => prev.map((r) => (r.id === updated.id ? updated : r)))
        setPageNotice(`"${updated.name}" shabloni yangilandi.`)
      } else {
        const created = await createExpenseTemplate(values)
        setRows((prev) => [...prev, created].sort((a, b) => a.name.localeCompare(b.name)))
        setPageNotice(`"${created.name}" shabloni qo'shildi.`)
      }
      setFormOpen(false)
      setEditing(null)
    } catch (e: unknown) {
      setFormError(billingErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  const handleDelete = async (row: ExpenseTemplate) => {
    if (!window.confirm(`"${row.name}" shabloni o'chiriladi. Davom etasizmi?`)) return
    setDeletingId(row.id)
    setPageNotice(null)
    try {
      await deleteExpenseTemplate(row.id)
      setRows((prev) => prev.filter((r) => r.id !== row.id))
      setPageNotice(`"${row.name}" shabloni o'chirildi.`)
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
          <h1 className="text-xl font-semibold text-slate-800">Rejalashtirilgan chiqimlar</h1>
          <p className="text-sm text-slate-400">
            Jami {rows.length} ta shablon · {activeCount} tasi faol — har biri o'z kunida
            direktorga Telegram eslatmasini yuboradi.
          </p>
        </div>
        {canManageExpenseTemplates && (
          <Button onClick={openCreate}>
            <Plus className="h-4 w-4" /> Yangi shablon
          </Button>
        )}
      </div>

      {pageNotice && <Notice tone="success">{pageNotice}</Notice>}

      <Card className="p-0">
        <AsyncBlock
          loading={loading}
          error={error}
          empty={rows.length === 0}
          emptyText="Hozircha birorta rejalashtirilgan chiqim shabloni yo'q."
          emptyAction={
            canManageExpenseTemplates ? (
              <Button variant="secondary" onClick={openCreate}>
                <Plus className="h-4 w-4" /> Birinchi shablonni qo'shish
              </Button>
            ) : undefined
          }
          onRetry={load}
        >
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">Nomi</th>
                  <th className="px-4 py-3">Toifa</th>
                  <th className="px-4 py-3">Summa</th>
                  <th className="px-4 py-3">Kun</th>
                  <th className="px-4 py-3">Holat</th>
                  <th className="px-4 py-3 text-right">Amallar</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((row) => (
                  <tr key={row.id} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3">
                      <span className="flex items-center gap-3">
                        <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-brand-50 text-brand-600">
                          <CalendarClock className="h-4 w-4" />
                        </span>
                        <span className="font-medium text-slate-800">{row.name}</span>
                      </span>
                    </td>
                    <td className="px-4 py-3 text-slate-600">{row.categoryName}</td>
                    <td className="px-4 py-3 font-medium tabular-nums text-slate-700">
                      {formatMoney(row.amount)}
                    </td>
                    <td className="px-4 py-3 text-slate-600">har oyning {row.dayOfMonth}-kuni</td>
                    <td className="px-4 py-3">
                      {row.isActive ? (
                        <StatusPill tone="success">Faol</StatusPill>
                      ) : (
                        <StatusPill>Faolsiz</StatusPill>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      <span className="flex justify-end gap-1">
                        {canManageExpenseTemplates ? (
                          <>
                            <IconBtn icon={Pencil} title="Tahrirlash" onClick={() => openEdit(row)} />
                            <IconBtn
                              icon={Trash2}
                              title="O'chirish"
                              tone="danger"
                              disabled={deletingId === row.id}
                              onClick={() => void handleDelete(row)}
                            />
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

      {canManageExpenseTemplates && (
        <ExpenseTemplateFormModal
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
