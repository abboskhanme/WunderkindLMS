import { useEffect, useState } from 'react'
import { useParams, Link } from 'react-router-dom'
import { ArrowLeft } from 'lucide-react'
import type { SchoolClass, ScheduleTemplate } from '@/types'
import { getClasses } from '@/api/services/classes'
import { getTemplates } from '@/api/services/scheduleTemplates'
import { Loader } from '@/components/ui/Loader'
import { ScheduleBoard } from './ScheduleBoard'

export function TemplateEditorPage() {
  const { id = '', templateId = '' } = useParams()
  const [cls, setCls] = useState<SchoolClass | null>(null)
  const [template, setTemplate] = useState<ScheduleTemplate | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    Promise.all([getClasses(), getTemplates(id)])
      .then(([cl, tpls]) => {
        setCls(cl.find((c) => c.id === id) ?? null)
        setTemplate(tpls.find((t) => t.id === templateId) ?? null)
      })
      .finally(() => setLoading(false))
  }, [id, templateId])

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-3">
        <Link
          to={`/admin/schedule/manage/${id}`}
          className="rounded-lg p-2 text-slate-500 transition-colors hover:bg-slate-100"
        >
          <ArrowLeft className="h-5 w-5" />
        </Link>
        <div>
          <h1 className="text-xl font-semibold text-slate-800">
            {template ? template.name : 'Jadval'}
          </h1>
          <p className="text-sm text-slate-400">
            {cls ? `${cls.name}-sinf · ` : ''}soatni bosing, yon paneldan fan va o'qituvchi tanlab yarating
          </p>
        </div>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : template ? (
        <ScheduleBoard classId={id} template={template} />
      ) : (
        <p className="py-12 text-center text-slate-400">Jadval topilmadi</p>
      )}
    </div>
  )
}
