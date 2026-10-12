import { useCallback, useEffect, useState } from 'react'
import { ArrowRight, Check, Clock, Undo2 } from 'lucide-react'
import type { IncomingCashBoxTransfer } from '@/api/services/cashBoxes'
import { acceptCashBoxTransfer, getIncomingTransfers, returnCashBoxTransfer } from '@/api/services/cashBoxes'
import { financeErrorMessage } from '@/api/services/cashier'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { ReasonModal } from '@/pages/admin/billing/ReasonModal'
import { formatDateTime, formatSumWithUnit, methodLabels } from './format'

interface Props {
  /** O'zgarsa ro'yxat qayta yuklanadi (kassa amalidan keyin). */
  reloadKey: number
  /** Qabul qilindi yoki qaytarildi — sahifa kassalar va jadvalni yangilaydi. */
  onDecided: () => void
}

/**
 * Joriy foydalanuvchiga kelgan, javob kutayotgan ko'chirishlar (mijoz, 2026-10-12):
 * "Mendan tasdiqlansa keyin pul menga o'tishi kerak. Xato bo'lsa men qaytarishim kerak".
 *
 * Faqat qabul qiluvchi kassaning MAS'ULIGA ko'rinadi (server shu odamnikini qaytaradi).
 * "Qabul qilish" — pul yuborgan kassadan chiqib, bu kassaga tushadi; "Qaytarish" — pul
 * yuborganda qoladi, sabab majburiy (kassir uni jadvalda ko'radi). Bo'sh bo'lsa hech
 * narsa chizilmaydi.
 */
export function IncomingTransfers({ reloadKey, onDecided }: Props) {
  const [items, setItems] = useState<IncomingCashBoxTransfer[]>([])
  const [confirmId, setConfirmId] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [returning, setReturning] = useState<IncomingCashBoxTransfer | null>(null)
  const [returnBusy, setReturnBusy] = useState(false)
  const [returnError, setReturnError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      setItems(await getIncomingTransfers())
    } catch {
      // Panel yordamchi: yuklanmasa sahifaning qolgani ishlayveradi.
      setItems([])
    }
  }, [])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- ro'yxatni kassa amalidan keyin qayta yuklaymiz (maqsadli)
    void load()
  }, [load, reloadKey])

  const accept = async (id: string) => {
    setBusyId(id)
    setError(null)
    try {
      await acceptCashBoxTransfer(id)
      setConfirmId(null)
      await load()
      onDecided()
    } catch (err) {
      setError(financeErrorMessage(err, "Qabul qilib bo'lmadi."))
    } finally {
      setBusyId(null)
    }
  }

  const submitReturn = async (reason: string) => {
    if (!returning || returnBusy) return
    setReturnBusy(true)
    setReturnError(null)
    try {
      await returnCashBoxTransfer(returning.id, reason)
      setReturning(null)
      await load()
      onDecided()
    } catch (err) {
      setReturnError(financeErrorMessage(err, "Qaytarib bo'lmadi."))
    } finally {
      setReturnBusy(false)
    }
  }

  if (items.length === 0) return null

  const total = items.reduce((sum, t) => sum + t.amount, 0)

  return (
    <Card className="border-amber-200 bg-amber-50/60 p-4">
      <div className="mb-3 flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="flex items-center gap-2 font-semibold text-slate-800">
          <Clock className="h-4 w-4 text-amber-600" />
          Sizga kelgan ko'chirishlar ({items.length})
        </h2>
        <p className="text-sm tabular-nums text-slate-600">Jami: {formatSumWithUnit(total)}</p>
      </div>
      <p className="mb-3 text-xs text-slate-500">
        Pulni sanab chiqing. Qabul qilsangiz, pul yuborgan kassadan kamayib sizning kassangizga tushadi. Xato
        bo'lsa — qaytaring: pul yuborganda qoladi.
      </p>

      {error && <p className="mb-2 text-sm text-red-600">{error}</p>}

      <ul className="space-y-2">
        {items.map((t) => (
          <li
            key={t.id}
            className="flex flex-wrap items-center justify-between gap-3 rounded-xl bg-white p-3 shadow-sm"
          >
            <div className="min-w-0">
              <p className="text-lg font-semibold tabular-nums text-slate-800">
                {formatSumWithUnit(t.amount)}
                <span className="ml-2 text-sm font-normal text-slate-500">{methodLabels[t.method] ?? t.method}</span>
              </p>
              <p className="mt-0.5 flex flex-wrap items-center gap-1 text-sm text-slate-600">
                {t.fromBoxName}
                <ArrowRight className="h-3.5 w-3.5 text-slate-400" />
                {t.toBoxName}
              </p>
              <p className="mt-0.5 text-xs text-slate-400">
                {formatDateTime(t.createdAt)} · yubordi: {t.senderName}
              </p>
              {t.note && <p className="mt-1 text-sm text-slate-600">«{t.note}»</p>}
            </div>

            {confirmId === t.id ? (
              <div className="flex items-center gap-2">
                <span className="text-sm text-slate-600">Pul to'liq keldimi?</span>
                <Button onClick={() => void accept(t.id)} disabled={busyId === t.id}>
                  <Check className="h-4 w-4" /> Ha, qabul qilaman
                </Button>
                <Button variant="ghost" onClick={() => setConfirmId(null)} disabled={busyId === t.id}>
                  Yo'q
                </Button>
              </div>
            ) : (
              <div className="flex items-center gap-2">
                <Button onClick={() => setConfirmId(t.id)}>
                  <Check className="h-4 w-4" /> Qabul qilish
                </Button>
                <Button
                  variant="secondary"
                  onClick={() => {
                    setReturnError(null)
                    setReturning(t)
                  }}
                >
                  <Undo2 className="h-4 w-4" /> Qaytarish
                </Button>
              </div>
            )}
          </li>
        ))}
      </ul>

      <ReasonModal
        open={returning !== null}
        title="Ko'chirishni qaytarish"
        description={
          returning
            ? `${formatSumWithUnit(returning.amount)} — ${returning.fromBoxName}. Pul yuborgan kassada qoladi, sababni kassir ko'radi.`
            : ''
        }
        confirmLabel="Qaytarish"
        busy={returnBusy}
        error={returnError}
        onClose={() => setReturning(null)}
        onConfirm={(reason) => void submitReturn(reason)}
      />
    </Card>
  )
}
