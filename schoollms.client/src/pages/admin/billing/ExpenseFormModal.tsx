/**
 * Chiqim yozish.
 *
 * TO'LOV USULI MAJBURIY (F1.01). Server `method` siz so'rovni rad etadi
 * (`invalid_method`) va ilgari formada bu maydon umuman yo'q edi — ya'ni
 * "Yangi chiqim" tugmasi har safar xato berardi. Usul shunchaki yorliq emas:
 * u jurnalning KREDIT satrini belgilaydi — naqd bo'lsa pul kassadan, qolgan
 * usullarda bankdan chiqadi (SPEC §8.1 Q13 mantig'ining ko'zgusi).
 *
 * Chegara (SPEC §4.5) SERVERDAN keladi (`approvalThreshold`), klientda
 * konstanta sifatida saqlanmaydi. Forma buni summa yozilayotgan paytda
 * AYTADI — chiqim yozilgandan keyin emas: admin nima bo'lishini oldindan
 * bilib turishi kerak, "saqladim, endi nega kutayapti?" degan savol
 * tug'ilmasin.
 *
 * Tahrirlash oynasi YO'Q: yozilgan chiqim o'zgartirilmaydi (SPEC §4.1),
 * xato yozuv storno bilan tuzatiladi.
 */
import { useEffect, useState } from 'react'
import { AlertTriangle } from 'lucide-react'
import type { ExpenseInput } from '@/api/services/expenses'
import type { PaymentMethod } from '@/types'
import { expenseCategories } from '@/config/constants'
import { paymentMethodLabels } from '@/pages/admin/finance/reportLabels'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { formatMoney } from '@/lib/utils'
import { Notice } from './BillingUi'

const today = () => new Date().toISOString().slice(0, 10)

/** Usullar tartibi kassa ekranidagi bilan bir xil (Naqd birinchi — eng ko'p ishlatiladi). */
const methods: PaymentMethod[] = ['cash', 'card', 'transfer', 'online']

interface Props {
  open: boolean
  busy: boolean
  error: string | null
  /** Serverdagi ikkinchi tasdiq chegarasi; hali yuklanmagan bo'lsa `null`. */
  approvalThreshold: number | null
  onClose: () => void
  onSubmit: (values: ExpenseInput) => void
}

export function ExpenseFormModal({
  open,
  busy,
  error,
  approvalThreshold,
  onClose,
  onSubmit,
}: Props) {
  const [onDate, setOnDate] = useState(today())
  const [category, setCategory] = useState(expenseCategories[0]?.value ?? 'other')
  const [method, setMethod] = useState<PaymentMethod>('cash')
  const [amount, setAmount] = useState('')
  const [note, setNote] = useState('')

  useEffect(() => {
    if (!open) return
    /* eslint-disable react-hooks/set-state-in-effect -- oyna ochilganda formani tozalash (maqsadli) */
    setOnDate(today())
    setCategory(expenseCategories[0]?.value ?? 'other')
    setMethod('cash')
    setAmount('')
    setNote('')
    /* eslint-enable react-hooks/set-state-in-effect */
  }, [open])

  const amountNumber = Number(amount)
  const amountValid = amount.trim() !== '' && Number.isFinite(amountNumber) && amountNumber > 0
  const needsApproval =
    amountValid && approvalThreshold !== null && amountNumber > approvalThreshold
  const valid = amountValid && onDate !== '' && category !== ''

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!valid || busy) return
    const trimmedNote = note.trim()
    onSubmit({
      onDate,
      category,
      amount: amountNumber,
      method,
      note: trimmedNote === '' ? undefined : trimmedNote,
    })
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Yangi chiqim"
      size="md"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button type="submit" form="expense-form" disabled={!valid || busy}>
            {busy ? 'Saqlanmoqda...' : needsApproval ? 'Tasdiqqa yuborish' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <form id="expense-form" onSubmit={handleSubmit} className="space-y-4">
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Input
            label="Sana"
            required
            type="date"
            value={onDate}
            max={today()}
            onChange={(e) => setOnDate(e.target.value)}
          />
          <Select
            label="Toifa"
            required
            value={category}
            onChange={(e) => setCategory(e.target.value)}
          >
            {expenseCategories.map((c) => (
              <option key={c.value} value={c.value}>
                {c.label}
              </option>
            ))}
          </Select>
        </div>

        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <div>
            <Input
              label="Summa (so'm)"
              required
              type="number"
              min={1}
              step={1000}
              inputMode="numeric"
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
            />
            {amountValid && (
              <p className="mt-1 text-xs text-slate-500">{formatMoney(amountNumber)}</p>
            )}
            {amount.trim() !== '' && !amountValid && (
              <p className="mt-1 text-xs text-red-600">Summa noldan katta bo'lishi kerak.</p>
            )}
          </div>

          <div>
            <Select
              label="To'lov usuli"
              required
              value={method}
              onChange={(e) => setMethod(e.target.value as PaymentMethod)}
            >
              {methods.map((m) => (
                <option key={m} value={m}>
                  {paymentMethodLabels[m]}
                </option>
              ))}
            </Select>
            <p className="mt-1 text-xs text-slate-500">
              {method === 'cash' ? 'Pul kassadan chiqadi.' : 'Pul bank hisobidan chiqadi.'}
            </p>
          </div>
        </div>

        {needsApproval && approvalThreshold !== null && (
          <div className="flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <p>
              Summa {formatMoney(approvalThreshold)} dan yuqori — chiqim{' '}
              <b>ikkinchi tasdiqni</b> talab qiladi va tasdiq navbatiga tushadi. Tasdiqni siz
              emas, boshqa mas'ul beradi va to'lov usulini o'sha tasdiqlovchi belgilaydi.
            </p>
          </div>
        )}

        <Textarea
          label="Izoh"
          rows={2}
          placeholder="masalan: Sentabr oyi elektr to'lovi"
          value={note}
          onChange={(e) => setNote(e.target.value)}
        />

        {error && <Notice>{error}</Notice>}
      </form>
    </Modal>
  )
}
