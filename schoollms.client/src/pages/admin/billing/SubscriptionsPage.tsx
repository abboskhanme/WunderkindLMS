/**
 * O'quvchi obunalari — kim nimaga yozilgan va qancha to'laydi (P1-17).
 *
 * Ro'yxat O'QUVCHI bo'yicha guruhlangan, ichida esa TOIFA bo'yicha qatorlar:
 * summa, tafsilot (avtobus yo'nalishi / yotoqxona xonasi), boshlanish va
 * tugash sanasi. Bu P1-17 ning aynan birinchi qabul mezoni.
 *
 * NEGA "JAMI OYLIK" USTUNI YO'Q: `BillingDtos.cs` ning 2-qoidasi — pulni
 * frontend hisoblamaydi. Obunalar yig'indisi kerak bo'lganda uni server
 * beradi (o'quvchining moliyaviy kartochkasi, P1-09), bu yerda `reduce`
 * bilan yasalmaydi.
 */
import { useCallback, useEffect, useMemo, useState } from 'react'
import { CalendarOff, Pencil, Plus, Users } from 'lucide-react'
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
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Select } from '@/components/ui/Input'
import { formatMoney } from '@/lib/utils'
import { AsyncBlock, BillingGuard, IconBtn, Notice, StatusPill } from './BillingUi'
import { useBillingAccess } from './access'
import { EndSubscriptionModal } from './EndSubscriptionModal'
import { StudentSelect } from './StudentSelect'
import { SubscriptionFormModal } from './SubscriptionFormModal'
import { useStudentsIndex } from './useStudents'

export function SubscriptionsPage() {
  return (
    <BillingGuard>
      <SubscriptionsView />
    </BillingGuard>
  )
}

interface StudentGroup {
  studentId: string
  studentName: string
  rows: SubscriptionRecord[]
}

function groupByStudent(rows: SubscriptionRecord[]): StudentGroup[] {
  const map = new Map<string, StudentGroup>()
  for (const row of rows) {
    const group = map.get(row.studentId)
    if (group) group.rows.push(row)
    else map.set(row.studentId, { studentId: row.studentId, studentName: row.studentName, rows: [row] })
  }
  return [...map.values()]
    .map((g) => ({ ...g, rows: [...g.rows].sort((a, b) => a.categoryName.localeCompare(b.categoryName, 'uz')) }))
    .sort((a, b) => a.studentName.localeCompare(b.studentName, 'uz'))
}

function SubscriptionsView() {
  const { canManageSubscriptions } = useBillingAccess()
  const students = useStudentsIndex()

  const [rows, setRows] = useState<SubscriptionRecord[]>([])
  const [categories, setCategories] = useState<FeeCategory[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [studentFilter, setStudentFilter] = useState('')
  const [categoryFilter, setCategoryFilter] = useState('')
  const [activeOnly, setActiveOnly] = useState(true)

  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<SubscriptionRecord | null>(null)
  const [presetStudentId, setPresetStudentId] = useState<string | undefined>(undefined)
  const [ending, setEnding] = useState<SubscriptionRecord | null>(null)
  const [busy, setBusy] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const load = useCallback(() => {
    setLoading(true)
    setError(null)
    Promise.all([
      getSubscriptions({
        studentId: studentFilter || undefined,
        categoryId: categoryFilter || undefined,
        activeOnly,
      }),
      getFeeCategories(),
    ])
      .then(([subs, cats]) => {
        setRows(subs)
        setCategories(cats)
      })
      .catch((e: unknown) => setError(billingErrorMessage(e, "Obunalarni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [studentFilter, categoryFilter, activeOnly])

  // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr o'zgarganda qayta yuklash (FinancePage bilan bir xil naqsh)
  useEffect(() => load(), [load])

  const groups = useMemo(() => groupByStudent(rows), [rows])

  const openCreate = (studentId?: string) => {
    setEditing(null)
    setPresetStudentId(studentId)
    setActionError(null)
    setFormOpen(true)
  }

  const openEdit = (row: SubscriptionRecord) => {
    setEditing(row)
    setPresetStudentId(undefined)
    setActionError(null)
    setFormOpen(true)
  }

  const handleCreate = async (values: SubscriptionInput) => {
    setBusy(true)
    setActionError(null)
    try {
      const created = await createSubscription(values)
      setFormOpen(false)
      setNotice(`${created.studentName} — "${created.categoryName}" obunasi ochildi.`)
      load()
    } catch (e: unknown) {
      setActionError(billingErrorMessage(e, "Obunani ochib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const handleUpdate = async (id: string, values: SubscriptionUpdate) => {
    setBusy(true)
    setActionError(null)
    try {
      const updated = await updateSubscription(id, values)
      setRows((prev) => prev.map((r) => (r.id === updated.id ? updated : r)))
      setFormOpen(false)
      setEditing(null)
      setNotice(`${updated.studentName} — "${updated.categoryName}" obunasi yangilandi.`)
    } catch (e: unknown) {
      setActionError(billingErrorMessage(e, "Obunani saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  /**
   * F1.06 — obunani yopish + (agar tanlangan bo'lsa) kelajakdagi oylarni
   * bekor qilish. Ikkalasi ATAYLAB ikkita mustaqil server yo'li: avval har
   * bir tanlangan hisob-faktura `voidInvoice` bilan KETMA-KET bekor
   * qilinadi (har biri o'zining advisory qulfi va ikki qavatli nazorati
   * bilan — F10.02, `InvoiceService.VoidAsync`), so'ng obunaning o'zi
   * yopiladi. Birortasi rad etilsa — TO'XTAYDI, keyingisiga o'tmaydi va
   * obunani ham yopmaydi: admin xatoni ko'rib, kerak bo'lsa qayta uradi
   * (allaqachon bekor qilinganlari serverda idempotent — "already_void").
   */
  const handleEnd = async (
    id: string,
    endsOn: string,
    voidInvoiceIds: string[],
    reason: string,
  ) => {
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
      setNotice(
        `${ended.studentName} — "${ended.categoryName}" obunasi ${endsOn} sanasida yopildi${voidedNote}.`,
      )
      load()
    } catch (e: unknown) {
      setActionError(billingErrorMessage(e, "Obunani yopib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">O'quvchi obunalari</h1>
          <p className="text-sm text-slate-400">
            {groups.length} o'quvchi · {rows.length} ta obuna
          </p>
        </div>
        {canManageSubscriptions && (
          <Button onClick={() => openCreate()}>
            <Plus className="h-4 w-4" /> Yangi obuna
          </Button>
        )}
      </div>

      {notice && <Notice tone="success">{notice}</Notice>}
      {actionError && !formOpen && !ending && <Notice>{actionError}</Notice>}

      <Card>
        <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
          <StudentSelect
            options={students.options}
            loading={students.loading}
            error={students.error}
            value={studentFilter}
            onChange={setStudentFilter}
            label="O'quvchi bo'yicha"
            emptyLabel="Barcha o'quvchilar"
          />
          <Select
            label="Toifa bo'yicha"
            value={categoryFilter}
            onChange={(e) => setCategoryFilter(e.target.value)}
          >
            <option value="">Barcha toifalar</option>
            {categories.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </Select>
          <label className="flex cursor-pointer items-center gap-2 self-end pb-2 text-sm font-medium text-slate-700">
            <input
              type="checkbox"
              checked={activeOnly}
              onChange={(e) => setActiveOnly(e.target.checked)}
              className="h-4 w-4 rounded border-slate-300 accent-brand-600"
            />
            Faqat amaldagi obunalar
          </label>
        </div>
      </Card>

      <AsyncBlock
        framed
        loading={loading}
        error={error}
        empty={groups.length === 0}
        emptyText={
          activeOnly
            ? "Tanlangan shart bo'yicha amaldagi obuna topilmadi."
            : "Tanlangan shart bo'yicha obuna topilmadi."
        }
        emptyAction={
          canManageSubscriptions ? (
            <Button variant="secondary" onClick={() => openCreate()}>
              <Plus className="h-4 w-4" /> Obuna ochish
            </Button>
          ) : undefined
        }
        onRetry={load}
      >
        <div className="space-y-4">
          {groups.map((group) => (
            <Card key={group.studentId} className="p-0">
              <header className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-100 px-5 py-3">
                <div className="flex items-center gap-3">
                  <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-brand-50 text-brand-600">
                    <Users className="h-4 w-4" />
                  </span>
                  <div>
                    <p className="font-medium text-slate-800">{group.studentName}</p>
                    <p className="text-xs text-slate-400">{group.rows.length} ta obuna</p>
                  </div>
                </div>
                {canManageSubscriptions && (
                  <Button variant="secondary" onClick={() => openCreate(group.studentId)}>
                    <Plus className="h-4 w-4" /> Obuna qo'shish
                  </Button>
                )}
              </header>

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
                    {group.rows.map((row) => (
                      <tr key={row.id} className="hover:bg-slate-50/60">
                        <td className="px-4 py-3 font-medium text-slate-800">{row.categoryName}</td>
                        <td className="px-4 py-3 text-right tabular-nums text-slate-800">
                          {formatMoney(row.monthlyAmount)}
                        </td>
                        <td className="px-4 py-3 text-slate-600">{row.detail || '—'}</td>
                        <td className="px-4 py-3 text-slate-600">{row.startsOn}</td>
                        <td className="px-4 py-3 text-slate-600">{row.endsOn ?? 'muddatsiz'}</td>
                        <td className="px-4 py-3">
                          {row.isActive ? (
                            <StatusPill tone="success">Amalda</StatusPill>
                          ) : (
                            <StatusPill>Yopilgan</StatusPill>
                          )}
                        </td>
                        <td className="px-4 py-3">
                          <span className="flex justify-end gap-0.5">
                            {canManageSubscriptions ? (
                              <>
                                <IconBtn
                                  icon={Pencil}
                                  title="Narx / tafsilotni tahrirlash"
                                  onClick={() => openEdit(row)}
                                />
                                {row.isActive && (
                                  <IconBtn
                                    icon={CalendarOff}
                                    title="Obunani yopish"
                                    tone="danger"
                                    onClick={() => {
                                      setActionError(null)
                                      setEnding(row)
                                    }}
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
            </Card>
          ))}
        </div>
      </AsyncBlock>

      {canManageSubscriptions && (
        <>
          <SubscriptionFormModal
            open={formOpen}
            initial={editing}
            categories={categories}
            students={students.options}
            studentsLoading={students.loading}
            studentsError={students.error}
            presetStudentId={presetStudentId}
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
        </>
      )}
    </div>
  )
}
