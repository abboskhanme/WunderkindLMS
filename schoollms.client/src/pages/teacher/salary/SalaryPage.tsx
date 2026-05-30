import { useEffect, useState } from 'react'
import type { MonthStatus, SalaryLedger } from '@/types'
import { getTeacherSalary } from '@/api/services/teacher'
import { formatMoney, cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

const statusLabel: Record<MonthStatus, string> = {
  paid: "To'langan",
  partial: 'Qisman',
  unpaid: "To'lanmagan",
}
const statusColor: Record<MonthStatus, string> = {
  paid: 'text-emerald-600',
  partial: 'text-amber-600',
  unpaid: 'text-red-600',
}

export function TeacherSalaryPage() {
  const today = new Date().toISOString().slice(0, 10)
  const yearStart = `${new Date().getFullYear()}-01-01`
  const [from, setFrom] = useState(yearStart)
  const [to, setTo] = useState(today)
  const [ledger, setLedger] = useState<SalaryLedger | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- davr o'zgarganda maoshni qayta yuklash (maqsadli)
    setLoading(true)
    getTeacherSalary(from, to)
      .then(setLedger)
      .finally(() => setLoading(false))
  }, [from, to])

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Maosh</h1>
        <p className="text-sm text-slate-400">Belgilangan davr bo'yicha oylik hisob-kitobi</p>
      </div>

      <Card className="flex flex-wrap items-center gap-3">
        <label className="text-sm text-slate-500">Davr:</label>
        <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} className={control} />
        <span className="text-slate-400">—</span>
        <input type="date" value={to} onChange={(e) => setTo(e.target.value)} className={control} />
      </Card>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : !ledger ? (
        <Card>
          <p className="py-8 text-center text-slate-400">Ma'lumot yo'q</p>
        </Card>
      ) : (
        <>
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <StatCard label="Oylik" value={formatMoney(ledger.salary)} />
            <StatCard label="Hisoblangan" value={formatMoney(ledger.totalExpected)} />
            <StatCard label="Berilgan" value={formatMoney(ledger.totalPaid)} tone="emerald" />
            <StatCard
              label="Qoldiq"
              value={formatMoney(ledger.remaining)}
              tone={ledger.remaining > 0 ? 'red' : 'slate'}
            />
          </div>

          <Card className="p-0">
            <p className="border-b border-slate-100 px-4 py-3 font-semibold text-slate-800">Oylar bo'yicha</p>
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="bg-slate-50 text-left text-xs uppercase text-slate-400">
                    <th className="px-4 py-2 font-medium">Oy</th>
                    <th className="px-4 py-2 font-medium">Hisoblangan</th>
                    <th className="px-4 py-2 font-medium">Berilgan</th>
                    <th className="px-4 py-2 font-medium">Qoldiq</th>
                    <th className="px-4 py-2 font-medium">Holat</th>
                  </tr>
                </thead>
                <tbody>
                  {ledger.months.map((m) => (
                    <tr key={m.month} className="border-t border-slate-100">
                      <td className="px-4 py-2 font-medium text-slate-700">{m.month}</td>
                      <td className="px-4 py-2 text-slate-600">{formatMoney(m.expected)}</td>
                      <td className="px-4 py-2 text-slate-600">{formatMoney(m.paid)}</td>
                      <td className="px-4 py-2 text-slate-600">{formatMoney(m.remaining)}</td>
                      <td className={cn('px-4 py-2 font-medium', statusColor[m.status])}>
                        {statusLabel[m.status]}
                      </td>
                    </tr>
                  ))}
                  {ledger.months.length === 0 && (
                    <tr>
                      <td colSpan={5} className="px-4 py-8 text-center text-slate-400">
                        Bu davrda oy yo'q
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </Card>

          <Card className="p-0">
            <p className="border-b border-slate-100 px-4 py-3 font-semibold text-slate-800">Berilgan maoshlar</p>
            <div className="divide-y divide-slate-100">
              {ledger.payments.length === 0 && (
                <p className="px-4 py-8 text-center text-slate-400">Hali maosh berilmagan</p>
              )}
              {ledger.payments.map((p, i) => (
                <div key={i} className="flex items-center justify-between gap-2 px-4 py-2.5 text-sm">
                  <div>
                    <p className="font-medium text-slate-700">{formatMoney(p.amount)}</p>
                    {p.note && <p className="text-xs text-slate-400">{p.note}</p>}
                  </div>
                  <span className="text-xs text-slate-400">{p.date}</span>
                </div>
              ))}
            </div>
          </Card>
        </>
      )}
    </div>
  )
}

function StatCard({
  label,
  value,
  tone = 'slate',
}: {
  label: string
  value: string
  tone?: 'slate' | 'emerald' | 'red'
}) {
  const color =
    tone === 'emerald' ? 'text-emerald-600' : tone === 'red' ? 'text-red-600' : 'text-slate-800'
  return (
    <Card>
      <p className="text-xs text-slate-400">{label}</p>
      <p className={cn('mt-1 text-lg font-semibold', color)}>{value}</p>
    </Card>
  )
}
