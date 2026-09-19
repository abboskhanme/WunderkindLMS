/**
 * Bonus / jarima registri (F11.01, F11.02) — `PayrollAdjustmentsController.cs`
 * ning ekrani. Bitta komponent, ikkita marshrut:
 *
 *   /hr/bonus   -> <AdjustmentsPage kind="bonus" />
 *   /hr/penalty -> <AdjustmentsPage kind="penalty" />
 *
 * (finance-parity.md F11.01: "two routes, one component"; marshrut va menyu
 * yozuvi orkestrator faylida — App.tsx / navigation.ts, bu vazifaning
 * qamrovidan tashqarida.)
 *
 * EduSchool'dagi "Bonus" va "Jarima" — xodimga (o'qituvchi yoki boshqa
 * xodim) bir martalik qo'lda kiritiladigan to'lov. HR qoidalar dvigateli
 * (`hr_rules`, avtomatik penalty/bonus qoidalari) BILAN ARALASHTIRMANG —
 * bu qo'lda yoziladigan, alohida yozuv (hr.md §2.7).
 *
 * O'CHIRISH VA TAHRIRLASH TUGMASI YO'Q (registr uchun) — va bo'lmaydi:
 * jadval bazada faqat qo'shiladi (SPEC §4.1). Xato yozuv faqat "Bekor
 * qilish" (storno) bilan tuzatiladi.
 */
import { useCallback, useEffect, useState } from 'react'
import { Plus, Settings2 } from 'lucide-react'
import type {
  AdjustmentKind,
  CreatePayrollAdjustmentInput,
  PayrollAdjustment,
} from '@/api/services/payrollAdjustments'
import {
  adjustmentKindLabels,
  createPayrollAdjustment,
  getPayrollAdjustments,
  reversePayrollAdjustment,
} from '@/api/services/payrollAdjustments'
import { billingErrorMessage } from '@/api/services/billingError'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { AsyncBlock, Notice, StatusPill } from '../billing/BillingUi'
import { ReasonModal } from '../billing/ReasonModal'
import { AdjustmentFormModal } from './AdjustmentFormModal'
import { AdjustmentReasonsModal } from './AdjustmentReasonsModal'

const MONTH_NAMES = [
  "Yan", "Fev", "Mar", "Apr", "May", "Iyun", "Iyul", "Avg", "Sen", "Okt", "Noy", "Dek",
]

interface Props {
  kind: AdjustmentKind
}

export function AdjustmentsPage({ kind }: Props) {
  const label = adjustmentKindLabels[kind]

  const [rows, setRows] = useState<PayrollAdjustment[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [pageNotice, setPageNotice] = useState<string | null>(null)

  const [formOpen, setFormOpen] = useState(false)
  const [formBusy, setFormBusy] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

  const [reasonsOpen, setReasonsOpen] = useState(false)

  const [reversing, setReversing] = useState<PayrollAdjustment | null>(null)
  const [reverseBusy, setReverseBusy] = useState(false)
  const [reverseError, setReverseError] = useState<string | null>(null)

  const load = useCallback(() => {
    setLoading(true)
    setError(null)
    getPayrollAdjustments({ kind })
      .then(setRows)
      .catch((e: unknown) => setError(billingErrorMessage(e, `${label} ro'yxatini yuklab bo'lmadi`)))
      .finally(() => setLoading(false))
  }, [kind, label])

  // eslint-disable-next-line react-hooks/set-state-in-effect -- sahifa/kind almashganda birinchi yuklash
  useEffect(() => load(), [load])

  const openCreate = () => {
    setFormError(null)
    setFormOpen(true)
  }

  const submitCreate = async (values: CreatePayrollAdjustmentInput) => {
    setFormBusy(true)
    setFormError(null)
    try {
      const created = await createPayrollAdjustment(values)
      setRows((prev) => [created, ...prev])
      setPageNotice(`${label} yozildi: ${created.employeeName} — ${formatMoney(created.amount)} so'm.`)
      setFormOpen(false)
    } catch (e: unknown) {
      setFormError(billingErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setFormBusy(false)
    }
  }

  const submitReverse = async (reason: string) => {
    if (!reversing) return
    setReverseBusy(true)
    setReverseError(null)
    try {
      const mirror = await reversePayrollAdjustment(reversing.id, reason)
      setRows((prev) => [
        mirror,
        ...prev.map((r) => (r.id === reversing.id ? { ...r, reversed: true } : r)),
      ])
      setPageNotice(`${label} bekor qilindi: ${reversing.employeeName}.`)
      setReversing(null)
    } catch (e: unknown) {
      setReverseError(billingErrorMessage(e, "Bekor qilib bo'lmadi"))
    } finally {
      setReverseBusy(false)
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">{label}</h1>
          <p className="text-sm text-slate-400">Jami {rows.length} ta yozuv</p>
        </div>
        <div className="flex gap-2">
          <Button variant="secondary" onClick={() => setReasonsOpen(true)}>
            <Settings2 className="h-4 w-4" /> Sabablarni boshqarish
          </Button>
          <Button onClick={openCreate}>
            <Plus className="h-4 w-4" /> Yangi {label.toLowerCase()}
          </Button>
        </div>
      </div>

      {pageNotice && <Notice tone="success">{pageNotice}</Notice>}

      <Card className="p-0">
        <AsyncBlock
          loading={loading}
          error={error}
          empty={rows.length === 0}
          emptyText={`Hozircha birorta ${label.toLowerCase()} yozuvi yo'q.`}
          emptyAction={
            <Button variant="secondary" onClick={openCreate}>
              <Plus className="h-4 w-4" /> Birinchisini qo'shish
            </Button>
          }
          onRetry={load}
        >
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">Xodim</th>
                  <th className="px-4 py-3">Sabab</th>
                  <th className="px-4 py-3">Summa</th>
                  <th className="px-4 py-3">Oy</th>
                  <th className="px-4 py-3">Holat</th>
                  <th className="px-4 py-3">Izoh</th>
                  <th className="px-4 py-3">Yozgan</th>
                  <th className="px-4 py-3">Sana</th>
                  <th className="px-4 py-3 text-right">Amal</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((row) => (
                  <tr key={row.id} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3">
                      <div className="font-medium text-slate-800">{row.employeeName}</div>
                      <div className="text-xs text-slate-400">
                        {row.employeeKind === 'teacher' ? "O'qituvchi" : 'Xodim'}
                      </div>
                    </td>
                    <td className="px-4 py-3 text-slate-600">
                      <span className="block max-w-[14rem] truncate" title={row.reasonName}>
                        {row.reasonName}
                      </span>
                    </td>
                    <td className="px-4 py-3 font-medium text-slate-800">
                      {formatMoney(row.amount)}
                    </td>
                    <td className="px-4 py-3 text-slate-600">
                      {MONTH_NAMES[row.periodMonth - 1]} {row.periodYear}
                    </td>
                    <td className="px-4 py-3">
                      {row.reversalOf ? (
                        <StatusPill tone="info">Storno</StatusPill>
                      ) : row.reversed ? (
                        <StatusPill>Bekor qilingan</StatusPill>
                      ) : (
                        <StatusPill tone="success">Faol</StatusPill>
                      )}
                    </td>
                    <td className="max-w-[16rem] truncate px-4 py-3 text-slate-500" title={row.comment ?? ''}>
                      {row.comment ?? '—'}
                    </td>
                    <td className="px-4 py-3 text-slate-500">{row.createdByName}</td>
                    <td className="px-4 py-3 text-slate-400">
                      {new Date(row.createdAt).toLocaleDateString('uz-UZ')}
                    </td>
                    <td className="px-4 py-3 text-right">
                      {!row.reversalOf && !row.reversed ? (
                        <Button
                          variant="secondary"
                          className="px-2 py-1 text-xs"
                          onClick={() => { setReverseError(null); setReversing(row) }}
                        >
                          Bekor qilish
                        </Button>
                      ) : (
                        <span className="text-xs text-slate-300">—</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </AsyncBlock>
      </Card>

      <AdjustmentFormModal
        open={formOpen}
        kind={kind}
        busy={formBusy}
        error={formError}
        onClose={() => setFormOpen(false)}
        onSubmit={submitCreate}
      />

      <AdjustmentReasonsModal
        open={reasonsOpen}
        kind={kind}
        onClose={() => setReasonsOpen(false)}
        onChanged={() => { /* ro'yxat keyingi ochilishda yangi sabablarni o'qiydi */ }}
      />

      <ReasonModal
        open={reversing !== null}
        title={`${label}ni bekor qilish`}
        description={
          reversing
            ? `${reversing.employeeName} — ${formatMoney(reversing.amount)} so'm. `
              + 'Qarshi qator qo\'shiladi, original yozuv o\'zgarmaydi.'
            : ''
        }
        confirmLabel="Bekor qilish"
        busy={reverseBusy}
        error={reverseError}
        onClose={() => setReversing(null)}
        onConfirm={submitReverse}
      />
    </div>
  )
}

function formatMoney(value: number): string {
  return value.toLocaleString('uz-UZ', { maximumFractionDigits: 0 })
}
