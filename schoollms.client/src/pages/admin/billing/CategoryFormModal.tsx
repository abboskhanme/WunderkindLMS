/**
 * To'lov toifasini qo'shish / tahrirlash.
 *
 * KOD O'ZGARMAYDI. Yaratilgandan keyin `code` ga daromad hisobi
 * (`Accounts.RevenueFor`) va butun hisobot tarixi bog'lanadi — kodni
 * o'zgartirish eski daromadni jimgina boshqa hisobga ko'chirardi. Server
 * buni `category_code_immutable` bilan rad etadi; forma esa maydonni
 * umuman ochmaydi va sababini yozib qo'yadi.
 */
import { useEffect, useState } from 'react'
import type { FeeCategory } from '@/types'
import type { FeeCategoryInput } from '@/api/services/billingCatalog'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Notice } from './BillingUi'

interface Props {
  open: boolean
  initial: FeeCategory | null
  busy: boolean
  error: string | null
  onClose: () => void
  onSubmit: (values: FeeCategoryInput) => void
}

export function CategoryFormModal({ open, initial, busy, error, onClose, onSubmit }: Props) {
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [isActive, setIsActive] = useState(true)

  useEffect(() => {
    if (!open) return
    /* eslint-disable react-hooks/set-state-in-effect -- oyna ochilganda formani initial bilan sinxronlash (maqsadli) */
    setCode(initial?.code ?? '')
    setName(initial?.name ?? '')
    setIsActive(initial?.isActive ?? true)
    /* eslint-enable react-hooks/set-state-in-effect */
  }, [open, initial])

  const editing = initial !== null
  const trimmedName = name.trim()
  const trimmedCode = code.trim().toLowerCase()
  const valid = trimmedName.length > 0 && trimmedCode.length > 0

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!valid || busy) return
    onSubmit({ code: trimmedCode, name: trimmedName, isActive })
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={editing ? 'Toifani tahrirlash' : 'Yangi to\'lov toifasi'}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button type="submit" form="fee-category-form" disabled={!valid || busy}>
            {busy ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <form id="fee-category-form" onSubmit={handleSubmit} className="space-y-4">
        <div>
          <Input
            label="Kod"
            required
            placeholder="masalan: club"
            value={code}
            disabled={editing}
            onChange={(e) => setCode(e.target.value)}
          />
          <p className="mt-1 text-xs text-slate-400">
            {editing
              ? "Kod o'zgartirilmaydi: unga daromad hisobi va hisobot tarixi bog'langan."
              : 'Faqat lotin harflari, kichik yoziladi. Keyin o\'zgartirib bo\'lmaydi.'}
          </p>
        </div>

        <Input
          label="Nomi"
          required
          placeholder="masalan: To'garak"
          value={name}
          onChange={(e) => setName(e.target.value)}
        />

        <label className="inline-flex cursor-pointer items-center gap-2 text-sm font-medium text-slate-700">
          <input
            type="checkbox"
            checked={isActive}
            onChange={(e) => setIsActive(e.target.checked)}
            className="h-4 w-4 rounded border-slate-300 accent-brand-600"
          />
          Faol — yangi obunalarda tanlash mumkin
        </label>

        {error && <Notice>{error}</Notice>}
      </form>
    </Modal>
  )
}
