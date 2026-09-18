/**
 * Rejalashtirilgan chiqim shablonini qo'shish / tahrirlash (F6.01).
 *
 * BU SHABLON — CHIQIM EMAS: saqlash hech qanday pul yozuvi qo'ymaydi, faqat
 * "har oyning shu kunida taxminan shuncha xarajat kutilmoqda" degan yozuvni
 * o'zgartiradi. Haqiqiy chiqim "Chiqimlar" ekranida, alohida, qo'lda yoziladi
 * (`ExpensesPage.tsx`) — bu yerda "chiqim sifatida yozish" tugmasi YO'Q va
 * bo'lmaydi.
 */
import { useEffect, useState } from 'react'
import type { ExpenseTemplate, ExpenseTemplateInput } from '@/api/services/expenseTemplates'
import { expenseCategories } from '@/config/constants'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { Notice } from './BillingUi'

interface Props {
  open: boolean
  initial: ExpenseTemplate | null
  busy: boolean
  error: string | null
  onClose: () => void
  onSubmit: (values: ExpenseTemplateInput) => void
}

export function ExpenseTemplateFormModal({ open, initial, busy, error, onClose, onSubmit }: Props) {
  const [name, setName] = useState('')
  const [category, setCategory] = useState(expenseCategories[0]?.value ?? 'other')
  const [amount, setAmount] = useState('')
  const [dayOfMonth, setDayOfMonth] = useState('5')
  const [isActive, setIsActive] = useState(true)

  useEffect(() => {
    if (!open) return
    /* eslint-disable react-hooks/set-state-in-effect -- oyna ochilganda formani initial bilan sinxronlash (maqsadli) */
    setName(initial?.name ?? '')
    setCategory(initial?.categoryCode ?? expenseCategories[0]?.value ?? 'other')
    setAmount(initial ? String(initial.amount) : '')
    setDayOfMonth(initial ? String(initial.dayOfMonth) : '5')
    setIsActive(initial?.isActive ?? true)
    /* eslint-enable react-hooks/set-state-in-effect */
  }, [open, initial])

  const editing = initial !== null
  const trimmedName = name.trim()
  const amountNumber = Number(amount)
  const amountValid = amount.trim() !== '' && Number.isFinite(amountNumber) && amountNumber > 0
  const dayNumber = Number(dayOfMonth)
  const dayValid = Number.isInteger(dayNumber) && dayNumber >= 1 && dayNumber <= 28
  const valid = trimmedName.length > 0 && amountValid && dayValid

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!valid || busy) return
    onSubmit({
      name: trimmedName,
      categoryCode: category,
      amount: amountNumber,
      dayOfMonth: dayNumber,
      isActive,
    })
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={editing ? 'Shablonni tahrirlash' : 'Yangi rejalashtirilgan chiqim'}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button type="submit" form="expense-template-form" disabled={!valid || busy}>
            {busy ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <form id="expense-template-form" onSubmit={handleSubmit} className="space-y-4">
        <Input
          label="Nomi"
          required
          placeholder="masalan: Internet to'lovi"
          value={name}
          onChange={(e) => setName(e.target.value)}
        />

        <Select label="Toifa" required value={category} onChange={(e) => setCategory(e.target.value)}>
          {expenseCategories.map((c) => (
            <option key={c.value} value={c.value}>
              {c.label}
            </option>
          ))}
        </Select>

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
            {amount.trim() !== '' && !amountValid && (
              <p className="mt-1 text-xs text-red-600">Summa noldan katta bo'lishi kerak.</p>
            )}
          </div>

          <div>
            <Input
              label="Oyning kuni"
              required
              type="number"
              min={1}
              max={28}
              inputMode="numeric"
              value={dayOfMonth}
              onChange={(e) => setDayOfMonth(e.target.value)}
            />
            {dayOfMonth.trim() !== '' && !dayValid && (
              <p className="mt-1 text-xs text-red-600">1 dan 28 gacha bo'lishi kerak.</p>
            )}
          </div>
        </div>
        <p className="-mt-2 text-xs text-slate-400">
          Har oyning shu kunida direktorga Telegram orqali eslatma boradi. 29/30/31 tanlanmaydi —
          hamma oyda bo'lavermaydi.
        </p>

        <label className="inline-flex cursor-pointer items-center gap-2 text-sm font-medium text-slate-700">
          <input
            type="checkbox"
            checked={isActive}
            onChange={(e) => setIsActive(e.target.checked)}
            className="h-4 w-4 rounded border-slate-300 accent-brand-600"
          />
          Faol — eslatma yuboriladi va hisobotda ko'rinadi
        </label>

        {error && <Notice>{error}</Notice>}
      </form>
    </Modal>
  )
}
