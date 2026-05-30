import { useEffect, useState } from 'react'
import type { MonthStatus, SalaryLedger, SalaryReportRow } from '@/types'
import { getSalaryLedger } from '@/api/services/teachers'
import { Modal } from '@/components/ui/Modal'
import { Loader } from '@/components/ui/Loader'
import { AuditHistoryList } from '@/components/audit/AuditHistoryList'
import { formatDate, formatMoney, cn } from '@/lib/utils'
import { formatMonth, monthStatusLabels } from '@/config/constants'

interface Props {
  teacher: SalaryReportRow | null
  from: string
  to: string
  onClose: () => void
}

const statusStyles: Record<MonthStatus, string> = {
  paid: 'bg-emerald-50 text-emerald-700',
  partial: 'bg-amber-50 text-amber-700',
  unpaid: 'bg-red-50 text-red-700',
}

export function TeacherSalaryDetailModal({ teacher, from, to, onClose }: Props) {
  const [ledger, setLedger] = useState<SalaryLedger | null>(null)
  const [loading, setLoading] = useState(false)

  useEffect(() => {
    if (!teacher) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda hisobni yuklash (maqsadli)
    setLoading(true)
    setLedger(null)
    getSalaryLedger(teacher.teacherId, from, to)
      .then(setLedger)
      .finally(() => setLoading(false))
  }, [teacher, from, to])

  return (
    <Modal open={!!teacher} onClose={onClose} size="lg" title="O'qituvchi oyligi">
      {loading || !ledger ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-5">
          {/* Sarlavha */}
          <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl bg-slate-50 px-4 py-3">
            <div>
              <p className="font-semibold text-slate-800">{ledger.fullName}</p>
              <p className="text-sm text-slate-500">Belgilangan oylik: {formatMoney(ledger.salary)}</p>
            </div>
            <div className="text-right">
              <p className="text-xs text-slate-400">Qoldiq</p>
              <p
                className={cn(
                  'text-lg font-semibold',
                  ledger.remaining > 0
                    ? 'text-red-600'
                    : ledger.remaining < 0
                      ? 'text-emerald-600'
                      : 'text-slate-600',
                )}
              >
                {ledger.remaining < 0
                  ? `+${formatMoney(-ledger.remaining)}`
                  : formatMoney(ledger.remaining)}
              </p>
            </div>
          </div>

          {/* Qisqa ko'rsatkichlar */}
          <div className="grid grid-cols-3 gap-3">
            <SummaryCell label="Hisoblangan" value={formatMoney(ledger.totalExpected)} />
            <SummaryCell label="Berilgan" value={formatMoney(ledger.totalPaid)} valueClass="text-emerald-600" />
            <SummaryCell
              label="Qoldiq"
              value={ledger.remaining < 0 ? `+${formatMoney(-ledger.remaining)}` : formatMoney(ledger.remaining)}
              valueClass={ledger.remaining > 0 ? 'text-red-600' : 'text-slate-600'}
            />
          </div>

          {/* Oylar bo'yicha */}
          <div>
            <p className="mb-2 text-sm font-medium text-slate-600">Qaysi oyda qancha berilgani</p>
            <div className="overflow-hidden rounded-xl border border-slate-200">
              <table className="w-full text-left text-sm">
                <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-4 py-2.5">Oy</th>
                    <th className="px-4 py-2.5 text-right">Belgilangan</th>
                    <th className="px-4 py-2.5 text-right">Berilgan</th>
                    <th className="px-4 py-2.5 text-right">Qoldiq</th>
                    <th className="px-4 py-2.5 text-center">Holat</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {ledger.months.map((m) => (
                    <tr key={m.month} className="hover:bg-slate-50/60">
                      <td className="px-4 py-2.5 font-medium text-slate-700">{formatMonth(m.month)}</td>
                      <td className="px-4 py-2.5 text-right text-slate-600">{formatMoney(m.expected)}</td>
                      <td className="px-4 py-2.5 text-right text-emerald-600">{formatMoney(m.paid)}</td>
                      <td
                        className={cn(
                          'px-4 py-2.5 text-right',
                          m.remaining > 0 ? 'text-red-600' : 'text-slate-400',
                        )}
                      >
                        {m.remaining < 0 ? `+${formatMoney(-m.remaining)}` : formatMoney(m.remaining)}
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
                    <td className="px-4 py-2.5 text-right">{formatMoney(ledger.totalExpected)}</td>
                    <td className="px-4 py-2.5 text-right text-emerald-700">{formatMoney(ledger.totalPaid)}</td>
                    <td className="px-4 py-2.5 text-right" colSpan={2} />
                  </tr>
                </tfoot>
              </table>
            </div>
          </div>

          {/* Berilgan maoshlar ro'yxati */}
          <div>
            <p className="mb-2 text-sm font-medium text-slate-600">Berilgan maoshlar</p>
            {ledger.payments.length === 0 ? (
              <p className="text-sm text-slate-400">Bu davrda maosh berilmagan</p>
            ) : (
              <ul className="space-y-1.5">
                {ledger.payments.map((p, i) => (
                  <li
                    key={i}
                    className="flex items-center justify-between rounded-lg border border-slate-100 px-3 py-2 text-sm"
                  >
                    <span className="text-slate-500">{formatDate(p.date)}</span>
                    <span className="font-medium text-slate-700">{formatMoney(p.amount)}</span>
                  </li>
                ))}
              </ul>
            )}
          </div>

          {/* O'zgarishlar tarixi */}
          <div>
            <p className="mb-2 text-sm font-medium text-slate-600">O'zgarishlar tarixi</p>
            <AuditHistoryList
              filters={{ teacherId: ledger.teacherId }}
              emptyLabel="Maosh bo'yicha o'zgarishlar yo'q"
            />
          </div>
        </div>
      )}
    </Modal>
  )
}

function SummaryCell({
  label,
  value,
  valueClass = 'text-slate-700',
}: {
  label: string
  value: string
  valueClass?: string
}) {
  return (
    <div className="rounded-xl border border-slate-100 px-3 py-2.5 text-center">
      <p className="text-xs text-slate-400">{label}</p>
      <p className={cn('mt-0.5 font-semibold', valueClass)}>{value}</p>
    </div>
  )
}
