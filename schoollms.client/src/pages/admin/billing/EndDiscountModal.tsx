/**
 * End a running discount (docs/modules/billing-plans.md §8): the user picks the LAST month that
 * still gets it, the server receives that month's last day as `endsOn`, and no month after it
 * gets the discount. The discount itself is not deleted — its history stays.
 */
import { useEffect, useState } from 'react'
import type { DiscountRecord } from '@/api/services/billingCatalog'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { MonthPicker } from '@/components/ui/DatePicker'
import { Notice } from './BillingUi'
import { currentMonth, discountValueLabel, monthEnd, monthLabel } from './discountUtils'

interface Props {
  open: boolean
  discount: DiscountRecord | null
  busy: boolean
  error: string | null
  onClose: () => void
  onConfirm: (id: string, endsOn: string) => void
}

/** The current month, but never before the discount's own first month. */
function defaultMonth(discount: DiscountRecord | null): string {
  const now = currentMonth()
  const start = discount?.startsOn.slice(0, 7) ?? now
  return start > now ? start : now
}

export function EndDiscountModal({ open, discount, busy, error, onClose, onConfirm }: Props) {
  const [month, setMonth] = useState(() => defaultMonth(discount))

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- reset the month each time the modal opens (intentional)
    setMonth(defaultMonth(discount))
  }, [open, discount])

  if (!discount) return null

  const minMonth = discount.startsOn.slice(0, 7)
  // An end date already set can only be brought forward here, not pushed back.
  const maxMonth = discount.endsOn ? discount.endsOn.slice(0, 7) : undefined
  const valid = month !== '' && month >= minMonth && (maxMonth === undefined || month <= maxMonth)

  /** The last day of the chosen month — or the existing end date, if that is earlier. */
  const endsOn = (() => {
    if (month === '') return ''
    const last = monthEnd(month)
    return discount.endsOn && discount.endsOn < last ? discount.endsOn : last
  })()

  const name = discount.discountTypeName ?? discount.reason

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Chegirmani tugatish"
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button
            variant="danger"
            disabled={!valid || busy}
            onClick={() => valid && !busy && onConfirm(discount.id, endsOn)}
          >
            {busy ? 'Tugatilmoqda...' : 'Tugatish'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="rounded-xl border border-slate-200 bg-slate-50/70 px-3 py-2.5">
          <p className="font-medium text-slate-800">{name}</p>
          <p className="text-xs text-slate-500">
            {discountValueLabel(discount.percent, discount.amount)} ·{' '}
            {discount.categoryName ?? 'Barcha abonementlar'}
          </p>
        </div>

        <div>
          <MonthPicker
            label="Oxirgi oy"
            required
            value={month}
            onChange={setMonth}
            min={minMonth}
            max={maxMonth}
            className="w-full"
            invalid={!valid}
          />
          <p className="mt-1 text-xs text-slate-400">Chegirma shu oy bilan tugaydi.</p>
        </div>

        {valid && (
          <Notice tone="info">
            {monthLabel(month)} dan keyingi oylarga bu chegirma qo'llanmaydi.
          </Notice>
        )}

        {error && <Notice>{error}</Notice>}
      </div>
    </Modal>
  )
}
