import { useEffect, useState } from 'react'
import { UserMinus } from 'lucide-react'
import { cancelWithdrawal, listWithdrawals, type WithdrawalEntry } from '@/api/services/studentWithdrawals'
import { financeErrorMessage } from '@/api/services/cashier'
import { formatDate, formatMoney } from '@/lib/utils'

interface Props {
  studentId: string
  /** O'zgarsa ro'yxat qayta yuklanadi (yangi amaldan keyin). */
  reloadKey: number
  /** Bekor qilingach — kartochka, moliya va abonementlar yangilansin. */
  onChanged: () => void
}

/**
 * Abonement tab'ining tepasida: kuchdagi "chiqib ketdi" amallari. 30 kun ichida xato bo'lsa bekor
 * qilinadi — qarz va abonement qaytadi (`StudentWithdrawalService.CancelAsync`).
 */
export function WithdrawalNotices({ studentId, reloadKey, onChanged }: Props) {
  const [rows, setRows] = useState<WithdrawalEntry[]>([])
  const [confirming, setConfirming] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    listWithdrawals(studentId)
      .then((list) => {
        if (alive) setRows(list.filter((w) => !w.cancelledAt))
      })
      .catch(() => {
        if (alive) setRows([])
      })
    return () => {
      alive = false
    }
  }, [studentId, reloadKey])

  const cancel = async (id: string) => {
    setBusy(true)
    setError(null)
    try {
      await cancelWithdrawal(studentId, id)
      setConfirming(null)
      setRows((cur) => cur.filter((w) => w.id !== id))
      onChanged()
    } catch (e) {
      setError(financeErrorMessage(e, "Bekor qilib bo'lmadi."))
    } finally {
      setBusy(false)
    }
  }

  if (rows.length === 0) return null

  return (
    <div className="space-y-2">
      {rows.map((w) => (
        <div key={w.id} className="flex flex-wrap items-center gap-3 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm">
          <UserMinus className="h-4 w-4 shrink-0 text-amber-600" />
          <div className="min-w-0 flex-1 text-amber-900">
            <b className="font-semibold">Chiqib ketdi: {formatDate(w.lastDay)}</b>
            {' · '}
            {formatMoney(w.writtenOff)} hisobdan chiqarilgan
            {w.released > 0 && <>, {formatMoney(w.released)} avansga qaytgan</>}
            <span className="block text-xs text-amber-700">
              {formatDate(w.createdAt.slice(0, 10))}
              {w.createdByName ? ` · ${w.createdByName}` : ''}
              {w.note ? ` · ${w.note}` : ''}
            </span>
          </div>
          {w.canCancel &&
            (confirming === w.id ? (
              <div className="flex items-center gap-2">
                <span className="text-xs text-amber-800">Qarz va abonement qaytadi.</span>
                <button
                  type="button"
                  disabled={busy}
                  onClick={() => cancel(w.id)}
                  className="rounded-md bg-red-600 px-2.5 py-1 text-xs font-medium text-white hover:bg-red-700 disabled:opacity-50"
                >
                  Ha, bekor qilish
                </button>
                <button
                  type="button"
                  onClick={() => setConfirming(null)}
                  className="rounded-md px-2 py-1 text-xs font-medium text-slate-600 hover:bg-white"
                >
                  Yo'q
                </button>
              </div>
            ) : (
              <button
                type="button"
                onClick={() => setConfirming(w.id)}
                className="rounded-md px-2.5 py-1 text-xs font-medium text-amber-800 hover:bg-white"
              >
                Bekor qilish
              </button>
            ))}
        </div>
      ))}
      {error && <p className="text-sm text-red-600">{error}</p>}
    </div>
  )
}
