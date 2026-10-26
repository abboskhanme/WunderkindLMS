import { useEffect, useMemo, useState } from 'react'
import { AlertTriangle, Archive, RefreshCw } from 'lucide-react'
import type { SubscriptionRecord } from '@/api/services/billingCatalog'
import {
  getMonthRemovalPreview,
  removeSubscriptionMonths,
  type MonthRemovalPaidTo,
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
 * paid ones too. The month leaves the accounts.
 *
 * 2026-10-26 (billing-plans.md §10.5): what was paid for it goes back to the pupil's balance (default) or,
 * as before, leaves the accounts with the receipts reversed. The month can be billed again right away
 * from the abonement, together with other abonements whose "Hisob" start skipped it — this is how the
 * imported September "O'qish to'lovi" (EduSchool's whole month) is split by abonement.
 */
export function RemoveMonthsModal({ subscription, onClose, onDone }: Props) {
  const [preview, setPreview] = useState<MonthRemovalPreview | null>(null)
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [reason, setReason] = useState('')
  const [deleteSubscription, setDeleteSubscription] = useState(false)
  const [paidTo, setPaidTo] = useState<MonthRemovalPaidTo>('balance')
  const [rebill, setRebill] = useState(false)
  const [alsoBill, setAlsoBill] = useState<Set<string>>(new Set())
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
    setPaidTo('balance')
    setRebill(false)
    setAlsoBill(new Set())
    setError(null)
    getMonthRemovalPreview(subscription.id)
      .then((p) => active && setPreview(p))
      .catch((e) => active && setError(billingErrorMessage(e, "Oylarni yuklab bo'lmadi")))
    return () => {
      active = false
    }
  }, [subscription])

  const storno = paidTo === 'storno'
  const months = useMemo(() => preview?.months ?? [], [preview])
  const chosen = months.filter((m) => selected.has(m.invoiceId))
  // Only the storno path is blocked by one's own receipt (two-person rule); the balance path reverses none.
  const blocked = (m: (typeof months)[number]) => storno && !!m.blockedReason
  const removable = months.filter((m) => !blocked(m))
  const allChosen = months.length > 0 && chosen.length === months.length
  const paidOut = chosen.reduce((s, m) => s + m.paid, 0)
  const receipts = new Set(chosen.flatMap((m) => m.payments.map((p) => p.receiptNo)))
  const chosenMonths = new Set(chosen.map((m) => m.month))
  const others = (preview?.others ?? [])
    .map((o) => ({ ...o, hits: o.months.filter((x) => chosenMonths.has(x.month)) }))
    .filter((o) => o.hits.length > 0)
  const alsoChosen = others.filter((o) => alsoBill.has(o.subscriptionId))
  const rebillTotal = chosen.reduce((s, m) => s + (m.rebill ?? 0), 0)
  const alsoTotal = alsoChosen.reduce((s, o) => s + o.hits.reduce((t, x) => t + x.amount, 0), 0)
  const lostDiscount = chosen.reduce((s, m) => s + m.discount, 0)

  const toggle = (id: string) =>
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const choosePaidTo = (value: MonthRemovalPaidTo) => {
    setPaidTo(value)
    // A month blocked for storno cannot stay selected once storno is picked.
    if (value === 'storno')
      setSelected((prev) => new Set([...prev].filter((id) => !months.find((m) => m.invoiceId === id)?.blockedReason)))
  }

  const chooseRebill = (value: boolean) => {
    setRebill(value)
    // Re-billing a lumped month: the skipped abonements are what the lump held — offered ticked.
    setAlsoBill(value ? new Set((preview?.others ?? []).map((o) => o.subscriptionId)) : new Set())
    if (value) setDeleteSubscription(false)
  }

  const toggleOther = (id: string) =>
    setAlsoBill((prev) => {
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
        deleteSubscription: deleteSubscription && allChosen && !rebill,
        paidTo,
        rebill,
        alsoBill: alsoChosen.map((o) => o.subscriptionId),
      })
      onDone(result)
    } catch (e) {
      setError(billingErrorMessage(e, "Oylarni o'chirib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const action = rebill ? 'Qayta hisoblash' : "O'chirish"

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
          <Button
            variant={rebill ? 'primary' : 'danger'}
            onClick={confirm}
            disabled={busy || chosen.length === 0 || !reason.trim()}
          >
            {busy ? 'Bajarilmoqda...' : chosen.length ? `${action} (${chosen.length} oy)` : action}
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
            hisob-kitobdan butunlay chiqadi (arxivda qoladi). Unga to'langan pul o'quvchi balansiga qaytadi yoki cheklar storno
            qilinib kassadan chiqadi. Kerak bo'lsa oy shu zahoti abonement narxi bilan qayta yoziladi.
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
                    (blocked(m) ? 'cursor-not-allowed opacity-60' : 'cursor-pointer hover:bg-slate-50/60')
                  }
                >
                  <input
                    type="checkbox"
                    disabled={blocked(m)}
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
                        {rebill && selected.has(m.invoiceId) && (
                          <span className="ml-2 font-semibold text-blue-700">
                            → {m.rebill === null ? "abonement yo'q" : formatMoney(m.rebill)}
                          </span>
                        )}
                      </span>
                    </div>
                    {m.payments.length > 0 && (
                      <p className="text-xs text-slate-400">
                        Cheklar: {m.payments.map((p) => `#${p.receiptNo} (${formatMoney(p.share)})`).join(', ')}
                      </p>
                    )}
                    {blocked(m) && <p className="text-xs text-amber-700">{m.blockedReason}</p>}
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

          <fieldset className="space-y-1.5">
            <legend className="mb-1 text-sm font-medium text-slate-700">To'langan pul</legend>
            <label className="flex items-start gap-2 text-sm text-slate-700">
              <input type="radio" name="paidTo" checked={!storno} onChange={() => choosePaidTo('balance')} className="mt-0.5 h-4 w-4" />
              <span>
                O'quvchi balansiga qaytadi
                <span className="block text-xs text-slate-400">Kassa va cheklar o'zgarmaydi — pul eng eski ochiq qarzlarni yopadi.</span>
              </span>
            </label>
            <label className="flex items-start gap-2 text-sm text-slate-700">
              <input type="radio" name="paidTo" checked={storno} onChange={() => choosePaidTo('storno')} className="mt-0.5 h-4 w-4" />
              <span>
                Kassadan chiqadi (cheklar storno)
                <span className="block text-xs text-slate-400">Pul hisobotlardan chiqadi; chekning boshqa oylar qismi qayta rasmiylashtiriladi.</span>
              </span>
            </label>
          </fieldset>

          <div className="space-y-2 rounded-xl border border-slate-200 px-3 py-2.5">
            <label className="flex items-start gap-2 text-sm text-slate-700">
              <input type="checkbox" checked={rebill} onChange={(e) => chooseRebill(e.target.checked)} className="mt-0.5 h-4 w-4" />
              <span>
                <span className="inline-flex items-center gap-1.5 font-medium">
                  <RefreshCw className="h-3.5 w-3.5" /> Abonement narxi bilan qayta hisoblash
                </span>
                <span className="block text-xs text-slate-400">
                  O'chirilgan oy darhol shu abonement bo'yicha qayta yoziladi (masalan, EduSchool'dan kelgan umumiy summa o'rniga).
                </span>
              </span>
            </label>
            {rebill && others.length > 0 && (
              <div className="ml-6 space-y-1.5">
                <p className="text-xs font-medium text-slate-500">Shu oy uchun boshqa abonementlarni ham hisoblash:</p>
                {others.map((o) => (
                  <label key={o.subscriptionId} className="flex items-start gap-2 text-sm text-slate-700">
                    <input
                      type="checkbox"
                      checked={alsoBill.has(o.subscriptionId)}
                      onChange={() => toggleOther(o.subscriptionId)}
                      className="mt-0.5 h-4 w-4"
                    />
                    <span>
                      {o.planName ?? o.categoryName} —{' '}
                      {o.hits.map((x) => `${formatMonth(x.month)}: ${formatMoney(x.amount)}`).join(', ')}
                      <span className="block text-xs text-slate-400">Hozir hisobi keyingi oydan boshlanadi — bu oy umumiy summa ichida edi.</span>
                    </span>
                  </label>
                ))}
              </div>
            )}
            {rebill && lostDiscount > 0 && (
              <p className="ml-6 text-xs text-amber-700">
                Tanlangan oydagi chegirma/kasallik ({formatMoney(lostDiscount)}) qayta hisoblashda qo'llanmaydi — kerak bo'lsa qaytadan
                qo'shing.
              </p>
            )}
          </div>

          {chosen.length > 0 && (
            <div className="flex gap-2 rounded-xl bg-red-50 px-3 py-2.5 text-sm text-red-700">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
              <span>
                {chosen.length} ta oy o'chiriladi.
                {paidOut <= 0
                  ? " To'lov yo'q — faqat hisob o'chadi."
                  : storno
                    ? ` ${formatMoney(paidOut)} to'lov kassa va hisobotlardan chiqadi (${receipts.size} ta chek storno).`
                    : ` ${formatMoney(paidOut)} to'lov o'quvchi balansiga qaytadi.`}
                {rebill && ` Qayta yoziladi: ${formatMoney(rebillTotal + alsoTotal)}.`} Buni orqaga qaytarib bo'lmaydi.
              </span>
            </div>
          )}

          <label
            className={'flex items-center gap-2 text-sm ' + (allChosen && !rebill ? 'text-slate-700' : 'text-slate-400')}
          >
            <input
              type="checkbox"
              disabled={!allChosen || rebill}
              checked={deleteSubscription && allChosen && !rebill}
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
            placeholder="Masalan: EduSchool'dan kelgan umumiy summa abonement bo'yicha ajratildi"
          />

          {error && <Notice tone="danger">{error}</Notice>}
        </div>
      )}
    </Modal>
  )
}
