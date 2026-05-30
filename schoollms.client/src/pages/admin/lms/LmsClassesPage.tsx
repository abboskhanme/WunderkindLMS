import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { GraduationCap, ChevronRight, BookOpen } from 'lucide-react'
import type { LmsSubject, SchoolClass } from '@/types'
import { getClasses } from '@/api/services/classes'
import { getLmsSubjects } from '@/api/services/lms'
import { languageLabels } from '@/config/constants'
import { Loader } from '@/components/ui/Loader'

export function LmsClassesPage() {
  const navigate = useNavigate()
  const [classes, setClasses] = useState<SchoolClass[]>([])
  const [subjects, setSubjects] = useState<LmsSubject[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    Promise.all([getClasses(), getLmsSubjects()])
      .then(([cls, subs]) => {
        setClasses(cls)
        setSubjects(subs)
      })
      .finally(() => setLoading(false))
  }, [])

  const countFor = (classId: string) => subjects.filter((s) => s.classId === classId).length

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Ta'lim (LMS)</h1>
        <p className="text-sm text-slate-400">
          Sinf tanlang — uning fanlar va dars materiallarini boshqaring
        </p>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : classes.length === 0 ? (
        <p className="py-12 text-center text-slate-400">Sinflar yo'q</p>
      ) : (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {classes.map((c) => {
            const cnt = countFor(c.id)
            return (
              <button
                key={c.id}
                type="button"
                onClick={() => navigate(`/admin/lms/${c.id}`)}
                className="flex items-center justify-between gap-3 rounded-2xl border border-slate-200/80 bg-white p-4 text-left shadow-sm transition-colors hover:border-brand-300 hover:bg-brand-50/40"
              >
                <div className="flex items-center gap-3">
                  <div className="flex h-11 w-11 items-center justify-center rounded-xl bg-brand-50 text-brand-600">
                    <GraduationCap className="h-5 w-5" />
                  </div>
                  <div>
                    <p className="font-semibold text-slate-800">{c.name}-sinf</p>
                    <p className="text-xs text-slate-400">
                      {languageLabels[c.language]}
                      {c.room ? ` · ${c.room}-xona` : ''}
                    </p>
                  </div>
                </div>

                <div className="flex items-center gap-2">
                  {cnt > 0 && (
                    <span className="flex items-center gap-1 rounded-lg bg-brand-50 px-2 py-1 text-xs font-medium text-brand-700">
                      <BookOpen className="h-3.5 w-3.5" />
                      {cnt} ta fan
                    </span>
                  )}
                  <ChevronRight className="h-5 w-5 text-slate-300" />
                </div>
              </button>
            )
          })}
        </div>
      )}
    </div>
  )
}
