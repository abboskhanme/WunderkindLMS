import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import type { FinanceDirection, FinanceTransaction, MonthSalary, Teacher } from '@/types'
import type { FinanceTransactionPayload } from '@/api/services/finance'
import { getTeachers, getSalaryMonth } from '@/api/services/teachers'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { categoriesByDirection, financeDirectionLabels, formatMonth } from '@/config/constants'
import { formatMoney, cn } from '@/lib/utils'

interface Props {
  open: boolean
  onClose: () => void
  onSubmit: (values: FinanceTransactionPayload) => void
  initial?: FinanceTransaction | null
}

const today = () => new Date().toISOString().slice(0, 10)

const emptyFor = (direction: FinanceDirection): FinanceTransactionPayload => ({
  date: today(),
  direction,
  category: categoriesByDirection[direction][0].value,
  amount: 0,
  note: '',
})

export function TransactionFormModal({ open, onClose, onSubmit, initial }: Props) {
  const [form, setForm] = useState<FinanceTransactionPayload>(emptyFor('income'))
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [monthInfo, setMonthInfo] = useState<MonthSalary | null>(null)

  const isSalaryExpense = form.direction === 'expense' && form.category === 'salary'
  const month = form.date?.slice(0, 7)

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda formani initial bilan sinxronlash (maqsadli)
    setForm(
      initial
        ? {
            date: initial.date,
            direction: initial.direction,
            category: initial.category,
            amount: initial.amount,
            note: initial.note ?? '',
            studentId: initial.studentId,
            teacherId: initial.teacherId,
          }
        : emptyFor('income'),
    )
  }, [open, initial])

  // O'qituvchilar ro'yxatini (oylik maosh tanlovi uchun) API'dan olamiz
  useEffect(() => {
    if (open) getTeachers().then(setTeachers)
  }, [open])

  // Oylik maosh + o'qituvchi tanlanganda: shu oy uchun belgilangan/berilgan/qoldiq
  useEffect(() => {
    if (!open || !isSalaryExpense || !form.teacherId || !month) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- shartlar buzilganda panelni tozalash (maqsadli)
      setMonthInfo(null)
      return
    }
    let active = true
    getSalaryMonth(form.teacherId, month).then((m) => {
      if (!active) return
      setMonthInfo(m)
      // Yangi amalda qoldiqni avtomatik summaga qo'yamiz
      if (!initial && m) {
        const teacherName = teachers.find((t) => t.id === form.teacherId)?.fullName ?? ''
        setForm((f) => ({
          ...f,
          amount: Math.max(0, m.remaining),
          note: f.note?.trim() ? f.note : `Oylik maosh — ${teacherName} (${formatMonth(month)})`,
        }))
      }
    })
    return () => {
      active = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- teachers nomini faqat prefill uchun ishlatamiz
  }, [open, isSalaryExpense, form.teacherId, month, initial])

  const update = <K extends keyof FinanceTransactionPayload>(
    key: K,
    value: FinanceTransactionPayload[K],
  ) => setForm((f) => ({ ...f, [key]: value }))

  // Yo'nalish o'zgarsa, toifani shu yo'nalishning birinchi qiymatiga moslaymiz
  const changeDirection = (direction: FinanceDirection) =>
    setForm((f) => ({ ...f, direction, category: categoriesByDirection[direction][0].value }))

  const handleSubmit = (e: FormEvent) => {
    e.preventDefault()
    if (form.amount <= 0 || !form.date) return
    if (isSalaryExpense && !form.teacherId) return
    onSubmit({
      ...form,
      note: form.note?.trim() || undefined,
      // teacherId faqat oylik maosh chiqimida saqlanadi
      teacherId: isSalaryExpense ? form.teacherId : undefined,
    })
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={initial ? 'Amalni tahrirlash' : 'Yangi moliyaviy amal'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button
            type="submit"
            form="finance-form"
            disabled={form.amount <= 0 || (isSalaryExpense && !form.teacherId)}
          >
            Saqlash
          </Button>
        </>
      }
    >
      <form id="finance-form" onSubmit={handleSubmit} className="space-y-4">
        <div className="grid grid-cols-2 gap-4">
          <Select
            label="Yo'nalish"
            value={form.direction}
            onChange={(e) => changeDirection(e.target.value as FinanceDirection)}
          >
            <option value="income">{financeDirectionLabels.income}</option>
            <option value="expense">{financeDirectionLabels.expense}</option>
          </Select>
          <Select
            label="Toifa"
            value={form.category}
            onChange={(e) => update('category', e.target.value)}
          >
            {categoriesByDirection[form.direction].map((c) => (
              <option key={c.value} value={c.value}>
                {c.label}
              </option>
            ))}
          </Select>
        </div>

        {/* Oylik maosh: o'qituvchi tanlash + shu oy holati */}
        {isSalaryExpense && (
          <div className="space-y-3 rounded-lg border border-slate-200 bg-slate-50/60 p-3">
            <Select
              label="O'qituvchi"
              value={form.teacherId ?? ''}
              onChange={(e) => update('teacherId', e.target.value || undefined)}
            >
              <option value="">— tanlang —</option>
              {teachers.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.fullName}
                </option>
              ))}
            </Select>

            {form.teacherId && monthInfo && (
              <div className="grid grid-cols-3 gap-2 text-center text-sm">
                <InfoCell label={`${formatMonth(monthInfo.month)} belgilangan`} value={formatMoney(monthInfo.expected)} />
                <InfoCell
                  label="Berilgan"
                  value={formatMoney(monthInfo.paid)}
                  valueClass="text-emerald-600"
                />
                <InfoCell
                  label="Qoldiq"
                  value={
                    monthInfo.remaining < 0
                      ? `+${formatMoney(-monthInfo.remaining)}`
                      : formatMoney(monthInfo.remaining)
                  }
                  valueClass={monthInfo.remaining > 0 ? 'text-red-600' : 'text-slate-500'}
                />
              </div>
            )}
            {form.teacherId && monthInfo && monthInfo.remaining <= 0 && (
              <p className="text-xs text-amber-600">
                Bu oy uchun maosh to'liq berilgan — qo'shimcha summa ortiqcha hisoblanadi.
              </p>
            )}
          </div>
        )}

        <div className="grid grid-cols-2 gap-4">
          <Input
            label="Summa (so'm)"
            type="number"
            min={0}
            step={50000}
            required
            value={form.amount}
            onChange={(e) => update('amount', Number(e.target.value))}
          />
          <Input
            label="Sana"
            type="date"
            required
            value={form.date}
            onChange={(e) => update('date', e.target.value)}
          />
        </div>
        <Textarea
          label="Izoh"
          rows={2}
          value={form.note}
          onChange={(e) => update('note', e.target.value)}
        />
      </form>
    </Modal>
  )
}

function InfoCell({
  label,
  value,
  valueClass = 'text-slate-700',
}: {
  label: string
  value: string
  valueClass?: string
}) {
  return (
    <div className="rounded-lg bg-white px-2 py-1.5">
      <p className="text-xs text-slate-400">{label}</p>
      <p className={cn('mt-0.5 font-semibold', valueClass)}>{value}</p>
    </div>
  )
}
