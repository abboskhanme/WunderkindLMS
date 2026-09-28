/**
 * Create / edit an abonement (docs/modules/billing-plans.md §5–6).
 *
 * The TYPE (category) is chosen once, on creation, and then shown read-only: the server
 * does not accept it on update, because revenue accounts and reports run by category.
 *
 * A price change is not retroactive: the server moves every live subscription of the plan
 * to the new price from the first day of next month. The form says so — with the student
 * count and the date — BEFORE the user saves, so nobody is surprised by it afterwards.
 */
import { useEffect, useState } from 'react'
import type { FeeCategory, SubscriptionPlan } from '@/types'
import type {
  SubscriptionPlanInput,
  SubscriptionPlanUpdate,
} from '@/api/services/billingCatalog'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { formatMoney } from '@/lib/utils'
import { Notice } from './BillingUi'
import { DAYS_PER_WEEK_OPTIONS, formatDayMonth, nextMonthStart, scheduleLabel } from './planUtils'

const NAME_MAX = 160

/** Same comparison the server uses for "unique among active plans". */
const normalize = (name: string) => name.trim().toLocaleLowerCase('uz-Latn-UZ')

interface Props {
  open: boolean
  /** null = new abonement. */
  initial: SubscriptionPlan | null
  categories: FeeCategory[]
  /** Loaded plans — only to warn about a taken name before the server's 409. */
  plans: SubscriptionPlan[]
  busy: boolean
  error: string | null
  onClose: () => void
  onCreate: (values: SubscriptionPlanInput) => void
  onUpdate: (id: string, values: SubscriptionPlanUpdate) => void
}

export function PlanFormModal({
  open,
  initial,
  categories,
  plans,
  busy,
  error,
  onClose,
  onCreate,
  onUpdate,
}: Props) {
  const editing = initial !== null

  const [name, setName] = useState('')
  const [categoryId, setCategoryId] = useState('')
  const [price, setPrice] = useState('')
  /** '' = "Butun oy", '1'..'7' = "Haftasiga N kun". */
  const [schedule, setSchedule] = useState('')

  useEffect(() => {
    if (!open) return
    /* eslint-disable react-hooks/set-state-in-effect -- sync the form with `initial` when the modal opens (intentional) */
    setName(initial?.name ?? '')
    setCategoryId(initial?.categoryId ?? '')
    setPrice(initial ? String(initial.monthlyAmount) : '')
    setSchedule(initial?.daysPerWeek != null ? String(initial.daysPerWeek) : '')
    /* eslint-enable react-hooks/set-state-in-effect */
  }, [open, initial])

  const trimmedName = name.trim()
  const priceNumber = Number(price)
  const priceValid = price.trim() !== '' && Number.isFinite(priceNumber) && priceNumber >= 0

  // Uniqueness only binds active plans; an archived plan being edited does not compete.
  const willBeActive = initial?.isActive ?? true
  const nameTaken =
    willBeActive &&
    trimmedName !== '' &&
    plans.some((p) => p.isActive && p.id !== initial?.id && normalize(p.name) === normalize(trimmedName))

  const priceChanged = initial !== null && priceValid && priceNumber !== initial.monthlyAmount
  const effectiveFrom = formatDayMonth(nextMonthStart())

  const valid =
    trimmedName.length > 0 &&
    trimmedName.length <= NAME_MAX &&
    !nameTaken &&
    priceValid &&
    (editing || categoryId !== '')

  const daysPerWeek = schedule === '' ? null : Number(schedule)

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!valid || busy) return
    if (editing && initial) {
      onUpdate(initial.id, {
        name: trimmedName,
        monthlyAmount: priceNumber,
        // Archive / restore lives in the table actions; the form keeps the current state.
        isActive: initial.isActive,
        daysPerWeek,
      })
      return
    }
    onCreate({ name: trimmedName, categoryId, monthlyAmount: priceNumber, daysPerWeek })
  }

  const activeCategories = categories.filter((c) => c.isActive)

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={editing ? 'Abonementni tahrirlash' : 'Yangi abonement'}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button type="submit" form="subscription-plan-form" disabled={!valid || busy}>
            {busy ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <form id="subscription-plan-form" onSubmit={handleSubmit} className="space-y-4">
        <div>
          <Input
            label="Nomi"
            required
            maxLength={NAME_MAX}
            placeholder="masalan: 5-11 sinflar"
            value={name}
            onChange={(e) => setName(e.target.value)}
          />
          {nameTaken && (
            <p className="mt-1 text-xs text-red-600">Bu nomli faol abonement allaqachon bor.</p>
          )}
        </div>

        {editing && initial ? (
          <div>
            <span className="mb-1 block text-sm font-medium text-slate-600">Turi</span>
            <div className="rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 text-sm font-medium text-slate-800">
              {initial.categoryName}
            </div>
            <p className="mt-1 text-xs text-slate-400">
              Tur o'zgarmaydi — hisob-kitob va hisobotlar shu tur bo'yicha yuritiladi.
            </p>
          </div>
        ) : (
          <div>
            <Select
              label="Turi"
              required
              value={categoryId}
              onChange={(e) => setCategoryId(e.target.value)}
            >
              <option value="">Tanlang...</option>
              {activeCategories.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </Select>
            <p className="mt-1 text-xs text-slate-400">
              {activeCategories.length === 0
                ? "Faol to'lov toifasi yo'q — avval To'lov toifalari sahifasida qo'shing."
                : "Yaratilgandan keyin tur o'zgartirilmaydi."}
            </p>
          </div>
        )}

        <div>
          <Input
            label="Oylik narx (so'm)"
            required
            type="number"
            min={0}
            step={1000}
            inputMode="numeric"
            placeholder="masalan: 1690000"
            value={price}
            onChange={(e) => setPrice(e.target.value)}
          />
          {priceValid && (
            <p className="mt-1 text-xs text-slate-500">{formatMoney(priceNumber)}</p>
          )}
        </div>

        <div>
          <Select label="Jadval" value={schedule} onChange={(e) => setSchedule(e.target.value)}>
            <option value="">{scheduleLabel(null)}</option>
            {DAYS_PER_WEEK_OPTIONS.map((n) => (
              <option key={n} value={String(n)}>
                {scheduleLabel(n)}
              </option>
            ))}
          </Select>
          <p className="mt-1 text-xs text-slate-400">
            Narx oylik qoladi — jadval faqat kasallik chegirmasida dars kunlarini sanash uchun.
          </p>
        </div>

        {initial && priceChanged &&
          (initial.studentCount > 0 ? (
            <Notice tone="info">
              Yangi narx shu abonementdagi {initial.studentCount} ta o'quvchiga {effectiveFrom} dan
              qo'llanadi. O'tgan oylar o'zgarmaydi.
            </Notice>
          ) : (
            <p className="text-xs text-slate-400">
              Hozir bu abonementda o'quvchi yo'q — yangi narx bundan keyin ochiladigan obunalarga
              qo'llanadi.
            </p>
          ))}

        {error && <Notice>{error}</Notice>}
      </form>
    </Modal>
  )
}
