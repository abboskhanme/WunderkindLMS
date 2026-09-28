/**
 * Create / edit a discount type — "Chegirma turlari" (docs/modules/billing-plans.md §8).
 *
 * Which abonement type (category) it applies to is chosen once, on creation, and then shown
 * read-only: the server does not accept it on update.
 *
 * A discount is a fixed so'm amount — no percent (client, 2026-09-28: "chegirma foizda emas summada
 * bo'lsin"); `percent` is always sent as 0.
 *
 * A new amount is not retroactive: the server moves every running discount of the type to the new
 * value from the next month, written invoices stay as they are. The form says so — with the
 * student count — BEFORE the user saves.
 */
import { useEffect, useState } from 'react'
import type { DiscountType, FeeCategory } from '@/types'
import type { DiscountTypeInput, DiscountTypeUpdate } from '@/api/services/billingCatalog'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { formatMoney } from '@/lib/utils'
import { Notice } from './BillingUi'

const NAME_MAX = 160

/** Same comparison the server uses for "unique among active types". */
const normalize = (name: string) => name.trim().toLocaleLowerCase('uz-Latn-UZ')

interface Props {
  open: boolean
  /** null = new type. */
  initial: DiscountType | null
  categories: FeeCategory[]
  /** Loaded types — only to warn about a taken name before the server's 409. */
  types: DiscountType[]
  busy: boolean
  error: string | null
  onClose: () => void
  onCreate: (values: DiscountTypeInput) => void
  onUpdate: (id: string, values: DiscountTypeUpdate) => void
}

export function DiscountTypeFormModal({
  open,
  initial,
  categories,
  types,
  busy,
  error,
  onClose,
  onCreate,
  onUpdate,
}: Props) {
  const editing = initial !== null

  const [name, setName] = useState('')
  const [amount, setAmount] = useState('')
  /** '' = every abonement type. */
  const [categoryId, setCategoryId] = useState('')

  useEffect(() => {
    if (!open) return
    /* eslint-disable react-hooks/set-state-in-effect -- sync the form with `initial` when the modal opens (intentional) */
    setName(initial?.name ?? '')
    setAmount(initial && initial.amount > 0 ? String(initial.amount) : '')
    setCategoryId(initial?.categoryId ?? '')
    /* eslint-enable react-hooks/set-state-in-effect */
  }, [open, initial])

  const trimmedName = name.trim()
  const amountNumber = amount.trim() === '' ? 0 : Number(amount)
  const amountValid = Number.isFinite(amountNumber) && amountNumber > 0

  // Uniqueness only binds active types; an archived type being edited does not compete.
  const willBeActive = initial?.isActive ?? true
  const nameTaken =
    willBeActive &&
    trimmedName !== '' &&
    types.some((t) => t.isActive && t.id !== initial?.id && normalize(t.name) === normalize(trimmedName))

  // An older type that still carries a percent becomes amount-only on save — also a change.
  const valueChanged =
    initial !== null && amountValid && (amountNumber !== initial.amount || initial.percent !== 0)

  const valid = trimmedName.length > 0 && trimmedName.length <= NAME_MAX && !nameTaken && amountValid

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!valid || busy) return
    if (editing && initial) {
      onUpdate(initial.id, {
        name: trimmedName,
        percent: 0,
        amount: amountNumber,
        // Archive / restore lives in the table actions; the form keeps the current state.
        isActive: initial.isActive,
      })
      return
    }
    onCreate({
      name: trimmedName,
      percent: 0,
      amount: amountNumber,
      categoryId: categoryId === '' ? null : categoryId,
    })
  }

  const activeCategories = categories.filter((c) => c.isActive)

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={editing ? 'Chegirma turini tahrirlash' : 'Yangi chegirma turi'}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button type="submit" form="discount-type-form" disabled={!valid || busy}>
            {busy ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <form id="discount-type-form" onSubmit={handleSubmit} className="space-y-4">
        <div>
          <Input
            label="Nomi"
            required
            maxLength={NAME_MAX}
            placeholder="masalan: Aka-uka chegirmasi"
            value={name}
            onChange={(e) => setName(e.target.value)}
          />
          {nameTaken && (
            <p className="mt-1 text-xs text-red-600">Bu nomli faol chegirma turi allaqachon bor.</p>
          )}
        </div>

        <div>
          <Input
            label="Summa (so'm)"
            required
            type="number"
            min={0}
            step={1000}
            inputMode="numeric"
            placeholder="masalan: 200000"
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
          />
          {amountValid ? (
            <p className="mt-1 text-xs text-slate-500">
              Har oy abonement narxidan {formatMoney(amountNumber)} ayriladi.
            </p>
          ) : (
            amount !== '' && <p className="mt-1 text-xs text-red-600">Summa 0 dan katta bo'lsin.</p>
          )}
        </div>

        {editing && initial ? (
          <div>
            <span className="mb-1 block text-sm font-medium text-slate-600">Qaysi abonementga</span>
            <div className="rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 text-sm font-medium text-slate-800">
              {initial.categoryName ?? 'Barcha abonementlar'}
            </div>
            <p className="mt-1 text-xs text-slate-400">
              Yaratilgandan keyin o'zgarmaydi — kerak bo'lsa yangi tur qo'shing.
            </p>
          </div>
        ) : (
          <div>
            <Select
              label="Qaysi abonementga"
              value={categoryId}
              onChange={(e) => setCategoryId(e.target.value)}
            >
              <option value="">Barcha abonementlar</option>
              {activeCategories.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </Select>
            <p className="mt-1 text-xs text-slate-400">
              Tur tanlansa, chegirma faqat shu turdagi abonementlarga qo'llanadi. Yaratilgandan
              keyin o'zgarmaydi.
            </p>
          </div>
        )}

        {initial && valueChanged &&
          (initial.studentCount > 0 ? (
            <Notice tone="info">
              Yangi qiymat shu turdagi {initial.studentCount} ta o'quvchining chegirmasiga keyingi
              oydan qo'llanadi. Hisoblangan oylar o'zgarmaydi.
            </Notice>
          ) : (
            <p className="text-xs text-slate-400">
              Hozir bu tur hech kimga biriktirilmagan — yangi qiymat bundan keyingi
              biriktirishlarga qo'llanadi.
            </p>
          ))}

        {error && <Notice>{error}</Notice>}
      </form>
    </Modal>
  )
}
