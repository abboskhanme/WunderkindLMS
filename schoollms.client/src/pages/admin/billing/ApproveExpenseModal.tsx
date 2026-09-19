/**
 * Chiqimni tasdiqlash (SPEC §4.5) — ikkinchi shaxsning qarori.
 *
 * NEGA BU YERDA TO'LOV USULI SO'RALADI (F1.02)
 * --------------------------------------------
 * Chegaradan yuqori chiqim jurnalga tasdiq lahzasida tushadi va AYNAN shunda
 * pul qaysi hisobdan chiqishi hal bo'ladi: naqd — kassadan, qolgan usullar —
 * bankdan. `expenses` jadvalida usul ustuni yo'q (u jurnalning kredit
 * satrida yashaydi), shuning uchun yozgan odamning tanlovi saqlanmaydi —
 * qarorni pul chiqishiga ruxsat bergan odam beradi. Ilgari bu tugma tanasiz
 * `POST` qilardi va server so'rovni o'qiy olmasdi: tasdiqlash HAR SAFAR
 * yiqilardi.
 *
 * Tugma o'zi yozgan chiqimda ko'rsatilmaydi — buni sahifa hal qiladi
 * (`canApproveRecord`), server esa `self_approval` bilan mustaqil rad etadi.
 */
import { useEffect, useState } from 'react'
import { ShieldCheck } from 'lucide-react'
import type { ExpenseRecord } from '@/api/services/expenses'
import type { PaymentMethod } from '@/types'
import { financeCategoryLabel } from '@/config/constants'
import { paymentMethodLabels } from '@/pages/admin/finance/reportLabels'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Select } from '@/components/ui/Input'
import { formatMoney } from '@/lib/utils'
import { Notice } from './BillingUi'
import { formatDate } from '@/lib/utils'

const methods: PaymentMethod[] = ['cash', 'card', 'transfer', 'online']

interface Props {
  /** Tasdiqlanayotgan chiqim; `null` — oyna yopiq. */
  expense: ExpenseRecord | null
  busy: boolean
  error: string | null
  onClose: () => void
  onConfirm: (method: PaymentMethod) => void
}

export function ApproveExpenseModal({ expense, busy, error, onClose, onConfirm }: Props) {
  const [method, setMethod] = useState<PaymentMethod>('cash')

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oyna ochilganda tanlovni tozalash (maqsadli)
    if (expense) setMethod('cash')
  }, [expense])

  return (
    <Modal
      open={expense !== null}
      onClose={onClose}
      title="Chiqimni tasdiqlash"
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button disabled={busy} onClick={() => onConfirm(method)}>
            <ShieldCheck className="h-4 w-4" />
            {busy ? 'Tasdiqlanmoqda...' : 'Tasdiqlash'}
          </Button>
        </>
      }
    >
      {expense && (
        <div className="space-y-4">
          <div className="rounded-xl bg-slate-50 px-4 py-3 text-sm">
            <p className="font-semibold text-slate-800">
              {formatMoney(expense.amount)} — {financeCategoryLabel(expense.category)}
            </p>
            <p className="text-slate-500">
              {formatDate(expense.onDate)} · yozgan: {expense.createdByName}
            </p>
            {expense.note && <p className="mt-1 text-slate-500">{expense.note}</p>}
          </div>

          <Select
            label="To'lov usuli"
            required
            value={method}
            onChange={(e) => setMethod(e.target.value as PaymentMethod)}
          >
            {methods.map((m) => (
              <option key={m} value={m}>
                {paymentMethodLabels[m]}
              </option>
            ))}
          </Select>
          <p className="text-xs text-slate-500">
            {method === 'cash'
              ? 'Pul kassadan chiqadi.'
              : 'Pul bank hisobidan chiqadi.'}{' '}
            Tasdiqlagandan keyin chiqim jurnalga tushadi va hisobotlarda ko'rinadi. Xato
            bo'lsa faqat storno bilan tuzatiladi.
          </p>

          {error && <Notice>{error}</Notice>}
        </div>
      )}
    </Modal>
  )
}
