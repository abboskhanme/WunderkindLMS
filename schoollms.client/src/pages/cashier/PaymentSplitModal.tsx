import { useCallback, useEffect, useState } from 'react'
import { AlertTriangle, Inbox, RefreshCw, Sparkles } from 'lucide-react'
import type { AllocationSuggestion, Payment, PaymentMethod } from '@/types'
import type { AllocationInput, CashierStudent } from '@/api/services/cashier'
import {
  acceptPayment,
  financeErrorMessage,
  isAborted,
  suggestAllocation,
} from '@/api/services/cashier'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { cn } from '@/lib/utils'
import { MoneyInput } from './MoneyInput'
import { formatPeriod, formatSum, formatSumWithUnit, methodLabels, parseSum } from './format'

interface PaymentSplitModalProps {
  open: boolean
  student: CashierStudent
  /** Kassir sahifada kiritgan summa (har doim > 0). */
  amount: number
  method: PaymentMethod
  note: string
  /**
   * Shaklda tanlangan sana — "YYYY-MM-DD". Bugun bo'lsa ham yuboriladi:
   * server uchun bugungi sana hozirgi lahza bilan bir xil natija beradi
   * (`AcceptPaymentRequest.ReceivedOn`).
   */
  receivedOn: string
  onClose: () => void
  onAccepted: (payment: Payment) => void
}

/** Bitta taqsimot qatori: serverdan kelgan taklif + kassir kiritgan xom matn. */
interface Line {
  suggestion: AllocationSuggestion
  /** Xom matn — `MoneyInput` shu ko'rinishda saqlaydi. */
  raw: string
}

/**
 * Taqsimot muharriri (P1-16, SPEC §3.7).
 *
 * FIFO SERVERDA HISOBLANADI. `GET /api/cash/payments/suggest-allocation`
 * o'quvchining qoldig'i bor hisob-fakturalarini eng eskisidan boshlab
 * qaytaradi va summani o'sha tartibda bo'lib beradi
 * (`PaymentService.SuggestAllocationAsync`). Bu yerda FIFO qayta
 * yozilmaydi: pul formulasining ikkinchi nusxasi bir kun birinchisidan
 * ajralib ketadi, va buni faqat chekni ushlab turgan ota-ona sezadi.
 *
 * TAKLIF — MAJBURIY EMAS. Kassir har qatorni o'zgartira oladi (masalan
 * ota-ona "avval avtobusni yopaylik" desa). Ikki chegara qattiq:
 *   1) bitta qatorga hisob-faktura QOLDIG'IDAN ko'p yozib bo'lmaydi —
 *      backend baribir 400 `allocation_exceeds_invoice` qaytaradi;
 *   2) qatorlar yig'indisi TO'LOV SUMMASIDAN oshmaydi — kiritish paytida
 *      cheklab qo'yiladi, shuning uchun "Qoldiq" hisoblagichi manfiy
 *      qiymatga umuman tusha olmaydi.
 *
 * BU OYNADA TAHRIRLASH VA O'CHIRISH TUGMASI YO'Q: taqsimot qatori — hali
 * yozilmagan niyat, yozuv emas. Qatorni "olib tashlash" uchun summasi 0 ga
 * qo'yiladi va u so'rovga umuman kirmaydi.
 */
export function PaymentSplitModal({
  open,
  student,
  amount,
  method,
  note,
  receivedOn,
  onClose,
  onAccepted,
}: PaymentSplitModalProps) {
  const [lines, setLines] = useState<Line[]>([])
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setLoading(true)
      setLoadError(null)
      // Oldingi urinishning yozish xatosi yangi taklif bilan birga ketadi:
      // u eski summaga tegishli edi va ekranda qolib ketsa chalg'itadi.
      setSubmitError(null)
      try {
        const rows = await suggestAllocation(student.id, amount, signal)
        setLines(rows.map((s) => ({ suggestion: s, raw: s.suggested > 0 ? String(s.suggested) : '' })))
      } catch (err) {
        if (isAborted(err)) return
        setLoadError(financeErrorMessage(err, "Taqsimot taklifini olib bo'lmadi."))
      } finally {
        setLoading(false)
      }
    },
    [student.id, amount],
  )

  useEffect(() => {
    if (!open) return
    const controller = new AbortController()
    void load(controller.signal)
    return () => controller.abort()
  }, [open, load])

  const allocated = lines.reduce((sum, line) => sum + (parseSum(line.raw) ?? 0), 0)
  /** Manfiy bo'lishi MUMKIN EMAS: kiritish paytida yig'indi `amount` bilan cheklangan. */
  const remainder = Math.max(0, round2(amount - allocated))

  const setLineValue = (invoiceId: string, raw: string) => {
    setLines((prev) => {
      const others = prev
        .filter((l) => l.suggestion.invoiceId !== invoiceId)
        .reduce((sum, l) => sum + (parseSum(l.raw) ?? 0), 0)

      return prev.map((line) => {
        if (line.suggestion.invoiceId !== invoiceId) return line
        if (raw === '') return { ...line, raw: '' }

        const typed = parseSum(raw)
        if (typed === null) return { ...line, raw }

        // Ikki chegara: hisob-faktura qoldig'i va to'lovning bo'sh qismi.
        const cap = round2(Math.min(line.suggestion.remaining, Math.max(0, amount - others)))
        const capped = Math.min(typed, cap)
        return { ...line, raw: capped === 0 ? '0' : String(capped) }
      })
    })
  }

  const applySuggestion = () => {
    setLines((prev) =>
      prev.map((line) => ({
        ...line,
        raw: line.suggestion.suggested > 0 ? String(line.suggestion.suggested) : '',
      })),
    )
  }

  const allocations: AllocationInput[] = lines
    .map((line) => ({ invoiceId: line.suggestion.invoiceId, amount: parseSum(line.raw) ?? 0 }))
    .filter((a) => a.amount > 0)

  const submit = async () => {
    if (busy) return
    setBusy(true)
    setSubmitError(null)
    try {
      const payment = await acceptPayment({
        studentId: student.id,
        amount,
        method,
        note: note.trim() || undefined,
        allocations,
        receivedOn,
      })
      onAccepted(payment)
    } catch (err) {
      setSubmitError(financeErrorMessage(err, "To'lovni qabul qilib bo'lmadi."))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={busy ? () => undefined : onClose}
      title="To'lovni taqsimlash"
      size="lg"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Orqaga
          </Button>
          <Button onClick={submit} disabled={busy || loading || loadError !== null}>
            {busy ? 'Yozilmoqda...' : `${formatSum(amount)} so'mni qabul qilish`}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <header className="rounded-xl bg-slate-50 px-4 py-3">
          <p className="font-medium text-slate-800">{student.fullName}</p>
          <p className="text-sm text-slate-500">
            {student.className} · {methodLabels[method]} · {formatSumWithUnit(amount)}
          </p>
        </header>

        {loading && <Loader label="Taqsimot tayyorlanmoqda..." />}

        {!loading && loadError && (
          <div className="rounded-xl border border-red-200 bg-red-50/70 px-4 py-3">
            <div className="flex items-start gap-2 text-sm text-red-700">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
              <span>{loadError}</span>
            </div>
            <Button variant="secondary" className="mt-3" onClick={() => void load()}>
              <RefreshCw className="h-4 w-4" /> Qayta urinish
            </Button>
          </div>
        )}

        {!loading && !loadError && lines.length === 0 && (
          <div className="flex flex-col items-center gap-2 rounded-xl border border-dashed border-slate-200 py-10 text-center">
            <Inbox className="h-8 w-8 text-slate-300" />
            <p className="font-medium text-slate-600">Ochiq hisob-faktura yo'q</p>
            <p className="max-w-sm text-sm text-slate-400">
              Bu o'quvchining qarzi yo'q. Summani baribir qabul qilsangiz, u avans
              bo'lib qoladi va keyingi hisob-fakturaga o'tadi.
            </p>
          </div>
        )}

        {!loading && !loadError && lines.length > 0 && (
          <>
            <div className="overflow-hidden rounded-xl border border-slate-200">
              <table className="w-full text-sm">
                <thead className="bg-slate-50 text-left text-xs uppercase tracking-wide text-slate-500">
                  <tr>
                    <th className="px-4 py-2 font-medium">Toifa</th>
                    <th className="px-4 py-2 font-medium">Davr</th>
                    <th className="px-4 py-2 text-right font-medium">Qarz qoldig'i</th>
                    <th className="w-44 px-4 py-2 text-right font-medium">Yo'naltiriladi</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {lines.map((line, index) => {
                    const value = parseSum(line.raw) ?? 0
                    const full = value > 0 && Math.abs(value - line.suggestion.remaining) < 0.005
                    return (
                      <tr key={line.suggestion.invoiceId} className="align-middle">
                        <td className="px-4 py-2">
                          <span className="font-medium text-slate-800">
                            {line.suggestion.categoryName}
                          </span>
                          {index === 0 && (
                            <span className="ml-2 rounded-full bg-brand-50 px-2 py-0.5 text-xs font-medium text-brand-700">
                              eng eski
                            </span>
                          )}
                        </td>
                        <td className="px-4 py-2 text-slate-500">
                          {formatPeriod(line.suggestion.periodMonth)}
                        </td>
                        <td className="px-4 py-2 text-right tabular-nums text-slate-600">
                          {formatSum(line.suggestion.remaining)}
                        </td>
                        <td className="px-4 py-2">
                          <MoneyInput
                            value={line.raw}
                            onValueChange={(raw) => setLineValue(line.suggestion.invoiceId, raw)}
                            disabled={busy}
                            placeholder="0"
                            className={cn(full && 'border-emerald-300 bg-emerald-50/50')}
                          />
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>

            <div className="flex flex-wrap items-center justify-between gap-3">
              <Button variant="ghost" onClick={applySuggestion} disabled={busy}>
                <Sparkles className="h-4 w-4" /> Taklifni qaytarish (eng eskidan)
              </Button>

              <dl className="flex flex-wrap items-center gap-4 text-sm">
                <Counter label="Taqsimlandi" value={formatSum(allocated)} />
                <Counter
                  label="Qoldiq"
                  value={formatSum(remainder)}
                  tone={remainder > 0 ? 'warn' : 'ok'}
                />
              </dl>
            </div>

            {remainder > 0 && (
              <p className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-900">
                Taqsimlanmagan {formatSumWithUnit(remainder)} avans sifatida qoladi va
                keyingi hisob-fakturaga o'tadi.
              </p>
            )}
          </>
        )}

        {submitError && (
          <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">{submitError}</p>
        )}
      </div>
    </Modal>
  )
}

function Counter({
  label,
  value,
  tone,
}: {
  label: string
  value: string
  tone?: 'ok' | 'warn'
}) {
  return (
    <div className="flex items-baseline gap-2">
      <dt className="text-slate-500">{label}:</dt>
      <dd
        className={cn(
          'font-semibold tabular-nums',
          tone === 'warn' ? 'text-amber-700' : tone === 'ok' ? 'text-emerald-700' : 'text-slate-800',
        )}
      >
        {value}
      </dd>
    </div>
  )
}

/** Ikki xonagacha yaxlitlash — baza ustuni `numeric(14,2)`. */
function round2(value: number): number {
  return Math.round(value * 100) / 100
}
