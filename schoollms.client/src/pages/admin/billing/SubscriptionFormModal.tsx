/**
 * Obuna ochish / tahrirlash.
 *
 * Tahrirlashda o'quvchi, toifa va boshlanish sanasi O'ZGARMAYDI (server
 * `UpdateSubscriptionRequest` da faqat narx, tafsilot va tugash sanasini
 * qabul qiladi): boshqa o'quvchiga "ko'chirilgan" obuna hisob-fakturalar
 * tarixini buzardi. Kerak bo'lsa eskisi yopiladi va yangisi ochiladi.
 */
import { useCallback, useEffect, useState } from 'react'
import type { FeeCategory } from '@/types'
import type {
  SubscriptionInput,
  SubscriptionRecord,
  SubscriptionUpdate,
} from '@/api/services/billingCatalog'
import { getSubscriptionDefault } from '@/api/services/billingCatalog'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { formatMoney } from '@/lib/utils'
import { Notice } from './BillingUi'
import { StudentSelect } from './StudentSelect'
import type { StudentOption } from './useStudents'
import { DatePicker } from '@/components/ui/DatePicker'

/** Toifaga qarab "tafsilot" maydonining nomi va namunasi. */
function detailHint(code: string | undefined): { label: string; placeholder: string } {
  switch (code) {
    case 'bus':
      return { label: "Yo'nalish", placeholder: "masalan: Yunusobod yo'nalishi" }
    case 'dormitory':
      return { label: 'Xona / blok', placeholder: 'masalan: 2-blok, 14-xona' }
    case 'meals':
      return { label: 'Ovqat turi', placeholder: 'masalan: Tushlik' }
    default:
      return { label: 'Tafsilot', placeholder: 'ixtiyoriy izoh' }
  }
}

const today = () => new Date().toISOString().slice(0, 10)

interface Props {
  open: boolean
  /** null = yangi obuna. */
  initial: SubscriptionRecord | null
  categories: FeeCategory[]
  students: StudentOption[]
  studentsLoading: boolean
  studentsError: string | null
  /** Yangi obuna shu o'quvchiga oldindan bog'lanadi (kartochkadagi tugma). */
  presetStudentId?: string
  busy: boolean
  error: string | null
  onClose: () => void
  onCreate: (values: SubscriptionInput) => void
  onUpdate: (id: string, values: SubscriptionUpdate) => void
}

export function SubscriptionFormModal({
  open,
  initial,
  categories,
  students,
  studentsLoading,
  studentsError,
  presetStudentId,
  busy,
  error,
  onClose,
  onCreate,
  onUpdate,
}: Props) {
  const editing = initial !== null

  const [studentId, setStudentId] = useState('')
  const [categoryId, setCategoryId] = useState('')
  const [amount, setAmount] = useState('')
  const [amountTouched, setAmountTouched] = useState(false)
  const [detail, setDetail] = useState('')
  const [startsOn, setStartsOn] = useState(today())
  const [endsOn, setEndsOn] = useState('')
  const [suggestion, setSuggestion] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    /* eslint-disable react-hooks/set-state-in-effect -- oyna ochilganda formani initial bilan sinxronlash (maqsadli) */
    setStudentId(initial?.studentId ?? presetStudentId ?? '')
    setCategoryId(initial?.categoryId ?? '')
    setAmount(initial ? String(initial.monthlyAmount) : '')
    setAmountTouched(false)
    setDetail(initial?.detail ?? '')
    setStartsOn(initial?.startsOn ?? today())
    setEndsOn(initial?.endsOn ?? '')
    setSuggestion(null)
    /* eslint-enable react-hooks/set-state-in-effect */
  }, [open, initial, presetStudentId])

  /**
   * Sinf oylik to'lovidan taklif — faqat YANGI obunada va admin summani
   * hali qo'lda tegmagan bo'lsa. Hech narsa yozilmaydi, faqat forma to'ladi.
   *
   * ATAYLAB `useEffect` emas, tanlov o'zgargan PAYTDA chaqiriladi: taklif
   * foydalanuvchi harakatining natijasi, holatlar sinxronizatsiyasi emas.
   */
  const applyDefault = useCallback(
    async (nextStudentId: string, nextCategoryId: string) => {
      if (editing || !nextStudentId || !nextCategoryId || amountTouched) return
      try {
        const d = await getSubscriptionDefault(nextStudentId, nextCategoryId)
        if (d.source === 'class_fee' && d.monthlyAmount > 0) {
          setAmount(String(d.monthlyAmount))
          setSuggestion(`Sinf oylik to'lovidan taklif: ${formatMoney(d.monthlyAmount)}`)
          return
        }
        setSuggestion(null)
      } catch {
        // Taklif — qulaylik, majburiyat emas. Yiqilsa admin summani o'zi yozadi.
        setSuggestion(null)
      }
    },
    [editing, amountTouched],
  )

  const selectedCategory = categories.find((c) => c.id === categoryId)
  const hint = detailHint(selectedCategory?.code ?? initial?.categoryCode)

  const amountNumber = Number(amount)
  const amountValid = amount.trim() !== '' && Number.isFinite(amountNumber) && amountNumber >= 0
  const periodValid = endsOn === '' || endsOn >= (initial?.startsOn ?? startsOn)
  const valid =
    amountValid && periodValid && (editing || (studentId !== '' && categoryId !== '' && startsOn !== ''))

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!valid || busy) return
    const trimmedDetail = detail.trim()
    if (editing && initial) {
      onUpdate(initial.id, {
        monthlyAmount: amountNumber,
        detail: trimmedDetail === '' ? undefined : trimmedDetail,
        endsOn: endsOn === '' ? undefined : endsOn,
      })
      return
    }
    onCreate({
      studentId,
      categoryId,
      monthlyAmount: amountNumber,
      detail: trimmedDetail === '' ? undefined : trimmedDetail,
      startsOn,
      endsOn: endsOn === '' ? undefined : endsOn,
    })
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={editing ? 'Obunani tahrirlash' : 'Yangi obuna'}
      size="md"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button type="submit" form="subscription-form" disabled={!valid || busy}>
            {busy ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <form id="subscription-form" onSubmit={handleSubmit} className="space-y-4">
        {editing && initial ? (
          <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm">
            <p className="font-medium text-slate-800">{initial.studentName}</p>
            <p className="text-slate-500">
              {initial.categoryName} · {initial.startsOn} dan
            </p>
          </div>
        ) : (
          <>
            <StudentSelect
              options={students}
              loading={studentsLoading}
              error={studentsError}
              value={studentId}
              onChange={(id) => {
                setStudentId(id)
                void applyDefault(id, categoryId)
              }}
              required
            />
            <Select
              label="To'lov toifasi"
              required
              value={categoryId}
              onChange={(e) => {
                setCategoryId(e.target.value)
                void applyDefault(studentId, e.target.value)
              }}
            >
              <option value="">Tanlang...</option>
              {categories
                .filter((c) => c.isActive)
                .map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
            </Select>
          </>
        )}

        <div>
          <Input
            label="Oylik summa (so'm)"
            required
            type="number"
            min={0}
            step={1000}
            inputMode="numeric"
            value={amount}
            onChange={(e) => {
              setAmount(e.target.value)
              setAmountTouched(true)
              setSuggestion(null)
            }}
          />
          {amountValid && amountNumber > 0 && (
            <p className="mt-1 text-xs text-slate-500">{formatMoney(amountNumber)}</p>
          )}
          {suggestion && <p className="mt-1 text-xs text-brand-600">{suggestion}</p>}
        </div>

        <Input
          label={hint.label}
          placeholder={hint.placeholder}
          value={detail}
          onChange={(e) => setDetail(e.target.value)}
        />

        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <div>
            <DatePicker
              label="Boshlanish sanasi"
              required
              value={editing && initial ? initial.startsOn : startsOn}
              disabled={editing}
              onChange={(value: string) => setStartsOn(value)}
            />
            {editing && (
              <p className="mt-1 text-xs text-slate-400">
                Boshlanish sanasi o'zgarmaydi — unga hisoblangan oylar bog'langan.
              </p>
            )}
          </div>
          <div>
            <DatePicker
              label="Tugash sanasi"
              value={endsOn}
              onChange={(value: string) => setEndsOn(value)}
            />
            <p className="mt-1 text-xs text-slate-400">
              Bo'sh qoldirilsa — muddatsiz, har oy hisoblanadi.
            </p>
          </div>
        </div>

        {!periodValid && (
          <Notice tone="danger">
            Tugash sanasi boshlanish sanasidan oldin bo'la olmaydi.
          </Notice>
        )}
        {error && <Notice>{error}</Notice>}
      </form>
    </Modal>
  )
}
