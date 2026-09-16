import { useEffect, useState, type ReactNode } from 'react'
import { Download, FileBarChart } from 'lucide-react'
import type { SchoolClass } from '@/types'
import { getClasses } from '@/api/services/classes'
import {
  getSubjectAttainment,
  type SubjectAttainmentCell,
  type SubjectAttainmentReport as Report,
  type SubjectAttainmentRow,
} from '@/api/services/gradesReport'
import { quarters as allQuarters } from '@/config/constants'
import { cn, exportToCsv } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'

/** Ko'rsatkich: o'rtacha baho yoki sifat foizi. Ikkalasi ham serverdan keladi. */
type Metric = 'average' | 'quality'

const quarterLabel = (qs: number[]) =>
  qs.length === 0 ? '' : `${qs.slice().sort((a, b) => a - b).join(', ')}-chorak`

/** O'rtacha baho: "4,25". Qiymat yo'q bo'lsa bo'sh — nol EMAS. */
const avg = (v: number | null) => (v == null ? '' : v.toFixed(2).replace('.', ','))
const qlt = (v: number) => `${v.toFixed(1).replace('.0', '').replace('.', ',')}%`

/** Katak rangi: past o'zlashtirish darhol ko'zga tashlansin. */
function cellTone(metric: Metric, cell: SubjectAttainmentCell): string {
  if (cell.values === 0) return 'text-slate-300'
  const v = metric === 'average' ? cell.average : cell.qualityPct
  if (v == null) return 'text-slate-300'
  if (metric === 'average') {
    if (v >= 4.5) return 'text-emerald-700 font-semibold'
    if (v >= 3.5) return 'text-slate-700'
    if (v >= 3) return 'text-amber-600'
    return 'text-red-600 font-semibold'
  }
  if (v >= 75) return 'text-emerald-700 font-semibold'
  if (v >= 50) return 'text-slate-700'
  if (v >= 25) return 'text-amber-600'
  return 'text-red-600 font-semibold'
}

/**
 * O'zlashtirish (fanlar bo'yicha) — Analitika #3: sinf × fan pivoti.
 *
 * Bu mavjud "Baholar hisoboti" oilasining yangi kesimi, parallel hisobot emas: baho manbai
 * ("Sinf bo'yicha" hisobotdagi kabi rasmiy chorak bahosi kunlik o'rtachadan ustun) bir xil,
 * shuning uchun ikki ekran bir xil sinf uchun bir xil raqam ko'rsatadi.
 *
 * Barcha o'rtacha va foizlar SERVERDA hisoblanadi.
 */
export function SubjectAttainmentReport() {
  const [classes, setClasses] = useState<SchoolClass[]>([])
  const [loadingClasses, setLoadingClasses] = useState(true)
  const [selectedClasses, setSelectedClasses] = useState<Set<string>>(new Set())
  const [selectedQuarters, setSelectedQuarters] = useState<Set<number>>(new Set())
  const [report, setReport] = useState<Report | null>(null)
  const [builtQuarters, setBuiltQuarters] = useState<number[]>([])
  const [building, setBuilding] = useState(false)
  const [metric, setMetric] = useState<Metric>('average')
  const [showQuarters, setShowQuarters] = useState(false)

  useEffect(() => {
    getClasses()
      .then(setClasses)
      .finally(() => setLoadingClasses(false))
  }, [])

  const toggleClass = (id: string) =>
    setSelectedClasses((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const allClassesChecked = classes.length > 0 && classes.every((c) => selectedClasses.has(c.id))
  const toggleAllClasses = () =>
    setSelectedClasses(allClassesChecked ? new Set() : new Set(classes.map((c) => c.id)))

  const toggleQuarter = (q: number) =>
    setSelectedQuarters((prev) => {
      const next = new Set(prev)
      if (next.has(q)) next.delete(q)
      else next.add(q)
      return next
    })

  const canBuild = selectedClasses.size > 0 && selectedQuarters.size > 0

  const build = () => {
    if (!canBuild) return
    const qs = [...selectedQuarters].sort((a, b) => a - b)
    setBuilding(true)
    getSubjectAttainment([...selectedClasses], qs)
      .then((r) => {
        setReport(r)
        setBuiltQuarters(qs)
      })
      .finally(() => setBuilding(false))
  }

  const exportCsv = () => {
    if (!report) return
    const headers = ['Sinf', "O'quvchi", ...report.subjects.map((s) => s.name), "Sinf o'rtachasi"]
    const line = (row: SubjectAttainmentRow) => [
      row.className,
      String(row.students),
      ...row.cells.map((c) =>
        metric === 'average' ? avg(c.average) : c.values === 0 ? '' : qlt(c.qualityPct),
      ),
      metric === 'average' ? avg(row.average) : qlt(row.qualityPct),
    ]
    exportToCsv(
      'ozlashtirish-fanlar-boyicha.csv',
      headers,
      [...report.rows.map(line), line(report.school)],
    )
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">
          Baholar hisoboti — Fanlar bo'yicha
        </h1>
        <p className="text-sm text-slate-400">
          Sinf × fan kesimida o'rtacha baho va sifat ko'rsatkichi, maktab o'rtachasi bilan
        </p>
      </div>

      {loadingClasses ? (
        <Loader label="Yuklanmoqda..." />
      ) : classes.length === 0 ? (
        <Card>
          <p className="py-8 text-center text-sm text-slate-400">Sinflar yo'q</p>
        </Card>
      ) : (
        <>
          {/* 1-qadam: sinflar */}
          <Card>
            <div className="mb-3 flex items-center justify-between">
              <h2 className="font-semibold text-slate-800">Sinflar</h2>
              <button
                type="button"
                onClick={toggleAllClasses}
                className="text-sm font-medium text-brand-600 hover:text-brand-700"
              >
                {allClassesChecked ? 'Tanlovni bekor qilish' : 'Barchasini tanlash'}
              </button>
            </div>
            <div className="grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-4">
              {classes.map((c) => (
                <label
                  key={c.id}
                  className={cn(
                    'flex cursor-pointer items-center gap-2 rounded-lg border px-3 py-2 text-sm transition-colors',
                    selectedClasses.has(c.id)
                      ? 'border-brand-300 bg-brand-50 text-brand-800'
                      : 'border-slate-200 text-slate-600 hover:bg-slate-50',
                  )}
                >
                  <input
                    type="checkbox"
                    checked={selectedClasses.has(c.id)}
                    onChange={() => toggleClass(c.id)}
                    className="h-4 w-4 accent-brand-600"
                  />
                  {c.name}
                </label>
              ))}
            </div>
          </Card>

          {/* 2-qadam: choraklar */}
          {selectedClasses.size > 0 && (
            <Card>
              <h2 className="mb-3 font-semibold text-slate-800">Choraklar</h2>
              <div className="flex flex-wrap gap-2">
                {allQuarters.map((q) => (
                  <label
                    key={q}
                    className={cn(
                      'flex cursor-pointer items-center gap-2 rounded-lg border px-4 py-2 text-sm transition-colors',
                      selectedQuarters.has(q)
                        ? 'border-brand-300 bg-brand-50 text-brand-800'
                        : 'border-slate-200 text-slate-600 hover:bg-slate-50',
                    )}
                  >
                    <input
                      type="checkbox"
                      checked={selectedQuarters.has(q)}
                      onChange={() => toggleQuarter(q)}
                      className="h-4 w-4 accent-brand-600"
                    />
                    {q}-chorak
                  </label>
                ))}
              </div>
              <div className="mt-4 flex flex-wrap items-center gap-2">
                <Button onClick={build} disabled={!canBuild || building}>
                  <FileBarChart className="h-4 w-4" />
                  {building ? 'Hisoblanmoqda...' : 'Hisobot qurish'}
                </Button>
                {report && report.rows.length > 0 && (
                  <Button variant="secondary" onClick={exportCsv}>
                    <Download className="h-4 w-4" /> Yuklab olish
                  </Button>
                )}
              </div>
            </Card>
          )}

          {/* Natija */}
          {building ? (
            <Loader label="Hisobot tayyorlanmoqda..." />
          ) : report ? (
            report.rows.length === 0 || report.subjects.length === 0 ? (
              <Card>
                <p className="py-8 text-center text-sm text-slate-400">
                  Tanlangan sinf va choraklar bo'yicha ma'lumot topilmadi
                </p>
              </Card>
            ) : (
              <Card className="space-y-3">
                <div className="flex flex-wrap items-end justify-between gap-3">
                  <div>
                    <h2 className="text-lg font-semibold text-slate-800">
                      Hisobot: O'zlashtirish (fanlar bo'yicha)
                    </h2>
                    <p className="text-sm text-slate-500">{quarterLabel(builtQuarters)}</p>
                  </div>
                  <div className="flex flex-wrap items-center gap-2">
                    <div className="flex w-fit gap-1 rounded-lg bg-slate-100 p-1">
                      <MetricBtn
                        active={metric === 'average'}
                        onClick={() => setMetric('average')}
                      >
                        O'rtacha baho
                      </MetricBtn>
                      <MetricBtn
                        active={metric === 'quality'}
                        onClick={() => setMetric('quality')}
                      >
                        Sifat %
                      </MetricBtn>
                    </div>
                    {builtQuarters.length > 1 && (
                      <label className="flex cursor-pointer items-center gap-2 text-sm text-slate-600">
                        <input
                          type="checkbox"
                          checked={showQuarters}
                          onChange={() => setShowQuarters((v) => !v)}
                          className="h-4 w-4 accent-brand-600"
                        />
                        Chorak yoyilmasi
                      </label>
                    )}
                  </div>
                </div>

                <div className="overflow-x-auto">
                  <table className="w-full text-left text-sm">
                    <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                      <tr>
                        <th className="sticky left-0 z-10 bg-slate-50 px-4 py-3">Sinf</th>
                        <th className="px-3 py-3 text-center">O'quvchi</th>
                        {report.subjects.map((s) => (
                          <th key={s.id} className="px-3 py-3 text-center">
                            {s.name}
                          </th>
                        ))}
                        <th className="px-3 py-3 text-center">Jami</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-100 tabular-nums">
                      {report.rows.map((row) => (
                        <PivotRow
                          key={row.classId}
                          row={row}
                          metric={metric}
                          quarters={showQuarters ? builtQuarters : []}
                        />
                      ))}
                      <PivotRow
                        row={report.school}
                        metric={metric}
                        quarters={showQuarters ? builtQuarters : []}
                      />
                    </tbody>
                  </table>
                </div>

                <p className="text-xs text-slate-400">
                  Bo'sh katak — shu fandan baho yo'q (nol emas). Sifat % — 4 va 5 baholarning
                  ulushi. Rasmiy chorak bahosi kunlik o'rtachadan ustun turadi.
                </p>
              </Card>
            )
          ) : null}
        </>
      )}
    </div>
  )
}

function PivotRow({
  row,
  metric,
  quarters,
}: {
  row: SubjectAttainmentRow
  metric: Metric
  quarters: number[]
}) {
  const isSchool = row.kind === 'school'
  return (
    <tr className={cn(isSchool ? 'bg-slate-50/70 font-semibold' : 'hover:bg-slate-50/60')}>
      <td
        className={cn(
          'sticky left-0 z-10 px-4 py-3 font-medium text-slate-800',
          isSchool ? 'bg-slate-50/70' : 'bg-white',
        )}
      >
        {row.className}
      </td>
      <td className="px-3 py-3 text-center text-slate-500">{row.students}</td>
      {row.cells.map((c) => (
        <td key={c.subjectId} className={cn('px-3 py-3 text-center', cellTone(metric, c))}>
          {c.values === 0 ? '—' : metric === 'average' ? avg(c.average) : qlt(c.qualityPct)}
          {quarters.length > 0 && c.values > 0 && (
            <span className="mt-0.5 block text-[11px] font-normal text-slate-400">
              {quarters
                .map((q) => (c.byQuarter[q] == null ? '·' : avg(c.byQuarter[q])))
                .join(' / ')}
            </span>
          )}
        </td>
      ))}
      <td className="px-3 py-3 text-center text-slate-800">
        {metric === 'average' ? avg(row.average) || '—' : qlt(row.qualityPct)}
      </td>
    </tr>
  )
}

function MetricBtn({
  active,
  onClick,
  children,
}: {
  active: boolean
  onClick: () => void
  children: ReactNode
}) {
  return (
    <button
      onClick={onClick}
      className={cn(
        'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
        active ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
      )}
    >
      {children}
    </button>
  )
}
