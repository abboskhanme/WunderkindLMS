import { useEffect, useState } from 'react'
import type { PaymentMethod } from '@/types'
import type { CashBox } from '@/api/services/cashBoxes'
import { cashBoxExchange, cashBoxIn, cashBoxOut, cashBoxTransfer } from '@/api/services/cashBoxes'
import { financeErrorMessage } from '@/api/services/cashier'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Select, Textarea } from '@/components/ui/Input'
import { Notice } from '@/pages/admin/billing/BillingUi'
import { MoneyInput } from './MoneyInput'
import { formatSum, methodLabels, parseSum } from './format'
import type { CashBoxActionMode } from './CashBoxCard'

const METHODS: PaymentMethod[] = ['cash', 'card', 'transfer', 'online']

const titles: Record<CashBoxActionMode, string> = {
  in: 'Kirim',
  out: 'Chiqim',
  transfer: "Ko'chirish",
  exchange: 'Ayirboshlash',
}

interface Props {
  box: CashBox
  /** Ko'chirish uchun manzil tanlash — o'zidan boshqa faol kassalar. */
  otherBoxes: CashBox[]
  mode: CashBoxActionMode
  onClose: () => void
  onDone: () => void
}

/**
 * Kassaning to'rtta amali — bitta oynada, EduSchool tuzilishi bo'yicha
 * (mijoz sxemasi: Kirim / Chiqim / Ko'chirish / Ayirboshlash).
 *
 * BU YERDA O'QUVCHI TANLANMAYDI: hisob-fakturaga taqsimlanadigan to'lov
 * pastdagi "O'quvchidan to'lov qabul qilish" bo'limidan o'zining eski
 * yo'li (`acceptPayment`) bilan ketadi. Bu oyna — kassaning umumiy pul
 * harakati (boshlang'ich mablag', xarajat, kassalararo ko'chirish,
 * usullar orasidagi ayirboshlash).
 */
export function CashBoxActionModal({ box, otherBoxes, mode, onClose, onDone }: Props) {
  const [amountRaw, setAmountRaw] = useState('')
  const [method, setMethod] = useState<PaymentMethod>('cash')
  const [fromMethod, setFromMethod] = useState<PaymentMethod>('cash')
  const [toMethod, setToMethod] = useState<PaymentMethod>('card')
  const [toBoxId, setToBoxId] = useState('')
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (mode === 'transfer' && toBoxId === '' && otherBoxes.length > 0) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- oyna "Ko'chirish" rejimida ochilganda manzil kassani boshlang'ich tanlash (maqsadli)
      setToBoxId(otherBoxes[0].id)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- faqat boshlang'ich tanlov, keyingi otherBoxes o'zgarishlarida qayta yozilmasin
  }, [mode])

  const amount = parseSum(amountRaw)
  const amountValid = amount !== null && amount > 0
  const exchangeValid = mode !== 'exchange' || fromMethod !== toMethod
  const transferValid = mode !== 'transfer' || toBoxId !== ''
  const valid = amountValid && exchangeValid && transferValid

  const submit = async () => {
    if (!valid || amount === null || busy) return
    setBusy(true)
    setError(null)
    const trimmedNote = note.trim() || undefined
    try {
      if (mode === 'in') {
        await cashBoxIn(box.id, { amount, method, note: trimmedNote })
      } else if (mode === 'out') {
        await cashBoxOut(box.id, { amount, method, note: trimmedNote })
      } else if (mode === 'transfer') {
        await cashBoxTransfer(box.id, { toBoxId, amount, method, note: trimmedNote })
      } else {
        await cashBoxExchange(box.id, { amount, fromMethod, toMethod, note: trimmedNote })
      }
      onDone()
    } catch (err) {
      setError(financeErrorMessage(err, "Amalni bajarib bo'lmadi."))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={busy ? () => undefined : onClose}
      title={`${titles[mode]} — ${box.name}`}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={submit} disabled={!valid || busy}>
            {busy ? 'Yozilmoqda...' : titles[mode]}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <MoneyInput
          label="Summa (so'm)"
          value={amountRaw}
          onValueChange={setAmountRaw}
          placeholder="0"
          invalid={amountRaw.length > 0 && !amountValid}
          hint={amountRaw.length > 0 && !amountValid ? "Summa noldan katta bo'lishi kerak." : undefined}
          autoFocus
        />

        {mode !== 'exchange' && (
          <Select label="To'lov usuli" value={method} onChange={(e) => setMethod(e.target.value as PaymentMethod)}>
            {METHODS.map((m) => (
              <option key={m} value={m}>
                {methodLabels[m]}
              </option>
            ))}
          </Select>
        )}

        {mode === 'exchange' && (
          <div className="grid grid-cols-2 gap-3">
            <Select
              label="Qaysi usuldan"
              value={fromMethod}
              onChange={(e) => setFromMethod(e.target.value as PaymentMethod)}
            >
              {METHODS.map((m) => (
                <option key={m} value={m}>
                  {methodLabels[m]}
                </option>
              ))}
            </Select>
            <Select
              label="Qaysi usulga"
              value={toMethod}
              onChange={(e) => setToMethod(e.target.value as PaymentMethod)}
            >
              {METHODS.map((m) => (
                <option key={m} value={m}>
                  {methodLabels[m]}
                </option>
              ))}
            </Select>
          </div>
        )}
        {mode === 'exchange' && !exchangeValid && (
          <p className="text-xs text-red-600">Ikki usul bir xil bo'lmasligi kerak.</p>
        )}

        {mode === 'transfer' && (
          <>
            <Select label="Qaysi kassaga" value={toBoxId} onChange={(e) => setToBoxId(e.target.value)}>
              <option value="">Tanlang</option>
              {otherBoxes.map((b) => (
                <option key={b.id} value={b.id}>
                  {b.name}
                </option>
              ))}
            </Select>
            {otherBoxes.length === 0 && (
              <p className="text-xs text-amber-600">Ko'chirish uchun boshqa faol kassa yo'q.</p>
            )}
          </>
        )}

        <Textarea
          label="Izoh (ixtiyoriy)"
          rows={2}
          value={note}
          onChange={(e) => setNote(e.target.value)}
          placeholder="Masalan: kassadan bankka inkassatsiya"
        />

        <p className="text-xs text-slate-400">
          Joriy qoldiq: {formatSum(box.balance)} so'm
        </p>

        {error && <Notice>{error}</Notice>}
      </div>
    </Modal>
  )
}
