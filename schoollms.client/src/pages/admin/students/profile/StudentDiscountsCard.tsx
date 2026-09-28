import { useCallback, useEffect, useMemo, useState } from 'react'
import { CalendarOff, Clock, Plus } from 'lucide-react'
import type { DiscountRecord } from '@/api/services/billingCatalog'
import { attachDiscount, endDiscount, getDiscounts } from '@/api/services/billingCatalog'
import { billingErrorMessage } from '@/api/services/billingError'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Toast } from '@/components/ui/Toast'
import { formatMoney } from '@/lib/utils'
import { AsyncBlock, IconBtn, StatusPill } from '@/pages/admin/billing/BillingUi'
import { useBillingAccess } from '@/pages/admin/billing/access'
import type { AttachDiscountValues } from '@/pages/admin/billing/AttachDiscountModal'
import { AttachDiscountModal } from '@/pages/admin/billing/AttachDiscountModal'
import { EndDiscountModal } from '@/pages/admin/billing/EndDiscountModal'
import type { StudentDiscountState } from '@/pages/admin/billing/discountUtils'
import {
  discountPeriodLabel,
  discountValueLabel,
  monthLabel,
  studentDiscountState,
  todayIso,
} from '@/pages/admin/billing/discountUtils'

interface Props {
  studentId: string
  studentName: string
  /** A discount changes the debt — the "To'lovlar" tab reloads. */
  onChanged?: () => void
}

const stateOrder: Record<StudentDiscountState, number> = {
  running: 0,
  pending: 1,
  ended: 2,
  rejected: 3,
}

function StatePill({ state }: { state: StudentDiscountState }) {
  switch (state) {
    case 'running':
      return <StatusPill tone="success">Amalda</StatusPill>
    case 'pending':
      return (
        <StatusPill tone="warning">
          <Clock className="h-3 w-3" /> Tasdiq kutilmoqda
        </StatusPill>
      )
    case 'rejected':
      return <StatusPill tone="danger">Rad etilgan</StatusPill>
    case 'ended':
      return <StatusPill>Tugagan</StatusPill>
  }
}

function attachedMessage(adjustedInvoices: number, adjustedTotal: number): string {
  if (adjustedInvoices > 0) {
    return `Chegirma biriktirildi: hisoblangan ${adjustedInvoices} ta oy qarzidan ${formatMoney(adjustedTotal)} ayrildi.`
  }
  return 'Chegirma biriktirildi.'
}

/**
 * The "Chegirmalar" card of the student's "Abonementlar" tab (docs/modules/billing-plans.md §8):
 * every discount of the student — catalog and hand-written — with its state, plus attaching a
 * catalog type (no approval) and ending a running discount.
 *
 * Write controls follow the subscriptions tab: `canManageSubscriptions` (the tab itself is only
 * shown under it; checked again here so the card stays safe wherever it is rendered).
 */
export function StudentDiscountsCard({ studentId, studentName, onChanged }: Props) {
  const { canManageSubscriptions } = useBillingAccess()

  const [rows, setRows] = useState<DiscountRecord[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [attachOpen, setAttachOpen] = useState(false)
  const [ending, setEnding] = useState<DiscountRecord | null>(null)
  const [busy, setBusy] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [toast, setToast] = useState<string | null>(null)
  const closeToast = useCallback(() => setToast(null), [])

  const load = useCallback(() => {
    setLoading(true)
    setError(null)
    getDiscounts({ studentId })
      .then((list) => setRows(list))
      .catch((e: unknown) => setError(billingErrorMessage(e, "Chegirmalarni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [studentId])

  // eslint-disable-next-line react-hooks/set-state-in-effect -- load when the student changes (SubscriptionsTab pattern)
  useEffect(() => load(), [load])

  const today = todayIso()

  /** Running first, then pending, ended, rejected; newest start first inside each group. */
  const sorted = useMemo(
    () =>
      rows
        .map((row) => ({ row, state: studentDiscountState(row, today) }))
        .sort(
          (a, b) =>
            stateOrder[a.state] - stateOrder[b.state] || b.row.startsOn.localeCompare(a.row.startsOn),
        ),
    [rows, today],
  )

  const runningCount = sorted.filter((s) => s.state === 'running').length

  const openAttach = () => {
    setActionError(null)
    setAttachOpen(true)
  }

  const handleAttach = async (values: AttachDiscountValues) => {
    setBusy(true)
    setActionError(null)
    try {
      const result = await attachDiscount({ studentId, ...values })
      setAttachOpen(false)
      setToast(attachedMessage(result.adjustedInvoices, result.adjustedTotal))
      load()
      onChanged?.()
    } catch (e: unknown) {
      setActionError(billingErrorMessage(e, "Chegirmani biriktirib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const handleEnd = async (id: string, endsOn: string) => {
    setBusy(true)
    setActionError(null)
    try {
      const ended = await endDiscount(id, endsOn)
      setEnding(null)
      setToast(
        `"${ended.discountTypeName ?? ended.reason}" chegirmasi ${monthLabel(endsOn.slice(0, 7))} bilan tugatildi.`,
      )
      load()
      onChanged?.()
    } catch (e: unknown) {
      setActionError(billingErrorMessage(e, "Chegirmani tugatib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <Card className="p-0">
        <header className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 px-5 py-3">
          <div>
            <p className="font-medium text-slate-800">Chegirmalar</p>
            <p className="text-xs text-slate-400">
              {loading || error ? '—' : `${rows.length} ta · ${runningCount} tasi amalda`}
            </p>
          </div>
          {canManageSubscriptions && (
            <Button onClick={openAttach}>
              <Plus className="h-4 w-4" /> Chegirma biriktirish
            </Button>
          )}
        </header>

        <AsyncBlock
          loading={loading}
          error={error}
          empty={rows.length === 0}
          emptyText="Bu o'quvchida chegirma yo'q."
          emptyAction={
            canManageSubscriptions ? (
              <Button variant="secondary" onClick={openAttach}>
                <Plus className="h-4 w-4" /> Chegirma biriktirish
              </Button>
            ) : undefined
          }
          onRetry={load}
        >
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-2">Chegirma</th>
                  <th className="px-4 py-2">Summa</th>
                  <th className="px-4 py-2">Qaysi abonementga</th>
                  <th className="px-4 py-2">Muddat</th>
                  <th className="px-4 py-2">Holat</th>
                  <th className="px-4 py-2 text-right">Amal</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {sorted.map(({ row, state }) => (
                  <tr key={row.id} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3">
                      <p className="font-medium text-slate-800">{row.discountTypeName ?? row.reason}</p>
                      <p className="text-xs text-slate-400">
                        {row.discountTypeId ? 'Katalogdan' : "Qo'lda yozilgan"}
                      </p>
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 tabular-nums text-slate-800">
                      {discountValueLabel(row.percent, row.amount)}
                    </td>
                    <td className="px-4 py-3 text-slate-600">
                      {row.categoryName ?? 'Barcha abonementlar'}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-slate-600">
                      {discountPeriodLabel(row.startsOn, row.endsOn)}
                    </td>
                    <td className="px-4 py-3">
                      <StatePill state={state} />
                    </td>
                    <td className="px-4 py-3">
                      <span className="flex justify-end gap-0.5">
                        {canManageSubscriptions && state === 'running' ? (
                          <IconBtn
                            icon={CalendarOff}
                            title="Tugatish"
                            tone="danger"
                            onClick={() => {
                              setActionError(null)
                              setEnding(row)
                            }}
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

      {canManageSubscriptions && (
        <>
          <AttachDiscountModal
            open={attachOpen}
            studentName={studentName}
            runningCount={runningCount}
            busy={busy}
            error={actionError}
            onClose={() => setAttachOpen(false)}
            onConfirm={handleAttach}
          />
          <EndDiscountModal
            open={ending !== null}
            discount={ending}
            busy={busy}
            error={actionError}
            onClose={() => setEnding(null)}
            onConfirm={handleEnd}
          />
        </>
      )}

      <Toast message={toast} onClose={closeToast} duration={6000} />
    </>
  )
}
