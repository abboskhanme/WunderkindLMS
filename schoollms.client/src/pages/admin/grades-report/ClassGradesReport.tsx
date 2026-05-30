import { useEffect, useState } from 'react'
import { Download, FileBarChart } from 'lucide-react'
import type { SchoolClass, Subject } from '@/types'
import { getClasses } from '@/api/services/classes'
import { getClassReport, type ClassReport, type ClassReportStudent } from '@/api/services/gradesReport'
import { cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'

type Period = 1 | 2 | 3 | 4 | 'all'
type Cat = 'excellent' | 'good' | 'satisfactory' | 'poor'

const fmt2 = (n: number) => n.toFixed(2).replace('.', ',')
const pctFmt = (n: number) => (Number.isInteger(n) ? String(n) : n.toFixed(2).replace('.', ','))
const otmOf = (avg: number) => Math.max(0, Math.min(100, ((avg - 2) / 3) * 100))
const round2 = (n: number) => Math.round(n * 100) / 100
const nameLines = (full: string) => full.trim().split(/\s+/).map((w) => w.toUpperCase())
const shortName = (full: string) => {
  const p = full.trim().split(/\s+/)
  return p.length > 1 ? `${p[0].toUpperCase()} ${p[1][0].toUpperCase()}.` : (p[0] ?? '').toUpperCase()
}
const markAt = (st: ClassReportStudent, subjId: string, q: number): number | null => {
  const a = st.averages[subjId]?.[String(q)]
  return a == null ? null : Math.round(a)
}
const classify = (marks: number[]): Cat | null => {
  if (marks.length === 0) return null
  if (marks.some((m) => m <= 2)) return 'poor'
  if (marks.some((m) => m === 3)) return 'satisfactory'
  if (marks.some((m) => m === 4)) return 'good'
  return 'excellent'
}

/* ---------- Group1: bitta chorak hisoboti ---------- */

interface G1Row {
  index: number
  name: string
  marks: (number | null)[]
  avg: number | null
  rank: number | null
}
interface G1Subj {
  name: string
  avg: number | null
  quality: number | null
  otm: number | null
}
interface G1Cat {
  count: number
  pct: number
  names: string[]
}
interface G1VM {
  className: string
  quarter: number
  total: number
  noGrades: number
  classAvg: number
  overallQuality: number
  overallOtm: number
  knowledgeQuality: number
  subjects: Subject[]
  rows: G1Row[]
  footer: G1Subj[]
  excellent: G1Cat
  goodTotal: G1Cat
  good4: G1Cat
  satTotal: G1Cat
  sat3: G1Cat
  poor: G1Cat
  homeroom: string
}

function computeG1(report: ClassReport, q: number): G1VM {
  const subjects = report.subjects
  const raw = report.students.map((st) => {
    const marks = subjects.map((s) => markAt(st, s.id, q))
    const nums = marks.filter((m): m is number => m != null)
    const avg = nums.length ? nums.reduce((a, b) => a + b, 0) / nums.length : null
    return { st, marks, nums, avg }
  })
  const graded = raw.filter((r) => r.avg != null)
  const distinct = [...new Set(graded.map((r) => round2(r.avg!)))].sort((a, b) => b - a)
  const rankOf = (avg: number) => distinct.indexOf(round2(avg)) + 1

  const rows: G1Row[] = raw.map((r, i) => ({
    index: i + 1,
    name: r.st.fullName,
    marks: r.marks,
    avg: r.avg,
    rank: r.avg != null ? rankOf(r.avg) : null,
  }))

  const footer: G1Subj[] = subjects.map((s, si) => {
    const ms = raw.map((r) => r.marks[si]).filter((m): m is number => m != null)
    if (!ms.length) return { name: s.name, avg: null, quality: null, otm: null }
    const avg = ms.reduce((a, b) => a + b, 0) / ms.length
    return { name: s.name, avg, quality: (ms.filter((m) => m >= 4).length / ms.length) * 100, otm: otmOf(avg) }
  })

  const cats = graded.map((r) => ({ name: r.st.fullName, nums: r.nums, cat: classify(r.nums)! }))
  const total = raw.length
  const cat = (pred: (c: { nums: number[]; cat: Cat; name: string }) => boolean): G1Cat => {
    const list = cats.filter(pred)
    return { count: list.length, pct: total ? (list.length / total) * 100 : 0, names: list.map((c) => shortName(c.name)) }
  }
  const excellent = cat((c) => c.cat === 'excellent')
  const goodTotal = cat((c) => c.cat === 'good')
  const good4 = cat((c) => c.cat === 'good' && c.nums.filter((m) => m === 4).length === 1)
  const satTotal = cat((c) => c.cat === 'satisfactory')
  const sat3 = cat((c) => c.cat === 'satisfactory' && c.nums.filter((m) => m === 3).length === 1)
  const poor = cat((c) => c.cat === 'poor')

  const withData = footer.filter((f) => f.avg != null)
  return {
    className: report.className,
    quarter: q,
    total,
    noGrades: total - graded.length,
    classAvg: graded.length ? graded.reduce((a, r) => a + r.avg!, 0) / graded.length : 0,
    overallQuality: withData.length ? withData.reduce((a, f) => a + f.quality!, 0) / withData.length : 0,
    overallOtm: withData.length ? withData.reduce((a, f) => a + f.otm!, 0) / withData.length : 0,
    knowledgeQuality: total ? ((excellent.count + goodTotal.count) / total) * 100 : 0,
    subjects,
    rows,
    footer,
    excellent,
    goodTotal,
    good4,
    satTotal,
    sat3,
    poor,
    homeroom: report.homeroomTeacher,
  }
}

/* ---------- Group2: butun davr hisoboti ---------- */

interface G2Student {
  index: number
  name: string
  rows: { label: string; marks: (number | null)[] }[]
}
interface G2VM {
  className: string
  subjects: Subject[]
  students: G2Student[]
  homeroom: string
}

function computeG2(report: ClassReport): G2VM {
  const quarters = [1, 2, 3, 4]
  const students = report.students.map((st, i) => {
    const rows = quarters.map((q) => ({
      label: `${q}-chorak`,
      marks: report.subjects.map((s) => markAt(st, s.id, q)),
    }))
    const yil = report.subjects.map((s) => {
      const ms = quarters.map((q) => markAt(st, s.id, q)).filter((m): m is number => m != null)
      return ms.length ? Math.round(ms.reduce((a, b) => a + b, 0) / ms.length) : null
    })
    rows.push({ label: 'Yil', marks: yil })
    return { index: i + 1, name: st.fullName, rows }
  })
  return { className: report.className, subjects: report.subjects, students, homeroom: report.homeroomTeacher }
}

/* ---------- Komponent ---------- */

export function ClassGradesReport() {
  const [classes, setClasses] = useState<SchoolClass[]>([])
  const [loadingClasses, setLoadingClasses] = useState(true)
  const [classId, setClassId] = useState('')
  const [period, setPeriod] = useState<Period>(1)
  const [report, setReport] = useState<ClassReport | null>(null)
  const [building, setBuilding] = useState(false)

  useEffect(() => {
    getClasses()
      .then((cls) => {
        setClasses(cls)
        setClassId(cls[0]?.id ?? '')
      })
      .finally(() => setLoadingClasses(false))
  }, [])

  const build = () => {
    if (!classId) return
    setBuilding(true)
    getClassReport(classId)
      .then(setReport)
      .finally(() => setBuilding(false))
  }

  // Sinf o'zgarsa — eski hisobotni tozalaymiz (qayta qurish kerak)
  const onClassChange = (id: string) => {
    setClassId(id)
    setReport(null)
  }

  const exportXls = () => {
    if (!report) return
    const html =
      period === 'all' ? buildG2Xls(computeG2(report)) : buildG1Xls(computeG1(report, period))
    const blob = new Blob(['﻿' + html], { type: 'application/vnd.ms-excel;charset=utf-8;' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = period === 'all' ? `${report.className}-butun-davr.xls` : `${report.className}-${period}chorak.xls`
    a.click()
    URL.revokeObjectURL(url)
  }

  const periods: { value: Period; label: string }[] = [
    { value: 1, label: '1-chorak' },
    { value: 2, label: '2-chorak' },
    { value: 3, label: '3-chorak' },
    { value: 4, label: '4-chorak' },
    { value: 'all', label: 'Barcha davr uchun' },
  ]

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Baholar hisoboti — Sinf bo'yicha</h1>
        <p className="text-sm text-slate-400">Sinf va davrni tanlab hisobot quring, so'ng Excelga yuklang</p>
      </div>

      {loadingClasses ? (
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
              <select
                value={classId}
                onChange={(e) => onClassChange(e.target.value)}
                className="min-w-[160px] rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
              >
                {classes.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </select>
            </label>

            <div>
              <span className="mb-1 block text-sm font-medium text-slate-600">Davr</span>
              <div className="flex flex-wrap gap-1 rounded-lg bg-slate-100 p-1">
                {periods.map((p) => (
                  <button
                    key={String(p.value)}
                    type="button"
                    onClick={() => setPeriod(p.value)}
                    className={cn(
                      'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                      period === p.value ? 'bg-white text-brand-700 shadow-sm' : 'text-slate-500 hover:text-slate-700',
                    )}
                  >
                    {p.label}
                  </button>
                ))}
              </div>
            </div>

            <div className="flex items-center gap-2">
              <Button onClick={build} disabled={!classId || building}>
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
            report.students.length === 0 ? (
              <Card>
                <p className="py-8 text-center text-sm text-slate-400">Bu sinfda o'quvchi yo'q</p>
              </Card>
            ) : (
              <Card className="overflow-x-auto">
                {period === 'all' ? (
                  <Group2View vm={computeG2(report)} />
                ) : (
                  <Group1View vm={computeG1(report, period)} />
                )}
              </Card>
            )
          ) : null}
        </>
      )}
    </div>
  )
}

/* ---------- Ekran ko'rinishlari ---------- */

const th = 'border border-slate-300 bg-slate-100 px-1.5 py-1 text-center text-xs font-semibold text-slate-600'
const td = 'border border-slate-200 px-1.5 py-1 text-center text-slate-700'
const vHead =
  'border border-slate-300 bg-slate-100 px-1 py-1 align-bottom text-center text-[11px] font-semibold text-slate-600'

function VerticalText({ text }: { text: string }) {
  return (
    <div className="mx-auto h-28 rotate-180 whitespace-nowrap [writing-mode:vertical-rl]">{text}</div>
  )
}

function NameCell({ name }: { name: string }) {
  return (
    <div className="leading-tight">
      {nameLines(name).map((w, i) => (
        <div key={i}>{w}</div>
      ))}
    </div>
  )
}

function Group1View({ vm }: { vm: G1VM }) {
  const cell = (m: number | null) => (m == null ? 'OZ' : String(m))
  return (
    <div className="space-y-4 text-sm">
      <div>
        <h2 className="text-lg font-semibold text-slate-800">Sinf o'zlashtirishi</h2>
        <p className="text-slate-500">
          {vm.className} — {vm.quarter}-chorak
        </p>
        <div className="mt-1 space-y-0.5 text-sm text-slate-600">
          <p>Sinfning umumiy o'rtacha balli: <b>{fmt2(vm.classAvg)}</b></p>
          <p>Fanlar bo'yicha bilimlar sifatining umumiy foizi: <b>{pctFmt(round2(vm.overallQuality))}</b></p>
          <p>Fanlar bo'yicha O'TM (%): <b>{pctFmt(round2(vm.overallOtm))}</b></p>
          <p>Sinfning bilimlar sifati foizi: <b>{pctFmt(round2(vm.knowledgeQuality))}</b></p>
          <p>Davr oxiriga: <b>{vm.total}</b> · Baholar mavjud emas: <b>{vm.noGrades}</b></p>
        </div>
      </div>

      <table className="border-collapse">
        <thead>
          <tr>
            <th className={th}>№</th>
            <th className={cn(th, 'text-left')}>Familiyasi, Ismi</th>
            {vm.subjects.map((s) => (
              <th key={s.id} className={vHead}>
                <VerticalText text={s.name} />
              </th>
            ))}
            <th className={vHead}><VerticalText text="O'rtacha ball" /></th>
            <th className={vHead}><VerticalText text="Reyting" /></th>
          </tr>
        </thead>
        <tbody>
          {vm.rows.map((r) => (
            <tr key={r.index} className="hover:bg-slate-50/60">
              <td className={td}>{r.index}</td>
              <td className={cn(td, 'text-left')}><NameCell name={r.name} /></td>
              {r.marks.map((m, i) => (
                <td key={i} className={td}>{cell(m)}</td>
              ))}
              <td className={cn(td, 'font-medium')}>{r.avg == null ? '—' : fmt2(r.avg)}</td>
              <td className={td}>{r.rank ?? '—'}</td>
            </tr>
          ))}
        </tbody>
        <tfoot className="font-medium">
          <tr className="bg-slate-50">
            <td className={cn(td, 'text-left')} colSpan={2}>Fan bo'yicha o'rtacha ball</td>
            {vm.footer.map((f, i) => (
              <td key={i} className={td}>{f.avg == null ? '-' : fmt2(f.avg)}</td>
            ))}
            <td className={td} colSpan={2} />
          </tr>
          <tr className="bg-slate-50">
            <td className={cn(td, 'text-left')} colSpan={2}>Bilimlar sifati foizi</td>
            {vm.footer.map((f, i) => (
              <td key={i} className={td}>{f.quality == null ? '-' : pctFmt(round2(f.quality))}</td>
            ))}
            <td className={td} colSpan={2} />
          </tr>
          <tr className="bg-slate-50">
            <td className={cn(td, 'text-left')} colSpan={2}>O'TM (%)</td>
            {vm.footer.map((f, i) => (
              <td key={i} className={td}>{f.otm == null ? '-' : pctFmt(round2(f.otm))}</td>
            ))}
            <td className={td} colSpan={2} />
          </tr>
        </tfoot>
      </table>

      <ClassifTable vm={vm} />
      <Signatures homeroom={vm.homeroom} />
    </div>
  )
}

function ClassifRow({ label, c, indent }: { label: string; c: G1Cat; indent?: boolean }) {
  return (
    <tr>
      <td className={cn(td, 'text-left', indent && 'pl-6 text-slate-500')}>{label}</td>
      <td className={td}>{c.count}</td>
      <td className={td}>{pctFmt(round2(c.pct))}%</td>
      <td className={cn(td, 'text-left text-xs')}>{c.names.join(', ')}</td>
    </tr>
  )
}

function ClassifTable({ vm }: { vm: G1VM }) {
  return (
    <table className="border-collapse">
      <thead>
        <tr>
          <th className={cn(th, 'text-left')}>O'zlashtirish</th>
          <th className={th}>Soni</th>
          <th className={th}>Sinfda %</th>
          <th className={cn(th, 'text-left')}>FISH</th>
        </tr>
      </thead>
      <tbody>
        <ClassifRow label="A'lochilar" c={vm.excellent} />
        <ClassifRow label="Yaxshi o'zlashtiruvchilar" c={vm.goodTotal} />
        <ClassifRow label="Bitta «4» li" c={vm.good4} indent />
        <ClassifRow label="Muvaffaqiyatlar" c={vm.satTotal} />
        <ClassifRow label="Bitta «3» li" c={vm.sat3} indent />
        <ClassifRow label="Yaxshi o'zlashtirmaydigan" c={vm.poor} />
      </tbody>
    </table>
  )
}

function Group2View({ vm }: { vm: G2VM }) {
  return (
    <div className="space-y-4 text-sm">
      <div>
        <h2 className="text-lg font-semibold text-slate-800">Sinf o'zlashtirishi — butun davr</h2>
        <p className="text-slate-500">{vm.className}</p>
      </div>
      <table className="border-collapse">
        <thead>
          <tr>
            <th className={th}>№</th>
            <th className={cn(th, 'text-left')}>FISH</th>
            <th className={th}>Davr</th>
            {vm.subjects.map((s) => (
              <th key={s.id} className={vHead}>
                <VerticalText text={s.name} />
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {vm.students.flatMap((st) =>
            st.rows.map((row, ri) => (
              <tr key={`${st.index}-${ri}`} className={row.label === 'Yil' ? 'bg-slate-50 font-medium' : ''}>
                {ri === 0 && (
                  <>
                    <td className={td} rowSpan={st.rows.length}>{st.index}</td>
                    <td className={cn(td, 'text-left')} rowSpan={st.rows.length}>
                      <NameCell name={st.name} />
                    </td>
                  </>
                )}
                <td className={cn(td, 'whitespace-nowrap text-left')}>{row.label}</td>
                {row.marks.map((m, i) => (
                  <td key={i} className={td}>{m == null ? '—' : m}</td>
                ))}
              </tr>
            )),
          )}
        </tbody>
      </table>
      <Signatures homeroom={vm.homeroom} />
    </div>
  )
}

function Signatures({ homeroom }: { homeroom: string }) {
  return (
    <div className="space-y-2 pt-2 text-sm text-slate-600">
      <p>Sinf rahbari _______________________________ {homeroom}</p>
      <p>O'quv ishlari bo'yicha direktor o'rinbosari _______________________________</p>
    </div>
  )
}

/* ---------- Excel (.xls) ---------- */

const esc = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
const xlsName = (full: string) => nameLines(full).map(esc).join('<br/>')

const xlsStyle = `<style>
  table{border-collapse:collapse;font-family:Calibri,Arial,sans-serif;font-size:11px;}
  th,td{border:0.5pt solid #999;padding:3px 6px;text-align:center;vertical-align:middle;}
  th{background:#dbe5f1;font-weight:bold;}
  td.l,th.l{text-align:left;}
  tr.b td{background:#f2f2f2;font-weight:bold;}
  h3,p{font-family:Calibri,Arial,sans-serif;margin:2px 0;}
</style>`

function xlsDoc(inner: string): string {
  return `<html xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:x="urn:schemas-microsoft-com:office:excel" xmlns="http://www.w3.org/TR/REC-html40"><head><meta charset="utf-8"/>${xlsStyle}</head><body>${inner}</body></html>`
}

function buildG1Xls(vm: G1VM): string {
  const subjTh = vm.subjects.map((s) => `<th>${esc(s.name)}</th>`).join('')
  const body = vm.rows
    .map(
      (r) =>
        `<tr><td>${r.index}</td><td class="l">${xlsName(r.name)}</td>${r.marks
          .map((m) => `<td>${m == null ? 'OZ' : m}</td>`)
          .join('')}<td>${r.avg == null ? '—' : fmt2(r.avg)}</td><td>${r.rank ?? '—'}</td></tr>`,
    )
    .join('')
  const foot = (label: string, vals: string[]) =>
    `<tr class="b"><td class="l" colspan="2">${label}</td>${vals.map((v) => `<td>${v}</td>`).join('')}<td></td><td></td></tr>`
  const footAvg = foot('Fan bo‘yicha o‘rtacha ball', vm.footer.map((f) => (f.avg == null ? '-' : fmt2(f.avg))))
  const footQ = foot('Bilimlar sifati foizi', vm.footer.map((f) => (f.quality == null ? '-' : pctFmt(round2(f.quality)))))
  const footO = foot('O‘TM (%)', vm.footer.map((f) => (f.otm == null ? '-' : pctFmt(round2(f.otm)))))

  const cl = (label: string, c: G1Cat, indent = false) =>
    `<tr><td class="l">${indent ? '&nbsp;&nbsp;&nbsp;&nbsp;' : ''}${esc(label)}</td><td>${c.count}</td><td>${pctFmt(round2(c.pct))}%</td><td class="l">${c.names.map(esc).join('<br/>')}</td></tr>`

  return xlsDoc(`
<h3>Sinf o'zlashtirishi</h3>
<p>${esc(vm.className)} — ${vm.quarter}-chorak</p>
<p>Sinfning umumiy o'rtacha balli: ${fmt2(vm.classAvg)} &nbsp; Bilim sifati foizi: ${pctFmt(round2(vm.knowledgeQuality))}</p>
<p>Fanlar bo'yicha bilim sifati: ${pctFmt(round2(vm.overallQuality))} &nbsp; O'TM (%): ${pctFmt(round2(vm.overallOtm))}</p>
<p>Davr oxiriga: ${vm.total} &nbsp; Baholar mavjud emas: ${vm.noGrades}</p><br/>
<table>
<thead><tr><th>№</th><th class="l">Familiyasi, Ismi</th>${subjTh}<th>O'rtacha ball</th><th>Reyting</th></tr></thead>
<tbody>${body}</tbody>
<tfoot>${footAvg}${footQ}${footO}</tfoot>
</table>
<br/>
<table>
<thead><tr><th class="l">O'zlashtirish</th><th>Soni</th><th>Sinfda %</th><th class="l">FISH</th></tr></thead>
<tbody>
${cl("A'lochilar", vm.excellent)}
${cl("Yaxshi o'zlashtiruvchilar", vm.goodTotal)}
${cl('Bitta «4» li', vm.good4, true)}
${cl('Muvaffaqiyatlar', vm.satTotal)}
${cl('Bitta «3» li', vm.sat3, true)}
${cl("Yaxshi o'zlashtirmaydigan", vm.poor)}
</tbody>
</table>
<br/><br/>
<p>Sinf rahbari _______________________________ ${esc(vm.homeroom)}</p>
<p>O'quv ishlari bo'yicha direktor o'rinbosari _______________________________</p>`)
}

function buildG2Xls(vm: G2VM): string {
  const subjTh = vm.subjects.map((s) => `<th>${esc(s.name)}</th>`).join('')
  const body = vm.students
    .map((st) =>
      st.rows
        .map((row, ri) => {
          const head =
            ri === 0
              ? `<td rowspan="${st.rows.length}">${st.index}</td><td class="l" rowspan="${st.rows.length}">${xlsName(st.name)}</td>`
              : ''
          const cls = row.label === 'Yil' ? ' class="b"' : ''
          return `<tr${cls}>${head}<td class="l">${esc(row.label)}</td>${row.marks
            .map((m) => `<td>${m == null ? '—' : m}</td>`)
            .join('')}</tr>`
        })
        .join(''),
    )
    .join('')

  return xlsDoc(`
<h3>Sinf o'zlashtirishi — butun davr</h3>
<p>${esc(vm.className)}</p><br/>
<table>
<thead><tr><th>№</th><th class="l">FISH</th><th>Davr</th>${subjTh}</tr></thead>
<tbody>${body}</tbody>
</table>
<br/><br/>
<p>Sinf rahbari _______________________________ ${esc(vm.homeroom)}</p>
<p>O'quv ishlari bo'yicha direktor o'rinbosari _______________________________</p>`)
}
