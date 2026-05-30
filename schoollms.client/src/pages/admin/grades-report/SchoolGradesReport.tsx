import { useEffect, useState } from 'react'
import { Download, FileBarChart } from 'lucide-react'
import type { SchoolClass } from '@/types'
import { getClasses } from '@/api/services/classes'
import {
  getSchoolGradesReport,
  type GradesProgressReport,
  type GradesProgressRow,
} from '@/api/services/gradesReport'
import { quarters as allQuarters } from '@/config/constants'
import { cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'

const num = (n: number) => (Number.isInteger(n) ? String(n) : n.toFixed(2).replace('.', ','))
const avg = (n: number) => (n > 0 ? n.toFixed(2).replace('.', ',') : '')
const names = (s: string) => s.split('\n').map((x) => x.trim()).filter(Boolean)
const quarterLabel = (qs: number[]) =>
  qs.length === 0 ? '' : `${qs.slice().sort((a, b) => a - b).join(', ')}-chorak`

/**
 * Maktab bo'yicha o'zlashtirish hisoboti (emaktab "O'zlashtirish" ko'rinishida): sinflar +
 * choraklar tanlanadi, "Hisobot qurish" bosilganda a'lochi/yaxshi/muvaffaqiyat/o'zlashtirmaydigan
 * bo'yicha jadval chiqadi va Excel (.xls) ko'rinishida yuklab olinadi.
 */
export function SchoolGradesReport() {
  const [classes, setClasses] = useState<SchoolClass[]>([])
  const [loadingClasses, setLoadingClasses] = useState(true)
  const [selectedClasses, setSelectedClasses] = useState<Set<string>>(new Set())
  const [selectedQuarters, setSelectedQuarters] = useState<Set<number>>(new Set())
  const [report, setReport] = useState<GradesProgressReport | null>(null)
  const [builtQuarters, setBuiltQuarters] = useState<number[]>([])
  const [building, setBuilding] = useState(false)

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
    getSchoolGradesReport([...selectedClasses], qs)
      .then((r) => {
        setReport(r)
        setBuiltQuarters(qs)
      })
      .finally(() => setBuilding(false))
  }

  const exportXls = () => {
    if (!report) return
    const html = buildXls(report, builtQuarters)
    const blob = new Blob(['﻿' + html], { type: 'application/vnd.ms-excel;charset=utf-8;' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = 'ozlashtirish-hisoboti.xls'
    a.click()
    URL.revokeObjectURL(url)
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Baholar hisoboti — Maktab bo'yicha</h1>
        <p className="text-sm text-slate-400">
          Sinflar va choraklarni tanlab hisobot quring, so'ng Excelga yuklang
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
                  <Button variant="secondary" onClick={exportXls}>
                    <Download className="h-4 w-4" /> Excelga yuklab olish
                  </Button>
                )}
              </div>
            </Card>
          )}

          {/* Natija */}
          {building ? (
            <Loader label="Hisobot tayyorlanmoqda..." />
          ) : report ? (
            report.rows.length === 0 ? (
              <Card>
                <p className="py-8 text-center text-sm text-slate-400">
                  Tanlangan sinf va choraklar bo'yicha ma'lumot topilmadi
                </p>
              </Card>
            ) : (
              <Card className="space-y-3 overflow-x-auto">
                <div>
                  <h2 className="text-lg font-semibold text-slate-800">Hisobot: O'zlashtirish</h2>
                  <p className="text-sm text-slate-500">{quarterLabel(builtQuarters)}</p>
                  <p className="mt-1 text-sm text-slate-500">
                    Davr oxiriga: <b>{report.totalStudents}</b> · Baholar mavjud emas:{' '}
                    <b>{report.noGradesCount}</b>
                  </p>
                </div>
                <ReportTable rows={report.rows} />
              </Card>
            )
          ) : null}
        </>
      )}
    </div>
  )
}

/* ---------- Ekrandagi jadval ---------- */

const th = 'border border-slate-300 bg-slate-100 px-2 py-2 text-center text-xs font-semibold text-slate-600'
const td = 'border border-slate-200 px-2 py-1.5 text-center text-slate-700'
const tdName = 'border border-slate-200 px-2 py-1.5 text-left text-xs text-slate-600'

function ReportTable({ rows }: { rows: GradesProgressRow[] }) {
  const blanks = (n: number) => Array.from({ length: n }, (_, i) => <td key={i} className={td} />)

  return (
    <table className="w-full border-collapse text-sm">
      <thead>
        <tr>
          <th className={th} rowSpan={3}>Sinf</th>
          <th className={th} rowSpan={3}>Ta'lim tili</th>
          <th className={th} colSpan={11}>O'quvchilar</th>
          <th className={th} rowSpan={3}>O'rtacha reyting</th>
          <th className={th} rowSpan={3}>O'TM (%)</th>
        </tr>
        <tr>
          <th className={th} rowSpan={2}>Jami</th>
          <th className={th} colSpan={3}>A'lochilar</th>
          <th className={th} colSpan={2}>Yaxshi o'zlashtiruvchilar</th>
          <th className={th} colSpan={2}>Muvaffaqiyatlar</th>
          <th className={th} colSpan={3}>Yaxshi o'zlashtirmaydigan</th>
        </tr>
        <tr>
          <th className={th}>Jami</th>
          <th className={th}>%</th>
          <th className={th}>FISH</th>
          <th className={th}>Jami</th>
          <th className={th}>%</th>
          <th className={th}>Jami</th>
          <th className={th}>%</th>
          <th className={th}>Jami</th>
          <th className={th}>%</th>
          <th className={th}>FISH</th>
        </tr>
      </thead>
      <tbody>
        {rows.map((r, i) => {
          const bold = r.kind !== 'class'
          const rowCls = bold ? 'bg-slate-50 font-semibold' : 'hover:bg-slate-50/60'
          return (
            <tr key={`${r.kind}-${r.label}-${i}`} className={rowCls}>
              <td className={cn(td, 'text-left font-medium text-slate-800')}>{r.label}</td>
              <td className={td}>{r.language}</td>
              <td className={td}>{r.total}</td>
              {r.showCategories ? (
                <>
                  <td className={td}>{r.excellentCount}</td>
                  <td className={td}>{num(r.excellentPct)}</td>
                  <td className={tdName}>
                    {names(r.excellentNames).map((n, j) => (
                      <div key={j}>{n}</div>
                    ))}
                  </td>
                  <td className={td}>{r.goodCount}</td>
                  <td className={td}>{num(r.goodPct)}</td>
                  <td className={td}>{r.satisfactoryCount}</td>
                  <td className={td}>{num(r.satisfactoryPct)}</td>
                  <td className={td}>{r.poorCount}</td>
                  <td className={td}>{num(r.poorPct)}</td>
                  <td className={tdName}>
                    {names(r.poorNames).map((n, j) => (
                      <div key={j}>{n}</div>
                    ))}
                  </td>
                  <td className={td}>{avg(r.avgRating)}</td>
                  <td className={td}>{num(r.otmPct)}</td>
                </>
              ) : (
                blanks(12)
              )}
            </tr>
          )
        })}
      </tbody>
    </table>
  )
}

/* ---------- Excel (.xls) — HTML jadval Excelda ochiladi ---------- */

function buildXls(report: GradesProgressReport, qs: number[]): string {
  const esc = (s: string) =>
    s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
  const nameCell = (s: string) => names(s).map(esc).join('<br/>')

  const body = report.rows
    .map((r) => {
      const bold = r.kind !== 'class' ? ' class="b"' : ''
      const cat = r.showCategories
        ? `
        <td>${r.excellentCount}</td><td>${num(r.excellentPct)}</td><td class="l">${nameCell(r.excellentNames)}</td>
        <td>${r.goodCount}</td><td>${num(r.goodPct)}</td>
        <td>${r.satisfactoryCount}</td><td>${num(r.satisfactoryPct)}</td>
        <td>${r.poorCount}</td><td>${num(r.poorPct)}</td><td class="l">${nameCell(r.poorNames)}</td>
        <td>${avg(r.avgRating)}</td><td>${num(r.otmPct)}</td>`
        : '<td></td>'.repeat(12)
      return `<tr${bold}><td class="l">${esc(r.label)}</td><td>${esc(r.language)}</td><td>${r.total}</td>${cat}</tr>`
    })
    .join('')

  return `<html xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:x="urn:schemas-microsoft-com:office:excel" xmlns="http://www.w3.org/TR/REC-html40">
<head><meta charset="utf-8"/>
<style>
  table { border-collapse: collapse; font-family: Calibri, Arial, sans-serif; font-size: 11px; }
  th, td { border: 0.5pt solid #999; padding: 3px 6px; text-align: center; vertical-align: middle; }
  th { background: #dbe5f1; font-weight: bold; }
  td.l { text-align: left; }
  tr.b td { background: #f2f2f2; font-weight: bold; }
  h3 { font-family: Calibri, Arial, sans-serif; margin: 0; }
  p { font-family: Calibri, Arial, sans-serif; margin: 2px 0; }
</style></head>
<body>
<h3>Hisobot: O'zlashtirish</h3>
<p>${qs.slice().sort((a, b) => a - b).join(', ')}-chorak</p>
<p>Davr oxiriga: ${report.totalStudents} &nbsp; Baholar mavjud emas: ${report.noGradesCount}</p>
<br/>
<table>
<thead>
<tr>
  <th rowspan="3">Sinf</th><th rowspan="3">Ta'lim tili</th><th colspan="11">O'quvchilar</th>
  <th rowspan="3">O'rtacha reyting</th><th rowspan="3">O'TM (%)</th>
</tr>
<tr>
  <th rowspan="2">Jami</th><th colspan="3">A'lochilar</th><th colspan="2">Yaxshi o'zlashtiruvchilar</th>
  <th colspan="2">Muvaffaqiyatlar</th><th colspan="3">Yaxshi o'zlashtirmaydigan</th>
</tr>
<tr>
  <th>Jami</th><th>%</th><th>FISH</th><th>Jami</th><th>%</th><th>Jami</th><th>%</th><th>Jami</th><th>%</th><th>FISH</th>
</tr>
</thead>
<tbody>${body}</tbody>
</table>
</body></html>`
}
