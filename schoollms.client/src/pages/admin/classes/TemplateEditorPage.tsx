import { useEffect, useState } from 'react'
import { useParams, Link } from 'react-router-dom'
import { ArrowLeft } from 'lucide-react'
import type { ScheduleTemplate } from '@/types'
import { getTemplates } from '@/api/services/scheduleTemplates'
import { Loader } from '@/components/ui/Loader'
import { ScheduleBoard } from './ScheduleBoard'
import { resolveScheduleOwner, type ScheduleOwner } from './scheduleOwner'

/**
 * Bitta jadval variantini tahrirlash. `:id` — EGAning id'si: sinf yoki
 * o'quv guruhi (`docs/modules/students-parity.md` §2.1.4), shuning uchun
 * sahifa ikkalasi uchun ham bitta.
 */
export function TemplateEditorPage() {
  const { id = '', templateId = '' } = useParams()
  const [owner, setOwner] = useState<ScheduleOwner | null>(null)
  const [template, setTemplate] = useState<ScheduleTemplate | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    Promise.all([resolveScheduleOwner(id), getTemplates(id)])
      .then(([o, tpls]) => {
        setOwner(o)
        setTemplate(tpls.find((t) => t.id === templateId) ?? null)
      })
      .finally(() => setLoading(false))
  }, [id, templateId])

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-3">
        <Link title="Orqaga" aria-label="Orqaga"
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
            {owner ? `${owner.subtitle} · ` : ''}katakni bosing — fan va o'qituvchi tanlash oynasi ochiladi
          </p>
        </div>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : template ? (
        <ScheduleBoard
          classId={id}
          template={template}
          onTemplateReplaced={(t) => {
            // Faol jadvalning nusxasi olindi: sarlavha va manzil yangi versiyaga o'tadi. Sahifa qayta
            // yuklanmaydi (useParams o'zgarmaydi) — ekrandagi darslar saqlanib qoladi.
            setTemplate((prev) => (prev ? { ...prev, id: t.id, name: t.name } : t))
            window.history.replaceState(window.history.state, '', `/admin/schedule/manage/${id}/template/${t.id}`)
          }}
        />
      ) : (
        <p className="py-12 text-center text-slate-400">Jadval topilmadi</p>
      )}
    </div>
  )
}
