/**
 * Chegirma SO'RASH.
 *
 * Mijoz javobi (SPEC §8.1 Q5): CHEGARA YO'Q — har qanday chegirma direktor
 * tasdig'ini talab qiladi. Shuning uchun bu formada "darhol qo'llash"
 * varianti YO'Q va bo'lmaydi; oyna ochilishidayoq nima bo'lishini aytadi.
 *
 * Foiz avval, summa keyin ayriladi (server `DiscountService.ChargeFor` bilan
 * bir xil tartib) — forma shuni izohda takrorlaydi, chunki tartib natijani
 * o'zgartiradi.
 */
import { useEffect, useState } from 'react'
import { Clock } from 'lucide-react'
import type { FeeCategory } from '@/types'
import type { DiscountInput } from '@/api/services/billingCatalog'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { formatMoney } from '@/lib/utils'
import { Notice } from './BillingUi'
import { StudentSelect } from './StudentSelect'
import type { StudentOption } from './useStudents'
import { DatePicker } from '@/components/ui/DatePicker'

const today = () => new Date().toISOString().slice(0, 10)

interface Props {
  open: boolean
  categories: FeeCategory[]
  students: StudentOption[]
  studentsLoading: boolean
  studentsError: string | null
  busy: boolean
  error: string | null
  onClose: () => void
  onSubmit: (values: DiscountInput) => void
}

export function DiscountFormModal({
  open,
  categories,
  students,
  studentsLoading,
  studentsError,
  busy,
  error,
  onClose,
  onSubmit,
}: Props) {
  const [studentId, setStudentId] = useState('')
  const [categoryId, setCategoryId] = useState('')
  const [percent, setPercent] = useState('')
  const [amount, setAmount] = useState('')
  const [reason, setReason] = useState('')
  const [startsOn, setStartsOn] = useState(today())
  const [endsOn, setEndsOn] = useState('')

  useEffect(() => {
    if (!open) return
    /* eslint-disable react-hooks/set-state-in-effect -- oyna ochilganda formani tozalash (maqsadli) */
    setStudentId('')
    setCategoryId('')
    setPercent('')
    setAmount('')
    setReason('')
    setStartsOn(today())
    setEndsOn('')
    /* eslint-enable react-hooks/set-state-in-effect */
  }, [open])

  const percentNumber = percent.trim() === '' ? 0 : Number(percent)
  const amountNumber = amount.trim() === '' ? 0 : Number(amount)

  const percentValid = Number.isFinite(percentNumber) && percentNumber >= 0 && percentNumber <= 100
  const amountValid = Number.isFinite(amountNumber) && amountNumber >= 0
  const notEmpty = percentNumber > 0 || amountNumber > 0
  const periodValid = endsOn === '' || endsOn >= startsOn
  const valid =
    studentId !== '' &&
    percentValid &&
    amountValid &&
    notEmpty &&
    reason.trim().length > 0 &&
    startsOn !== '' &&
    periodValid

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!valid || busy) return
    onSubmit({
      studentId,
      categoryId: categoryId === '' ? undefined : categoryId,
      percent: percentNumber,
      amount: amountNumber,
      reason: reason.trim(),
      startsOn,
      endsOn: endsOn === '' ? undefined : endsOn,
    })
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Chegirma so'rash"
      size="md"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button type="submit" form="discount-form" disabled={!valid || busy}>
            {busy ? 'Yuborilmoqda...' : 'Tasdiqqa yuborish'}
          </Button>
        </>
      }
    >
      <form id="discount-form" onSubmit={handleSubmit} className="space-y-4">
        <div className="flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800">
          <Clock className="mt-0.5 h-4 w-4 shrink-0" />
          <p>
            Chegirma <b>tasdiq navbatiga</b> tushadi. Direktor tasdiqlamaguncha hisob-kitobga
            ta'sir qilmaydi — hisob-faktura to'liq summada hisoblanaveradi.
          </p>
        </div>

        <StudentSelect
          options={students}
          loading={studentsLoading}
          error={studentsError}
          value={studentId}
          onChange={setStudentId}
          required
        />

        <div>
          <Select
            label="To'lov toifasi"
            value={categoryId}
            onChange={(e) => setCategoryId(e.target.value)}
          >
            <option value="">Barcha toifalarga</option>
            {categories
              .filter((c) => c.isActive)
              .map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
          </Select>
          <p className="mt-1 text-xs text-slate-400">
            Bo'sh qoldirilsa chegirma o'quvchining barcha toifalariga qo'llanadi.
          </p>
        </div>

        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <div>
            <Input
              label="Foiz (%)"
              type="number"
              min={0}
              max={100}
              step={0.5}
              inputMode="decimal"
              placeholder="0"
              value={percent}
              onChange={(e) => setPercent(e.target.value)}
            />
            {!percentValid && (
              <p className="mt-1 text-xs text-red-600">Foiz 0 va 100 orasida bo'lsin.</p>
            )}
          </div>
          <div>
            <Input
              label="Aniq summa (so'm)"
              type="number"
              min={0}
              step={1000}
              inputMode="numeric"
              placeholder="0"
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
            />
            {amountValid && amountNumber > 0 && (
              <p className="mt-1 text-xs text-slate-500">{formatMoney(amountNumber)}</p>
            )}
          </div>
        </div>

        <p className="text-xs text-slate-400">
          Avval foiz ayriladi, keyin aniq summa. Ikkalasini birga kiritish mumkin.
        </p>

        <Textarea
          label="Sabab"
          required
          rows={2}
          placeholder="masalan: Aka-uka chegirmasi"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
        />

        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <DatePicker
            label="Boshlanish sanasi"
            required
            value={startsOn}
            onChange={(value: string) => setStartsOn(value)}
          />
          <div>
            <DatePicker
              label="Tugash sanasi"
              value={endsOn}
              onChange={(value: string) => setEndsOn(value)}
            />
            <p className="mt-1 text-xs text-slate-400">Bo'sh = muddatsiz.</p>
          </div>
        </div>

        {!notEmpty && (percent !== '' || amount !== '') && (
          <Notice tone="danger">
            Chegirma bo'sh: foiz ham, summa ham 0. Hech bo'lmaganda bittasini kiriting.
          </Notice>
        )}
        {!periodValid && (
          <Notice tone="danger">Tugash sanasi boshlanish sanasidan oldin bo'la olmaydi.</Notice>
        )}
        {error && <Notice>{error}</Notice>}
      </form>
    </Modal>
  )
}
