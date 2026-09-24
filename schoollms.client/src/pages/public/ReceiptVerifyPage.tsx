import { useParams } from 'react-router-dom'
import { AlertTriangle, BadgeCheck, Loader2, XCircle } from 'lucide-react'
import { useAsync } from '@/hooks/useAsync'
import { getPublicReceipt } from '@/api/services/publicReceipt'
import { cn } from '@/lib/utils'

/**
 * Chek QR kodi ochadigan sahifa (mijoz, 2026-09-24): "chekdan scaner qilib to'lov haqida malumot ham ololsin".
 * Login talab qilmaydi; to'lov faqat chekdagi o'zgarmas token bo'yicha topiladi va uning HOZIRGI holati
 * ko'rsatiladi — storno qilingan chek "bekor qilingan" bo'lib chiqadi.
 */
export function ReceiptVerifyPage() {
  const { token = '' } = useParams()
  const { data, loading, error } = useAsync(() => getPublicReceipt(token), [token])

  return (
    <div className="min-h-screen bg-slate-50 px-4 py-8">
      <div className="mx-auto max-w-md">
        <img src="/receipt-logo.jpg" alt="Wunderkind xalqaro maktabi" className="mx-auto mb-6 h-14 w-auto" />

        {loading && (
          <div className="flex items-center justify-center gap-2 py-16 text-slate-500">
            <Loader2 className="h-5 w-5 animate-spin" /> Chek tekshirilmoqda…
          </div>
        )}

        {!loading && (error || !data) && (
          <div className="rounded-2xl border border-slate-200 bg-white p-6 text-center">
            <AlertTriangle className="mx-auto h-8 w-8 text-amber-500" />
            <p className="mt-2 font-semibold text-slate-800">Chek topilmadi</p>
            <p className="mt-1 text-sm text-slate-500">QR kod noto'g'ri yoki bu chek tizimda yo'q.</p>
          </div>
        )}

        {!loading && data && (
          <div className="overflow-hidden rounded-2xl border border-slate-200 bg-white">
            <div
              className={cn(
                'flex items-center gap-3 px-5 py-4',
                data.status === 'valid' ? 'bg-emerald-50 text-emerald-800' : 'bg-red-50 text-red-800',
              )}
            >
              {data.status === 'valid' ? <BadgeCheck className="h-7 w-7 shrink-0" /> : <XCircle className="h-7 w-7 shrink-0" />}
              <div>
                <p className="font-semibold">
                  {data.status === 'valid'
                    ? 'Chek haqiqiy — to\'lov qabul qilingan'
                    : data.status === 'reversal'
                      ? "Bu — to'lovni bekor qilish cheki"
                      : "To'lov bekor qilingan"}
                </p>
                {data.cancelledAtText && <p className="text-sm opacity-80">{data.cancelledAtText}</p>}
              </div>
            </div>

            <div className="space-y-4 px-5 py-4">
              <div>
                <p className="text-xs uppercase tracking-wide text-slate-400">{data.schoolName}</p>
                <p className="text-lg font-semibold text-slate-800">To'lov cheki № {data.receiptNo}</p>
                <p className="text-sm text-slate-500">{data.receivedAtText}</p>
              </div>

              <div>
                <p className="text-xs text-slate-400">O'quvchi</p>
                <p className="font-medium text-slate-800">{data.studentName}</p>
                {data.className && <p className="text-sm text-slate-500">Sinf: {data.className}</p>}
              </div>

              <div className="divide-y divide-slate-100 rounded-xl border border-slate-100">
                {data.lines.map((l, i) => (
                  <div key={i} className="flex items-start justify-between gap-3 px-3 py-2.5">
                    <div className="min-w-0">
                      <p className="font-medium text-slate-800">{l.categoryName}</p>
                      <p className="text-xs text-slate-500">
                        {[l.periodText, l.statusText].filter(Boolean).join(' · ')}
                      </p>
                    </div>
                    <p className="shrink-0 font-semibold tabular-nums text-slate-800">{l.amountText}</p>
                  </div>
                ))}
              </div>

              <div className="flex items-baseline justify-between">
                <p className="font-semibold text-slate-800">Jami</p>
                <p className="text-lg font-bold tabular-nums text-slate-900">{data.totalText}</p>
              </div>

              <dl className="grid grid-cols-2 gap-y-1 text-sm">
                <dt className="text-slate-500">To'lov turi</dt>
                <dd className="text-right text-slate-800">{data.methodText}</dd>
                <dt className="text-slate-500">Kassir</dt>
                <dd className="text-right text-slate-800">{data.cashierName}</dd>
              </dl>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
