import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
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
import { AlertTriangle, ChevronLeft, ChevronRight, Download, WifiOff } from 'lucide-react'
import {
  getTurnstileAttendance,
  getTurnstileLateEarly,
  getTurnstileTodayLate,
  getTurnstileViolations,
  type TurnstileAttendanceReport,
  type TurnstileLateEarlySummary,
  type TurnstileTodayLate,
  type TurnstileViolationsPage,
} from '@/api/services/turnstileAnalytics'
import { cn, exportToCsv } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { ClassFilter, DateInput, PageHead, Tile } from './shared'
import { daysAgo, shortDate, syncLabel, today, useClassNames } from './helpers'

/**
 * Turniket analitikasi (#11).
 *
 * Kun yoki oraliq uchun: kim kirdi, kim UMUMAN kirmadi, kim kechikdi va kim
 * darslar tugamasdan chiqib ketdi. Sarlavhadagi raqam — bugun kechikkanlar soni.
 *
 * Qurilma ID biriktirilmagan o'quvchi hisobotga KIRMAYDI (turniket uni ko'ra
 * olmaydi) — ular alohida "Biriktirilmagan" filtrida ko'rinadi, aks holda
 * hisobot ularni "kelmagan" deb ko'rsatib yolg'on gapirardi.
 */

const statuses = [
  { key: '', label: 'Barchasi' },
  { key: 'entered', label: 'Kirganlar' },
  { key: 'missing', label: 'Kirmaganlar' },
  { key: 'late', label: 'Kechikkanlar' },
  { key: 'early', label: 'Erta ketganlar' },
  { key: 'unlinked', label: 'Biriktirilmagan' },
]

const violationTypes = [
  { key: '', label: 'Hammasi' },
  { key: 'late', label: 'Kechikish' },
  { key: 'early', label: 'Erta ketish' },
]

export function TurnstileAnalyticsPage() {
  const classes = useClassNames()
  const [from, setFrom] = useState(daysAgo(6))
  const [to, setTo] = useState(today())
  const [className, setClassName] = useState('')
  const [status, setStatus] = useState('')

  const [report, setReport] = useState<TurnstileAttendanceReport | null>(null)
  const [trend, setTrend] = useState<TurnstileLateEarlySummary | null>(null)
  const [headline, setHeadline] = useState<TurnstileTodayLate | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const [violations, setViolations] = useState<TurnstileViolationsPage | null>(null)
  const [vType, setVType] = useState('')
  const [vPage, setVPage] = useState(1)

  const load = useCallback(() => {
    setLoading(true)
    setError('')
    Promise.all([
      getTurnstileAttendance({ from, to, className: className || undefined, status: status || undefined }),
      getTurnstileLateEarly({ from, to, className: className || undefined }),
      getTurnstileTodayLate(),
    ])
      .then(([a, t, h]) => {
        setReport(a)
        setTrend(t)
        setHeadline(h)
      })
      .catch((e) => setError(e?.response?.data?.message ?? "Hisobotni yuklab bo'lmadi"))
      .finally(() => setLoading(false))
  }, [from, to, className, status])

  // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr yoki sahifa o'zgarganda qayta yuklash (maqsadli, loyihadagi mavjud naqsh)
  useEffect(load, [load])

  useEffect(() => {
    getTurnstileViolations({
      from,
      to,
      className: className || undefined,
      type: vType || undefined,
      page: vPage,
      pageSize: 20,
    })
      .then(setViolations)
      .catch(() => setViolations(null))
  }, [from, to, className, vType, vPage])

  // Oraliq yoki filtr o'zgarsa buzilishlar birinchi sahifadan boshlanadi —
  // effektda emas, o'zgartirgan joyda (ikki marta so'rov ketmasin).
  const pick = <T,>(set: (v: T) => void) => (v: T) => {
    set(v)
    setVPage(1)
  }

  const s = report?.summary
  const chartData = (trend?.days ?? []).map((d) => ({
    name: shortDate(d.date).slice(0, 5),
    Kirdi: d.entered,
    Kelmadi: d.missing,
    Kechikdi: d.late,
  }))

  const exportRows = () =>
    exportToCsv(
      `turniket-analitika-${from}_${to}.csv`,
      ['F.I.SH', 'Sinf', 'Qurilma ID', 'Kirgan kun', 'Kelmagan kun', 'Kechikish', 'Erta ketish', "O'rtacha kelish", 'Foiz'],
      (report?.rows ?? []).map((r) => [
        r.fullName,
        r.className,
        r.deviceUserId,
        String(r.daysEntered),
        String(r.daysMissed),
        String(r.lateDays),
        String(r.earlyDays),
        r.avgCheckIn,
        `${r.attendanceRate}%`,
      ]),
    )

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <PageHead
          title="Turniket analitikasi"
          hint="Kim kirdi, kim kirmadi, kim kechikdi — turniket/FaceID hodisalari bo'yicha"
        />
        <div className="flex flex-wrap items-center gap-2">
          <DateInput value={from} onChange={pick(setFrom)} title="Boshlanish sanasi" />
          <span className="text-slate-300">—</span>
          <DateInput value={to} onChange={pick(setTo)} title="Tugash sanasi" />
          <ClassFilter value={className} onChange={pick(setClassName)} classes={classes} />
          <Button variant="secondary" onClick={exportRows} disabled={!report?.rows.length}>
            <Download className="h-4 w-4" /> CSV
          </Button>
        </div>
      </div>

      {report && !report.turnstileEnabled && (
        <Link
          to="/admin/settings/turnstile"
          className="flex items-center gap-2 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800 hover:bg-amber-100"
        >
          <WifiOff className="h-4 w-4" /> Turniket integratsiyasi o'chiq — hisobot faqat eski hodisalarni
          ko'rsatadi. Sozlash uchun bosing.
        </Link>
      )}

      {error && (
        <div className="rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">{error}</div>
      )}

      {/* Sarlavhadagi raqam — bugun kechikkanlar. */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
        <Tile
          label="Bugun kechikdi"
          value={headline?.late ?? 0}
          hint={headline?.schoolDay === false ? "Bugun o'quv kuni emas" : `Bugun kirgan: ${headline?.entered ?? 0}`}
          tone={(headline?.late ?? 0) > 0 ? 'warn' : 'good'}
        />
        <Tile label="O'quvchi" value={s?.students ?? 0} hint={`Biriktirilgan: ${s?.linked ?? 0}`} />
        <Tile label="Kirgan" value={s?.entered ?? 0} tone="good" hint={`${s?.attendanceRate ?? 0}% davomat`} />
        <Tile label="Umuman kirmagan" value={s?.neverEntered ?? 0} tone={(s?.neverEntered ?? 0) > 0 ? 'bad' : 'neutral'} />
        <Tile label="Kechikish" value={s?.lateDays ?? 0} tone="warn" hint={`${s?.lateStudents ?? 0} o'quvchi`} />
        <Tile
          label="Biriktirilmagan"
          value={s?.unlinked ?? 0}
          hint="Turniket ularni ko'rmaydi"
          tone={(s?.unlinked ?? 0) > 0 ? 'warn' : 'neutral'}
        />
      </div>

      {chartData.length > 1 && (
        <Card>
          <div className="mb-3 flex items-center justify-between">
            <h2 className="text-sm font-semibold text-slate-700">Kunlar bo'yicha</h2>
            <span className="text-xs text-slate-400">
              O'quv kunlari: {report?.schoolDays ?? 0} · Oxirgi sinx: {syncLabel(report?.lastSync ?? '')}
            </span>
          </div>
          <ResponsiveContainer width="100%" height={260}>
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
              <Bar dataKey="Kelmadi" fill="#cbd5e1" radius={[6, 6, 0, 0]} maxBarSize={28} />
              <Line type="monotone" dataKey="Kechikdi" stroke="#f59e0b" strokeWidth={2} dot={{ r: 3 }} />
            </ComposedChart>
          </ResponsiveContainer>
        </Card>
      )}

      <Card className="p-0">
        <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-100 px-4 py-3">
          <h2 className="text-sm font-semibold text-slate-700">O'quvchilar</h2>
          <div className="flex w-fit gap-1 rounded-lg bg-slate-100 p-1">
            {statuses.map((st) => (
              <button
                key={st.key}
                onClick={() => setStatus(st.key)}
                className={cn(
                  'rounded-md px-3 py-1 text-xs font-medium transition-colors',
                  status === st.key ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
                )}
              >
                {st.label}
              </button>
            ))}
          </div>
        </div>

        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : !report || report.rows.length === 0 ? (
          <p className="py-10 text-center text-slate-400">Bu filtrga mos o'quvchi yo'q</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">F.I.SH</th>
                  <th className="px-4 py-3">Sinf</th>
                  <th className="px-4 py-3 text-center">Kirgan kun</th>
                  <th className="px-4 py-3 text-center">Kelmagan</th>
                  <th className="px-4 py-3 text-center">Kechikish</th>
                  <th className="px-4 py-3 text-center">Erta ketish</th>
                  <th className="px-4 py-3 text-center">O'rtacha kelish</th>
                  <th className="px-4 py-3 text-center">Davomat</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {report.rows.map((r) => (
                  <tr key={r.studentId} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3 font-medium text-slate-800">
                      <span className="block max-w-[14rem] truncate" title={r.fullName}>
                        {r.fullName}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-slate-500">{r.className || '—'}</td>
                    <td className="px-4 py-3 text-center text-slate-600">{r.daysEntered}</td>
                    <td className={cn('px-4 py-3 text-center', r.daysMissed > 0 ? 'font-medium text-rose-600' : 'text-slate-300')}>
                      {r.daysMissed || '—'}
                    </td>
                    <td className={cn('px-4 py-3 text-center', r.lateDays > 0 ? 'font-medium text-amber-600' : 'text-slate-300')}>
                      {r.lateDays ? `${r.lateDays} (${r.lateMinutes} daq.)` : '—'}
                    </td>
                    <td className={cn('px-4 py-3 text-center', r.earlyDays > 0 ? 'font-medium text-amber-600' : 'text-slate-300')}>
                      {r.earlyDays || '—'}
                    </td>
                    <td className="px-4 py-3 text-center text-slate-600">{r.avgCheckIn || '—'}</td>
                    <td className="px-4 py-3 text-center">
                      {r.deviceUserId ? (
                        <span
                          className={cn(
                            'rounded-full px-2 py-0.5 text-xs font-medium',
                            r.attendanceRate >= 90
                              ? 'bg-emerald-50 text-emerald-700'
                              : r.attendanceRate >= 70
                                ? 'bg-amber-50 text-amber-700'
                                : 'bg-rose-50 text-rose-700',
                          )}
                        >
                          {r.attendanceRate}%
                        </span>
                      ) : (
                        <span className="rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-500">
                          ID yo'q
                        </span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {/* Buzilishlar — alohida, sahifalangan ro'yxat. */}
      <Card className="p-0">
        <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-100 px-4 py-3">
          <h2 className="flex items-center gap-2 text-sm font-semibold text-slate-700">
            <AlertTriangle className="h-4 w-4 text-amber-500" />
            Buzilishlar
            {violations && (
              <span className="text-xs font-normal text-slate-400">
                kechikish {violations.lateTotal} · erta ketish {violations.earlyTotal}
              </span>
            )}
          </h2>
          <div className="flex w-fit gap-1 rounded-lg bg-slate-100 p-1">
            {violationTypes.map((t) => (
              <button
                key={t.key}
                onClick={() => pick(setVType)(t.key)}
                className={cn(
                  'rounded-md px-3 py-1 text-xs font-medium transition-colors',
                  vType === t.key ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
                )}
              >
                {t.label}
              </button>
            ))}
          </div>
        </div>

        {!violations || violations.items.length === 0 ? (
          <p className="py-10 text-center text-slate-400">Buzilish yo'q — hammasi o'z vaqtida</p>
        ) : (
          <>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-4 py-3">Sana</th>
                    <th className="px-4 py-3">F.I.SH</th>
                    <th className="px-4 py-3">Sinf</th>
                    <th className="px-4 py-3">Turi</th>
                    <th className="px-4 py-3 text-center">Kutilgan</th>
                    <th className="px-4 py-3 text-center">Kirgan</th>
                    <th className="px-4 py-3 text-center">Chiqqan</th>
                    <th className="px-4 py-3 text-center">Farq</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {violations.items.map((v) => (
                    <tr key={`${v.studentId}-${v.date}-${v.type}`} className="hover:bg-slate-50/60">
                      <td className="px-4 py-3 text-slate-500">{shortDate(v.date)}</td>
                      <td className="px-4 py-3 font-medium text-slate-800">
                      <span className="block max-w-[14rem] truncate" title={v.fullName}>
                        {v.fullName}
                      </span>
                    </td>
                      <td className="px-4 py-3 text-slate-500">{v.className || '—'}</td>
                      <td className="px-4 py-3">
                        <span
                          className={cn(
                            'rounded-full px-2 py-0.5 text-xs font-medium',
                            v.type === 'late' ? 'bg-amber-50 text-amber-700' : 'bg-sky-50 text-sky-700',
                          )}
                        >
                          {v.type === 'late' ? 'Kechikdi' : 'Erta ketdi'}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-center text-slate-500">{v.expected || '—'}</td>
                      <td className="px-4 py-3 text-center text-slate-600">{v.checkIn || '—'}</td>
                      <td className="px-4 py-3 text-center text-slate-600">{v.checkOut || '—'}</td>
                      <td className="px-4 py-3 text-center font-medium text-slate-700">{v.minutes} daq.</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="flex items-center justify-between border-t border-slate-100 px-4 py-3 text-sm text-slate-500">
              <span>
                Jami {violations.total} ta · {violations.page}/{violations.pages} sahifa
              </span>
              <div className="flex gap-2">
                <Button
                  variant="secondary"
                  onClick={() => setVPage((p) => Math.max(1, p - 1))}
                  disabled={violations.page <= 1}
                >
                  <ChevronLeft className="h-4 w-4" /> Oldingi
                </Button>
                <Button
                  variant="secondary"
                  onClick={() => setVPage((p) => p + 1)}
                  disabled={violations.page >= violations.pages}
                >
                  Keyingi <ChevronRight className="h-4 w-4" />
                </Button>
              </div>
            </div>
          </>
        )}
      </Card>
    </div>
  )
}
