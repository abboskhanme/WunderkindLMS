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
import { TEACHER_POSITION } from '@/lib/employees'
import { Notice } from '../billing/BillingUi'

/** Bitta ro'yxatdagi xodim (o'qituvchi yoki boshqa xodim) — tanlov qiymati `kind:id`. */
interface EmployeeOption {
  value: string
  kind: EmployeeKind
  id: string
  fullName: string
  position: string
}

const optionValue = (kind: EmployeeKind, id: string) => `${kind}:${id}`

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

  // Tanlangan xodim: `teacher:<id>` yoki `staff:<id>` (mijoz, 2026-09-26: bitta ro'yxat).
  const [employee, setEmployee] = useState('')
  const [employeeSearch, setEmployeeSearch] = useState('')
  const [reasonId, setReasonId] = useState('')
  const [amount, setAmount] = useState('')
  const [periodYear, setPeriodYear] = useState(today.getFullYear())
  const [periodMonth, setPeriodMonth] = useState(today.getMonth() + 1)
  const [comment, setComment] = useState('')
  const [imageUrl, setImageUrl] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    /* eslint-disable react-hooks/set-state-in-effect -- oyna ochilganda formani boshlang'ich holatga keltirish (maqsadli) */
    setEmployee('')
    setEmployeeSearch('')
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

  // O'qituvchilar va boshqa xodimlar — BITTA ro'yxat, ism bo'yicha, lavozimi bilan.
  const employees = useMemo<EmployeeOption[]>(
    () =>
      [
        ...teachers.map((t) => ({
          value: optionValue('teacher', t.id),
          kind: 'teacher' as const,
          id: t.id,
          fullName: t.fullName,
          position: TEACHER_POSITION,
        })),
        ...staff.map((s) => ({
          value: optionValue('staff', s.id),
          kind: 'staff' as const,
          id: s.id,
          fullName: s.fullName,
          position: s.position || 'Xodim',
        })),
      ].sort((a, b) => a.fullName.localeCompare(b.fullName, 'uz')),
    [teachers, staff],
  )
  const selected = employees.find((e) => e.value === employee) ?? null
  // Qidiruv ro'yxatni toraytiradi; tanlangan xodim esa har doim ro'yxatda qoladi.
  const shownEmployees = useMemo(() => {
    const q = employeeSearch.trim().toLocaleLowerCase('uz')
    if (!q) return employees
    return employees.filter(
      (e) =>
        e.value === employee ||
        e.fullName.toLocaleLowerCase('uz').includes(q) ||
        e.position.toLocaleLowerCase('uz').includes(q),
    )
  }, [employees, employeeSearch, employee])

  const parsedAmount = Number(amount.replace(/\s/g, '').replace(',', '.'))
  const valid =
    selected !== null &&
    reasonId !== '' &&
    Number.isFinite(parsedAmount) &&
    parsedAmount > 0 &&
    periodMonth >= 1 &&
    periodMonth <= 12

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!valid || busy || !selected) return
    onSubmit({
      employeeKind: selected.kind,
      employeeId: selected.id,
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
        <div className="space-y-2">
          <Input
            label="Xodim"
            required
            type="search"
            placeholder="Ism yoki lavozim bo'yicha qidirish..."
            value={employeeSearch}
            onChange={(e) => setEmployeeSearch(e.target.value)}
          />
          <Select
            aria-label="Xodimni tanlang"
            value={employee}
            onChange={(e) => setEmployee(e.target.value)}
          >
            <option value="">
              {shownEmployees.length === 0 ? 'Hech kim topilmadi' : `Tanlang... (${shownEmployees.length} ta)`}
            </option>
            {shownEmployees.map((p) => (
              <option key={p.value} value={p.value}>
                {p.fullName} — {p.position}
              </option>
            ))}
          </Select>
        </div>

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
