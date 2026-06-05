import { useCallback, useEffect, useMemo, useState } from 'react'
import { CalendarRange, ClipboardList } from 'lucide-react'
import type { EvaluationBoard, EvaluationRow, TeacherClass } from '@/types'
import { getMyClasses, getTeacherEvalBoard, setTeacherEvalGrade } from '@/api/services/teacher'
import { cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

const uzMonths = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
]
function monthLabel(m: string): string {
  if (!m || m.length < 7) return m
  return `${uzMonths[Number(m.slice(5, 7)) - 1] ?? m} ${m.slice(0, 4)}`
}

const empty: EvaluationBoard = { months: [], month: '', week: 0, types: [], rows: [], subjectId: '', subjects: [] }

export function TeacherEvaluationPage() {
  const [classes, setClasses] = useState<TeacherClass[]>([])
  const [classId, setClassId] = useState('')
  const [subjectId, setSubjectId] = useState('')
  const [month, setMonth] = useState('')
  const [board, setBoard] = useState<EvaluationBoard>(empty)
  const [loading, setLoading] = useState(true)
  const [dataLoading, setDataLoading] = useState(false)

  useEffect(() => {
    getMyClasses()
      .then((cl) => {
        setClasses(cl)
        setClassId(cl[0]?.classId ?? '')
      })
      .finally(() => setLoading(false))
  }, [])

  const selectedClass = classes.find((c) => c.classId === classId) ?? null
  const subjects = useMemo(() => selectedClass?.subjects ?? [], [selectedClass])

  // Sinf o'zgarganda — fanни shu sinf fanlariga moslaymiz.
  useEffect(() => {
    const ids = subjects.map((s) => s.id)
    // eslint-disable-next-line react-hooks/set-state-in-effect -- sinf fanlariga moslash (maqsadli)
    setSubjectId((prev) => (ids.includes(prev) ? prev : (ids[0] ?? '')))
  }, [subjects])

  const load = useCallback((m: string | undefined, cId: string, sId: string) => {
    if (!cId || !sId) {
      setBoard(empty)
      return
    }
    setDataLoading(true)
    getTeacherEvalBoard(cId, sId, m)
      .then((b) => {
        setBoard(b)
        setMonth(b.month)
      })
      .finally(() => setDataLoading(false))
  }, [])

  // Sinf yoki fan o'zgarsa — eng so'nggi oy bilan yuklaymiz.
  useEffect(() => {
    load(undefined, classId, subjectId)
  }, [classId, subjectId, load])

  const onMonth = (m: string) => {
    setMonth(m)
    load(m, classId, subjectId)
  }

  const onGrade = (studentId: string, typeId: string, score: number | null) => {
    setBoard((prev) => ({
      ...prev,
      rows: prev.rows.map((r) => {
        if (r.studentId !== studentId) return r
        const grades = { ...r.grades }
        if (score == null) delete grades[typeId]
        else grades[typeId] = score
        const vals = Object.values(grades)
        const avg = vals.length ? Math.round((vals.reduce((a, b) => a + b, 0) / vals.length) * 10) / 10 : 0
        return { ...r, grades, avgGrade: avg }
      }),
    }))
    setTeacherEvalGrade(classId, subjectId, studentId, typeId, month, score).catch(() => {
      alert('Bahoni saqlashda xatolik — qayta yuklanmoqda')
      load(month, classId, subjectId)
    })
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Feedback</h1>
        <p className="text-sm text-slate-400">
          O'z faningiz bo'yicha o'quvchilarga feedback nomi kesimida feedback bering (1-5, oylik)
        </p>
      </div>

      {/* Sinf + fan + oy tanlovi */}
      <div className="flex flex-wrap items-center gap-2 rounded-xl border border-slate-200 bg-slate-50/60 p-3">
        <span className="text-sm font-medium text-slate-600">Sinf:</span>
        <select className={control} value={classId} onChange={(e) => setClassId(e.target.value)}>
          {classes.length === 0 && <option value="">— dars beradigan sinf yo'q —</option>}
          {classes.map((c) => (
            <option key={c.classId} value={c.classId}>
              {c.className}
            </option>
          ))}
        </select>

        <span className="ml-2 text-sm font-medium text-slate-600">Fan:</span>
        <select className={control} value={subjectId} onChange={(e) => setSubjectId(e.target.value)}>
          {subjects.length === 0 && <option value="">— fan yo'q —</option>}
          {subjects.map((s) => (
            <option key={s.id} value={s.id}>
              {s.name}
            </option>
          ))}
        </select>

        <CalendarRange className="ml-2 h-5 w-5 text-brand-600" />
        <span className="text-sm font-medium text-slate-600">Oy:</span>
        <select className={control} value={month} onChange={(e) => onMonth(e.target.value)}>
          {(board.months.length ? board.months : month ? [month] : []).map((m) => (
            <option key={m} value={m}>
              {monthLabel(m)}
            </option>
          ))}
        </select>
      </div>

      {dataLoading ? (
        <Loader label="Yuklanmoqda..." />
      ) : board.types.length === 0 ? (
        <Card className="flex flex-col items-center justify-center gap-3 py-16 text-center">
          <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-slate-100">
            <ClipboardList className="h-7 w-7 text-slate-400" />
          </div>
          <p className="text-sm font-medium text-slate-600">Hali feedback nomi yo'q</p>
          <p className="max-w-sm text-sm text-slate-400">
            Feedback nomlarini administrator qo'shadi — qo'shilgach shu yerda har bir nom bo'yicha 1-5 feedback qo'yasiz.
          </p>
        </Card>
      ) : !classId || !subjectId ? (
        <Card className="py-16 text-center text-slate-400">Sinf va fan tanlang</Card>
      ) : (
        <Card className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="w-10 px-3 py-3">#</th>
                  <th className="px-3 py-3">FISH</th>
                  {board.types.map((t) => (
                    <th key={t.id} className="px-3 py-3 text-center" title={t.description}>
                      {t.name}
                    </th>
                  ))}
                  <th className="px-3 py-3 text-center">O'rtacha</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {board.rows.map((row, i) => (
                  <EvalRow
                    key={row.studentId}
                    row={row}
                    index={i}
                    typeIds={board.types.map((t) => t.id)}
                    onGrade={onGrade}
                  />
                ))}
                {board.rows.length === 0 && (
                  <tr>
                    <td colSpan={3 + board.types.length} className="px-4 py-12 text-center text-slate-400">
                      Bu sinfda o'quvchi yo'q
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
  onGrade,
}: {
  row: EvaluationRow
  index: number
  typeIds: string[]
  onGrade: (studentId: string, typeId: string, score: number | null) => void
}) {
  return (
    <tr className="hover:bg-slate-50/60">
      <td className="px-3 py-2 text-slate-400">{index + 1}</td>
      <td className="whitespace-nowrap px-3 py-2 font-medium text-slate-800">{row.fullName}</td>
      {typeIds.map((typeId) => (
        <td key={typeId} className="px-3 py-2 text-center">
          <GradeSelect
            value={row.grades[typeId] ?? null}
            onChange={(score) => onGrade(row.studentId, typeId, score)}
          />
        </td>
      ))}
      <td className="px-3 py-2 text-center font-semibold text-slate-700">
        {row.avgGrade > 0 ? row.avgGrade : <span className="text-slate-300">—</span>}
      </td>
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
}: {
  value: number | null
  onChange: (score: number | null) => void
}) {
  return (
    <select
      value={value ?? ''}
      onChange={(e) => onChange(e.target.value ? Number(e.target.value) : null)}
      className={cn(
        'w-14 rounded-md border px-2 py-1 text-center text-sm font-semibold outline-none focus:border-brand-400',
        value ? gradeColor[value] : 'border-slate-200 bg-white text-slate-400',
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
