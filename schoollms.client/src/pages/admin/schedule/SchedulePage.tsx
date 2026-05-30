import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { CalendarDays, ChevronRight } from 'lucide-react'
import type { SchoolClass } from '@/types'
import { getClasses } from '@/api/services/classes'
import { languageLabels } from '@/config/constants'
import { Loader } from '@/components/ui/Loader'

export function SchedulePage() {
  const navigate = useNavigate()
  const [classes, setClasses] = useState<SchoolClass[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    getClasses()
      .then(setClasses)
      .finally(() => setLoading(false))
  }, [])

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Dars jadvali yaratish</h1>
        <p className="text-sm text-slate-400">
          Jadval yaratish va haftalarga biriktirish uchun sinfni tanlang
        </p>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {classes.map((c) => (
            <button
              key={c.id}
              onClick={() => navigate(`/admin/schedule/manage/${c.id}`)}
              className="flex items-center justify-between gap-3 rounded-2xl border border-slate-200/80 bg-white p-4 text-left shadow-sm transition-colors hover:border-brand-300 hover:bg-brand-50/40"
            >
              <div className="flex items-center gap-3">
                <div className="flex h-11 w-11 items-center justify-center rounded-xl bg-brand-50 text-brand-600">
                  <CalendarDays className="h-5 w-5" />
                </div>
                <div>
                  <p className="font-semibold text-slate-800">{c.name}</p>
                  <p className="text-xs text-slate-400">
                    {languageLabels[c.language]}
                    {c.room ? ` · ${c.room}-xona` : ''}
                  </p>
                </div>
              </div>
              <ChevronRight className="h-5 w-5 text-slate-300" />
            </button>
          ))}
          {classes.length === 0 && (
            <p className="col-span-full py-12 text-center text-slate-400">Sinflar yo'q</p>
          )}
        </div>
      )}
    </div>
  )
}
