import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { BookOpen, ChevronRight, Layers } from 'lucide-react'
import type { LmsSubject, LmsUnlockMode } from '@/types'
import { getTeacherLmsSubjects } from '@/api/services/teacher'
import { Loader } from '@/components/ui/Loader'
import { Card } from '@/components/ui/Card'
import { cn } from '@/lib/utils'

const unlockLabel: Record<LmsUnlockMode, string> = {
  all: 'Hammasi ochiq',
  sequential: 'Ketma-ket',
  batch: 'Guruhli',
}
const unlockColor: Record<LmsUnlockMode, string> = {
  all: 'bg-emerald-100 text-emerald-700',
  sequential: 'bg-amber-100 text-amber-700',
  batch: 'bg-brand-100 text-brand-700',
}

export function TeacherLmsPage() {
  const navigate = useNavigate()
  const [subjects, setSubjects] = useState<LmsSubject[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    getTeacherLmsSubjects()
      .then(setSubjects)
      .finally(() => setLoading(false))
  }, [])

  // Sinf bo'yicha guruhlash
  const byClass = subjects.reduce<Record<string, { className: string; items: LmsSubject[] }>>(
    (acc, s) => {
      if (!acc[s.classId]) acc[s.classId] = { className: s.className, items: [] }
      acc[s.classId].items.push(s)
      return acc
    },
    {},
  )
  const groups = Object.values(byClass)

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Ta'lim (LMS)</h1>
        <p className="text-sm text-slate-400">
          Sinflaringizdagi qo'shimcha ta'lim fanlari va o'quvchilar progressi
        </p>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : subjects.length === 0 ? (
        <Card className="py-16 text-center">
          <BookOpen className="mx-auto mb-3 h-10 w-10 text-slate-300" />
          <p className="font-medium text-slate-500">Sinflaringizda LMS fanil yo'q</p>
          <p className="mt-1 text-sm text-slate-400">
            Administrator qo'shimcha ta'lim fanlarini qo'shganda bu yerda ko'rinadi
          </p>
        </Card>
      ) : (
        <div className="space-y-8">
          {groups.map(({ className, items }) => (
            <div key={className}>
              {/* Sinf sarlavhasi */}
              <div className="mb-3 flex items-center gap-2">
                <div className="h-px flex-1 bg-slate-200" />
                <span className="rounded-full bg-slate-100 px-3 py-0.5 text-sm font-semibold text-slate-600">
                  {className}-sinf
                </span>
                <div className="h-px flex-1 bg-slate-200" />
              </div>

              {/* Fanlar kartochalari */}
              <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
                {items.map((s) => (
                  <button
                    key={s.id}
                    type="button"
                    onClick={() => navigate(`/teacher/lms/${s.id}`)}
                    className="flex flex-col gap-3 rounded-2xl border border-slate-200/80 bg-white p-4 text-left shadow-sm transition-all hover:border-brand-300 hover:shadow-md"
                  >
                    <div className="flex items-start justify-between gap-2">
                      <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-brand-50 text-brand-600">
                        <BookOpen className="h-5 w-5" />
                      </div>
                      <span
                        className={cn(
                          'rounded-full px-2.5 py-0.5 text-[11px] font-semibold',
                          unlockColor[s.unlockMode],
                        )}
                      >
                        {unlockLabel[s.unlockMode]}
                        {s.unlockMode === 'batch' && ` (${s.batchSize})`}
                      </span>
                    </div>

                    <div className="min-w-0">
                      <p className="truncate font-semibold text-slate-800">{s.title}</p>
                      {s.description && (
                        <p className="mt-0.5 line-clamp-1 text-xs text-slate-400">{s.description}</p>
                      )}
                    </div>

                    <div className="flex items-center justify-between">
                      <span className="flex items-center gap-1 text-xs text-slate-500">
                        <Layers className="h-3.5 w-3.5" />
                        {s.topicsCount} ta mavzu
                      </span>
                      <span className="flex items-center gap-1 text-xs font-medium text-brand-600">
                        Ko'rish <ChevronRight className="h-3.5 w-3.5" />
                      </span>
                    </div>
                  </button>
                ))}
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
