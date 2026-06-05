import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { Search, ClipboardList, CalendarRange } from 'lucide-react'
import type { EvaluationBoard, EvaluationRow } from '@/types'
import { getEvaluationBoard, setEvaluationGrade } from '@/api/services/studentEvaluation'
import { cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

type Sort = 'class' | 'name' | 'att-desc' | 'att-asc' | 'grade-desc' | 'grade-asc'

const emptyBoard: EvaluationBoard = { months: [], month: '', week: 0, types: [], rows: [], subjectId: 'all', subjects: [] }

const uzMonths = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
]

function monthLabel(m: string): string {
  if (!m || m.length < 7) return m
  const y = m.slice(0, 4)
  const idx = Number(m.slice(5, 7)) - 1
  return `${uzMonths[idx] ?? m} ${y}`
}

function recalcAvg(grades: Record<string, number>): number {
  const vals = Object.values(grades)
  if (vals.length === 0) return 0
  return Math.round((vals.reduce((a, b) => a + b, 0) / vals.length) * 10) / 10
}

export function StudentEvaluationPage() {
  const [board, setBoard] = useState<EvaluationBoard>(emptyBoard)
  const [loading, setLoading] = useState(true)
  const [month, setMonth] = useState('')
  const [search, setSearch] = useState('')
  const [classFilter, setClassFilter] = useState('all')
  const [sort, setSort] = useState<Sort>('class')
  /** Tanlangan fan id — baho doim shu fan bo'yicha qo'yiladi (umumiy yo'q). */
  const [subjectId, setSubjectId] = useState('')

  const load = useCallback((m?: string, subj?: string) => {
    setLoading(true)
    getEvaluationBoard(m || undefined, 0, subj)
      .then((b) => {
        setBoard(b)
        setMonth(b.month)
      })
      .finally(() => setLoading(false))
  }, [])

  // Dastlab fanlar ro'yxatini olish uchun yuklaymiz (subjectsiz).
  useEffect(() => {
    load(undefined, undefined)
  }, [load])

  // Fanlar kelgach birinchi fanni avtomatik tanlaymiz (umumiy ko'rinish yo'q).
  useEffect(() => {
    if (!subjectId && board.subjects && board.subjects.length > 0) {
      const first = board.subjects[0].id
      setSubjectId(first)
      load(board.month || undefined, first)
    }
  }, [board.subjects, board.month, subjectId, load])

  const editable = subjectId !== '' && subjectId !== 'all'

  const onMonth = (m: string) => {
    setMonth(m)
    load(m, subjectId)
  }

  const onSubject = (s: string) => {
    setSubjectId(s)
    load(month, s)
  }

  const classNames = useMemo(
    () =>
      [...new Set(board.rows.map((r) => r.className).filter(Boolean))].sort((a, b) =>
        a.localeCompare(b),
      ),
    [board.rows],
  )

  const rows = useMemo(() => {
    let r = board.rows
    if (classFilter !== 'all') r = r.filter((x) => x.className === classFilter)
    const q = search.trim().toLowerCase()
    if (q) r = r.filter((x) => x.fullName.toLowerCase().includes(q))
    const arr = [...r]
    switch (sort) {
      case 'name':
        arr.sort((a, b) => a.fullName.localeCompare(b.fullName))
        break
      case 'att-desc':
        arr.sort((a, b) => b.attended - a.attended)
        break
      case 'att-asc':
        arr.sort((a, b) => a.attended - b.attended)
        break
      case 'grade-desc':
        arr.sort((a, b) => b.avgGrade - a.avgGrade)
        break
      case 'grade-asc':
        arr.sort((a, b) => a.avgGrade - b.avgGrade)
        break
    }
    return arr
  }, [board.rows, classFilter, search, sort])

  const onGrade = (studentId: string, typeId: string, score: number | null) => {
    if (!editable) return // "Hammasi (o'rtacha)" — faqat ko'rish
    setBoard((prev) => ({
      ...prev,
      rows: prev.rows.map((r) => {
        if (r.studentId !== studentId) return r
        const grades = { ...r.grades }
        if (score == null) delete grades[typeId]
        else grades[typeId] = score
        return { ...r, grades, avgGrade: recalcAvg(grades) }
      }),
    }))
    setEvaluationGrade(studentId, typeId, month, 0, score, subjectId).catch(() => {
      alert('Bahoni saqlashda xatolik — qayta yuklanmoqda')
      load(month, subjectId)
    })
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">O'quvchilarga feedback</h1>
        <p className="text-sm text-slate-400">
          Har oy bir marta — fan kesimida feedback turlari (yozma, og'zaki/suhbat...) bo'yicha 1-5
        </p>
      </div>

      {/* Oy + fan tanlovi */}
      <div className="flex flex-wrap items-center gap-2 rounded-xl border border-slate-200 bg-slate-50/60 p-3">
        <CalendarRange className="h-5 w-5 text-brand-600" />
        <span className="text-sm font-medium text-slate-600">Oy:</span>
        <select className={control} value={month} onChange={(e) => onMonth(e.target.value)}>
          {(board.months.length ? board.months : month ? [month] : []).map((m) => (
            <option key={m} value={m}>
              {monthLabel(m)}
            </option>
          ))}
        </select>
        <span className="ml-2 text-sm font-medium text-slate-600">Fan:</span>
        <select className={control} value={subjectId} onChange={(e) => onSubject(e.target.value)}>
          {(board.subjects ?? []).length === 0 && <option value="">Fan yo'q</option>}
          {(board.subjects ?? []).map((s) => (
            <option key={s.id} value={s.id}>
              {s.name}
            </option>
          ))}
        </select>
        <span className="text-xs text-slate-400">
          Tanlangan fan bo'yicha feedback qo'yiladi — umumiy o'rtacha o'quvchi profilida ko'rinadi
        </span>
      </div>

      {/* Filtrlar */}
      <div className="flex flex-wrap items-center gap-2">
        <div className="relative">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input
            className={cn(control, 'w-64 pl-9')}
            placeholder="Ism, familiya bo'yicha qidirish..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>
        <select
          className={control}
          value={classFilter}
          onChange={(e) => setClassFilter(e.target.value)}
        >
          <option value="all">Barcha sinflar</option>
          {classNames.map((c) => (
            <option key={c} value={c}>
              {c}
            </option>
          ))}
        </select>
        <select className={control} value={sort} onChange={(e) => setSort(e.target.value as Sort)}>
          <option value="class">Saralash: sinf bo'yicha</option>
          <option value="name">Ism (A-Z)</option>
          <option value="att-desc">Davomat: qatnashgan ko'pdan</option>
          <option value="att-asc">Davomat: qatnashgan kamdan</option>
          <option value="grade-desc">Baho: yuqoridan</option>
          <option value="grade-asc">Baho: pastdan</option>
        </select>
        <span className="ml-auto text-sm text-slate-400">{rows.length} o'quvchi</span>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : board.types.length === 0 ? (
        <Card className="flex flex-col items-center justify-center gap-3 py-16 text-center">
          <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-slate-100">
            <ClipboardList className="h-7 w-7 text-slate-400" />
          </div>
          <p className="text-sm font-medium text-slate-600">Hali feedback nomi yo'q</p>
          <p className="max-w-sm text-sm text-slate-400">
            Avval{' '}
            <Link
              to="/admin/students/baholash-turlari"
              className="font-medium text-brand-600 hover:underline"
            >
              Feedback nomi
            </Link>{' '}
            bo'limida nom qo'shing — keyin shu yerda har bir nom bo'yicha 1-5 feedback qo'yasiz.
          </p>
        </Card>
      ) : (
        <Card className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="w-10 px-3 py-3">#</th>
                  <th className="px-3 py-3">FISH</th>
                  <th className="px-3 py-3">Sinf</th>
                  <th className="px-3 py-3 text-center">Qatnashgan</th>
                  <th className="px-3 py-3">Davomat sabablari</th>
                  {board.types.map((t) => (
                    <th key={t.id} className="px-3 py-3 text-center" title={t.description}>
                      {t.name}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((row, i) => (
                  <EvalRow
                    key={row.studentId}
                    row={row}
                    index={i}
                    typeIds={board.types.map((t) => t.id)}
                    editable={editable}
                    onGrade={onGrade}
                  />
                ))}
                {rows.length === 0 && (
                  <tr>
                    <td
                      colSpan={5 + board.types.length}
                      className="px-4 py-12 text-center text-slate-400"
                    >
                      Hech narsa topilmadi
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </Card>
      )}
    </div>
  )
}

function EvalRow({
  row,
  index,
  typeIds,
  editable,
  onGrade,
}: {
  row: EvaluationRow
  index: number
  typeIds: string[]
  editable: boolean
  onGrade: (studentId: string, typeId: string, score: number | null) => void
}) {
  return (
    <tr className="hover:bg-slate-50/60">
      <td className="px-3 py-2 text-slate-400">{index + 1}</td>
      <td className="whitespace-nowrap px-3 py-2 font-medium text-slate-800">{row.fullName}</td>
      <td className="whitespace-nowrap px-3 py-2 text-slate-500">{row.className}</td>
      <td className="whitespace-nowrap px-3 py-2 text-center">
        {row.conducted > 0 ? (
          <span className="font-medium text-slate-700">
            {row.attended}
            <span className="text-slate-400"> / {row.conducted}</span>
          </span>
        ) : (
          <span className="text-slate-300">—</span>
        )}
      </td>
      <td className="px-3 py-2">
        {row.reasons.length === 0 ? (
          <span className="text-slate-300">—</span>
        ) : (
          <div className="flex flex-wrap gap-1">
            {row.reasons.map((r) => (
              <span
                key={r.reasonId}
                title={r.name}
                className={cn(
                  'inline-flex items-center gap-1 rounded-md px-1.5 py-0.5 text-xs font-medium',
                  r.isLate ? 'bg-amber-50 text-amber-700' : 'bg-red-50 text-red-600',
                )}
              >
                {r.short || r.name}
                <span className="font-semibold">×{r.count}</span>
              </span>
            ))}
          </div>
        )}
      </td>
      {typeIds.map((typeId) => (
        <td key={typeId} className="px-3 py-2 text-center">
          <GradeSelect
            value={row.grades[typeId] ?? null}
            disabled={!editable}
            onChange={(score) => onGrade(row.studentId, typeId, score)}
          />
        </td>
      ))}
    </tr>
  )
}

const gradeColor: Record<number, string> = {
  5: 'text-emerald-700 border-emerald-200 bg-emerald-50',
  4: 'text-brand-700 border-brand-200 bg-brand-50',
  3: 'text-amber-700 border-amber-200 bg-amber-50',
  2: 'text-red-600 border-red-200 bg-red-50',
  1: 'text-red-700 border-red-300 bg-red-50',
}

function GradeSelect({
  value,
  onChange,
  disabled,
}: {
  value: number | null
  onChange: (score: number | null) => void
  disabled?: boolean
}) {
  return (
    <select
      value={value ?? ''}
      disabled={disabled}
      onChange={(e) => onChange(e.target.value ? Number(e.target.value) : null)}
      className={cn(
        'w-14 rounded-md border px-2 py-1 text-center text-sm font-semibold outline-none focus:border-brand-400',
        value ? gradeColor[value] : 'border-slate-200 bg-white text-slate-400',
        disabled && 'cursor-default opacity-90',
      )}
    >
      <option value="">–</option>
      {[1, 2, 3, 4, 5].map((n) => (
        <option key={n} value={n}>
          {n}
        </option>
      ))}
    </select>
  )
}
