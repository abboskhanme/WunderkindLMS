/**
 * Davomat intizomi bo'yicha hisobot (docs/modules/existing-module-gaps.md §4, #6).
 *
 * Savol: KIM davomat orqali intizomiy ball yo'qotmoqda va QAYSI sinfda.
 * Manba: `GET /api/admin/attendance-discipline-report` (ruxsat: `discipline`).
 *
 * Bu yerda hech narsa qayta hisoblanmaydi — foiz ham, yig'indi ham serverniki.
 * Ikki ta'rif farqi (yo'qlik faqat o'tilgan darslar bo'yicha; ball esa Ballar
 * nazoratidagidek barcha belgilar bo'yicha) serverda, izoh bilan yozilgan.
 */
import { useState } from 'react'
import { AlertTriangle, CalendarX, Clock, Download, HelpCircle } from 'lucide-react'
import { useAsync } from '@/hooks/useAsync'
import {
  getAttendanceDisciplineReport,
  type AttendanceDisciplineStudent,
} from '@/api/services/attendanceDiscipline'
import { getClasses } from '@/api/services/classes'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { StatCard } from '@/components/ui/StatCard'
import { cn, exportToCsv } from '@/lib/utils'
import { DatePicker } from '@/components/ui/DatePicker'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

const todayStr = new Date().toISOString().slice(0, 10)

/** Sukut bo'yicha — oxirgi 30 kun (bugun ham kiradi), server bilan bir xil. */
function thirtyDaysAgo(): string {
  const d = new Date()
  d.setDate(d.getDate() - 29)
  return d.toISOString().slice(0, 10)
}

/** Ball rangi: jazo qizil, rag'bat yashil, nol — kulrang. */
function pointsClass(v: number): string {
  return v < 0 ? 'text-rose-600' : v > 0 ? 'text-emerald-600' : 'text-slate-400'
}

function riskClass(remaining: number): string {
  if (remaining < 60) return 'bg-rose-50 text-rose-700'
  if (remaining < 85) return 'bg-amber-50 text-amber-700'
  return 'bg-emerald-50 text-emerald-700'
}

export function AttendanceDisciplineReportPage() {
  const [from, setFrom] = useState(thirtyDaysAgo)
  const [to, setTo] = useState(todayStr)
  const [classId, setClassId] = useState('')

  const classesQ = useAsync(() => getClasses(), [])
  const reportQ = useAsync(
    () => getAttendanceDisciplineReport(from, to, classId),
    [from, to, classId],
  )

  const report = reportQ.data

  const handleExport = () => {
    if (!report) return
    exportToCsv(
      `davomat-intizomi_${report.from}_${report.to}.csv`,
      [
        'O\'quvchi',
        'Sinf',
        'Darslar',
        "Yo'qlik",
        'Kechikish',
        'Tekshirilmagan',
        'Davomat balli',
        "Qo'lda ball",
        'Qoldi',
      ],
      report.students.map((s: AttendanceDisciplineStudent) => [
        s.fullName,
        s.className,
        String(s.opportunities),
        String(s.absences),
        String(s.lates),
        String(s.unchecked),
        String(s.attendancePoints),
        String(s.manualPoints),
        String(s.remaining),
      ]),
    )
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Davomat intizomi</h1>
        <p className="text-sm text-slate-400">
          Kim davomat orqali intizomiy ball yo'qotmoqda va qaysi sinfda
        </p>
      </div>

      <Card className="flex flex-wrap items-center gap-3 p-4">
        <span className="text-sm font-medium text-slate-600">Davr:</span>
        <DatePicker
          value={from}
          onChange={(value: string) => setFrom(value)}
          className="w-40"
        />
        <span className="text-slate-400">—</span>
        <DatePicker
          value={to}
          onChange={(value: string) => setTo(value)}
          className="w-40"
        />
        <select
          value={classId}
          onChange={(e) => setClassId(e.target.value)}
          className={cn(control, 'ml-auto')}
        >
          <option value="">Barcha sinflar</option>
          {(classesQ.data ?? []).map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </select>
      </Card>

      {reportQ.loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : reportQ.error ? (
        <Card>
          <p className="text-sm text-rose-600">{reportQ.error}</p>
        </Card>
      ) : (
        report && (
          <>
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
              <StatCard
                label="Yo'qlik"
                value={report.totals.absences}
                icon={CalendarX}
                iconBg="bg-rose-50"
                iconColor="text-rose-600"
                hint={
                  report.totals.attendancePercent === null
                    ? "O'tilgan dars yo'q"
                    : `Davomat: ${report.totals.attendancePercent}%`
                }
              />
              <StatCard
                label="Kechikish"
                value={report.totals.lates}
                icon={Clock}
                iconBg="bg-amber-50"
                iconColor="text-amber-600"
                hint="Yo'qlik sifatida sanalmaydi"
              />
              <StatCard
                label="Tekshirilmagan"
                value={report.totals.unchecked}
                icon={HelpCircle}
                iconBg="bg-slate-100"
                iconColor="text-slate-500"
                hint="Davomati belgilanmagan darslar"
              />
              <StatCard
                label="Yo'qotilgan ball"
                value={report.totals.attendancePoints + report.totals.manualPoints}
                icon={AlertTriangle}
                iconBg="bg-violet-50"
                iconColor="text-violet-600"
                hint={`Davomat: ${report.totals.attendancePoints} · qo'lda: ${report.totals.manualPoints}`}
              />
            </div>

            <Card className="border-slate-200 bg-slate-50/60">
              <p className="text-sm text-slate-600">
                Davomati <b>belgilanmagan</b> katak "keldi" deb hisoblanmaydi — u alohida
                ustunda turadi. "Kech keldi" yo'qlik emas, lekin sababiga ball qo'yilgan
                bo'lsa ballga ta'sir qiladi. <b>Qoldi</b> — butun tarix bo'yicha (100 balldan),
                davr filtri unga ta'sir qilmaydi.
              </p>
            </Card>

            <Card className="p-0">
              <div className="flex items-center justify-between px-5 pt-5">
                <h2 className="text-sm font-semibold uppercase tracking-wide text-slate-400">
                  Sinflar
                </h2>
                <span className="text-xs text-slate-400">
                  {report.totals.students} o'quvchi bo'yicha
                </span>
              </div>
              <div className="mt-3 overflow-x-auto">
                <table className="w-full text-left text-sm">
                  <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                    <tr>
                      <th className="px-5 py-3">Sinf</th>
                      <th className="px-5 py-3 text-center">O'quvchi</th>
                      <th className="px-5 py-3 text-center">Yo'qlik</th>
                      <th className="px-5 py-3 text-center">Kechikish</th>
                      <th className="px-5 py-3 text-center">Tekshirilmagan</th>
                      <th className="px-5 py-3 text-center">Davomat</th>
                      <th className="px-5 py-3 text-center">Ball</th>
                      <th className="px-5 py-3 text-center">1 o'quvchiga</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {report.classes.length === 0 ? (
                      <tr>
                        <td colSpan={8} className="px-5 py-8 text-center text-sm text-slate-400">
                          Bu davrda ma'lumot yo'q
                        </td>
                      </tr>
                    ) : (
                      report.classes.map((c) => (
                        <tr key={c.classId}>
                          <td className="px-5 py-3 font-medium text-slate-800">{c.className}</td>
                          <td className="px-5 py-3 text-center text-slate-600">{c.students}</td>
                          <td className="px-5 py-3 text-center text-slate-600">{c.absences}</td>
                          <td className="px-5 py-3 text-center text-slate-600">{c.lates}</td>
                          <td className="px-5 py-3 text-center text-slate-500">{c.unchecked}</td>
                          <td className="px-5 py-3 text-center text-slate-600">
                            {c.attendancePercent === null ? '—' : `${c.attendancePercent}%`}
                          </td>
                          <td
                            className={cn(
                              'px-5 py-3 text-center font-medium',
                              pointsClass(c.attendancePoints),
                            )}
                          >
                            {c.attendancePoints}
                          </td>
                          <td className="px-5 py-3 text-center text-slate-500">
                            {c.penaltyPerStudent > 0 ? `−${c.penaltyPerStudent}` : '—'}
                          </td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
            </Card>

            <Card className="p-0">
              <div className="flex flex-wrap items-center justify-between gap-2 px-5 pt-5">
                <div>
                  <h2 className="text-sm font-semibold uppercase tracking-wide text-slate-400">
                    O'quvchilar
                  </h2>
                  <p className="mt-1 text-xs text-slate-400">
                    Faqat shu davrda belgisi yoki qo'lda balli bo'lganlar
                  </p>
                </div>
                <Button variant="secondary" onClick={handleExport}>
                  <Download className="h-4 w-4" /> CSV
                </Button>
              </div>
              <div className="mt-3 overflow-x-auto">
                <table className="w-full text-left text-sm">
                  <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                    <tr>
                      <th className="px-5 py-3">O'quvchi</th>
                      <th className="px-5 py-3">Sinf</th>
                      <th className="px-5 py-3 text-center">Yo'qlik</th>
                      <th className="px-5 py-3 text-center">Kechikish</th>
                      <th className="px-5 py-3 text-center">Tekshirilmagan</th>
                      <th className="px-5 py-3 text-center">Davomat</th>
                      <th className="px-5 py-3 text-center">Davomat balli</th>
                      <th className="px-5 py-3 text-center">Qo'lda</th>
                      <th className="px-5 py-3 text-center">Qoldi</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {report.students.length === 0 ? (
                      <tr>
                        <td colSpan={9} className="px-5 py-8 text-center text-sm text-slate-400">
                          Bu davrda birorta belgi yo'q
                        </td>
                      </tr>
                    ) : (
                      report.students.map((s) => (
                        <tr key={s.studentId} className="hover:bg-slate-50/60">
                          <td className="px-5 py-3 font-medium text-slate-800">
                      <span className="block max-w-[14rem] truncate" title={s.fullName}>
                        {s.fullName}
                      </span>
                    </td>
                          <td className="px-5 py-3 text-slate-500">{s.className}</td>
                          <td className="px-5 py-3 text-center text-slate-600">{s.absences}</td>
                          <td className="px-5 py-3 text-center text-slate-600">{s.lates}</td>
                          <td className="px-5 py-3 text-center text-slate-500">{s.unchecked}</td>
                          <td className="px-5 py-3 text-center text-slate-600">
                            {s.attendancePercent === null ? '—' : `${s.attendancePercent}%`}
                          </td>
                          <td
                            className={cn(
                              'px-5 py-3 text-center font-medium',
                              pointsClass(s.attendancePoints),
                            )}
                          >
                            {s.attendancePoints}
                          </td>
                          <td className={cn('px-5 py-3 text-center', pointsClass(s.manualPoints))}>
                            {s.manualPoints}
                          </td>
                          <td className="px-5 py-3 text-center">
                            <span
                              className={cn(
                                'inline-block rounded px-2 py-0.5 text-xs font-medium',
                                riskClass(s.remaining),
                              )}
                            >
                              {s.remaining}
                            </span>
                          </td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
            </Card>

            {report.reasons.length > 0 && (
              <Card className="p-0">
                <h2 className="px-5 pt-5 text-sm font-semibold uppercase tracking-wide text-slate-400">
                  Sabablar
                </h2>
                <div className="mt-3 overflow-x-auto">
                  <table className="w-full text-left text-sm">
                    <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                      <tr>
                        <th className="px-5 py-3">Sabab</th>
                        <th className="px-5 py-3">Manba</th>
                        <th className="px-5 py-3 text-center">Ball</th>
                        <th className="px-5 py-3 text-center">Soni</th>
                        <th className="px-5 py-3 text-center">Jami</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-100">
                      {report.reasons.map((r) => (
                        <tr key={`${r.kind}-${r.reasonId}-${r.pointsEach}`}>
                          <td className="px-5 py-3 font-medium text-slate-800">
                      <span className="block max-w-[14rem] truncate" title={r.name}>
                        {r.name}
                      </span>
                    </td>
                          <td className="px-5 py-3 text-slate-500">
                            {r.kind === 'attendance' ? 'Jurnal davomati' : "Qo'lda kiritilgan"}
                          </td>
                          <td className={cn('px-5 py-3 text-center', pointsClass(r.pointsEach))}>
                            {r.pointsEach}
                          </td>
                          <td className="px-5 py-3 text-center text-slate-600">{r.count}</td>
                          <td
                            className={cn(
                              'px-5 py-3 text-center font-medium',
                              pointsClass(r.totalPoints),
                            )}
                          >
                            {r.totalPoints}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </Card>
            )}
          </>
        )
      )}
    </div>
  )
}
