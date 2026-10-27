/**
 * Delete a discount for good (client, 2026-10-27): unlike "Tugatish" it is as if the discount had
 * never been attached — every month it already reduced gets its debt back (the server reverses
 * it and lets the next discount take over), and it disappears from the list.
 */
import type { DiscountRecord } from '@/api/services/billingCatalog'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Notice } from './BillingUi'
import { discountPeriodLabel, discountValueLabel } from './discountUtils'

interface Props {
  open: boolean
  discount: DiscountRecord | null
  busy: boolean
  error: string | null
  onClose: () => void
  onConfirm: (id: string) => void
}

export function DeleteDiscountModal({ open, discount, busy, error, onClose, onConfirm }: Props) {
  if (!discount) return null

  const name = discount.discountTypeName ?? discount.reason

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Chegirmani o'chirish"
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button variant="danger" disabled={busy} onClick={() => !busy && onConfirm(discount.id)}>
            {busy ? "O'chirilmoqda..." : "Butunlay o'chirish"}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="rounded-xl border border-slate-200 bg-slate-50/70 px-3 py-2.5">
          <p className="font-medium text-slate-800">{name}</p>
          <p className="text-xs text-slate-500">
            {discountValueLabel(discount.percent, discount.amount)} ·{' '}
            {discount.categoryName ?? 'Barcha abonementlar'} ·{' '}
            {discountPeriodLabel(discount.startsOn, discount.endsOn)}
          </p>
        </div>

        <Notice tone="info">
          Chegirma ro'yxatdan yo'qoladi. U allaqachon kamaytirgan oylar qarzi qaytariladi — xuddi
          chegirma umuman bo'lmagandek. Faqat keyingi oylardan to'xtatish kerak bo'lsa,
          "Tugatish"dan foydalaning.
        </Notice>

        {error && <Notice>{error}</Notice>}
      </div>
    </Modal>
  )
}
