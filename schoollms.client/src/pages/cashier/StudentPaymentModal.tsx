import { useEffect, useMemo, useState } from 'react'
import { AlertTriangle } from 'lucide-react'
import type { AllocationSuggestion, Payment, PaymentMethod } from '@/types'
import type { CashierStudent } from '@/api/services/cashier'
import { PROBE_AMOUNT, financeErrorMessage, isAborted, suggestAllocation } from '@/api/services/cashier'
import type { CashBox } from '@/api/services/cashBoxes'
import { getCashBoxes } from '@/api/services/cashBoxes'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Select } from '@/components/ui/Input'
import { IncomeForm } from './CashierPage'
import { PaymentSplitModal } from './PaymentSplitModal'
import { ReceiptPreview } from './ReceiptPreview'

interface StudentPaymentModalProps {
  open: boolean
  student: CashierStudent
  onClose: () => void
  /** To'lov yozilib, chek yopilgach — profil moliya ma'lumotini yangilaydi. */
  onPaid: () => void
}

/**
 * O'quvchi profilidan tezkor o'qish to'lovi (mijoz, 2026-09-24): "student profili ichiga ... birdaniga shu
 * student uchun o'quv to'lovi qiladigan modal window ... kassadagi kabi modal windowni shu yerda chaqirib
 * ishlatsin".
 *
 * Kassa sahifasining O'ZI ishlatiladi — `IncomeForm` → `PaymentSplitModal` → `ReceiptPreview`, ya'ni bir xil
 * FIFO taqsimot, bir xil server yo'li (`acceptPayment`) va bir xil chek. Farqi faqat kirish eshigida: o'quvchi
 * oldindan tanlangan va almashtirilmaydi, tur "O'quvchi oylik to'lovi" bilan cheklangan, kassa — asosiy
 * (default) kassa, faol kassa bir nechta bo'lsa tanlab olinadi.
 */
export function StudentPaymentModal({ open, student, onClose, onPaid }: StudentPaymentModalProps) {
  const [boxes, setBoxes] = useState<CashBox[]>([])
  const [boxId, setBoxId] = useState<string | null>(null)
  const [boxesLoading, setBoxesLoading] = useState(true)
  const [boxesError, setBoxesError] = useState<string | null>(null)

  const [invoices, setInvoices] = useState<AllocationSuggestion[]>([])
  const [invoicesLoading, setInvoicesLoading] = useState(true)
  const [invoicesError, setInvoicesError] = useState<string | null>(null)
  const [invoicesReload, setInvoicesReload] = useState(0)

  const [splitDraft, setSplitDraft] = useState<{
    amount: number
    method: PaymentMethod
    note: string
    receivedOn: string
    cashBoxId: string
  } | null>(null)
  const [receipt, setReceipt] = useState<Payment | null>(null)

  useEffect(() => {
    if (!open) return
    let alive = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oyna ochilganda kassalar yuklanadi (maqsadli)
    setBoxesLoading(true)
    setBoxesError(null)
    getCashBoxes()
      .then((rows) => {
        if (!alive) return
        const active = rows.filter((b) => b.isActive)
        setBoxes(active)
        setBoxId((current) =>
          current && active.some((b) => b.id === current)
            ? current
            : (active.find((b) => b.isDefault)?.id ?? active[0]?.id ?? null),
        )
      })
      .catch((err: unknown) => {
        if (alive) setBoxesError(financeErrorMessage(err, "Kassalarni yuklab bo'lmadi."))
      })
      .finally(() => {
        if (alive) setBoxesLoading(false)
      })
    return () => {
      alive = false
    }
  }, [open])

  useEffect(() => {
    if (!open) return
    const controller = new AbortController()
    // eslint-disable-next-line react-hooks/set-state-in-effect -- qarz ro'yxati qayta so'raladi, yuklanish holati shu yerda boshlanadi (maqsadli)
    setInvoicesLoading(true)
    setInvoicesError(null)
    // Kassa sahifasidagi kabi: `PROBE_AMOUNT` bilan faqat qoldiqlar o'qiladi, FIFO taklifi taqsimot oynasida.
    suggestAllocation(student.id, PROBE_AMOUNT, controller.signal)
      .then((rows) => {
        setInvoices(rows)
        setInvoicesLoading(false)
      })
      .catch((err: unknown) => {
        if (isAborted(err)) return
        setInvoicesError(financeErrorMessage(err, "Hisob-fakturalarni yuklab bo'lmadi."))
        setInvoicesLoading(false)
      })
    return () => controller.abort()
  }, [open, student.id, invoicesReload])

  const debt = useMemo(() => invoices.reduce((sum, row) => sum + row.remaining, 0), [invoices])
  const box = boxes.find((b) => b.id === boxId) ?? null

  return (
    <>
      <Modal
        open={open && splitDraft === null && receipt === null}
        onClose={onClose}
        title={box ? `O'qish to'lovi — ${box.name}` : "O'qish to'lovi"}
        size="md"
      >
        {boxesLoading ? (
          <Loader />
        ) : boxesError || !box ? (
          <div className="rounded-xl border border-red-200 bg-red-50/70 px-3 py-3">
            <div className="flex items-start gap-2 text-sm text-red-700">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
              <span>{boxesError ?? "Faol kassa yo'q — avval Kassa bo'limida kassa oching."}</span>
            </div>
          </div>
        ) : (
          <div className="space-y-4">
            {boxes.length > 1 && (
              <Select label="Kassa" value={box.id} onChange={(e) => setBoxId(e.target.value)}>
                {boxes.map((b) => (
                  <option key={b.id} value={b.id}>
                    {b.name}
                    {b.isDefault ? ' (asosiy)' : ''}
                  </option>
                ))}
              </Select>
            )}
            <IncomeForm
              key={box.id}
              box={box}
              student={student}
              onSelectStudent={() => {}}
              onClearStudent={() => {}}
              invoices={invoices}
              invoicesLoading={invoicesLoading}
              invoicesError={invoicesError}
              onInvoicesRetry={() => setInvoicesReload((n) => n + 1)}
              debt={debt}
              onStudentSubmit={setSplitDraft}
              onDone={onClose}
              onCancel={onClose}
              preferStudentType
              lockStudent
            />
          </div>
        )}
      </Modal>

      {splitDraft && (
        <PaymentSplitModal
          open
          student={student}
          amount={splitDraft.amount}
          method={splitDraft.method}
          note={splitDraft.note}
          receivedOn={splitDraft.receivedOn}
          cashBoxId={splitDraft.cashBoxId}
          onClose={() => setSplitDraft(null)}
          onAccepted={(payment) => {
            setSplitDraft(null)
            setReceipt(payment)
          }}
        />
      )}

      {receipt && (
        <ReceiptPreview
          open
          payment={receipt}
          onClose={() => {
            setReceipt(null)
            setInvoicesReload((n) => n + 1)
            onPaid()
          }}
        />
      )}
    </>
  )
}
