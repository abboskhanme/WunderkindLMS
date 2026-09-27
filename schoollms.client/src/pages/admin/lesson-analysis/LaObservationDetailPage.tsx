import { useCallback, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft, Download, Pencil, RefreshCw, Sparkles, Trash2 } from 'lucide-react'
import {
  deleteObservation,
  downloadObservationPdf,
  getCriteria,
  getObservation,
  laErrorMessage,
  regenerateAiSummary,
} from '@/api/services/lessonAnalysis'
import { useAuth } from '@/context/auth-context'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Toast } from '@/components/ui/Toast'
import { ObservationView } from '@/components/lesson-analysis/ObservationView'
import { isLaAdmin } from '@/components/lesson-analysis/format'
import { Notice, StatusChips } from '@/components/lesson-analysis/ui'
import { useLoad } from '@/components/lesson-analysis/useLoad'
import { laPaths } from './paths'

/** Tahlil — eski `EvaluationDetail.tsx` (staff). */
export function LaObservationDetailPage() {
  const { id = '' } = useParams()
  const navigate = useNavigate()
  const { user } = useAuth()
  const admin = isLaAdmin(user)

  const observation = useLoad(`obs|${id}`, () => getObservation(id))
  const criteria = useLoad('criteria-all', () => getCriteria(true))
  const [busy, setBusy] = useState<'ai' | 'pdf' | 'delete' | null>(null)
  const [error, setError] = useState<string | null>(null)
  const clearError = useCallback(() => setError(null), [])

  const back = () => (window.history.length > 1 ? navigate(-1) : navigate(laPaths.list))

  if (observation.error) {
    return (
      <div className="space-y-3">
        <Button variant="secondary" className="px-3 py-1.5" onClick={back}>
          <ArrowLeft className="h-4 w-4" />
          Orqaga
        </Button>
        <Notice>{observation.error}</Notice>
      </div>
    )
  }
  if (!observation.data || (!criteria.data && !criteria.error)) return <Loader label="Yuklanmoqda..." />
  const o = observation.data
  const sections = criteria.data ?? o.sections ?? []
  const draft = o.status === 'draft'
  // qoralamani `lessonAnalysis` ruxsatli har kim, yakunlanganini — muallif yoki admin (server `canEdit`)
  const canEdit = draft || o.canEdit

  async function regenerate() {
    setBusy('ai')
    setError(null)
    try {
      const updated = await regenerateAiSummary(o.id)
      observation.setData(() => updated)
    } catch (err) {
      setError(laErrorMessage(err, 'AI xulosa olinmadi'))
    } finally {
      setBusy(null)
    }
  }

  async function downloadPdf() {
    setBusy('pdf')
    setError(null)
    try {
      await downloadObservationPdf(o)
    } catch (err) {
      setError(laErrorMessage(err, 'PDF yuklanmadi'))
    } finally {
      setBusy(null)
    }
  }

  async function remove() {
    if (!confirm(`${o.teacherName} — ${o.lessonDate} tahlilini o‘chirasizmi? Bu amalni qaytarib bo‘lmaydi.`)) return
    setBusy('delete')
    setError(null)
    try {
      await deleteObservation(o.id)
      navigate(laPaths.list, { replace: true })
    } catch (err) {
      setError(laErrorMessage(err, 'O‘chirib bo‘lmadi'))
      setBusy(null)
    }
  }

  const actions = (
    <>
      <StatusChips status={o.status} hasAi={Boolean(o.aiSummary)} />
      <div className="flex w-full flex-wrap gap-2.5 sm:w-auto">
        {!draft && (
          <Button variant="secondary" onClick={downloadPdf} disabled={busy !== null}>
            <Download className="h-4 w-4" />
            {busy === 'pdf' ? 'Tayyorlanmoqda…' : 'PDF yuklab olish'}
          </Button>
        )}
        {!draft && o.canEdit && (
          <Button variant="secondary" onClick={regenerate} disabled={busy !== null}>
            <Sparkles className="h-4 w-4" />
            {busy === 'ai' ? 'Yaratilmoqda…' : 'AI xulosani qayta yaratish'}
          </Button>
        )}
        {canEdit && (
          <Button variant="secondary" onClick={() => navigate(laPaths.edit(o.id))} disabled={busy !== null}>
            <Pencil className="h-4 w-4" />
            {draft ? 'Davom ettirish' : 'Tahrirlash'}
          </Button>
        )}
        {admin && (
          <Button
            variant="ghost"
            onClick={remove}
            disabled={busy !== null}
            className="text-red-600 hover:bg-red-50"
          >
            <Trash2 className="h-4 w-4" />
            O‘chirish
          </Button>
        )}
      </div>
    </>
  )

  const aiExtra =
    !draft && o.aiError ? (
      <Notice kind="info">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <span>AI xulosa yaratilmadi: {o.aiError}</span>
          {o.canEdit && (
            <Button variant="secondary" onClick={regenerate} disabled={busy !== null} className="px-3 py-1.5">
              <RefreshCw className="h-4 w-4" />
              {busy === 'ai' ? 'Yaratilmoqda…' : 'Qayta yaratish'}
            </Button>
          )}
        </div>
      </Notice>
    ) : null

  return (
    <div className="space-y-4">
      <Button variant="secondary" className="px-3 py-1.5" onClick={back}>
        <ArrowLeft className="h-4 w-4" />
        Orqaga
      </Button>
      {criteria.error && <Notice kind="info">Mezonlar ro‘yxati yuklanmadi — {criteria.error}</Notice>}
      <ObservationView observation={o} sections={sections} actions={actions} aiExtra={aiExtra} />
      <Toast message={error} onClose={clearError} tone="error" duration={6000} />
    </div>
  )
}
