/**
 * Obunani yopish — o'quvchi avtobusdan chiqdi, yotoqxonadan ko'chdi va h.k.
 *
 * Obuna O'CHIRILMAYDI: unga hisoblangan oylar va to'lovlar bog'langan.
 * Yopilgan obuna ko'rsatilgan sanadan keyin hisoblanmaydi, tarixi esa
 * joyida qoladi.
 */
import { useEffect, useState } from 'react'
import type { SubscriptionRecord } from '@/api/services/billingCatalog'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { formatMoney } from '@/lib/utils'
import { Notice } from './BillingUi'

interface Props {
  open: boolean
  subscription: SubscriptionRecord | null
  busy: boolean
  error: string | null
  onClose: () => void
  onConfirm: (id: string, endsOn: string) => void
}

const today = () => new Date().toISOString().slice(0, 10)

export function EndSubscriptionModal({
  open,
  subscription,
  busy,
  error,
  onClose,
  onConfirm,
}: Props) {
  const [endsOn, setEndsOn] = useState(today())

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oyna ochilganda sanani bugungiga qaytarish (maqsadli)
    if (open) setEndsOn(subscription?.endsOn ?? today())
  }, [open, subscription])

  if (!subscription) return null

  const valid = endsOn !== '' && endsOn >= subscription.startsOn

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Obunani yopish"
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button disabled={!valid || busy} onClick={() => onConfirm(subscription.id, endsOn)}>
            {busy ? 'Bajarilmoqda...' : 'Yopish'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm">
          <p className="font-medium text-slate-800">{subscription.studentName}</p>
          <p className="text-slate-500">
            {subscription.categoryName} · {formatMoney(subscription.monthlyAmount)} / oy
          </p>
        </div>

        <Input
          label="Tugash sanasi"
          required
          type="date"
          min={subscription.startsOn}
          value={endsOn}
          onChange={(e) => setEndsOn(e.target.value)}
        />

        <p className="text-sm text-slate-500">
          Shu sanadan keyin bu obuna hisoblanmaydi. Yozuv o'chirilmaydi — oldingi oylar
          va to'lovlar joyida qoladi.
        </p>

        {!valid && (
          <Notice tone="danger">
            Tugash sanasi boshlanish sanasidan ({subscription.startsOn}) oldin bo'la olmaydi.
          </Notice>
        )}
        {error && <Notice>{error}</Notice>}
      </div>
    </Modal>
  )
}
