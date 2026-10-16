/**
 * Mahsulot qo'shish / tahrirlash — mini-magazin katalogi (2026-10-15, docs/modules/shop.md).
 * Mijoz: "Mahsulot nomi, turi va narxini kiritish" — plus the Kirim type it is sold under, because
 * that type decides which P&L line the sale lands on. Same shape as `TransactionTypeFormModal.tsx`.
 */
import { useEffect, useState } from 'react'
import type { ShopProduct, ShopProductInput, ShopProductKind } from '@/api/services/shop'
import { shopProductKindLabels, shopProductKinds } from '@/api/services/shop'
import type { TransactionType } from '@/api/services/transactionTypes'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { formatMoney } from '@/lib/utils'
import { accountLabel } from '@/pages/admin/finance/reportLabels'
import { Notice } from './BillingUi'

interface Props {
  open: boolean
  initial: ShopProduct | null
  /** Active Kirim types (plus the product's own, when editing) — the "Kirim turi" choices. */
  types: TransactionType[]
  busy: boolean
  error: string | null
  onClose: () => void
  onSubmit: (values: ShopProductInput) => void
}

export function ShopProductFormModal({ open, initial, types, busy, error, onClose, onSubmit }: Props) {
  const [name, setName] = useState('')
  const [kind, setKind] = useState<ShopProductKind>('book')
  const [price, setPrice] = useState('')
  const [transactionTypeId, setTransactionTypeId] = useState('')
  const [position, setPosition] = useState('0')
  const [isActive, setIsActive] = useState(true)

  useEffect(() => {
    if (!open) return
    /* eslint-disable react-hooks/set-state-in-effect -- oyna ochilganda formani initial bilan sinxronlash (maqsadli) */
    setName(initial?.name ?? '')
    setKind(initial?.kind ?? 'book')
    setPrice(initial ? String(initial.price) : '')
    setTransactionTypeId(initial?.transactionTypeId ?? '')
    setPosition(String(initial?.position ?? 0))
    setIsActive(initial?.isActive ?? true)
    /* eslint-enable react-hooks/set-state-in-effect */
  }, [open, initial])

  const editing = initial !== null
  const trimmedName = name.trim()
  const priceNumber = Number(price)
  // Tiyin at most — the server refuses a third decimal rather than rounding it away.
  const priceValid = price.trim() !== '' && Number.isFinite(priceNumber) && priceNumber >= 0 && /^\d+(\.\d{1,2})?$/.test(price.trim())
  const positionNumber = Number(position)
  const positionValid = position.trim() !== '' && Number.isInteger(positionNumber)
  const valid = trimmedName.length > 0 && priceValid && transactionTypeId !== '' && positionValid
  const selectedType = types.find((t) => t.id === transactionTypeId)
  const priceChanged = initial !== null && priceValid && priceNumber !== initial.price

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!valid || busy) return
    onSubmit({ name: trimmedName, kind, price: priceNumber, transactionTypeId, position: positionNumber, isActive })
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={editing ? 'Mahsulotni tahrirlash' : 'Yangi mahsulot'}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button type="submit" form="shop-product-form" disabled={!valid || busy}>
            {busy ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <form id="shop-product-form" onSubmit={handleSubmit} className="space-y-4">
        <Input
          label="Nomi"
          required
          autoFocus
          placeholder="masalan: Ingliz tili 5-sinf kitobi"
          value={name}
          onChange={(e) => setName(e.target.value)}
        />

        <Select label="Turi" required value={kind} onChange={(e) => setKind(e.target.value as ShopProductKind)}>
          {shopProductKinds.map((k) => (
            <option key={k} value={k}>
              {shopProductKindLabels[k]}
            </option>
          ))}
        </Select>

        <div>
          <Input
            label="Narx (so'm)"
            required
            type="number"
            min={0}
            step={1000}
            inputMode="decimal"
            placeholder="masalan: 85000"
            value={price}
            onChange={(e) => setPrice(e.target.value)}
          />
          {priceValid && <p className="mt-1 text-xs text-slate-500">{formatMoney(priceNumber)}</p>}
          {price.trim() !== '' && !priceValid && (
            <p className="mt-1 text-xs text-red-600">Narx manfiy bo'lmasin, ko'pi bilan 2 kasr.</p>
          )}
          {priceChanged && (
            <p className="mt-1 text-xs text-slate-400">Yangi narx faqat keyingi sotuvlarga qo'llanadi — sotilganlar o'zgarmaydi.</p>
          )}
        </div>

        <div>
          <Select
            label="Kirim turi"
            required
            value={transactionTypeId}
            onChange={(e) => setTransactionTypeId(e.target.value)}
          >
            <option value="" disabled>
              Tanlang...
            </option>
            {types.map((t) => (
              <option key={t.id} value={t.id}>
                {t.name}
              </option>
            ))}
          </Select>
          <p className="mt-1 text-xs text-slate-400">
            Kassada shu tur tanlanganda mahsulot ro'yxatda chiqadi.
            {selectedType && <> Sotuv P&amp;L'da "{accountLabel(selectedType.ledgerAccount)}" qatoriga tushadi.</>}
          </p>
          {types.length === 0 && (
            <p className="mt-1 text-xs text-red-600">Faol Kirim turi yo'q — avval "Tranzaksiya turi" bo'limida qo'shing.</p>
          )}
        </div>

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
          Faol — kassada sotiladi
        </label>

        {error && <Notice>{error}</Notice>}
      </form>
    </Modal>
  )
}
