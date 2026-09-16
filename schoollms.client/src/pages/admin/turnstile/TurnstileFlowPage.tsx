import { useCallback, useEffect, useState } from 'react'
import {
  Bar,
  CartesianGrid,
  ComposedChart,
  Legend,
  Line,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { Clock, Download, LogIn, LogOut } from 'lucide-react'
import {
  getTurnstileFlow,
  type TurnstileFlowReport,
} from '@/api/services/turnstileAnalytics'
import { cn, exportToCsv } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { ClassFilter, DateInput, PageHead, Tile, daysAgo, today, useClassNames } from './shared'

/**
 * Turniket kirib-chiqish statistikasi (#12).
 *
 * Savol oddiy: ertalabki to'lqin qachon bo'ladi va undan KEYIN kim kiradi.
 * Shuning uchun asosiy raqam — "to'lqindan keyin kirganlar": maktab kunini
 * kechiktiradigan bolalar aynan shu ustunda.
 *
 * Kirish/chiqish qoidasi butun tizimda bir xil: kunning birinchi o'tishi —
 * kirish, oxirgisi — chiqish (qurilmaning yo'nalish maydoniga tayanilmaydi,
 * u ko'p o'rnatmada bo'sh keladi).
 */

const groupings = [
  { key: 'hour', label: 'Soat bo\'yicha' },
  { key: 'period', label: 'Dars bo\'yicha' },
]

export function TurnstileFlowPage() {
  const classes = useClassNames()
  const [from, setFrom] = useState(daysAgo(6))
  const [to, setTo] = useState(today())
  const [className, setClassName] = useState('')
  const [groupBy, setGroupBy] = useState('hour')

  const [report, setReport] = useState<TurnstileFlowReport | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const load = useCallback(() => {
    setLoading(true)
    setError('')
    getTurnstileFlow({ from, to, className: className || undefined, groupBy })
      .then(setReport)
      .catch((e) => setError(e?.response?.data?.message ?? "Hisobotni yuklab bo'lmadi"))
      .finally(() => setLoading(false))
  }, [from, to, className, groupBy])

  useEffect(load, [load])

  const chartData = (report?.buckets ?? []).map((b) => ({
    name: b.label,
    Kirdi: b.entered,
    Chiqdi: b.exited,
    'Jami o\'tish': b.passes,
  }))

  const exportBuckets = () =>
    exportToCsv(
      `turniket-kirish-chiqish-${from}_${to}.csv`,
      ['Oraliq', 'Kirdi', 'Chiqdi', "Jami o'tish", 'Kirishlar ulushi'],
      (report?.buckets ?? []).map((b) => [
        b.label,
        String(b.entered),
        String(b.exited),
        String(b.passes),
        `${b.enteredPct}%`,
      ]),
    )

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <PageHead
          title="Kirib-chiqish statistikasi"
          hint="Kun davomida kirish va chiqishlar qanday taqsimlanadi — ertalabki to'lqin va undan keyingilar"
        />
        <div className="flex flex-wrap items-center gap-2">
          <DateInput value={from} onChange={setFrom} title="Boshlanish sanasi" />
          <span className="text-slate-300">—</span>
          <DateInput value={to} onChange={setTo} title="Tugash sanasi" />
          <ClassFilter value={className} onChange={setClassName} classes={classes} />
          <div className="flex w-fit gap-1 rounded-lg bg-slate-100 p-1">
            {groupings.map((g) => (
              <button
                key={g.key}
                onClick={() => setGroupBy(g.key)}
                className={cn(
                  'rounded-md px-3 py-1.5 text-xs font-medium transition-colors',
                  groupBy === g.key ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
                )}
              >
                {g.label}
              </button>
            ))}
          </div>
          <Button variant="secondary" onClick={exportBuckets} disabled={!report?.buckets.length}>
            <Download className="h-4 w-4" /> CSV
          </Button>
        </div>
      </div>

      {error && (
        <div className="rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">{error}</div>
      )}

      {report && groupBy === 'period' && report.groupBy === 'hour' && (
        <div className="rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          Dars vaqtlari (qo'ng'iroqlar jadvali) kiritilmagan — soat kesimida ko'rsatilmoqda.
        </div>
      )}

      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
        <Tile label="Kirish" value={report?.entered ?? 0} tone="good" hint={`${report?.days ?? 0} o'quv kuni`} />
        <Tile label="Chiqish" value={report?.exited ?? 0} hint="Qayd etilgan chiqishlar" />
        <Tile label="Eng gavjum oraliq" value={report?.peakLabel || '—'} hint={`${report?.peakEntered ?? 0} ta kirish`} />
        <Tile
          label="To'lqindan keyin"
          value={report?.afterPeak ?? 0}
          tone={(report?.afterPeakPct ?? 0) > 20 ? 'warn' : 'neutral'}
          hint={`Kirishlarning ${report?.afterPeakPct ?? 0}%`}
        />
        <Tile label="O'rtacha kelish" value={report?.avgCheckIn || '—'} />
        <Tile
          label="Eng erta / eng kech"
          value={`${report?.earliestCheckIn || '—'} / ${report?.latestCheckIn || '—'}`}
        />
      </div>

      <Card>
        <h2 className="mb-3 flex items-center gap-2 text-sm font-semibold text-slate-700">
          <Clock className="h-4 w-4 text-amber-500" />
          Kun davomidagi taqsimot
        </h2>
        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : chartData.length === 0 ? (
          <p className="py-10 text-center text-slate-400">Bu oraliqda turniket hodisasi yo'q</p>
        ) : (
          <ResponsiveContainer width="100%" height={320}>
            <ComposedChart data={chartData} margin={{ top: 10, right: 10, left: 0, bottom: 0 }}>
              <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#e2e8f0" />
              <XAxis dataKey="name" tickLine={false} axisLine={false} tick={{ fontSize: 12, fill: '#94a3b8' }} />
              <YAxis tickLine={false} axisLine={false} tick={{ fontSize: 12, fill: '#94a3b8' }} allowDecimals={false} />
              <Tooltip
                cursor={{ fill: 'rgba(0,0,0,0.03)' }}
                contentStyle={{ borderRadius: 12, border: '1px solid #e2e8f0', fontSize: 13 }}
              />
              <Legend wrapperStyle={{ fontSize: 13 }} />
              <Bar dataKey="Kirdi" fill="#10b981" radius={[6, 6, 0, 0]} maxBarSize={28} />
              <Bar dataKey="Chiqdi" fill="#60a5fa" radius={[6, 6, 0, 0]} maxBarSize={28} />
              <Line type="monotone" dataKey="Jami o'tish" stroke="#94a3b8" strokeWidth={2} dot={{ r: 2 }} />
            </ComposedChart>
          </ResponsiveContainer>
        )}
      </Card>

      <div className="grid gap-6 lg:grid-cols-2">
        <Card className="p-0">
          <h2 className="border-b border-slate-100 px-4 py-3 text-sm font-semibold text-slate-700">Oraliqlar</h2>
          <div className="max-h-96 overflow-auto">
            <table className="w-full text-left text-sm">
              <thead className="sticky top-0 bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">Oraliq</th>
                  <th className="px-4 py-3 text-center">Kirdi</th>
                  <th className="px-4 py-3 text-center">Chiqdi</th>
                  <th className="px-4 py-3 text-center">Ulush</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {(report?.buckets ?? []).map((b) => (
                  <tr key={b.label} className="hover:bg-slate-50/60">
                    <td className="px-4 py-2.5 font-medium text-slate-700">{b.label}</td>
                    <td className="px-4 py-2.5 text-center">
                      <span className="inline-flex items-center gap-1 text-emerald-700">
                        <LogIn className="h-3.5 w-3.5" /> {b.entered}
                      </span>
                    </td>
                    <td className="px-4 py-2.5 text-center">
                      <span className="inline-flex items-center gap-1 text-sky-700">
                        <LogOut className="h-3.5 w-3.5" /> {b.exited}
                      </span>
                    </td>
                    <td className="px-4 py-2.5 text-center text-slate-500">{b.enteredPct}%</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>

        <Card className="p-0">
          <h2 className="border-b border-slate-100 px-4 py-3 text-sm font-semibold text-slate-700">
            Sinflar kesimi
          </h2>
          <div className="max-h-96 overflow-auto">
            <table className="w-full text-left text-sm">
              <thead className="sticky top-0 bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">Sinf</th>
                  <th className="px-4 py-3 text-center">O'rtacha kelish</th>
                  <th className="px-4 py-3 text-center">Eng gavjum</th>
                  <th className="px-4 py-3 text-center">Keyin kirgan</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {(report?.classes ?? []).map((c) => (
                  <tr key={c.className || '—'} className="hover:bg-slate-50/60">
                    <td className="px-4 py-2.5 font-medium text-slate-700">{c.className || 'Sinfsiz'}</td>
                    <td className="px-4 py-2.5 text-center text-slate-600">{c.avgCheckIn || '—'}</td>
                    <td className="px-4 py-2.5 text-center text-slate-500">
                      {c.peakLabel || '—'} <span className="text-slate-300">({c.peakEntered})</span>
                    </td>
                    <td
                      className={cn(
                        'px-4 py-2.5 text-center',
                        c.afterPeak > 0 ? 'font-medium text-amber-600' : 'text-slate-300',
                      )}
                    >
                      {c.afterPeak || '—'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>
      </div>
    </div>
  )
}
