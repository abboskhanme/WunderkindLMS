/**
 * Obunani yopish — o'quvchi avtobusdan chiqdi, yotoqxonadan ko'chdi va h.k.
 *
 * Obuna O'CHIRILMAYDI: unga hisoblangan oylar va to'lovlar bog'langan.
 * Yopilgan obuna ko'rsatilgan sanadan keyin hisoblanmaydi, tarixi esa
 * joyida qoladi.
 *
 * F1.06 (docs/modules/finance-parity.md §2.1) — SETTLEMENT QOIDASI:
 *
 *  1. Tugash sanasi tushgan OYNING O'ZI proratsiya QILINMAYDI: obuna oy bilan
 *     kesishsa yetarli (server qoidasi — `InvoiceService.AccrueMonthAsync`),
 *     ya'ni shu oy to'liq qarz bo'lib qoladi. Buni bu oyna o'zgartirmaydi.
 *  2. Tugash oyidan KEYINGI oylarga allaqachon hisob-faktura chiqarilgan bo'lsa
 *     (masalan, butun yil oldindan to'ldirilgan) — ular ENDI qarz emas, shuning
 *     uchun `previewEndSubscription` orqali ko'rsatiladi va admin ULARDAN
 *     qaysilarini bekor qilishni O'ZI tanlaydi (avtomatik emas — SPEC §4.3,
 *     har bir bekor qilish sababli va izlanadigan bo'lishi kerak).
 *  3. To'lov tushgan oy BEKOR QILINMAYDI (server ham rad etadi): avval o'sha
 *     to'lov storno qilinishi kerak — bu yerda faqat ko'rsatiladi, majburlanmaydi.
 *  4. Bekor qilish mavjud `voidInvoice` orqali HAR BIR oy uchun ALOHIDA
 *     ketma-ket chaqiriladi, so'ng obunaning o'zi yopiladi — ikkalasi ham
 *     alohida, allaqachon qulflangan (advisory lock) server yo'llari.
 */
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import type { SubscriptionRecord } from '@/api/services/billingCatalog'
import { previewEndSubscription } from '@/api/services/billingCatalog'
import { canVoid } from '@/api/services/invoices'
import { billingErrorMessage } from '@/api/services/billingError'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Textarea } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { formatMoney } from '@/lib/utils'
import { formatMonth } from '@/config/constants'
import { Notice } from './BillingUi'
import { DatePicker } from '@/components/ui/DatePicker'

const MIN_REASON = 3

interface Props {
  open: boolean
  subscription: SubscriptionRecord | null
  busy: boolean
  error: string | null
  onClose: () => void
  onConfirm: (id: string, endsOn: string, voidInvoiceIds: string[], reason: string) => void
}

const today = () => new Date().toISOString().slice(0, 10)

export function EndSubscriptionModal({
  open,
  subscription,
  busy,
  error,
  onClose,
  onConfirm,
}: Props) {
  const [endsOn, setEndsOn] = useState(today())
  const [reason, setReason] = useState('')
  const [selected, setSelected] = useState<Set<string>>(new Set())

  const [preview, setPreview] = useState<Awaited<ReturnType<typeof previewEndSubscription>> | null>(null)
  const [previewLoading, setPreviewLoading] = useState(false)
  const [previewError, setPreviewError] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oyna ochilganda formani tozalash (maqsadli)
    setEndsOn(subscription?.endsOn ?? today())
    setReason('')
    setSelected(new Set())
  }, [open, subscription])

  const valid = endsOn !== '' && subscription !== null && endsOn >= subscription.startsOn

  useEffect(() => {
    if (!open || !subscription || !valid) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- shart bajarilmasa oldingi ko'rishni tozalash (maqsadli)
      setPreview(null)
      return
    }
    let cancelled = false
    setPreviewLoading(true)
    setPreviewError(null)
    previewEndSubscription(subscription.id, endsOn)
      .then((result) => {
        if (cancelled) return
        setPreview(result)
        // Sana o'zgarsa avvalgi tanlov endi boshqa ro'yxatga tegishli — tozalanadi.
        setSelected(new Set())
      })
      .catch((e: unknown) => {
        if (cancelled) return
        setPreviewError(billingErrorMessage(e, "Kelajakdagi oylarni ko'rib bo'lmadi"))
      })
      .finally(() => {
        if (!cancelled) setPreviewLoading(false)
      })
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- subscription.id/startsOn o'zgarmaydi, faqat sana va ochiq/yopiqlik kerak
  }, [open, subscription?.id, endsOn, valid])

  if (!subscription) return null

  const futureInvoices = preview?.futureInvoices ?? []
  const voidableIds = futureInvoices.filter((i) => canVoid(i)).map((i) => i.id)
  const hasSelection = selected.size > 0
  const reasonOk = !hasSelection || reason.trim().length >= MIN_REASON
  const canSubmit = valid && reasonOk && !busy

  const toggle = (id: string) => {
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Obunani yopish"
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button
            disabled={!canSubmit}
            onClick={() => onConfirm(subscription.id, endsOn, [...selected], reason.trim())}
          >
            {busy ? 'Bajarilmoqda...' : 'Yopish'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm">
          <p className="font-medium text-slate-800">{subscription.studentName}</p>
          <p className="text-slate-500">
            {subscription.categoryName} · {formatMoney(subscription.monthlyAmount)} / oy
          </p>
        </div>

        <DatePicker
          label="Tugash sanasi"
          required
          min={subscription.startsOn}
          value={endsOn}
          onChange={(value: string) => setEndsOn(value)}
        />

        <p className="text-sm text-slate-500">
          Shu sanadan keyin bu obuna hisoblanmaydi. Yozuv o'chirilmaydi — oldingi oylar
          va to'lovlar joyida qoladi. <b>Tugash sanasi tushgan oyning o'zi proratsiya
          qilinmaydi</b> — to'liq oy sifatida qarzda qoladi.
        </p>

        {!valid && (
          <Notice tone="danger">
            Tugash sanasi boshlanish sanasidan ({subscription.startsOn}) oldin bo'la olmaydi.
          </Notice>
        )}

        {valid && previewLoading && <Loader label="Kelajakdagi oylar tekshirilmoqda..." />}
        {valid && previewError && <Notice>{previewError}</Notice>}

        {valid && !previewLoading && !previewError && futureInvoices.length > 0 && (
          <div className="space-y-2">
            <p className="text-sm font-medium text-slate-700">
              Bu oydan keyingi oylarga allaqachon hisob-faktura chiqarilgan — obuna
              to'xtagach ular endi qarz emas. Bekor qilmoqchi bo'lganlaringizni tanlang:
            </p>
            <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200">
              {futureInvoices.map((invoice) => {
                const voidable = canVoid(invoice)
                return (
                  <li key={invoice.id} className="flex items-start gap-3 px-3 py-2 text-sm">
                    <input
                      type="checkbox"
                      className="mt-0.5 h-4 w-4 rounded border-slate-300 accent-brand-600 disabled:opacity-30"
                      checked={selected.has(invoice.id)}
                      disabled={!voidable}
                      onChange={() => toggle(invoice.id)}
                    />
                    <div className="flex-1">
                      <div className="flex items-baseline justify-between gap-2">
                        <span className="font-medium text-slate-800">
                          {formatMonth(invoice.periodMonth.slice(0, 7))}
                        </span>
                        <span className="tabular-nums text-slate-700">
                          {formatMoney(invoice.payable)}
                        </span>
                      </div>
                      {!voidable && (
                        <p className="mt-0.5 text-xs text-amber-700">
                          Bu oyga to'lov taqsimlangan ({formatMoney(invoice.paid)}) — avval{' '}
                          <Link to="/admin/finance/transactions" className="font-medium underline">
                            tranzaksiyalar jurnalida
                          </Link>{' '}
                          storno qiling.
                        </p>
                      )}
                    </div>
                  </li>
                )
              })}
            </ul>

            {hasSelection && (
              <Textarea
                label="Bekor qilish sababi"
                required
                rows={2}
                placeholder="Masalan: o'quvchi avtobusdan chiqdi, qolgan oylar kerak emas"
                value={reason}
                onChange={(e) => setReason(e.target.value)}
              />
            )}
            {hasSelection && !reasonOk && (
              <p className="text-xs text-slate-400">
                Sabab kamida {MIN_REASON} ta belgidan iborat bo'lsin.
              </p>
            )}
            {voidableIds.length === 0 && (
              <Notice tone="info">
                Kelajakdagi oylarning hammasiga to'lov taqsimlangan — bekor qilishdan oldin
                tegishli to'lovlarni storno qiling.
              </Notice>
            )}
          </div>
        )}

        {error && <Notice>{error}</Notice>}
      </div>
    </Modal>
  )
}
