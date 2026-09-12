import { useEffect, useState } from 'react'
import {
  AlertTriangle,
  CheckCircle2,
  FileText,
  Loader2,
  RefreshCw,
  Send,
} from 'lucide-react'
import type { Payment } from '@/types'
import type { ReceiptDelivery } from '@/api/services/cashier'
import { financeErrorMessage, getReceiptPdf, sendReceiptToTelegram } from '@/api/services/cashier'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { cn } from '@/lib/utils'
import { formatDateTime, formatPeriod, formatSum, formatSumWithUnit, methodLabels } from './format'

interface ReceiptPreviewProps {
  open: boolean
  payment: Payment
  /** Yopilganda kassa yangi to'lovga tayyorlanadi. */
  onClose: () => void
}

/**
 * Chek oynasi — to'lov qabul qilingandan KEYIN (P1-16, SPEC §4.7).
 *
 * IKKI NUSXA. Ekrandagi ko'rinish kassir uchun (chek raqamini ovoz chiqarib
 * aytadi), PDF esa RASMIY nusxa — uni server QuestPDF bilan chizadi va
 * ichida maktab nomi, chek raqami, toifalar va kassir turadi. Uchinchi
 * nusxa Telegramda ota-onada qoladi: SPEC §4.7 ning butun ma'nosi shunda —
 * to'lovchining qo'lida maktab o'zgartira olmaydigan dalil bo'ladi.
 *
 * PDF HAVOLASI OLDINDAN TAYYORLANADI. `GET /api/receipts/{id}.pdf` JWT
 * talab qiladi, oddiy `<a href>` esa Authorization sarlavhasini yubormaydi
 * va 401 oladi. Shuning uchun fayl blob sifatida yuklab olinadi va havola
 * `blob:` manziliga qo'yiladi — kassir bir marta bosadi, brauzer esa
 * "yangi oyna" bloklovchisiga tushmaydi (kutish `onClick` ichida emas).
 *
 * BU OYNADA TAHRIRLASH VA BEKOR QILISH TUGMASI YO'Q. To'lov yozilgan —
 * u endi o'zgarmas (SPEC §4.1). Xato bo'lsa admin storno qiladi; kassirda
 * bunday huquq yo'q (SPEC §4.3) va shuning uchun tugmasi ham yo'q.
 */
export function ReceiptPreview({ open, payment, onClose }: ReceiptPreviewProps) {
  const [pdf, setPdf] = useState<PdfState>({ status: 'loading' })
  const [telegram, setTelegram] = useState<TelegramState>({ status: 'idle' })
  /** "Qayta urinish" — effektni qaytadan ishga tushirish uchun hisoblagich. */
  const [pdfAttempt, setPdfAttempt] = useState(0)

  useEffect(() => {
    if (!open) return

    let url: string | null = null
    let cancelled = false

    // Telegram holati ham shu yerda tozalanadi: ikkalasining ham sababi bitta —
    // "oynada YANGI chek ochildi". Ikki alohida effekt bir xil qo'zg'atgichga
    // osilib turishi keyin biri unutilib qolishiga olib keladi.
    setTelegram({ status: 'idle' })
    setPdf({ status: 'loading' })
    getReceiptPdf(payment.id)
      .then((blob) => {
        if (cancelled) return
        url = URL.createObjectURL(blob)
        setPdf({ status: 'ready', url })
      })
      .catch((err: unknown) => {
        if (cancelled) return
        setPdf({ status: 'error', message: financeErrorMessage(err, "Chek PDF'i tayyorlanmadi.") })
      })

    return () => {
      cancelled = true
      if (url) URL.revokeObjectURL(url)
    }
  }, [open, payment.id, pdfAttempt])

  const send = async () => {
    setTelegram({ status: 'sending' })
    try {
      setTelegram({ status: 'done', result: await sendReceiptToTelegram(payment.id) })
    } catch (err) {
      setTelegram({
        status: 'error',
        message: financeErrorMessage(err, "Chekni yuborib bo'lmadi."),
      })
    }
  }

  const fileName = `chek-${payment.receiptNo}.pdf`

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={`Chek №${payment.receiptNo}`}
      size="md"
      footer={<Button onClick={onClose}>Yangi to'lov</Button>}
    >
      <div className="space-y-4">
        <div className="flex items-start gap-3 rounded-xl border border-emerald-200 bg-emerald-50/70 px-4 py-3">
          <CheckCircle2 className="mt-0.5 h-5 w-5 shrink-0 text-emerald-600" />
          <div>
            <p className="font-semibold text-emerald-900">
              To'lov qabul qilindi — {formatSumWithUnit(payment.amount)}
            </p>
            <p className="mt-0.5 text-sm text-emerald-800">
              {payment.studentName} · {methodLabels[payment.method]} ·{' '}
              {formatDateTime(payment.receivedAt)}
            </p>
          </div>
        </div>

        <dl className="divide-y divide-slate-100 overflow-hidden rounded-xl border border-slate-200">
          {payment.allocations.map((line) => (
            <div key={line.id} className="flex items-center justify-between px-4 py-2 text-sm">
              <dt className="text-slate-600">
                {line.categoryName}
                <span className="ml-2 text-xs text-slate-400">
                  {formatPeriod(line.periodMonth)}
                </span>
              </dt>
              <dd className="font-medium tabular-nums text-slate-800">
                {formatSum(line.amount)}
              </dd>
            </div>
          ))}

          {payment.unallocated > 0 && (
            <div className="flex items-center justify-between bg-amber-50/60 px-4 py-2 text-sm">
              <dt className="text-amber-800">Avans (taqsimlanmagan)</dt>
              <dd className="font-medium tabular-nums text-amber-900">
                {formatSum(payment.unallocated)}
              </dd>
            </div>
          )}

          <div className="flex items-center justify-between bg-slate-50 px-4 py-2 text-sm">
            <dt className="font-medium text-slate-700">Jami</dt>
            <dd className="font-semibold tabular-nums text-slate-900">
              {formatSum(payment.amount)}
            </dd>
          </div>
        </dl>

        <p className="text-xs text-slate-400">
          Kassir: {payment.cashierName}
          {payment.note ? ` · Izoh: ${payment.note}` : ''}
        </p>

        <div className="flex flex-wrap items-center gap-2">
          {pdf.status === 'loading' && (
            <Button variant="secondary" disabled>
              <Loader2 className="h-4 w-4 animate-spin" /> PDF tayyorlanmoqda...
            </Button>
          )}

          {pdf.status === 'ready' && (
            <a
              href={pdf.url}
              target="_blank"
              rel="noreferrer"
              download={fileName}
              className="inline-flex items-center justify-center gap-2 rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-brand-700"
            >
              <FileText className="h-4 w-4" /> Chekni PDF'da ochish
            </a>
          )}

          {pdf.status === 'error' && (
            <Button variant="secondary" onClick={() => setPdfAttempt((n) => n + 1)}>
              <RefreshCw className="h-4 w-4" /> PDF: qayta urinish
            </Button>
          )}

          <Button
            variant="secondary"
            onClick={send}
            disabled={telegram.status === 'sending'}
          >
            {telegram.status === 'sending' ? (
              <Loader2 className="h-4 w-4 animate-spin" />
            ) : (
              <Send className="h-4 w-4" />
            )}
            Telegramga yuborish
          </Button>
        </div>

        {pdf.status === 'error' && (
          <p className="flex items-start gap-2 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <span>{pdf.message}</span>
          </p>
        )}

        {telegram.status === 'done' && (
          <p
            className={cn(
              'rounded-lg px-3 py-2 text-sm',
              telegram.result.delivered
                ? 'bg-emerald-50 text-emerald-800'
                : 'bg-amber-50 text-amber-900',
            )}
          >
            {telegram.result.message}
          </p>
        )}

        {telegram.status === 'error' && (
          <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">
            {telegram.message}
          </p>
        )}
      </div>
    </Modal>
  )
}

type PdfState =
  | { status: 'loading' }
  | { status: 'ready'; url: string }
  | { status: 'error'; message: string }

type TelegramState =
  | { status: 'idle' }
  | { status: 'sending' }
  | { status: 'done'; result: ReceiptDelivery }
  | { status: 'error'; message: string }
