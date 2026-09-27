import { useCallback, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft, Download } from 'lucide-react'
import { downloadMyObservationPdf, getMyObservation, laErrorMessage, laErrorStatus } from '@/api/services/lessonAnalysis'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Toast } from '@/components/ui/Toast'
import { ObservationView } from '@/components/lesson-analysis/ObservationView'
import { Notice } from '@/components/lesson-analysis/ui'
import { useLoad } from '@/components/lesson-analysis/useLoad'
import { TEACHER_LA_BASE } from '@/pages/admin/lesson-analysis/paths'

/**
 * O'qituvchi — o'zining bitta YAKUNLANGAN tahlili (qoralama yoki boshqaning tahlili → 404).
 * Faqat o'qish va PDF: tahrirlash, AI xulosani qayta yaratish va AI xatosi ko'rsatilmaydi.
 */
export function MyObservationDetailPage() {
  const { id = '' } = useParams()
  const navigate = useNavigate()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const clearError = useCallback(() => setError(null), [])

  const observation = useLoad(`my-obs|${id}`, async () => {
    try {
      return await getMyObservation(id)
    } catch (err) {
      if (laErrorStatus(err) === 404) throw new Error('Tahlil topilmadi', { cause: err })
      throw err
    }
  })

  const back = (
    <Button
      variant="secondary"
      className="px-3 py-1.5"
      onClick={() => (window.history.length > 1 ? navigate(-1) : navigate(TEACHER_LA_BASE))}
    >
      <ArrowLeft className="h-4 w-4" />
      Orqaga
    </Button>
  )

  if (observation.error) {
    return (
      <div className="space-y-3">
        {back}
        <Notice>{observation.error}</Notice>
      </div>
    )
  }
  if (!observation.data) return <Loader label="Yuklanmoqda..." />
  const o = observation.data

  async function downloadPdf() {
    setBusy(true)
    setError(null)
    try {
      await downloadMyObservationPdf(o)
    } catch (err) {
      setError(laErrorMessage(err, 'PDF yuklanmadi'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="min-w-0 space-y-4">
      {back}
      {!o.sections && (
        <Notice kind="info">Mezonlar ro‘yxati hozircha mavjud emas — natija va xulosa quyida.</Notice>
      )}
      <ObservationView
        observation={o}
        sections={o.sections ?? []}
        actions={
          <Button variant="secondary" onClick={downloadPdf} disabled={busy}>
            <Download className="h-4 w-4" />
            {busy ? 'Tayyorlanmoqda…' : 'PDF yuklab olish'}
          </Button>
        }
      />
      <Toast message={error} onClose={clearError} tone="error" duration={6000} />
    </div>
  )
}
