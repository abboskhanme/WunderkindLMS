import { useEffect, useState } from 'react'
import { AlertTriangle, CheckCircle2, Undo2 } from 'lucide-react'
import type { SickDiscountCancelResult, SickDiscountEntry } from '@/api/services/cashier'
import { cancelSickDiscount, financeErrorCode, financeErrorMessage, isAborted, listSickDiscounts } from '@/api/services/cashier'
import { Button } from '@/components/ui/Button'
import { formatPeriod, formatSumWithUnit } from './format'

/** Only recent ones are offered for undo — a mistake is caught soon; older ones are history. */
const RECENT_DAYS = 30

interface SickDiscountNoticesProps {
  studentId: string
  /** Bumped by the host after a new sickness discount — the list reloads. */
  reloadKey: number
  /** The payment form is busy — no cancelling meanwhile. */
  disabled?: boolean
  onCancelled: (result: SickDiscountCancelResult) => void
}

interface Loaded {
  studentId: string
  rows: SickDiscountEntry[]
}

function cancelErrorMessage(err: unknown): string {
  const code = financeErrorCode(err)
  if (code === 'sick_discount_already_cancelled') {
    return financeErrorMessage(err, 'Bu kasallik chegirmasi allaqachon bekor qilingan.')
  }
  if (code === 'invoice_void') {
    return financeErrorMessage(err, "Hisob-faktura bekor qilingan — chegirmani qaytarib bo'lmaydi.")
  }
  return financeErrorMessage(err, "Bekor qilib bo'lmadi.")
}

/**
 * The green "Kasallik chegirmasi qo'llandi…" notice of the payment form, with "Bekor qilish" on it
 * (docs/modules/billing-plans.md §9.1; client, 2026-09-28: "adashib ketsa bekor qilish button ham
 * bo'lsin" — on this notice, "mana bu yerda"). Built from the server's list of the student's
 * recent standing sickness discounts, so one put before a page reload can still be undone.
 * Cancelling asks once, inline, and says what it does: the sum goes back onto the debt.
 */
export function SickDiscountNotices({ studentId, reloadKey, disabled = false, onCancelled }: SickDiscountNoticesProps) {
  const [loaded, setLoaded] = useState<Loaded | null>(null)
  const [cancelledKey, setCancelledKey] = useState(0)
  const [confirming, setConfirming] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState<{ studentId: string; text: string } | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    listSickDiscounts(studentId, controller.signal)
      .then((rows) => {
        const since = Date.now() - RECENT_DAYS * 24 * 60 * 60 * 1000
        setLoaded({ studentId, rows: rows.filter((r) => new Date(r.createdAt).getTime() >= since) })
      })
      .catch((err: unknown) => {
        // A convenience on top of the form — a failed load just shows nothing.
        if (!isAborted(err)) setLoaded({ studentId, rows: [] })
      })
    return () => controller.abort()
  }, [studentId, reloadKey, cancelledKey])

  const rows = loaded?.studentId === studentId ? loaded.rows : []

  const cancel = async (entry: SickDiscountEntry) => {
    setBusy(true)
    setError(null)
    try {
      const result = await cancelSickDiscount(entry.groupId)
      setConfirming(null)
      setDone({
        studentId,
        text: `Kasallik chegirmasi bekor qilindi: ${formatPeriod(result.month)} — ${formatSumWithUnit(result.total)} qarzga qaytdi.`,
      })
      onCancelled(result)
    } catch (err) {
      setError(cancelErrorMessage(err))
    } finally {
      setBusy(false)
      setCancelledKey((k) => k + 1)
    }
  }

  const notice = done?.studentId === studentId ? done.text : null
  if (rows.length === 0 && !notice && !error) return null

  return (
    <div className="space-y-2">
      {rows.map((entry) => (
        <div
          key={entry.groupId}
          className="flex flex-wrap items-center gap-x-3 gap-y-2 rounded-xl border border-emerald-200 bg-emerald-50/70 px-3 py-2.5 text-sm text-emerald-800"
        >
          <CheckCircle2 className="h-4 w-4 shrink-0" />
          <span className="min-w-0 flex-1">
            Kasallik chegirmasi qo'llandi: {formatPeriod(entry.month)}, {entry.days} kun —{' '}
            {formatSumWithUnit(entry.total)} ayrildi.
          </span>
          {confirming === entry.groupId ? (
            <span className="flex w-full flex-wrap items-center justify-end gap-2">
              <span className="text-xs text-emerald-900">
                {formatSumWithUnit(entry.total)} qarzga qaytadi. Bekor qilinsinmi?
              </span>
              <Button
                variant="danger"
                type="button"
                className="px-3 py-1.5"
                onClick={() => void cancel(entry)}
                disabled={busy || disabled}
              >
                {busy ? 'Bekor qilinmoqda...' : 'Ha, bekor qilish'}
              </Button>
              <Button
                variant="secondary"
                type="button"
                className="px-3 py-1.5"
                onClick={() => setConfirming(null)}
                disabled={busy}
              >
                Yo'q
              </Button>
            </span>
          ) : (
            <Button
              variant="secondary"
              type="button"
              className="ml-auto px-3 py-1.5"
              onClick={() => {
                setError(null)
                setDone(null)
                setConfirming(entry.groupId)
              }}
              disabled={busy || disabled}
            >
              <Undo2 className="h-4 w-4" /> Bekor qilish
            </Button>
          )}
        </div>
      ))}

      {notice && (
        <div className="flex items-start gap-2 rounded-xl border border-slate-200 bg-slate-50/70 px-3 py-2.5 text-sm text-slate-700">
          <Undo2 className="mt-0.5 h-4 w-4 shrink-0" />
          <span>{notice}</span>
        </div>
      )}

      {error && (
        <div className="flex items-start gap-2 rounded-xl border border-red-200 bg-red-50/70 px-3 py-2.5 text-sm text-red-700">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <span>{error}</span>
        </div>
      )}
    </div>
  )
}
