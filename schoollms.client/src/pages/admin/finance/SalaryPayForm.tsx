/**
 * "Maosh berish" — detail oynalarining pastidagi kichik forma (employees-unified.md).
 *
 * O'qituvchi → `POST /admin/teachers/{id}/salary-payments`,
 * boshqa xodim → `POST /admin/staff/{id}/salary-payments`. Tana bir xil.
 * Kassa, direktor tasdig'i chegarasi va jurnal satrlari SERVERDA: chegaradan yuqori
 * summa `pending` bo'lib qaytadi va shu yerda aytiladi.
 */
import { useState } from 'react'
import { Banknote } from 'lucide-react'
import type { EmployeeKind, PaymentMethod } from '@/types'
import type { ExpenseRecord } from '@/api/services/expenses'
import { payTeacherSalary } from '@/api/services/teachers'
import { payStaffSalary } from '@/api/services/staff'
import { billingErrorMessage } from '@/api/services/billingError'
import { paymentMethodLabels } from './reportLabels'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { formatMoney } from '@/lib/utils'
import { Notice } from '../billing/BillingUi'

/** Maosh odatda o'tkazma yoki naqd beriladi — shu ikkisi birinchi. */
const METHODS: PaymentMethod[] = ['transfer', 'cash', 'card']

interface Props {
  kind: EmployeeKind
  employeeId: string
  /** Formaga oldindan yoziladigan summa (odatda qoldiq); 0 — bo'sh. */
  suggested: number
  onPaid: (expense: ExpenseRecord) => void
}

export function SalaryPayForm({ kind, employeeId, suggested, onPaid }: Props) {
  const [open, setOpen] = useState(false)
  const [amount, setAmount] = useState('')
  const [method, setMethod] = useState<PaymentMethod>('transfer')
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState<string | null>(null)

  const parsed = Number(amount.replace(/\s/g, '').replace(',', '.'))
  const valid = amount.trim() !== '' && Number.isFinite(parsed) && parsed > 0

  const start = () => {
    setAmount(suggested > 0 ? String(Math.round(suggested)) : '')
    setMethod('transfer')
    setNote('')
    setError(null)
    setDone(null)
    setOpen(true)
  }

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!valid || busy) return
    setBusy(true)
    setError(null)
    try {
      const input = { amount: parsed, method, note: note.trim() || undefined }
      const expense =
        kind === 'staff' ? await payStaffSalary(employeeId, input) : await payTeacherSalary(employeeId, input)
      setDone(
        expense.status === 'pending'
          ? `${formatMoney(expense.amount)} yozildi — summa tasdiq chegarasidan yuqori, direktor tasdig'ini kutmoqda.`
          : `Maosh berildi: ${formatMoney(expense.amount)}.`,
      )
      setOpen(false)
      onPaid(expense)
    } catch (err: unknown) {
      setError(billingErrorMessage(err, "Maoshni yozib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  if (!open) {
    return (
      <div className="space-y-2">
        {done && <Notice tone="success">{done}</Notice>}
        <Button variant="secondary" onClick={start}>
          <Banknote className="h-4 w-4" /> Maosh berish
        </Button>
      </div>
    )
  }

  return (
    <form onSubmit={submit} className="space-y-3 rounded-2xl border border-slate-200 bg-slate-50/60 p-4">
      <p className="text-sm font-semibold text-slate-700">Maosh berish</p>
      <div className="grid gap-3 sm:grid-cols-2">
        <Input
          label="Summa (so'm)"
          required
          inputMode="numeric"
          autoFocus
          placeholder="masalan: 3000000"
          value={amount}
          onChange={(e) => setAmount(e.target.value)}
        />
        <Select label="Usul" value={method} onChange={(e) => setMethod(e.target.value as PaymentMethod)}>
          {METHODS.map((m) => (
            <option key={m} value={m}>
              {paymentMethodLabels[m]}
            </option>
          ))}
        </Select>
      </div>
      <Input
        label="Izoh"
        placeholder="Ixtiyoriy — masalan: Sentabr oyligi"
        value={note}
        onChange={(e) => setNote(e.target.value)}
      />
      {error && <Notice>{error}</Notice>}
      <div className="flex justify-end gap-2">
        <Button type="button" variant="secondary" onClick={() => setOpen(false)} disabled={busy}>
          Bekor qilish
        </Button>
        <Button type="submit" disabled={!valid || busy}>
          {busy ? 'Yozilmoqda...' : 'Berish'}
        </Button>
      </div>
    </form>
  )
}
