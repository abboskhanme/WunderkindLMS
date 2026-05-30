import { useEffect, useState } from 'react'
import { Wallet } from 'lucide-react'
import type { MonthLedger, Student } from '@/types'
import { getStudentLedger } from '@/api/services/students'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { formatMoney, cn } from '@/lib/utils'
import { formatMonth, monthStatusLabels } from '@/config/constants'

interface Props {
  student: Student | null
  onClose: () => void
  onSubmit: (amount: number, month: string) => void
}

/** "YYYY-MM" joriy oy */
const currentMonth = () => new Date().toISOString().slice(0, 7)

export function PaymentModal({ student, onClose, onSubmit }: Props) {
  const [amount, setAmount] = useState<number>(0)
  const [month, setMonth] = useState<string>(currentMonth())
  const [months, setMonths] = useState<MonthLedger[]>([])
  const [loading, setLoading] = useState(false)

  useEffect(() => {
    if (!student) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda oylar holatini yuklash (maqsadli)
    setLoading(true)
    setMonths([])
    getStudentLedger(student.id)
      .then((ledger) => {
        setMonths(ledger.months)
        // Standart tanlov: eng eski to'lanmagan/qisman oy; bo'lmasa — oxirgi yoki joriy oy
        const due = ledger.months.find((m) => m.remaining > 0)
        const target = due ?? ledger.months[ledger.months.length - 1]
        setMonth(target?.month ?? currentMonth())
        setAmount(due ? due.remaining : 0)
      })
      .finally(() => setLoading(false))
  }, [student])

  // Oy almashtirilganda summani shu oyning qoldig'iga moslaymiz
  const handleMonthChange = (value: string) => {
    setMonth(value)
    const m = months.find((x) => x.month === value)
    setAmount(m && m.remaining > 0 ? m.remaining : 0)
  }

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (amount <= 0 || !month) return
    onSubmit(amount, month)
  }

  const selected = months.find((m) => m.month === month)
  const newBalance = student ? student.balance + amount : 0
  // Ledgerda oy bo'lmasa (masalan hali hisoblanmagan), joriy oyni variant sifatida ko'rsatamiz
  const monthOptions = months.length > 0 ? months.map((m) => m.month) : [currentMonth()]

  return (
    <Modal
      open={!!student}
      onClose={onClose}
      size="sm"
      title="To'lov kiritish"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button type="submit" form="payment-form" disabled={amount <= 0 || !month || loading}>
            <Wallet className="h-4 w-4" /> Saqlash
          </Button>
        </>
      }
    >
      {student &&
        (loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : (
          <form id="payment-form" onSubmit={handleSubmit} className="space-y-4">
            <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm">
              <p className="text-slate-500">{student.fullName}</p>
              <p className="mt-1 text-slate-500">
                Joriy balans:{' '}
                <span className={cn('font-semibold', student.balance < 0 ? 'text-red-600' : 'text-emerald-600')}>
                  {formatMoney(student.balance)}
                </span>
              </p>
            </div>

            {/* Qaysi oy uchun to'lov */}
            <div>
              <label className="mb-1 block text-sm font-medium text-slate-600">Qaysi oy uchun</label>
              <select
                value={month}
                onChange={(e) => handleMonthChange(e.target.value)}
                className="w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
              >
                {monthOptions.map((mo) => {
                  const m = months.find((x) => x.month === mo)
                  const suffix = m
                    ? m.remaining > 0
                      ? ` — ${monthStatusLabels[m.status]} (qoldiq ${formatMoney(m.remaining)})`
                      : ` — ${monthStatusLabels[m.status]}`
                    : ''
                  return (
                    <option key={mo} value={mo}>
                      {formatMonth(mo)}
                      {suffix}
                    </option>
                  )
                })}
              </select>
              {selected && selected.remaining <= 0 && (
                <p className="mt-1 text-xs text-amber-600">
                  Bu oy allaqachon to'langan — to'lov avans sifatida hisobga olinadi.
                </p>
              )}
            </div>

            <Input
              label="To'lov summasi (so'm)"
              type="number"
              min={0}
              step={50000}
              autoFocus
              value={amount}
              onChange={(e) => setAmount(Number(e.target.value))}
            />
            {amount > 0 && (
              <p className="text-sm text-slate-500">
                To'lovdan keyingi balans:{' '}
                <span className={cn('font-semibold', newBalance < 0 ? 'text-red-600' : 'text-emerald-600')}>
                  {formatMoney(newBalance)}
                </span>
              </p>
            )}
          </form>
        ))}
    </Modal>
  )
}
