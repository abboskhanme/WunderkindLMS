import { useEffect, useState } from 'react'
import { AlertTriangle } from 'lucide-react'
import type { LedgerPayment, MonthStatus, Student, StudentLedger } from '@/types'
import { getStudentLedger } from '@/api/services/students'
import { Modal } from '@/components/ui/Modal'
import { Loader } from '@/components/ui/Loader'
import { AuditHistoryList } from '@/components/audit/AuditHistoryList'
import { formatDate, formatMoney, cn } from '@/lib/utils'
import { formatMonth, monthStatusLabels } from '@/config/constants'

interface Props {
  student: Student | null
  onClose: () => void
}

const statusStyles: Record<MonthStatus, string> = {
  paid: 'bg-emerald-50 text-emerald-700',
  partial: 'bg-amber-50 text-amber-700',
  unpaid: 'bg-red-50 text-red-700',
}

export function PaymentHistoryModal({ student, onClose }: Props) {
  const [ledger, setLedger] = useState<StudentLedger | null>(null)
  const [loading, setLoading] = useState(false)
  /**
   * Xato matni. Ilgari bu oyna xatoni UMUMAN ushlamasdi: moliya roli yo'q
   * xodimga server 403 qaytarardi, `ledger` null bo'lib qolardi va ekranda
   * CHEKSIZ aylanuvchi doira turardi. Ruxsat yo'qligi — o'qiladigan yozuv.
   */
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!student) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda tarixni yuklash (maqsadli)
    setLoading(true)
    setLedger(null)
    setError(null)
    getStudentLedger(student.id)
      .then(setLedger)
      .catch((e) =>
        setError(
          (e as { response?: { status?: number; data?: { message?: string } } })?.response?.status === 403
            ? "To'lov tarixini ko'rish uchun moliya ruxsati kerak"
            : (e as { response?: { data?: { message?: string } } })?.response?.data?.message ??
              "To'lov tarixini olib bo'lmadi",
        ),
      )
      .finally(() => setLoading(false))
  }, [student])

  return (
    <Modal open={!!student} onClose={onClose} size="lg" title="To'lov tarixi">
      {error ? (
        <div className="flex items-start gap-2 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <span>{error}</span>
        </div>
      ) : loading || !ledger ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-5">
          {/* Sarlavha */}
          <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl bg-slate-50 px-4 py-3">
            <div>
              <p className="font-semibold text-slate-800">{ledger.student.fullName}</p>
              <p className="text-sm text-slate-500">
                {ledger.student.className} · oylik {formatMoney(ledger.monthlyFee)}
              </p>
            </div>
            <div className="text-right">
              <p className="text-xs text-slate-400">Joriy balans</p>
              <p
                className={cn(
                  'text-lg font-semibold',
                  ledger.balance < 0
                    ? 'text-red-600'
                    : ledger.balance > 0
                      ? 'text-emerald-600'
                      : 'text-slate-600',
                )}
              >
                {ledger.balance > 0 ? `+${formatMoney(ledger.balance)}` : formatMoney(ledger.balance)}
              </p>
            </div>
          </div>

          {/* Oylar bo'yicha holat */}
          <div>
            <p className="mb-2 text-sm font-medium text-slate-600">Oylar bo'yicha</p>
            <div className="overflow-hidden rounded-xl border border-slate-200">
              <table className="w-full text-left text-sm">
                <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-4 py-2.5">Oy</th>
                    <th className="px-4 py-2.5 text-right">Hisoblangan</th>
                    <th className="px-4 py-2.5 text-right">Chegirma</th>
                    <th className="px-4 py-2.5 text-right">To'langan</th>
                    <th className="px-4 py-2.5 text-right">Qoldiq</th>
                    <th className="px-4 py-2.5 text-center">Holat</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {ledger.months.map((m) => (
                    <tr key={m.month} className="hover:bg-slate-50/60">
                      <td className="px-4 py-2.5 font-medium text-slate-700">{formatMonth(m.month)}</td>
                      <td className="px-4 py-2.5 text-right text-slate-600">{formatMoney(m.charged)}</td>
                      <td
                        className={cn(
                          'px-4 py-2.5 text-right',
                          m.discount > 0 ? 'text-amber-600 font-medium' : 'text-slate-300',
                        )}
                      >
                        {m.discount > 0 ? `−${formatMoney(m.discount)}` : '—'}
                      </td>
                      <td className="px-4 py-2.5 text-right text-emerald-600 font-medium">
                        {formatMoney(m.paid)}
                      </td>
                      <td
                        className={cn(
                          'px-4 py-2.5 text-right',
                          m.remaining > 0 ? 'text-red-600' : 'text-slate-400',
                        )}
                      >
                        {formatMoney(m.remaining)}
                      </td>
                      <td className="px-4 py-2.5 text-center">
                        <span
                          className={cn(
                            'rounded-md px-2 py-0.5 text-xs font-medium',
                            statusStyles[m.status],
                          )}
                        >
                          {monthStatusLabels[m.status]}
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
                <tfoot className="border-t border-slate-200 bg-slate-50 text-sm font-semibold text-slate-700">
                  <tr>
                    <td className="px-4 py-2.5">Jami</td>
                    <td className="px-4 py-2.5 text-right">{formatMoney(ledger.totalCharged)}</td>
                    <td className="px-4 py-2.5 text-right text-amber-700">
                      {ledger.totalDiscount > 0 ? `−${formatMoney(ledger.totalDiscount)}` : '—'}
                    </td>
                    <td className="px-4 py-2.5 text-right text-emerald-700">
                      {formatMoney(ledger.totalPaid)}
                    </td>
                    <td className="px-4 py-2.5 text-right" colSpan={2} />
                  </tr>
                </tfoot>
              </table>
            </div>
          </div>

          {/* To'lovlar tarixi (kassa yozuvlari) */}
          <div>
            <p className="mb-2 text-sm font-medium text-slate-600">Amalga oshirilgan to'lovlar</p>
            {ledger.payments.length === 0 ? (
              <p className="text-sm text-slate-400">To'lov yozuvlari yo'q</p>
            ) : (
              <ul className="space-y-1.5">
                {ledger.payments.map((p, i) => (
                  <PaymentRow key={i} payment={p} />
                ))}
              </ul>
            )}
          </div>

          {/* O'zgarishlar tarixi (audit) */}
          <div>
            <p className="mb-2 text-sm font-medium text-slate-600">O'zgarishlar tarixi</p>
            <AuditHistoryList
              filters={{ studentId: ledger.student.id }}
              emptyLabel="To'lovlar bo'yicha o'zgarishlar yo'q"
            />
          </div>
        </div>
      )}
    </Modal>
  )
}

/**
 * Bitta to'lov qatori.
 *
 * <b>Storno YASHIRILMAYDI</b> (SPEC §4.1: xato to'lov o'chirilmaydi, ustiga
 * qarshi yozuv qo'yiladi). Shuning uchun bu yerda uch xil qator bor: oddiy
 * to'lov, bekor QILINGAN to'lov (chizib tashlangan) va bekor QILUVCHI storno
 * qatori (manfiy, qizil). Uchovi ham ro'yxatda qoladi — direktor pulning
 * kelib, keyin qaytganini ko'rishi kerak, "hech narsa bo'lmagan" ko'rinish
 * emas. Holat izoh matnidan emas, serverning bayrog'idan olinadi.
 */
function PaymentRow({ payment }: { payment: LedgerPayment }) {
  const reversal = payment.isReversal === true
  const cancelled = payment.reversed === true

  return (
    <li
      className={cn(
        'flex items-center justify-between rounded-lg border px-3 py-2 text-sm',
        reversal ? 'border-red-100 bg-red-50/40' : 'border-slate-100',
      )}
    >
      <span className="flex flex-wrap items-center gap-2 text-slate-500">
        <span className={cancelled ? 'line-through decoration-slate-300' : undefined}>
          {formatDate(payment.date)}
        </span>

        {payment.month && (
          <span className="rounded-md bg-slate-100 px-1.5 py-0.5 text-xs font-medium text-slate-500">
            {formatMonth(payment.month)} uchun
          </span>
        )}

        {reversal && (
          <span className="rounded-md bg-red-100 px-1.5 py-0.5 text-xs font-medium text-red-700">
            STORNO
          </span>
        )}
        {cancelled && (
          <span className="rounded-md bg-slate-200 px-1.5 py-0.5 text-xs font-medium text-slate-600">
            bekor qilingan
          </span>
        )}

        {payment.note && <span className="text-xs text-slate-400">{payment.note}</span>}
      </span>

      <span
        className={cn(
          'font-medium',
          reversal ? 'text-red-600' : cancelled ? 'text-slate-400 line-through' : 'text-emerald-600',
        )}
      >
        {reversal ? '−' : '+'}
        {formatMoney(payment.amount)}
      </span>
    </li>
  )
}
