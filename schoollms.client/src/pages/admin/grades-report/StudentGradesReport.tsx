import { useEffect, useMemo, useState } from 'react'
import { Download, FileBarChart } from 'lucide-react'
import type { SchoolClass, Student } from '@/types'
import { getClasses } from '@/api/services/classes'
import { getStudents } from '@/api/services/students'
import { getStudentProgressReport, type StudentReport } from '@/api/services/gradesReport'
import { cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'
const quarters = [1, 2, 3, 4]
const esc = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')

/** Bir o'quvchi bo'yicha "O'zlashtirish va qatnashish" hisoboti (reportStudentsProgress.xls ko'rinishi). */
export function StudentGradesReport() {
  const [classes, setClasses] = useState<SchoolClass[]>([])
  const [students, setStudents] = useState<Student[]>([])
  const [loading, setLoading] = useState(true)
  const [classId, setClassId] = useState('')
  const [studentId, setStudentId] = useState('')
  const [report, setReport] = useState<StudentReport | null>(null)
  const [building, setBuilding] = useState(false)

  useEffect(() => {
    Promise.all([getClasses(), getStudents()])
      .then(([cls, sts]) => {
        setClasses(cls)
        setStudents(sts)
      })
      .finally(() => setLoading(false))
  }, [])

  const className = classes.find((c) => c.id === classId)?.name ?? ''
  const classStudents = useMemo(
    () => students.filter((s) => s.className === className).sort((a, b) => a.fullName.localeCompare(b.fullName)),
    [students, className],
  )

  const onClassChange = (id: string) => {
    setClassId(id)
    setStudentId('')
    setReport(null)
  }

  const build = () => {
    if (!studentId) return
    setBuilding(true)
    getStudentProgressReport(studentId)
      .then(setReport)
      .finally(() => setBuilding(false))
  }

  const exportXls = () => {
    if (!report) return
    const html = buildXls(report)
    const blob = new Blob(['﻿' + html], { type: 'application/vnd.ms-excel;charset=utf-8;' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `${report.fullName}-ozlashtirish.xls`
    a.click()
    URL.revokeObjectURL(url)
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Baholar hisoboti — O'quvchi bo'yicha</h1>
        <p className="text-sm text-slate-400">
          Sinf va o'quvchini tanlab hisobot quring, so'ng Excelga yuklang
        </p>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : classes.length === 0 ? (
        <Card>
          <p className="py-8 text-center text-sm text-slate-400">Sinflar yo'q</p>
        </Card>
      ) : (
        <>
          <Card className="flex flex-wrap items-end gap-4 p-4">
            <label className="block">
              <span className="mb-1 block text-sm font-medium text-slate-600">Sinf</span>
              <select value={classId} onChange={(e) => onClassChange(e.target.value)} className={cn(control, 'min-w-[140px]')}>
                <option value="">Tanlang</option>
                {classes.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </select>
            </label>

            <label className="block">
              <span className="mb-1 block text-sm font-medium text-slate-600">O'quvchi</span>
              <select
                value={studentId}
                onChange={(e) => {
                  setStudentId(e.target.value)
                  setReport(null)
                }}
                disabled={!classId}
                className={cn(control, 'min-w-[240px] disabled:bg-slate-50')}
              >
                <option value="">{classId ? 'Tanlang' : 'Avval sinfni tanlang'}</option>
                {classStudents.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.fullName}
                  </option>
                ))}
              </select>
            </label>

            <div className="flex items-center gap-2">
              <Button onClick={build} disabled={!studentId || building}>
                <FileBarChart className="h-4 w-4" />
                {building ? 'Hisoblanmoqda...' : 'Hisobot qurish'}
              </Button>
              {report && (
                <Button variant="secondary" onClick={exportXls}>
                  <Download className="h-4 w-4" /> Excelga yuklab olish
                </Button>
              )}
            </div>
          </Card>

          {building ? (
            <Loader label="Hisobot tayyorlanmoqda..." />
          ) : report ? (
            <Card className="overflow-x-auto">
              <StudentReportView report={report} />
            </Card>
          ) : classStudents.length === 0 && classId ? (
            <Card>
              <p className="py-8 text-center text-sm text-slate-400">Bu sinfda o'quvchi yo'q</p>
            </Card>
          ) : null}
        </>
      )}
    </div>
  )
}

/* ---------- Hisoblash yordamchilari ---------- */

const markCell = (report: StudentReport, subjId: string, q: number): string => {
  const g = report.grades[subjId]?.[String(q)]
  if (g != null) return String(Math.round(g))
  const active = report.subjects.some((s) => report.grades[s.id]?.[String(q)] != null)
  return active ? 'OZ' : ''
}
const yearMark = (report: StudentReport, subjId: string): string => {
  const ms = quarters
    .map((q) => report.grades[subjId]?.[String(q)])
    .filter((x): x is number => x != null)
  return ms.length ? String(Math.round(ms.reduce((a, b) => a + b, 0) / ms.length)) : ''
}
/* ---------- Ekran ko'rinishi ---------- */

const th = 'border border-slate-300 bg-slate-100 px-2 py-1.5 text-center text-xs font-semibold text-slate-600'
const td = 'border border-slate-200 px-2 py-1.5 text-center text-slate-700'

function StudentReportView({ report }: { report: StudentReport }) {
  return (
    <div className="space-y-4 text-sm">
      <div>
        <h2 className="text-lg font-semibold text-slate-800">O'zlashtirish va qatnashish</h2>
        <p className="text-slate-500">
          {report.fullName}, {report.className}
        </p>
      </div>

      <table className="border-collapse">
        <thead>
          <tr>
            <th className={th}>№</th>
            <th className={cn(th, 'text-left')}>Fanlar</th>
            <th className={th}>1-chorak</th>
            <th className={th}>2-chorak</th>
            <th className={th}>3-chorak</th>
            <th className={th}>4-chorak</th>
            <th className={th}>Yil</th>
            <th className={th}>Imtihon</th>
            <th className={th}>Yakuniy baho</th>
          </tr>
        </thead>
        <tbody>
          {report.subjects.map((s, i) => (
            <tr key={s.id} className="hover:bg-slate-50/60">
              <td className={td}>{i + 1}</td>
              <td className={cn(td, 'text-left')}>{s.name}</td>
              {quarters.map((q) => (
                <td key={q} className={td}>{markCell(report, s.id, q)}</td>
              ))}
              <td className={cn(td, 'font-medium')}>{yearMark(report, s.id)}</td>
              <td className={td} />
              <td className={td} />
            </tr>
          ))}
        </tbody>
      </table>

      <div className="space-y-2 pt-2 text-sm text-slate-600">
        <p>Sinf rahbari _______________________________ {report.homeroomTeacher}</p>
        <p>O'quv ishlari bo'yicha direktor o'rinbosari _______________________________</p>
        <p>Ota-ona _______________________________ {report.parentFullName}</p>
      </div>
    </div>
  )
}

/* ---------- Excel (.xls) ---------- */

function buildXls(report: StudentReport): string {
  const subjRows = report.subjects
    .map(
      (s, i) =>
        `<tr><td>${i + 1}</td><td class="l">${esc(s.name)}</td>${quarters
          .map((q) => `<td>${markCell(report, s.id, q)}</td>`)
          .join('')}<td>${yearMark(report, s.id)}</td><td></td><td></td></tr>`,
    )
    .join('')

  return `<html xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:x="urn:schemas-microsoft-com:office:excel" xmlns="http://www.w3.org/TR/REC-html40"><head><meta charset="utf-8"/>
<style>
  table{border-collapse:collapse;font-family:Calibri,Arial,sans-serif;font-size:11px;}
  th,td{border:0.5pt solid #999;padding:3px 6px;text-align:center;vertical-align:middle;}
  th{background:#dbe5f1;font-weight:bold;}
  td.l,th.l{text-align:left;}
  tr.b td{background:#f2f2f2;font-weight:bold;}
  h3,p{font-family:Calibri,Arial,sans-serif;margin:2px 0;}
</style></head><body>
<h3>O'zlashtirish va qatnashish</h3>
<p>${esc(report.fullName)}, ${esc(report.className)}</p><br/>
<table>
<thead><tr><th>№</th><th class="l">Fanlar</th><th>1-chorak</th><th>2-chorak</th><th>3-chorak</th><th>4-chorak</th><th>Yil</th><th>Imtihon</th><th>Yakuniy baho</th></tr></thead>
<tbody>${subjRows}</tbody>
</table>
<br/><br/>
<p>Sinf rahbari _______________________________ ${esc(report.homeroomTeacher)}</p>
<p>O'quv ishlari bo'yicha direktor o'rinbosari _______________________________</p>
<p>Ota-ona _______________________________ ${esc(report.parentFullName)}</p>
</body></html>`
}
