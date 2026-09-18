/**
 * Tranzaksiya turini qo'shish / tahrirlash.
 *
 * YARATISHDA `kind` TANLANADI (qaysi pill ochiq bo'lsa — o'sha), TAHRIRDA
 * O'ZGARMAYDI (`TransactionTypesPage.tsx` uni oldindan belgilaydi va bu
 * yerda ko'rsatiladi, lekin tahrirlanmaydi) — server ham xuddi shunday
 * (`TransactionTypeService.cs` izohi: eski kind bilan yozilgan tranzaksiyalar
 * bor bo'lishi mumkin).
 */
import { useEffect, useState } from 'react'
import type { TransactionType, TransactionTypeInput, TransactionTypeKind } from '@/api/services/transactionTypes'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Notice } from './BillingUi'

const kindLabels: Record<TransactionTypeKind, string> = { in: 'Kirim', out: 'Chiqim' }

interface Props {
  open: boolean
  kind: TransactionTypeKind
  initial: TransactionType | null
  busy: boolean
  error: string | null
  onClose: () => void
  onSubmit: (values: TransactionTypeInput) => void
}

export function TransactionTypeFormModal({ open, kind, initial, busy, error, onClose, onSubmit }: Props) {
  const [name, setName] = useState('')
  const [position, setPosition] = useState('0')
  const [isActive, setIsActive] = useState(true)

  useEffect(() => {
    if (!open) return
    /* eslint-disable react-hooks/set-state-in-effect -- oyna ochilganda formani initial bilan sinxronlash (maqsadli) */
    setName(initial?.name ?? '')
    setPosition(String(initial?.position ?? 0))
    setIsActive(initial?.isActive ?? true)
    /* eslint-enable react-hooks/set-state-in-effect */
  }, [open, initial])

  const editing = initial !== null
  const trimmedName = name.trim()
  const positionNumber = Number(position)
  const positionValid = position.trim() !== '' && Number.isInteger(positionNumber)
  const valid = trimmedName.length > 0 && positionValid

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!valid || busy) return
    onSubmit({ kind, name: trimmedName, position: positionNumber, isActive })
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={editing ? 'Turni tahrirlash' : `Yangi tur — ${kindLabels[kind]}`}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button type="submit" form="transaction-type-form" disabled={!valid || busy}>
            {busy ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <form id="transaction-type-form" onSubmit={handleSubmit} className="space-y-4">
        <p className="text-sm text-slate-500">
          Turkum: <span className="font-medium text-slate-700">{kindLabels[kind]}</span>
          {editing && ' — yaratilgandan keyin o\'zgarmaydi.'}
        </p>

        <Input
          label="Nomi"
          required
          autoFocus
          placeholder="masalan: Kitob uchun to'lov"
          value={name}
          onChange={(e) => setName(e.target.value)}
        />

        <div>
          <Input
            label="Tartib raqami"
            required
            type="number"
            step={1}
            inputMode="numeric"
            value={position}
            onChange={(e) => setPosition(e.target.value)}
          />
          <p className="mt-1 text-xs text-slate-400">Ro'yxatda kichigi tepada turadi.</p>
          {position.trim() !== '' && !positionValid && (
            <p className="mt-1 text-xs text-red-600">Butun son bo'lishi kerak.</p>
          )}
        </div>

        <label className="inline-flex cursor-pointer items-center gap-2 text-sm font-medium text-slate-700">
          <input
            type="checkbox"
            checked={isActive}
            onChange={(e) => setIsActive(e.target.checked)}
            className="h-4 w-4 rounded border-slate-300 accent-brand-600"
          />
          Faol — yangi tranzaksiyada tanlanadi
        </label>

        {error && <Notice>{error}</Notice>}
      </form>
    </Modal>
  )
}
