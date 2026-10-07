import { useEffect, useMemo, useState } from 'react'
import { AlertTriangle, Archive } from 'lucide-react'
import type { SubscriptionRecord } from '@/api/services/billingCatalog'
import {
  getMonthRemovalPreview,
  removeSubscriptionMonths,
  type MonthRemovalPreview,
  type MonthRemovalResult,
} from '@/api/services/monthRemoval'
import { billingErrorMessage } from '@/api/services/billingError'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Textarea } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { formatMoney } from '@/lib/utils'
import { formatMonth } from '@/config/constants'
import { Notice } from './BillingUi'

interface Props {
  subscription: SubscriptionRecord | null
  onClose: () => void
  onDone: (result: MonthRemovalResult) => void
}

/**
 * "Abonement oyini butunlay o'chirish" (client, 2026-10-07): pick any months of the subscription —
 * paid ones too. The month leaves the accounts; the receipts that paid it stay in the archive as
 * storno, and what they paid for other months is re-issued as a new receipt.
 */
export function RemoveMonthsModal({ subscription, onClose, onDone }: Props) {
  const [preview, setPreview] = useState<MonthRemovalPreview | null>(null)
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [reason, setReason] = useState('')
  const [deleteSubscription, setDeleteSubscription] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!subscription) return
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- yangi abonement: oyna boshidan (maqsadli)
    setPreview(null)
    setSelected(new Set())
    setReason('')
    setDeleteSubscription(false)
    setError(null)
    getMonthRemovalPreview(subscription.id)
      .then((p) => active && setPreview(p))
      .catch((e) => active && setError(billingErrorMessage(e, "Oylarni yuklab bo'lmadi")))
    return () => {
      active = false
    }
  }, [subscription])

  const months = useMemo(() => preview?.months ?? [], [preview])
  const chosen = months.filter((m) => selected.has(m.invoiceId))
  const removable = months.filter((m) => !m.blockedReason)
  const allChosen = months.length > 0 && chosen.length === months.length
  const paidOut = chosen.reduce((s, m) => s + m.paid, 0)
  const receipts = new Set(chosen.flatMap((m) => m.payments.map((p) => p.receiptNo)))

  const toggle = (id: string) =>
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const confirm = async () => {
    if (!subscription) return
    setBusy(true)
    setError(null)
    try {
      const result = await removeSubscriptionMonths(subscription.id, {
        invoiceIds: [...selected],
        reason: reason.trim(),
        deleteSubscription: deleteSubscription && allChosen,
      })
      onDone(result)
    } catch (e) {
      setError(billingErrorMessage(e, "Oylarni o'chirib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={subscription !== null}
      onClose={onClose}
      size="lg"
      title="Abonement oylarini o'chirish"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button variant="danger" onClick={confirm} disabled={busy || chosen.length === 0 || !reason.trim()}>
            {busy ? "O'chirilmoqda..." : chosen.length ? `O'chirish (${chosen.length} oy)` : "O'chirish"}
          </Button>
        </>
      }
    >
      {!preview ? (
        error ? <Notice tone="danger">{error}</Notice> : <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-4">
          <p className="text-sm text-slate-600">
            <b>{subscription?.planName ?? preview.categoryName}</b> — {formatMoney(preview.monthlyAmount)} / oy. O'chirilgan oy
            hisob-kitobdan butunlay chiqadi: qarz ham, to'lovi ham. To'lov cheklari arxivda (storno) qoladi; chek boshqa oylarni ham
            to'lagan bo'lsa, o'sha qismi yangi chek bilan qayta rasmiylashtiriladi.
          </p>

          {months.length === 0 ? (
            <p className="rounded-lg bg-slate-50 px-3 py-6 text-center text-sm text-slate-400">O'chiriladigan oy yo'q</p>
          ) : (
            <div className="overflow-hidden rounded-xl border border-slate-200">
              <div className="flex items-center justify-between border-b border-slate-100 bg-slate-50 px-3 py-2 text-xs font-medium text-slate-500">
                <label className="flex items-center gap-2">
                  <input
                    type="checkbox"
                    checked={removable.length > 0 && removable.every((m) => selected.has(m.invoiceId))}
                    onChange={(e) => setSelected(e.target.checked ? new Set(removable.map((m) => m.invoiceId)) : new Set())}
                    className="h-4 w-4"
                  />
                  Hammasi
                </label>
                <span>To'langan / summa</span>
              </div>
              {months.map((m) => (
                <label
                  key={m.invoiceId}
                  className={
                    'flex items-start gap-3 border-b border-slate-50 px-3 py-2.5 last:border-0 ' +
                    (m.blockedReason ? 'cursor-not-allowed opacity-60' : 'cursor-pointer hover:bg-slate-50/60')
                  }
                >
                  <input
                    type="checkbox"
                    disabled={!!m.blockedReason}
                    checked={selected.has(m.invoiceId)}
                    onChange={() => toggle(m.invoiceId)}
                    className="mt-0.5 h-4 w-4"
                  />
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center justify-between gap-2 text-sm">
                      <span className="font-medium text-slate-800">{formatMonth(m.month)}</span>
                      <span className="text-slate-600">
                        <span className={m.paid > 0 ? 'font-semibold text-emerald-700' : ''}>{formatMoney(m.paid)}</span> /{' '}
                        {formatMoney(m.payable)}
                      </span>
                    </div>
                    {m.payments.length > 0 && (
                      <p className="text-xs text-slate-400">
                        Cheklar: {m.payments.map((p) => `#${p.receiptNo} (${formatMoney(p.share)})`).join(', ')}
                      </p>
                    )}
                    {m.blockedReason && <p className="text-xs text-amber-700">{m.blockedReason}</p>}
                  </div>
                </label>
              ))}
            </div>
          )}

          {preview.removedMonths.length > 0 && (
            <p className="flex items-center gap-1.5 text-xs text-slate-400">
              <Archive className="h-3.5 w-3.5" /> Avval o'chirilgan: {preview.removedMonths.map(formatMonth).join(', ')}
            </p>
          )}

          {chosen.length > 0 && (
            <div className="flex gap-2 rounded-xl bg-red-50 px-3 py-2.5 text-sm text-red-700">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
              <span>
                {chosen.length} ta oy o'chiriladi.
                {paidOut > 0
                  ? ` ${formatMoney(paidOut)} to'lov kassa va hisobotlardan chiqadi (${receipts.size} ta chek storno).`
                  : " To'lov yo'q — faqat hisob o'chadi."}{' '}
                Buni orqaga qaytarib bo'lmaydi.
              </span>
            </div>
          )}

          <label className={'flex items-center gap-2 text-sm ' + (allChosen ? 'text-slate-700' : 'text-slate-400')}>
            <input
              type="checkbox"
              disabled={!allChosen}
              checked={deleteSubscription && allChosen}
              onChange={(e) => setDeleteSubscription(e.target.checked)}
              className="h-4 w-4"
            />
            Abonementning o'zini ham o'chirish (hamma oylari tanlanganda)
          </label>

          <Textarea
            label="Sabab"
            required
            rows={2}
            value={reason}
            maxLength={500}
            onChange={(e) => setReason(e.target.value)}
            placeholder="Masalan: xato kiritilgan, ikki marta yozilgan"
          />

          {error && <Notice tone="danger">{error}</Notice>}
        </div>
      )}
    </Modal>
  )
}
