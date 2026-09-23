import { useEffect, useMemo, useRef, useState } from 'react'
import {
  AlertTriangle,
  CheckCircle2,
  FileText,
  Loader2,
  Printer,
  RefreshCw,
  Send,
} from 'lucide-react'
import type { Payment } from '@/types'
import type { ReceiptDelivery, ReceiptPrint } from '@/api/services/cashier'
import {
  financeErrorMessage,
  getReceiptPdf,
  getReceiptPrint,
  sendReceiptToTelegram,
} from '@/api/services/cashier'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { cn } from '@/lib/utils'
import { printThermalReceipt, RECEIPT_COPY_LABELS, thermalReceiptHtml } from '@/lib/thermalReceipt'
import { formatDateTime, formatSumWithUnit, methodLabels } from './format'

interface ReceiptPreviewProps {
  open: boolean
  payment: Payment
  /** Yopilganda kassa yangi to'lovga tayyorlanadi. */
  onClose: () => void
  /**
   * Oyna ochilishi bilan 58 mm chekni ikki nusxada chop etish oynasini O'ZI
   * ochadi (mijoz, 2026-09-22). Sukut — ha: to'lovdan keyingi asosiy yo'l.
   */
  autoPrint?: boolean
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
 * 58 MM TERMAL CHEK (mijoz, 2026-09-22). Oyna ochilishi bilan server
 * tayyorlagan chek (`GET /api/receipts/{id}`) olinadi va chop etish oynasi
 * O'ZI ochiladi — ikki nusxa (maktab + ota-ona), har abonement oyida "to'liq
 * yopildi" yoki "qoldi". PDF endi o'zi ochilmaydi: u faqat ikkinchi darajali
 * havola. "Qayta chop etish" — printer qog'ozi tugagan yoki kassir oynani
 * yopib yuborgan holat uchun.
 *
 * BU OYNADA TAHRIRLASH VA BEKOR QILISH TUGMASI YO'Q. To'lov yozilgan —
 * u endi o'zgarmas (SPEC §4.1). Xato bo'lsa admin storno qiladi; kassirda
 * bunday huquq yo'q (SPEC §4.3) va shuning uchun tugmasi ham yo'q.
 */
export function ReceiptPreview({ open, payment, onClose, autoPrint = true }: ReceiptPreviewProps) {
  const [thermal, setThermal] = useState<ThermalState>({ status: 'loading' })
  const [thermalAttempt, setThermalAttempt] = useState(0)
  const [printError, setPrintError] = useState<string | null>(null)
  /** Qaysi to'lov avtomatik chop etildi — bir to'lov faqat BIR marta o'zi chiqadi. */
  const autoPrinted = useRef<string | null>(null)
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
    // eslint-disable-next-line react-hooks/set-state-in-effect -- yangi chek ochildi — oldingi PDF/Telegram holatini tiklaymiz (maqsadli)
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

  // 58 mm chek: ma'lumotni olish va (birinchi marta) chop etish oynasini ochish.
  useEffect(() => {
    if (!open) return
    let cancelled = false

    // 'loading' holati boshlang'ich qiymatda va "qayta urinish" tugmasida
    // qo'yiladi — effekt ichida sinxron setState yo'q.
    getReceiptPrint(payment.id)
      .then((receipt) => {
        if (cancelled) return
        setThermal({ status: 'ready', receipt })
        if (autoPrint && autoPrinted.current !== payment.id) {
          autoPrinted.current = payment.id
          printThermalReceipt(receipt).catch((err: unknown) =>
            setPrintError(err instanceof Error ? err.message : "Chop etib bo'lmadi."),
          )
        }
      })
      .catch((err: unknown) => {
        if (cancelled) return
        setThermal({ status: 'error', message: financeErrorMessage(err, 'Chek tayyorlanmadi.') })
      })

    return () => {
      cancelled = true
    }
  }, [open, payment.id, thermalAttempt, autoPrint])

  const reprint = () => {
    if (thermal.status !== 'ready') return
    setPrintError(null)
    printThermalReceipt(thermal.receipt).catch((err: unknown) =>
      setPrintError(err instanceof Error ? err.message : "Chop etib bo'lmadi."),
    )
  }

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

        {thermal.status === 'loading' && (
          <p className="flex items-center gap-2 text-sm text-slate-500">
            <Loader2 className="h-4 w-4 animate-spin" /> Chek tayyorlanmoqda...
          </p>
        )}

        {thermal.status === 'ready' && <ThermalPreview receipt={thermal.receipt} />}

        {thermal.status === 'error' && (
          <p className="flex items-start gap-2 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <span>{thermal.message}</span>
          </p>
        )}

        {printError && (
          <p className="flex items-start gap-2 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <span>{printError}</span>
          </p>
        )}

        <div className="flex flex-wrap items-center gap-2">
          {thermal.status === 'error' ? (
            <Button
              onClick={() => {
                setThermal({ status: 'loading' })
                setPrintError(null)
                setThermalAttempt((n) => n + 1)
              }}
            >
              <RefreshCw className="h-4 w-4" /> Chek: qayta urinish
            </Button>
          ) : (
            <Button onClick={reprint} disabled={thermal.status !== 'ready'}>
              <Printer className="h-4 w-4" /> Qayta chop etish
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

        {/* PDF — ikkinchi darajali: o'zi ochilmaydi, faqat so'ralsa. */}
        <div className="text-xs text-slate-500">
          {pdf.status === 'loading' && <span>PDF tayyorlanmoqda...</span>}
          {pdf.status === 'ready' && (
            <a
              href={pdf.url}
              target="_blank"
              rel="noreferrer"
              download={fileName}
              className="inline-flex items-center gap-1 text-slate-500 underline-offset-2 hover:text-slate-700 hover:underline"
            >
              <FileText className="h-3.5 w-3.5" /> Chekni PDF'da ochish (A5)
            </a>
          )}
          {pdf.status === 'error' && (
            <button
              type="button"
              onClick={() => setPdfAttempt((n) => n + 1)}
              className="inline-flex items-center gap-1 text-slate-500 hover:text-slate-700"
            >
              <RefreshCw className="h-3.5 w-3.5" /> PDF: qayta urinish
            </button>
          )}
        </div>

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

/**
 * 58 mm chekning ekrandagi nusxasi — AYNAN chop etiladigan HTML (bitta
 * nusxa), shuning uchun kassir qog'ozda nima chiqishini oldindan ko'radi.
 */
function ThermalPreview({ receipt }: { receipt: ReceiptPrint }) {
  const html = useMemo(() => thermalReceiptHtml(receipt, [RECEIPT_COPY_LABELS[0]]), [receipt])
  const [height, setHeight] = useState(360)

  return (
    <div className="space-y-1.5">
      <div className="flex justify-center rounded-xl bg-slate-100 p-3">
        <iframe
          title={`Chek №${receipt.receiptNo} — oldindan ko'rish`}
          srcDoc={html}
          className="bg-white shadow-sm"
          style={{ width: '58mm', height, border: 0 }}
          onLoad={(e) => {
            const doc = e.currentTarget.contentDocument
            if (doc) setHeight(Math.min(doc.documentElement.scrollHeight, 560))
          }}
        />
      </div>
      <p className="text-center text-xs text-slate-400">
        58 mm · {RECEIPT_COPY_LABELS.length} nusxa: {RECEIPT_COPY_LABELS.join(', ')}
      </p>
    </div>
  )
}

type ThermalState =
  | { status: 'loading' }
  | { status: 'ready'; receipt: ReceiptPrint }
  | { status: 'error'; message: string }

type PdfState =
  | { status: 'loading' }
  | { status: 'ready'; url: string }
  | { status: 'error'; message: string }

type TelegramState =
  | { status: 'idle' }
  | { status: 'sending' }
  | { status: 'done'; result: ReceiptDelivery }
  | { status: 'error'; message: string }
