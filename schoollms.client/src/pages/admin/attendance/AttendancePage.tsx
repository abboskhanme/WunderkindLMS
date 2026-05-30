import { useEffect, useState } from 'react'
import { Users, BookOpen, UserX, ChevronLeft, ChevronRight } from 'lucide-react'
import type { SchoolClass } from '@/types'
import { getClasses } from '@/api/services/classes'
import {
  getDailyAttendance,
  getSubjectAttendanceDetail,
  type DailyAttendance,
  type SubjectAttendance,
  type StudentStatus,
} from '@/api/services/attendance'
import { formatDate } from '@/lib/utils'
import { addDaysISO } from '@/lib/weeks'
import { Card } from '@/components/ui/Card'
import { StatCard } from '@/components/ui/StatCard'
import { Loader } from '@/components/ui/Loader'
import { SubjectAttendanceModal } from './SubjectAttendanceModal'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 outline-none focus:border-brand-400'

const weekdayNames = [
  'Yakshanba',
  'Dushanba',
  'Seshanba',
  'Chorshanba',
  'Payshanba',
  'Juma',
  'Shanba',
]
const pad = (n: number) => String(n).padStart(2, '0')
function todayISO(): string {
  const d = new Date()
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

export function AttendancePage() {
  const [classes, setClasses] = useState<SchoolClass[]>([])
  const [classId, setClassId] = useState('')
  const [date, setDate] = useState(todayISO())
  const [data, setData] = useState<DailyAttendance | null>(null)
  const [loading, setLoading] = useState(true)
  const [detail, setDetail] = useState<SubjectAttendance | null>(null)
  const [detailStudents, setDetailStudents] = useState<StudentStatus[]>([])
  const [detailLoading, setDetailLoading] = useState(false)

  useEffect(() => {
    getClasses()
      .then((cl) => {
        setClasses(cl)
        const initial = cl.find((c) => c.name === '9-A') ?? cl[0]
        setClassId(initial?.id ?? '')
      })
      .finally(() => setLoading(false))
  }, [])

  useEffect(() => {
    if (!classId) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- yangi so'rovdan oldin holatni tozalaymiz (maqsadli)
    setData(null)
    getDailyAttendance(classId, date).then(setData)
  }, [classId, date])

  const subjects = data?.subjects ?? []
  const totalAbsent = subjects.reduce((a, s) => a + s.absent, 0)

  const openDetail = (s: SubjectAttendance) => {
    setDetail(s)
    setDetailLoading(true)
    getSubjectAttendanceDetail(classId, s.subjectId, date)
      .then(setDetailStudents)
      .finally(() => setDetailLoading(false))
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Davomat</h1>
        <p className="text-sm text-slate-400">Sinf bo'yicha kunlik davomat hisoboti</p>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <>
          {/* Tanlovlar */}
          <div className="flex flex-wrap items-center gap-3">
            <select value={classId} onChange={(e) => setClassId(e.target.value)} className={control}>
              {classes.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}-sinf
                </option>
              ))}
            </select>
            <div className="flex items-center gap-1">
              <button
                onClick={() => setDate(addDaysISO(date, -1))}
                className="rounded-lg border border-slate-200 p-2 text-slate-500 hover:bg-slate-50"
              >
                <ChevronLeft className="h-4 w-4" />
              </button>
              <input
                type="date"
                value={date}
                onChange={(e) => setDate(e.target.value)}
                className={control}
              />
              <button
                onClick={() => setDate(addDaysISO(date, 1))}
                className="rounded-lg border border-slate-200 p-2 text-slate-500 hover:bg-slate-50"
              >
                <ChevronRight className="h-4 w-4" />
              </button>
            </div>
            <span className="text-sm text-slate-400">
              {weekdayNames[new Date(date).getDay()]}, {formatDate(date)}
            </span>
          </div>

          {/* Ko'rsatkichlar */}
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <StatCard label="O'quvchilar" value={data?.total ?? 0} icon={Users} />
            <StatCard
              label="Bugungi darslar"
              value={subjects.length}
              icon={BookOpen}
              iconBg="bg-violet-50"
              iconColor="text-violet-600"
            />
            <StatCard
              label="Kelmaganlar"
              value={totalAbsent}
              icon={UserX}
              iconBg="bg-red-50"
              iconColor="text-red-600"
            />
          </div>

          <Card className="p-0">
            {!data ? (
              <Loader label="Yuklanmoqda..." />
            ) : subjects.length === 0 ? (
              <p className="py-12 text-center text-slate-400">
                Bu kuni dars yo'q (dam olish kuni yoki jadval biriktirilmagan)
              </p>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-left text-sm">
                  <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                    <tr>
                      <th className="w-12 px-4 py-3">Dars</th>
                      <th className="px-4 py-3">Fan</th>
                      <th className="px-4 py-3 text-center">Jami</th>
                      <th className="px-4 py-3 text-center">Keldi</th>
                      <th className="px-4 py-3 text-center">Kelmadi</th>
                      <th className="px-4 py-3">Sabablar</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {subjects.map((s) => (
                      <tr
                        key={`${s.subjectId}-${s.period}`}
                        onClick={() => openDetail(s)}
                        className="cursor-pointer hover:bg-slate-50/60"
                      >
                        <td className="px-4 py-3 text-slate-400">{s.period}</td>
                        <td className="px-4 py-3 font-medium text-slate-800">{s.subjectName}</td>
                        <td className="px-4 py-3 text-center text-slate-600">{s.total}</td>
                        <td className="px-4 py-3 text-center font-semibold text-emerald-600">
                          {s.present}
                        </td>
                        <td className="px-4 py-3 text-center font-semibold text-red-600">
                          {s.absent || '—'}
                        </td>
                        <td className="px-4 py-3">
                          <div className="flex flex-wrap gap-1">
                            {s.reasons.map((r) => (
                              <span
                                key={r.name}
                                className="rounded-md bg-red-50 px-2 py-0.5 text-xs font-medium text-red-600"
                              >
                                {r.name}: {r.count}
                              </span>
                            ))}
                            {s.reasons.length === 0 && <span className="text-slate-300">—</span>}
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>

          <SubjectAttendanceModal
            open={!!detail}
            title={detail?.subjectName ?? ''}
            subtitle={detail ? `${detail.period}-dars · ${formatDate(date)}` : ''}
            students={detailStudents}
            loading={detailLoading}
            onClose={() => setDetail(null)}
          />
        </>
      )}
    </div>
  )
}
