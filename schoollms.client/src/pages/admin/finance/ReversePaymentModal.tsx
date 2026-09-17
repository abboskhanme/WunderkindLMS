/**
 * TO'LOVNI STORNO QILISH (docs/modules/finance-parity.md §2.9, F9.02).
 *
 * Nega alohida oyna, `ReasonModal` emas: bu yerda sabab so'rashdan tashqari
 * IKKI narsa kerak — (1) qaysi chek bekor qilinayotgani ko'rinib tursin
 * (kassir kuniga o'nlab chek yozadi, "storno qildim" degan xato qimmat),
 * (2) serverning rad etish SABABIGA qarab nima qilish kerakligi aytilsin.
 *
 * STORNO — O'CHIRISH EMAS (SPEC §4.1). Asl qator joyida qoladi, ustiga
 * qarshi qator qo'shiladi va ikkalasi ham jurnalda ko'rinib turadi.
 */
import { useEffect, useState } from 'react'
import { Undo2 } from 'lucide-react'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Textarea } from '@/components/ui/Input'
import { Notice } from '@/pages/admin/billing/BillingUi'
import { formatMoney } from '@/lib/utils'
import { formatDateTime, paymentMethodLabel } from './reportLabels'
import type { TransactionRow } from '@/api/services/transactions'

const MIN_REASON = 3

interface Props {
  /** Storno qilinayotgan qator; `null` — oyna yopiq. */
  row: TransactionRow | null
  busy: boolean
  error: string | null
  /** Serverning mashina o'qiydigan kodi (`no_open_shift`, ...). */
  errorCode: string | null
  onClose: () => void
  onConfirm: (reason: string) => void
}

/** Kodga qarab "endi nima qilish kerak" — matn emas, KOD bo'yicha. */
function hintFor(code: string | null): string | null {
  if (code === 'no_open_shift') {
    return "Storno qatori sizning O'Z ochiq smenangizga tushadi — pul bugun, sizning "
      + "kassangizdan chiqadi. Avval kassada smena oching."
  }
  if (code === 'own_payment_reversal') {
    return "Ikki qavatli nazorat (SPEC §4.5): o'zingiz qabul qilgan to'lovni o'zingiz "
      + 'storno qila olmaysiz. Buni boshqa admin yoki direktor bajarsin.'
  }
  if (code === 'already_reversed') {
    return "Bu to'lov allaqachon storno qilingan — ro'yxatni yangilang."
  }
  return null
}

export function ReversePaymentModal({ row, busy, error, errorCode, onClose, onConfirm }: Props) {
  const [reason, setReason] = useState('')

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oyna ochilganda formani tozalash (maqsadli)
    if (row) setReason('')
  }, [row])

  const trimmed = reason.trim()
  const valid = trimmed.length >= MIN_REASON
  const hint = hintFor(errorCode)

  return (
    <Modal
      open={row !== null}
      onClose={onClose}
      title="To'lovni storno qilish"
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button variant="danger" disabled={!valid || busy} onClick={() => onConfirm(trimmed)}>
            <Undo2 className="h-4 w-4" />
            {busy ? 'Bajarilmoqda...' : 'Storno qilish'}
          </Button>
        </>
      }
    >
      {row && (
        <div className="space-y-4">
          <div className="rounded-xl border border-slate-200 bg-slate-50/70 px-4 py-3 text-sm">
            <div className="flex items-baseline justify-between gap-3">
              <span className="text-slate-500">Chek №</span>
              <span className="font-semibold text-slate-800">{row.receiptNo ?? '—'}</span>
            </div>
            <div className="mt-1 flex items-baseline justify-between gap-3">
              <span className="text-slate-500">O'quvchi</span>
              <span className="font-medium text-slate-800">{row.personName ?? '—'}</span>
            </div>
            <div className="mt-1 flex items-baseline justify-between gap-3">
              <span className="text-slate-500">Summa</span>
              <span className="font-semibold text-slate-800">{formatMoney(row.amount)}</span>
            </div>
            <div className="mt-1 flex items-baseline justify-between gap-3">
              <span className="text-slate-500">Usul</span>
              <span className="text-slate-700">
                {row.method ? paymentMethodLabel(row.method) : '—'}
              </span>
            </div>
            <div className="mt-1 flex items-baseline justify-between gap-3">
              <span className="text-slate-500">Sana</span>
              <span className="text-slate-700">{formatDateTime(row.occurredAt)}</span>
            </div>
          </div>

          <p className="text-sm text-slate-600">
            To'lov <b>o'chirilmaydi</b>: u jurnalda qolib, ustiga qarshi qator qo'shiladi.
            Ikkalasi ham ro'yxatda ko'rinib turadi, sof summa esa nolga tushadi.
          </p>

          <Textarea
            label="Sabab"
            required
            rows={3}
            placeholder="Masalan: summa noto'g'ri kiritilgan"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
          />
          {!valid && trimmed.length > 0 && (
            <p className="text-xs text-slate-400">
              Sabab kamida {MIN_REASON} ta belgidan iborat bo'lsin.
            </p>
          )}

          {error && <Notice>{error}</Notice>}
          {hint && <Notice tone="info">{hint}</Notice>}
        </div>
      )}
    </Modal>
  )
}
