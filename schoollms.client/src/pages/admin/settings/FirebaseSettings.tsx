import { useEffect, useState } from 'react'
import { Check, CheckCircle2, XCircle } from 'lucide-react'
import {
  getFirebaseSettings,
  saveFirebaseSettings,
  type FirebaseConfig,
} from '@/api/services/settings'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'

/** Firebase service account (FCM push) sozlamasi — ilovaga push yuborish uchun. */
export function FirebaseSettings() {
  const [json, setJson] = useState('')
  const [configured, setConfigured] = useState(false)
  const [loading, setLoading] = useState(true)
  const [status, setStatus] = useState<'idle' | 'saving' | 'saved'>('idle')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getFirebaseSettings()
      .then((c: FirebaseConfig) => {
        setJson(c.serviceAccountJson)
        setConfigured(c.configured)
      })
      .finally(() => setLoading(false))
  }, [])

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setStatus('saving')
    setError(null)
    try {
      const saved = await saveFirebaseSettings(json.trim())
      setConfigured(saved.configured)
      setStatus('saved')
      setTimeout(() => setStatus('idle'), 2000)
    } catch {
      setStatus('idle')
      setError("JSON noto'g'ri — client_email, private_key, project_id bo'lishi kerak.")
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <Card>
      <div className="mb-1 flex items-center gap-2">
        <span className="font-semibold text-slate-800">Push (Firebase)</span>
        {configured ? (
          <span className="inline-flex items-center gap-1 rounded-full bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-700">
            <CheckCircle2 className="h-3.5 w-3.5" /> Sozlangan
          </span>
        ) : (
          <span className="inline-flex items-center gap-1 rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-500">
            <XCircle className="h-3.5 w-3.5" /> Sozlanmagan
          </span>
        )}
      </div>
      <p className="mb-4 text-sm text-slate-400">
        Ilovaga push (bildirishnoma) yuborish uchun Firebase <b>service account</b> kalitini (JSON)
        kiriting. Firebase Console → Project Settings → Service accounts → "Generate new private key"
        → yuklab olingan JSON faylni to'liq shu yerga qo'ying. Bitta loyiha ikkala ilova (ota-ona
        va o'qituvchi) uchun ishlaydi.
      </p>

      <form onSubmit={onSubmit} className="max-w-2xl space-y-4">
        <textarea
          value={json}
          onChange={(e) => setJson(e.target.value)}
          placeholder='{ "type": "service_account", "project_id": "...", "private_key": "...", "client_email": "..." }'
          spellCheck={false}
          className="h-56 w-full rounded-lg border border-slate-200 px-3 py-2 font-mono text-xs text-slate-800 outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
        />
        {error && <p className="text-sm font-medium text-red-600">{error}</p>}
        <div className="flex items-center gap-3">
          <Button type="submit" disabled={status === 'saving'}>
            {status === 'saving' ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
          {status === 'saved' && (
            <span className="inline-flex items-center gap-1 text-sm font-medium text-emerald-600">
              <Check className="h-4 w-4" /> Saqlandi
            </span>
          )}
        </div>
      </form>
    </Card>
  )
}
