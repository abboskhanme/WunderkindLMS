import { useEffect, useState } from 'react'
import type { PaymentMethod } from '@/types'
import type { CashBox } from '@/api/services/cashBoxes'
import { cashBoxExchange, cashBoxIn, cashBoxOut, cashBoxTransfer } from '@/api/services/cashBoxes'
import { financeErrorMessage } from '@/api/services/cashier'
import type { TransactionType } from '@/api/services/transactionTypes'
import { getTransactionTypes } from '@/api/services/transactionTypes'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { DatePicker } from '@/components/ui/DatePicker'
import { Select, Textarea } from '@/components/ui/Input'
import { Notice } from '@/pages/admin/billing/BillingUi'
import { MoneyInput } from './MoneyInput'
import { formatSum, methodLabels, parseSum } from './format'
import type { CashBoxActionMode } from './CashBoxCard'

const todayStr = () => new Date().toISOString().slice(0, 10)

/** Serverdagi chegara bilan bir xil: `CashBoxService.MaxBackdateDays` = 366. */
const earliestEntryDate = () => {
  const d = new Date()
  d.setDate(d.getDate() - 366)
  return d.toISOString().slice(0, 10)
}

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
  const [date, setDate] = useState(todayStr())
  const [transactionTypeId, setTransactionTypeId] = useState('')
  const [types, setTypes] = useState<TransactionType[]>([])
  const [typesLoading, setTypesLoading] = useState(false)
  const [typesError, setTypesError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  /* Chiqim turi katalogi — faqat "Chiqim" rejimida kerak (Kirim shu oynada
     ochilmaydi: uni `CashierPage` ning o'z `IncomeForm` i ochadi). */
  useEffect(() => {
    if (mode !== 'out') return
    let alive = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- katalogni bir marta yuklaymiz, yuklanish holati shu yerda boshlanadi (maqsadli)
    setTypesLoading(true)
    setTypesError(null)
    getTransactionTypes('out')
      .then((rows) => {
        if (!alive) return
        const active = rows.filter((t) => t.isActive)
        setTypes(active)
        setTransactionTypeId((current) => current || active[0]?.id || '')
      })
      .catch((err: unknown) => {
        if (alive) setTypesError(financeErrorMessage(err, "Chiqim turlarini yuklab bo'lmadi."))
      })
      .finally(() => {
        if (alive) setTypesLoading(false)
      })
    return () => {
      alive = false
    }
  }, [mode])

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
  // Chiqimda tur MAJBURIY — kirim shakli bilan bir xil qoida (mijoz shakli).
  const typeValid = mode !== 'out' || transactionTypeId !== ''
  const today = todayStr()
  const earliest = earliestEntryDate()
  const dateValid = date !== '' && date <= today && date >= earliest
  const valid = amountValid && exchangeValid && transferValid && typeValid && dateValid

  const submit = async () => {
    if (!valid || amount === null || busy) return
    setBusy(true)
    setError(null)
    const trimmedNote = note.trim() || undefined
    try {
      if (mode === 'in') {
        await cashBoxIn(box.id, { amount, method, note: trimmedNote, date })
      } else if (mode === 'out') {
        await cashBoxOut(box.id, { amount, method, note: trimmedNote, transactionTypeId, date })
      } else if (mode === 'transfer') {
        await cashBoxTransfer(box.id, { toBoxId, amount, method, note: trimmedNote, date })
      } else {
        await cashBoxExchange(box.id, { amount, fromMethod, toMethod, note: trimmedNote, date })
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
        {/* Chiqim turi — EduSchool chiqim shaklidagidek birinchi maydon. */}
        {mode === 'out' && (
          <div>
            <Select
              label="Tranzaksiya turi"
              required
              value={transactionTypeId}
              onChange={(e) => setTransactionTypeId(e.target.value)}
              disabled={typesLoading || types.length === 0}
            >
              {typesLoading && <option value="">Yuklanmoqda...</option>}
              {!typesLoading && types.length === 0 && <option value="">Turlar yo'q</option>}
              {!typesLoading &&
                types.length > 0 && [
                  <option key="" value="" disabled>
                    Tanlang...
                  </option>,
                  ...types.map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.name}
                    </option>
                  )),
                ]}
            </Select>
            {typesError && <p className="mt-1 text-xs text-red-600">{typesError}</p>}
          </div>
        )}

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

        {/* Sana — kirim shakli bilan bir xil: sukut bugun, orqaga yozish mumkin
            (mijoz, 2026-09-18), kelajak va bir yildan uzoq orqa — yo'q. */}
        <div>
          <DatePicker
            label="Sana"
            value={date}
            min={earliest}
            max={today}
            onChange={setDate}
            invalid={!dateValid}
          />
          <p className="mt-1 text-xs text-slate-400">
            Sukut bo'yicha bugun — o'tgan kun bilan ham yozish mumkin.
          </p>
        </div>

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
