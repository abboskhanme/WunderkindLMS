/**
 * Chegirmalar va TASDIQ NAVBATI (P1-17).
 *
 * MIJOZ QARORI — SPEC §8.1 Q5: chegara YO'Q. Har qanday chegirma direktor
 * tasdig'ini talab qiladi va `pending` holatda tug'iladi. Shuning uchun bu
 * sahifa "chegaradan oshdi" degan ISTISNO holatini emas, DOIMIY navbatni
 * ko'rsatadi: navbat sahifaning eng tepasida, qolgan hamma narsadan oldin.
 *
 * IKKI QAVATLI NAZORAT (SPEC §4.5): tasdiqlash/rad etish tugmalari
 * chegirmani SO'RAGAN odamga KO'RSATILMAYDI. Server ham buni rad etadi
 * (`self_approval`, `ck_discounts_approver_differs`), lekin interfeys uni
 * taklif qilmasligi kerak — mavjud bo'lmagan huquqni ko'rsatgan tugma
 * foydalanuvchini chalg'itadi.
 */
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Check, Clock, Plus, ShieldCheck, X } from 'lucide-react'
import type { DiscountStatus, FeeCategory } from '@/types'
import type { DiscountInput, DiscountRecord } from '@/api/services/billingCatalog'
import {
  approveDiscount,
  createDiscount,
  getDiscounts,
  getFeeCategories,
  getPendingDiscounts,
  rejectDiscount,
} from '@/api/services/billingCatalog'
import { billingErrorMessage } from '@/api/services/billingError'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Select } from '@/components/ui/Input'
import { formatMoney } from '@/lib/utils'
import { AsyncBlock, BillingGuard, Notice, PendingQueue, StatusPill } from './BillingUi'
import { useBillingAccess } from './access'
import { DiscountFormModal } from './DiscountFormModal'
import { ReasonModal } from './ReasonModal'
import { StudentSelect } from './StudentSelect'
import { useStudentsIndex } from './useStudents'

const statusLabels: Record<DiscountStatus, string> = {
  pending: 'Tasdiq kutmoqda',
  approved: 'Tasdiqlangan',
  rejected: 'Rad etilgan',
}

const statusTones: Record<DiscountStatus, 'warning' | 'success' | 'danger'> = {
  pending: 'warning',
  approved: 'success',
  rejected: 'danger',
}

/** "20% + 50 000 so'm" ko'rinishi. */
function discountLabel(percent: number, amount: number): string {
  const parts: string[] = []
  if (percent > 0) parts.push(`${percent}%`)
  if (amount > 0) parts.push(formatMoney(amount))
  return parts.length > 0 ? parts.join(' + ') : '—'
}

function periodLabel(startsOn: string, endsOn?: string): string {
  return endsOn ? `${startsOn} — ${endsOn}` : `${startsOn} — muddatsiz`
}

export function DiscountsPage() {
  return (
    <BillingGuard>
      <DiscountsView />
    </BillingGuard>
  )
}

function DiscountsView() {
  const { canGrantDiscount, canApproveDiscount, canApproveRecord, isOwn } = useBillingAccess()
  const students = useStudentsIndex()

  const [rows, setRows] = useState<DiscountRecord[]>([])
  const [pending, setPending] = useState<DiscountRecord[]>([])
  const [categories, setCategories] = useState<FeeCategory[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [studentFilter, setStudentFilter] = useState('')
  const [statusFilter, setStatusFilter] = useState<'' | DiscountStatus>('')

  const [formOpen, setFormOpen] = useState(false)
  const [rejecting, setRejecting] = useState<DiscountRecord | null>(null)
  const [busy, setBusy] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const load = useCallback(() => {
    setLoading(true)
    setError(null)
    Promise.all([
      getDiscounts({
        studentId: studentFilter || undefined,
        status: statusFilter || undefined,
      }),
      getPendingDiscounts(),
      getFeeCategories(),
    ])
      .then(([list, queue, cats]) => {
        setRows(list)
        setPending(queue)
        setCategories(cats)
      })
      .catch((e: unknown) => setError(billingErrorMessage(e, "Chegirmalarni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [studentFilter, statusFilter])

  // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr o'zgarganda qayta yuklash (FinancePage bilan bir xil naqsh)
  useEffect(() => load(), [load])

  const approvableCount = useMemo(
    () => pending.filter((d) => canApproveRecord(canApproveDiscount, d)).length,
    [pending, canApproveDiscount, canApproveRecord],
  )

  const handleCreate = async (values: DiscountInput) => {
    setBusy(true)
    setActionError(null)
    try {
      const created = await createDiscount(values)
      setFormOpen(false)
      setNotice(
        `${created.studentName} uchun chegirma tasdiq navbatiga qo'shildi. ` +
          "Direktor tasdiqlamaguncha hisob-kitobga ta'sir qilmaydi.",
      )
      load()
    } catch (e: unknown) {
      setActionError(billingErrorMessage(e, "Chegirmani yuborib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const handleApprove = async (row: DiscountRecord) => {
    setBusy(true)
    setActionError(null)
    setNotice(null)
    try {
      const approved = await approveDiscount(row.id)
      setNotice(`${approved.studentName} chegirmasi tasdiqlandi — endi hisob-kitobga kiradi.`)
      load()
    } catch (e: unknown) {
      setActionError(billingErrorMessage(e, "Tasdiqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const handleReject = async (reason: string) => {
    if (!rejecting) return
    setBusy(true)
    setActionError(null)
    setNotice(null)
    try {
      const rejected = await rejectDiscount(rejecting.id, reason)
      setRejecting(null)
      setNotice(`${rejected.studentName} chegirmasi rad etildi.`)
      load()
    } catch (e: unknown) {
      setActionError(billingErrorMessage(e, "Rad etib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const renderDecision = (row: DiscountRecord) => {
    if (row.status !== 'pending') {
      return (
        <span className="text-xs text-slate-500">
          {row.approvedByName ? row.approvedByName : '—'}
          {row.decidedAt && (
            <span className="block text-slate-400">{row.decidedAt.slice(0, 10)}</span>
          )}
        </span>
      )
    }

    // IKKI QAVATLI NAZORAT: so'ragan odamga tugma KO'RSATILMAYDI.
    if (canApproveRecord(canApproveDiscount, row)) {
      return (
        <span className="flex justify-end gap-2">
          <Button disabled={busy} onClick={() => handleApprove(row)}>
            <Check className="h-4 w-4" /> Tasdiqlash
          </Button>
          <Button
            variant="secondary"
            disabled={busy}
            onClick={() => {
              setActionError(null)
              setRejecting(row)
            }}
          >
            <X className="h-4 w-4" /> Rad etish
          </Button>
        </span>
      )
    }

    if (isOwn(row)) {
      return (
        <span className="text-xs text-slate-500">
          O'zingiz so'ragansiz — qarorni boshqa mas'ul qabul qiladi.
        </span>
      )
    }

    return <span className="text-xs text-slate-500">Direktor tasdig'i kutilmoqda</span>
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Chegirmalar</h1>
          <p className="text-sm text-slate-400">
            Har qanday chegirma direktor tasdig'ini talab qiladi — chegara yo'q.
          </p>
        </div>
        {canGrantDiscount && (
          <Button
            onClick={() => {
              setActionError(null)
              setFormOpen(true)
            }}
          >
            <Plus className="h-4 w-4" /> Chegirma so'rash
          </Button>
        )}
      </div>

      {notice && <Notice tone="success">{notice}</Notice>}
      {actionError && !formOpen && !rejecting && <Notice>{actionError}</Notice>}

      {/* ---- Tasdiq navbati: sahifaning eng ko'zga tashlanadigan bloki ---- */}
      {!loading && !error && pending.length > 0 && (
        <PendingQueue
          title="Tasdiq kutayotgan chegirmalar"
          hint={
            canApproveDiscount
              ? `Sizga ochiq: ${approvableCount} ta`
              : "Qarorni direktor qabul qiladi"
          }
          count={pending.length}
        >
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-amber-100/60 text-xs uppercase tracking-wide text-amber-800">
                <tr>
                  <th className="px-4 py-2">O'quvchi</th>
                  <th className="px-4 py-2">Toifa</th>
                  <th className="px-4 py-2">Chegirma</th>
                  <th className="px-4 py-2">Sabab</th>
                  <th className="px-4 py-2">Kim so'radi</th>
                  <th className="px-4 py-2 text-right">Qaror</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-amber-100">
                {pending.map((row) => (
                  <tr key={row.id}>
                    <td className="px-4 py-3 font-medium text-slate-800">{row.studentName}</td>
                    <td className="px-4 py-3 text-slate-600">
                      {row.categoryName ?? 'Barcha toifalar'}
                    </td>
                    <td className="px-4 py-3 font-medium text-slate-800">
                      {discountLabel(row.percent, row.amount)}
                    </td>
                    <td className="px-4 py-3 text-slate-600">{row.reason}</td>
                    <td className="px-4 py-3 text-slate-600">
                      {row.createdByName}
                      <span className="block text-xs text-slate-400">
                        {row.createdAt.slice(0, 10)}
                      </span>
                    </td>
                    <td className="px-4 py-3">{renderDecision(row)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </PendingQueue>
      )}

      {!loading && !error && pending.length === 0 && (
        <div className="flex items-center gap-2 rounded-2xl border border-emerald-200 bg-emerald-50 px-5 py-3 text-sm text-emerald-800">
          <ShieldCheck className="h-4 w-4" />
          Tasdiq navbati bo'sh — kutayotgan chegirma yo'q.
        </div>
      )}

      {/* ---- Filtrlar ---- */}
      <Card>
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
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
            label="Holat bo'yicha"
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value as '' | DiscountStatus)}
          >
            <option value="">Barcha holatlar</option>
            <option value="pending">Tasdiq kutmoqda</option>
            <option value="approved">Tasdiqlangan</option>
            <option value="rejected">Rad etilgan</option>
          </Select>
        </div>
      </Card>

      {/* ---- To'liq ro'yxat ---- */}
      <Card className="p-0">
        <AsyncBlock
          loading={loading}
          error={error}
          empty={rows.length === 0}
          emptyText="Tanlangan shart bo'yicha chegirma topilmadi."
          emptyAction={
            canGrantDiscount ? (
              <Button variant="secondary" onClick={() => setFormOpen(true)}>
                <Plus className="h-4 w-4" /> Chegirma so'rash
              </Button>
            ) : undefined
          }
          onRetry={load}
        >
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">O'quvchi</th>
                  <th className="px-4 py-3">Toifa</th>
                  <th className="px-4 py-3">Chegirma</th>
                  <th className="px-4 py-3">Muddat</th>
                  <th className="px-4 py-3">Sabab</th>
                  <th className="px-4 py-3">Holat</th>
                  <th className="px-4 py-3">Kim so'radi</th>
                  <th className="px-4 py-3 text-right">Qaror</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((row) => (
                  <tr key={row.id} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3 font-medium text-slate-800">{row.studentName}</td>
                    <td className="px-4 py-3 text-slate-600">
                      {row.categoryName ?? 'Barcha toifalar'}
                    </td>
                    <td className="px-4 py-3 font-medium text-slate-800">
                      {discountLabel(row.percent, row.amount)}
                    </td>
                    <td className="px-4 py-3 text-slate-600">
                      {periodLabel(row.startsOn, row.endsOn)}
                    </td>
                    <td className="px-4 py-3 text-slate-600">{row.reason}</td>
                    <td className="px-4 py-3">
                      <StatusPill tone={statusTones[row.status]}>
                        {row.status === 'pending' && <Clock className="h-3 w-3" />}
                        {statusLabels[row.status]}
                      </StatusPill>
                    </td>
                    <td className="px-4 py-3 text-slate-600">{row.createdByName}</td>
                    <td className="px-4 py-3 text-right">{renderDecision(row)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </AsyncBlock>
      </Card>

      <p className="text-xs text-slate-400">
        Tasdiqlanmagan chegirma hisob-kitobga umuman ta'sir qilmaydi: oylik hisoblash faqat
        tasdiqlanganlarini oladi. Rad etilgan chegirma o'chirilmaydi — tarix uchun qoladi.
      </p>

      {canGrantDiscount && (
        <DiscountFormModal
          open={formOpen}
          categories={categories}
          students={students.options}
          studentsLoading={students.loading}
          studentsError={students.error}
          busy={busy}
          error={actionError}
          onClose={() => setFormOpen(false)}
          onSubmit={handleCreate}
        />
      )}

      {canApproveDiscount && (
        <ReasonModal
          open={rejecting !== null}
          title="Chegirmani rad etish"
          description={
            rejecting
              ? `${rejecting.studentName} uchun so'ralgan chegirma rad etiladi. Sabab yozuvda qoladi.`
              : ''
          }
          confirmLabel="Rad etish"
          busy={busy}
          error={actionError}
          onClose={() => setRejecting(null)}
          onConfirm={handleReject}
        />
      )}
    </div>
  )
}
