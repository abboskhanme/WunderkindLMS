/**
 * HISOB-FAKTURANI BEKOR QILISH (docs/modules/finance-parity.md §2.10, F10.02).
 *
 * Nega alohida oyna, `ReasonModal` emas: serverning ikkita rad etishi
 * foydalanuvchiga BOSHQA-BOSHQA ish buyuradi va buni matn emas, KOD aytadi:
 *
 *  · `has_effective_allocation` → avval to'lovni storno qilish kerak,
 *    ya'ni odamni tranzaksiyalar jurnaliga yuborish kerak;
 *  · `self_reversal` → jurnalga o'zi qo'ygan odam o'zi bekor qila olmaydi
 *    (SPEC §4.5), demak buni BOSHQA admin bajarishi kerak. Oylik hisoblashni
 *    fon xizmati direktor nomidan yozadi — shuning uchun direktor avtomatik
 *    hisoblangan oyni o'zi bekor qila olmaydi, va buni oldindan bilishi kerak.
 *
 * BEKOR QILISH — O'CHIRISH EMAS (SPEC §4.1): qator `void` bo'lib qoladi,
 * jurnal partiyasi esa ko'zgu satrlar bilan qaytariladi.
 */
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Ban } from 'lucide-react'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Textarea } from '@/components/ui/Input'
import { Notice } from './BillingUi'
import { formatMoney } from '@/lib/utils'
import { formatMonth } from '@/config/constants'
import type { Invoice } from '@/types'

const MIN_REASON = 3

interface Props {
  /** Bekor qilinayotgan hisob-faktura; `null` — oyna yopiq. */
  invoice: Invoice | null
  busy: boolean
  error: string | null
  /** Serverning mashina o'qiydigan kodi. */
  errorCode: string | null
  onClose: () => void
  onConfirm: (reason: string) => void
}

export function VoidInvoiceModal({ invoice, busy, error, errorCode, onClose, onConfirm }: Props) {
  const [reason, setReason] = useState('')

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oyna ochilganda formani tozalash (maqsadli)
    if (invoice) setReason('')
  }, [invoice])

  const trimmed = reason.trim()
  const valid = trimmed.length >= MIN_REASON

  // Ekranda ko'rinadigan ogohlantirish: to'langan oyni bekor qilib bo'lmaydi,
  // va buni tugmani bosishdan OLDIN aytgan yaxshi.
  const alreadyPaid = invoice !== null && invoice.paid > 0

  return (
    <Modal
      open={invoice !== null}
      onClose={onClose}
      title="Hisob-fakturani bekor qilish"
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Yopish
          </Button>
          <Button
            variant="danger"
            disabled={!valid || busy || alreadyPaid}
            onClick={() => onConfirm(trimmed)}
          >
            <Ban className="h-4 w-4" />
            {busy ? 'Bajarilmoqda...' : 'Bekor qilish'}
          </Button>
        </>
      }
    >
      {invoice && (
        <div className="space-y-4">
          <div className="rounded-xl border border-slate-200 bg-slate-50/70 px-4 py-3 text-sm">
            <div className="flex items-baseline justify-between gap-3">
              <span className="text-slate-500">O'quvchi</span>
              <span className="font-medium text-slate-800">{invoice.studentName}</span>
            </div>
            <div className="mt-1 flex items-baseline justify-between gap-3">
              <span className="text-slate-500">Toifa · oy</span>
              <span className="text-slate-700">
                {invoice.categoryName} · {formatMonth(invoice.periodMonth.slice(0, 7))}
              </span>
            </div>
            <div className="mt-1 flex items-baseline justify-between gap-3">
              <span className="text-slate-500">To'lanadi</span>
              <span className="font-semibold text-slate-800">{formatMoney(invoice.payable)}</span>
            </div>
            <div className="mt-1 flex items-baseline justify-between gap-3">
              <span className="text-slate-500">To'langan</span>
              <span className="text-slate-700">{formatMoney(invoice.paid)}</span>
            </div>
          </div>

          <p className="text-sm text-slate-600">
            Hisob-faktura <b>o'chirilmaydi</b>: u "bekor qilingan" holatiga o'tadi va qarzga
            kirmay qoladi. Jurnaldagi yozuv ko'zgu satrlar bilan qaytariladi.
          </p>

          {alreadyPaid && (
            <Notice tone="info">
              Bu oyga to'lov taqsimlangan — avval{' '}
              <Link to="/admin/finance/transactions" className="font-medium underline">
                tranzaksiyalar jurnalida
              </Link>{' '}
              to'lovni storno qiling, keyin hisob-fakturani bekor qilasiz.
            </Notice>
          )}

          <Textarea
            label="Sabab"
            required
            rows={3}
            placeholder="Masalan: obuna narxi xato kiritilgan"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
          />
          {!valid && trimmed.length > 0 && (
            <p className="text-xs text-slate-400">
              Sabab kamida {MIN_REASON} ta belgidan iborat bo'lsin.
            </p>
          )}

          {error && <Notice>{error}</Notice>}

          {errorCode === 'has_effective_allocation' && (
            <Notice tone="info">
              <Link to="/admin/finance/transactions" className="font-medium underline">
                Tranzaksiyalar jurnaliga o'tish
              </Link>{' '}
              va to'lovni storno qilish.
            </Notice>
          )}
          {errorCode === 'self_reversal' && (
            <Notice tone="info">
              Bu hisob-fakturani boshqa admin bekor qilishi kerak: jurnalga yozuvni qo'ygan
              odam uni o'zi qaytara olmaydi (SPEC §4.5). Oylik hisoblashni tizim direktor
              nomidan yozadi, shuning uchun avtomatik hisoblangan oyni direktordan boshqa
              administrator bekor qiladi.
            </Notice>
          )}
        </div>
      )}
    </Modal>
  )
}
