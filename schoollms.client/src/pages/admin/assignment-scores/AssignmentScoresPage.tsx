import { useEffect, useMemo, useState } from 'react'
import { Trophy, X, CalendarClock } from 'lucide-react'
import type {
  AssignmentScoreboard,
  AssignmentScoreColumn,
  AssignmentScoreRow,
  AssignmentFormat,
  SchoolClass,
} from '@/types'
import { getClasses } from '@/api/services/classes'
import { getAssignmentScoreboard } from '@/api/services/assignments'
import { formatDate } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 outline-none focus:border-brand-400'

const formatLabel: Record<AssignmentFormat, string> = {
  written: 'Yozma',
  file: 'Fayl',
  test: 'Test',
  video: 'Video',
}

/** Ball nisbatiga qarab rang (yashil = yaxshi, sariq = o'rta, qizil = past). */
function scoreColor(score: number, max: number): string {
  if (max <= 0) return 'text-slate-700'
  const r = score / max
  if (r >= 0.85) return 'text-emerald-600'
  if (r >= 0.5) return 'text-amber-600'
  return 'text-red-500'
}

/**
 * Admin "Topshiriqlar bali" — asosiy jadval qisqa (№ / F.I.Sh. / Sinf / Jami ball).
 * O'quvchi bosilganda uning barcha topshiriqlari vertikal ro'yxat (modal) bo'lib ochiladi —
 * shuning uchun topshiriq ko'paysa ham jadval enga kengaymaydi.
 */
export function AssignmentScoresPage() {
  const [classes, setClasses] = useState<SchoolClass[]>([])
  const [classId, setClassId] = useState('')
  const [board, setBoard] = useState<AssignmentScoreboard | null>(null)
  const [loading, setLoading] = useState(false)
  const [selected, setSelected] = useState<AssignmentScoreRow | null>(null)
  const [sort, setSort] = useState<'name' | 'high' | 'low'>('name')

  useEffect(() => {
    getClasses().then((cl) => {
      setClasses(cl)
      if (cl.length > 0) setClassId(cl[0].id)
    })
  }, [])

  useEffect(() => {
    if (!classId) return
    setLoading(true)
    setSelected(null)
    getAssignmentScoreboard(classId)
      .then(setBoard)
      .finally(() => setLoading(false))
  }, [classId])

  // Topshiriq id → ustun (modalda nom/fan/maks ball ko'rsatish uchun).
  const colById = useMemo(() => {
    const m = new Map<string, AssignmentScoreColumn>()
    for (const a of board?.assignments ?? []) m.set(a.assignmentId, a)
    return m
  }, [board])

  const hasAssignments = (board?.assignments.length ?? 0) > 0

  // Saralash: ism (backend tartibi), yuqori ball, past ball. Baholanmaganlar doim oxirida.
  const sortedStudents = useMemo(() => {
    const list = [...(board?.students ?? [])]
    if (sort === 'name') return list
    return list.sort((a, b) => {
      const ag = a.gradedCount > 0
      const bg = b.gradedCount > 0
      if (ag !== bg) return ag ? -1 : 1
      if (!ag) return 0
      return sort === 'high' ? b.totalScore - a.totalScore : a.totalScore - b.totalScore
    })
  }, [board, sort])

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <Trophy className="h-6 w-6 text-brand-600" />
          <div>
            <h1 className="text-xl font-semibold text-slate-800">Topshiriqlar bali</h1>
            <p className="text-sm text-slate-400">
              O'quvchini bosing — uning barcha topshiriq ballari ochiladi
            </p>
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <select className={control} value={classId} onChange={(e) => setClassId(e.target.value)}>
            {classes.length === 0 && <option value="">Sinf yo'q</option>}
            {classes.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
          <select
            className={control}
            value={sort}
            onChange={(e) => setSort(e.target.value as 'name' | 'high' | 'low')}
          >
            <option value="name">Ism bo'yicha (A–Z)</option>
            <option value="high">Yuqori ball bo'yicha</option>
            <option value="low">Past ball bo'yicha</option>
          </select>
        </div>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : !board || !hasAssignments ? (
        <Card>
          <p className="py-12 text-center text-slate-400">Bu sinfga topshiriq berilmagan</p>
        </Card>
      ) : board.students.length === 0 ? (
        <Card>
          <p className="py-12 text-center text-slate-400">Sinfda o'quvchi yo'q</p>
        </Card>
      ) : (
        <Card className="overflow-hidden p-0">
          <table className="w-full border-collapse text-sm">
            <thead>
              <tr className="border-b border-slate-200 bg-slate-50 text-slate-700">
                <th className="w-12 px-4 py-3 text-left font-semibold">№</th>
                <th className="px-4 py-3 text-left font-semibold">F.I.Sh.</th>
                <th className="w-24 px-4 py-3 text-left font-semibold">Sinf</th>
                <th className="w-40 px-4 py-3 text-right font-semibold">Jami ball</th>
              </tr>
            </thead>
            <tbody>
              {sortedStudents.map((row, i) => (
                <tr
                  key={row.studentId}
                  onClick={() => setSelected(row)}
                  className="cursor-pointer border-b border-slate-100 transition-colors hover:bg-brand-50/50"
                >
                  <td className="px-4 py-2.5 text-slate-400">{i + 1}</td>
                  <td className="px-4 py-2.5 font-medium text-slate-700">{row.fullName}</td>
                  <td className="px-4 py-2.5 text-slate-500">{row.className}</td>
                  <td className="px-4 py-2.5 text-right">
                    {row.gradedCount > 0 ? (
                      <span className={`font-semibold ${scoreColor(row.totalScore, row.totalMax)}`}>
                        {row.totalScore}
                        <span className="font-normal text-slate-400"> / {row.totalMax}</span>
                      </span>
                    ) : (
                      <span className="text-slate-300">— / —</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>
      )}

      {/* O'quvchi tafsiloti — barcha topshiriqlar vertikal */}
      {selected && (
        <div
          className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-slate-900/40 p-4 py-8"
          onClick={() => setSelected(null)}
        >
          <div
            className="w-full max-w-xl rounded-2xl bg-white shadow-xl"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="flex items-start justify-between gap-3 border-b border-slate-100 px-6 py-4">
              <div>
                <h2 className="font-semibold text-slate-800">{selected.fullName}</h2>
                <p className="text-sm text-slate-400">
                  {selected.className}-sinf ·{' '}
                  {selected.gradedCount > 0 ? (
                    <span className={scoreColor(selected.totalScore, selected.totalMax)}>
                      Jami {selected.totalScore} / {selected.totalMax} ball
                    </span>
                  ) : (
                    "hali baholanmagan"
                  )}
                </p>
              </div>
              <button
                type="button"
                onClick={() => setSelected(null)}
                className="text-slate-400 hover:text-slate-600"
              >
                <X className="h-5 w-5" />
              </button>
            </div>

            <div className="max-h-[70vh] space-y-2 overflow-y-auto p-4">
              {selected.cells.map((c) => {
                const col = colById.get(c.assignmentId)
                if (!col) return null
                return (
                  <div
                    key={c.assignmentId}
                    className="flex items-center justify-between gap-3 rounded-xl border border-slate-100 bg-slate-50 px-4 py-3"
                  >
                    <div className="min-w-0">
                      <p className="truncate font-medium text-slate-800">{col.title}</p>
                      <div className="mt-1 flex flex-wrap items-center gap-1.5 text-xs">
                        <span className="rounded-full bg-brand-50 px-2 py-0.5 font-medium text-brand-700">
                          {formatLabel[col.format]}
                        </span>
                        {col.subjectName && (
                          <span className="rounded-full bg-white px-2 py-0.5 text-slate-500">
                            {col.subjectName}
                          </span>
                        )}
                        {col.dueDate && (
                          <span className="inline-flex items-center gap-1 text-amber-600">
                            <CalendarClock className="h-3.5 w-3.5" />
                            {formatDate(col.dueDate)}
                          </span>
                        )}
                      </div>
                    </div>
                    <div className="shrink-0 text-right">
                      {c.score !== null ? (
                        <span className={`text-lg font-bold ${scoreColor(c.score, col.maxScore)}`}>
                          {c.score}
                          <span className="text-sm font-normal text-slate-400"> / {col.maxScore}</span>
                        </span>
                      ) : c.completed ? (
                        <span className="text-xs font-medium text-slate-500">Tekshirilmoqda</span>
                      ) : (
                        <span className="text-xs font-medium text-red-400">Bajarilmagan</span>
                      )}
                    </div>
                  </div>
                )
              })}
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
