/**
 * Yangi bonus/jarima yozish (F11.01). `kind` sahifadan keladi va o'zgarmaydi
 * — bitta komponent ikkita marshrutga xizmat qiladi ("Bonus" / "Jarima"),
 * lekin bitta ochilgan forma ikkalasini aralashtirmaydi.
 */
import { useEffect, useMemo, useState } from 'react'
import type { Teacher, Staff } from '@/types'
import { getTeachers } from '@/api/services/teachers'
import { getStaff } from '@/api/services/staff'
import type {
  AdjustmentKind,
  AdjustmentReason,
  CreatePayrollAdjustmentInput,
  EmployeeKind,
} from '@/api/services/payrollAdjustments'
import { getAdjustmentReasons } from '@/api/services/payrollAdjustments'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { PhotoUpload } from '@/components/ui/PhotoUpload'
import { Notice } from '../billing/BillingUi'

const MONTHS = [
  "Yanvar", "Fevral", "Mart", "Aprel", "May", "Iyun",
  "Iyul", "Avgust", "Sentabr", "Oktabr", "Noyabr", "Dekabr",
]

interface Props {
  open: boolean
  kind: AdjustmentKind
  busy: boolean
  error: string | null
  onClose: () => void
  onSubmit: (values: CreatePayrollAdjustmentInput) => void
}

export function AdjustmentFormModal({ open, kind, busy, error, onClose, onSubmit }: Props) {
  const today = new Date()

  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [staff, setStaff] = useState<Staff[]>([])
  const [reasons, setReasons] = useState<AdjustmentReason[]>([])

  const [employeeKind, setEmployeeKind] = useState<EmployeeKind>('teacher')
  const [employeeId, setEmployeeId] = useState('')
  const [reasonId, setReasonId] = useState('')
  const [amount, setAmount] = useState('')
  const [periodYear, setPeriodYear] = useState(today.getFullYear())
  const [periodMonth, setPeriodMonth] = useState(today.getMonth() + 1)
  const [comment, setComment] = useState('')
  const [imageUrl, setImageUrl] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    /* eslint-disable react-hooks/set-state-in-effect -- oyna ochilganda formani boshlang'ich holatga keltirish (maqsadli) */
    setEmployeeKind('teacher')
    setEmployeeId('')
    setReasonId('')
    setAmount('')
    setPeriodYear(today.getFullYear())
    setPeriodMonth(today.getMonth() + 1)
    setComment('')
    setImageUrl(null)
    /* eslint-enable react-hooks/set-state-in-effect */

    getTeachers().then(setTeachers).catch(() => setTeachers([]))
    getStaff().then(setStaff).catch(() => setStaff([]))
    getAdjustmentReasons(kind).then(setReasons).catch(() => setReasons([]))
    // eslint-disable-next-line react-hooks/exhaustive-deps -- faqat oyna ochilganda bir marta
  }, [open, kind])

  const activeReasons = useMemo(() => reasons.filter((r) => r.isActive), [reasons])

  const parsedAmount = Number(amount.replace(/\s/g, '').replace(',', '.'))
  const valid =
    employeeId !== '' &&
    reasonId !== '' &&
    Number.isFinite(parsedAmount) &&
    parsedAmount > 0 &&
    periodMonth >= 1 &&
    periodMonth <= 12

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!valid || busy) return
    onSubmit({
      employeeKind,
      employeeId,
      kind,
      reasonId,
      amount: parsedAmount,
      periodYear,
      periodMonth,
      comment: comment.trim() || undefined,
      imageUrl: imageUrl ?? undefined,
    })
  }

  const title = kind === 'bonus' ? 'Yangi bonus' : 'Yangi jarima'

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={title}
      size="md"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button type="submit" form="adjustment-form" disabled={!valid || busy}>
            {busy ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <form id="adjustment-form" onSubmit={handleSubmit} className="space-y-4">
        <div>
          <span className="mb-1 block text-sm font-medium text-slate-600">
            Xodim turi <span className="text-red-500">*</span>
          </span>
          <div className="flex gap-2">
            {(['teacher', 'staff'] as const).map((k) => (
              <button
                key={k}
                type="button"
                onClick={() => {
                  setEmployeeKind(k)
                  setEmployeeId('')
                }}
                className={`flex-1 rounded-lg border px-3 py-2 text-sm font-medium transition-colors ${
                  employeeKind === k
                    ? 'border-brand-400 bg-brand-50 text-brand-700'
                    : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50'
                }`}
              >
                {k === 'teacher' ? "O'qituvchi" : 'Boshqa xodim'}
              </button>
            ))}
          </div>
        </div>

        <Select
          label="Xodim"
          required
          value={employeeId}
          onChange={(e) => setEmployeeId(e.target.value)}
        >
          <option value="">Tanlang...</option>
          {(employeeKind === 'teacher' ? teachers : staff).map((p) => (
            <option key={p.id} value={p.id}>{p.fullName}</option>
          ))}
        </Select>

        <Select
          label="Sabab"
          required
          value={reasonId}
          onChange={(e) => setReasonId(e.target.value)}
        >
          <option value="">Tanlang...</option>
          {activeReasons.map((r) => (
            <option key={r.id} value={r.id}>{r.name}</option>
          ))}
        </Select>
        {activeReasons.length === 0 && (
          <p className="-mt-2 text-xs text-amber-600">
            Faol sabab yo'q — avval "Sabablarni boshqarish" bo'limida qo'shing.
          </p>
        )}

        <Input
          label="Summa (so'm)"
          required
          inputMode="decimal"
          placeholder="masalan: 300000"
          value={amount}
          onChange={(e) => setAmount(e.target.value)}
        />

        <div className="grid grid-cols-2 gap-3">
          <Input
            label="Yil"
            type="number"
            required
            value={periodYear}
            onChange={(e) => setPeriodYear(Number(e.target.value))}
          />
          <Select
            label="Oy"
            required
            value={periodMonth}
            onChange={(e) => setPeriodMonth(Number(e.target.value))}
          >
            {MONTHS.map((name, i) => (
              <option key={name} value={i + 1}>{name}</option>
            ))}
          </Select>
        </div>

        <Textarea
          label="Izoh"
          rows={2}
          placeholder="Ixtiyoriy"
          value={comment}
          onChange={(e) => setComment(e.target.value)}
        />

        {kind === 'penalty' && (
          <PhotoUpload label="Rasm (ixtiyoriy)" value={imageUrl} onChange={setImageUrl} />
        )}

        {error && <Notice>{error}</Notice>}
      </form>
    </Modal>
  )
}
