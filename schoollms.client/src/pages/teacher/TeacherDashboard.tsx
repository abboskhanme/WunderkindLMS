import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { GraduationCap, ClipboardCheck, Crown } from 'lucide-react'
import type { TeacherClass } from '@/types'
import { getMyClasses } from '@/api/services/teacher'
import { useAuth } from '@/context/auth-context'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'

export function TeacherDashboard() {
  const { user } = useAuth()
  const [classes, setClasses] = useState<TeacherClass[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    getMyClasses()
      .then(setClasses)
      .finally(() => setLoading(false))
  }, [])

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">
          Assalomu alaykum{user?.fullName ? `, ${user.fullName}` : ''}!
        </h1>
        <p className="text-sm text-slate-400">O'qituvchi paneli</p>
      </div>

      <Link
        to="/teacher/assignments"
        className="flex items-center gap-3 rounded-2xl border border-brand-200 bg-brand-50 p-4 text-brand-700 transition-colors hover:bg-brand-100"
      >
        <ClipboardCheck className="h-6 w-6" />
        <div>
          <p className="font-semibold">Topshiriqlar</p>
          <p className="text-sm text-brand-600/80">Sinflaringizga qo'shimcha topshiriq yarating</p>
        </div>
      </Link>

      <div>
        <h2 className="mb-3 font-semibold text-slate-800">Dars beradigan sinflar</h2>
        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : classes.length === 0 ? (
          <Card>
            <p className="py-8 text-center text-slate-400">
              Sizga biriktirilgan sinf/fan yo'q. Maktab ma'muriyatiga murojaat qiling.
            </p>
          </Card>
        ) : (
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {classes.map((c) => (
              <Card key={c.classId} className="space-y-2">
                <div className="flex items-center justify-between gap-2">
                  <div className="flex items-center gap-2">
                    <div className="flex h-9 w-9 items-center justify-center rounded-xl bg-brand-50 text-brand-600">
                      <GraduationCap className="h-5 w-5" />
                    </div>
                    <p className="font-semibold text-slate-800">{c.className}</p>
                  </div>
                  {c.isHomeroom && (
                    <span className="inline-flex items-center gap-1 rounded-full bg-amber-50 px-2 py-0.5 text-[11px] font-medium text-amber-700">
                      <Crown className="h-3 w-3" /> Sinf rahbari
                    </span>
                  )}
                </div>
                <div className="flex flex-wrap gap-1.5">
                  {c.subjects.length === 0 ? (
                    <span className="text-xs text-slate-400">Dars beradigan fan yo'q</span>
                  ) : (
                    c.subjects.map((s) => (
                      <span
                        key={s.id}
                        className="rounded-md bg-slate-100 px-2 py-0.5 text-xs text-slate-600"
                      >
                        {s.name}
                      </span>
                    ))
                  )}
                </div>
              </Card>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
