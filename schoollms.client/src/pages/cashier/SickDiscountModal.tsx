import { useEffect, useMemo, useState } from 'react'
import { AlertTriangle, Thermometer } from 'lucide-react'
import type { AllocationSuggestion } from '@/types'
import type { CashierStudent, SickDiscountResult } from '@/api/services/cashier'
import {
  applySickDiscount,
  financeErrorCode,
  financeErrorMessage,
  isAborted,
  previewSickDiscount,
} from '@/api/services/cashier'
import { Button } from '@/components/ui/Button'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { formatPeriod, formatSumWithUnit } from './format'

interface SickDiscountModalProps {
  student: CashierStudent
  /** The student's open invoices — the months to choose from. */
  invoices: AllocationSuggestion[]
  onClose: () => void
  onApplied: (result: SickDiscountResult) => void
}

/** The two refusals the cashier can act on get their own wording when the server sends none. */
function sickErrorMessage(err: unknown, fallback: string): string {
  const code = financeErrorCode(err)
  if (code === 'invalid_days') {
    return financeErrorMessage(err, "Kasal kunlar soni shu oydagi dars kunlaridan ko'p bo'lmasligi kerak.")
  }
  if (code === 'nothing_to_deduct') {
    return financeErrorMessage(err, "Bu oy uchun ayiradigan qarz yo'q — oy to'liq to'langan.")
  }
  return financeErrorMessage(err, fallback)
}

/** Whole days, ≥ 1; anything else is "not entered yet". */
function parseDays(raw: string): number | null {
  if (!/^\d+$/.test(raw.trim())) return null
  const n = Number(raw.trim())
  return n >= 1 ? n : null
}

interface PreviewState {
  /** `${month}|${days}` the answer belongs to — an answer for older input is not shown. */
  key: string
  result: SickDiscountResult | null
  error: string | null
}

/**
 * "Kasallik chegirmasi" (docs/modules/billing-plans.md §9) — opened from the cashier's payment
 * form. The cashier picks a month of the student's open invoices and the number of sick days;
 * the server takes `gross ÷ lesson days × sick days` off EVERY open invoice of that month
 * (capped at what is still unpaid). No approval; recorded with who, when and how many days.
 *
 * All money is server-computed. Only the total is shown — which invoice gives how much is the
 * server's business (client, 2026-09-28: "jami ayriladiganni ko'rsatsin, qaysindan qancha
 * ayrilishi kerakmas").
 *
 * Mounted only while open (the parent renders it conditionally), so every opening starts from
 * a clean form. It sits inside the payment form's own modal: Escape is caught here, in the
 * capture phase, so it closes only this modal and not the payment form under it.
 */
export function SickDiscountModal({ student, invoices, onClose, onApplied }: SickDiscountModalProps) {
  const months = useMemo(
    () => [...new Set(invoices.map((i) => i.periodMonth.slice(0, 7)))].sort(),
    [invoices],
  )

  const [month, setMonth] = useState(() => months[0] ?? '')
  const [daysRaw, setDaysRaw] = useState('')
  const [note, setNote] = useState('')
  const [preview, setPreview] = useState<PreviewState | null>(null)
  const [busy, setBusy] = useState(false)
  const [submitError, setSubmitError] = useState<string | null>(null)

  const days = parseDays(daysRaw)
  const key = `${month}|${days ?? ''}`
  const ready = month !== '' && days !== null

  useEffect(() => {
    if (!ready || days === null) return
    const controller = new AbortController()
    // 300 ms: the cashier types "12" as two keystrokes — one request, not two.
    const timer = setTimeout(() => {
      previewSickDiscount(student.id, month, days, controller.signal)
        .then((result) => setPreview({ key, result, error: null }))
        .catch((err: unknown) => {
          if (isAborted(err)) return
          setPreview({ key, result: null, error: sickErrorMessage(err, "Hisoblab bo'lmadi.") })
        })
    }, 300)
    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [ready, student.id, month, days, key])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return
      e.stopPropagation()
      if (!busy) onClose()
    }
    window.addEventListener('keydown', onKey, true)
    return () => window.removeEventListener('keydown', onKey, true)
  }, [busy, onClose])

  const current = ready && preview?.key === key ? preview : null
  const previewLoading = ready && current === null
  const result = current?.result ?? null
  const canApply = result !== null && result.total > 0 && !busy

  const apply = async () => {
    if (!canApply || days === null) return
    setBusy(true)
    setSubmitError(null)
    try {
      const applied = await applySickDiscount({
        studentId: student.id,
        month,
        days,
        note: note.trim() || null,
      })
      onApplied(applied)
    } catch (err) {
      setSubmitError(sickErrorMessage(err, "Chegirmani qo'llab bo'lmadi."))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={() => !busy && onClose()}
      title={`Kasallik chegirmasi — ${student.fullName}`}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={() => void apply()} disabled={!canApply}>
            <Thermometer className="h-4 w-4" />
            {busy ? "Qo'llanmoqda..." : "Qo'llash"}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Select label="Qaysi oy" required value={month} onChange={(e) => setMonth(e.target.value)}>
            {months.map((m) => (
              <option key={m} value={m}>
                {formatPeriod(m)}
              </option>
            ))}
          </Select>
          <div>
            <Input
              label="Kasal kunlar soni"
              required
              autoFocus
              type="number"
              min={1}
              max={31}
              step={1}
              inputMode="numeric"
              placeholder="masalan: 5"
              value={daysRaw}
              onChange={(e) => setDaysRaw(e.target.value)}
            />
            {daysRaw.trim() !== '' && days === null && (
              <p className="mt-1 text-xs text-red-600">Kamida 1 kun, butun son.</p>
            )}
          </div>
        </div>

        {/* ---- Preview: idle / loading / error / the total (client: only the total, not per invoice) ---- */}
        <div className="rounded-xl border border-slate-200 bg-slate-50/70 px-3 py-3">
          {!ready && (
            <p className="text-center text-sm text-slate-400">
              Kasal kunlar sonini kiriting — hisob shu yerda ko'rinadi.
            </p>
          )}

          {previewLoading && <Loader className="py-4" label="Hisoblanmoqda..." />}

          {current?.error && (
            <div className="flex items-start gap-2 text-sm text-red-700">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
              <span>{current.error}</span>
            </div>
          )}

          {result && (
            <div className="flex items-center justify-between text-sm">
              <span className="text-slate-600">Jami ayriladi</span>
              <span className="text-base font-semibold tabular-nums text-slate-800">
                {formatSumWithUnit(result.total)}
              </span>
            </div>
          )}
        </div>

        <Textarea
          label="Izoh (ixtiyoriy)"
          rows={2}
          value={note}
          onChange={(e) => setNote(e.target.value)}
          placeholder="Masalan: shifokor ma'lumotnomasi bor"
        />

        <p className="text-xs text-slate-400">
          Oyning barcha ochiq hisob-fakturalaridan ayriladi. Tasdiq shart emas; kim, qachon va necha
          kun qo'ygani yozib qo'yiladi.
        </p>

        {submitError && (
          <div className="rounded-xl border border-red-200 bg-red-50/70 px-3 py-3">
            <div className="flex items-start gap-2 text-sm text-red-700">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
              <span>{submitError}</span>
            </div>
          </div>
        )}
      </div>
    </Modal>
  )
}
