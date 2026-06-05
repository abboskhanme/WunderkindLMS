import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Check, CheckCircle2, XCircle, Info } from 'lucide-react'
import { Link } from 'react-router-dom'
import { getGpsSettings, saveGpsSettings, type GpsConfig } from '@/api/services/settings'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

/**
 * GPS integratsiya sozlamasi. Avtobus GPS trackeri (yoki haydovchi ilovasi) joylashuvni webhook
 * orqali yuboradi; natija "Boshqaruv → GPS" bo'limida (xarita + izlar + to'xtashlar) ko'rinadi.
 */
export function GpsSettings() {
  const [cfg, setCfg] = useState<GpsConfig | null>(null)
  const [loading, setLoading] = useState(true)
  const [status, setStatus] = useState<'idle' | 'saving' | 'saved'>('idle')

  useEffect(() => {
    getGpsSettings().then(setCfg).finally(() => setLoading(false))
  }, [])

  const set = <K extends keyof GpsConfig>(k: K, v: GpsConfig[K]) =>
    setCfg((p) => (p ? { ...p, [k]: v } : p))

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (!cfg) return
    setStatus('saving')
    const saved = await saveGpsSettings({
      enabled: cfg.enabled,
      ingestToken: cfg.ingestToken,
      onlineMinutes: cfg.onlineMinutes,
      stopRadiusM: cfg.stopRadiusM,
      stopMinMinutes: cfg.stopMinMinutes,
    })
    setCfg(saved)
    setStatus('saved')
    setTimeout(() => setStatus('idle'), 2000)
  }

  if (loading || !cfg) return <Loader label="Yuklanmoqda..." />

  const webhookUrl = `${window.location.origin}/api/devices/gps`

  return (
    <form onSubmit={onSubmit} className="space-y-6">
      <Card>
        <div className="mb-1 flex items-center gap-2">
          <span className="font-semibold text-slate-800">GPS integratsiya</span>
          {cfg.enabled ? (
            <span className="inline-flex items-center gap-1 rounded-full bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-700">
              <CheckCircle2 className="h-3.5 w-3.5" /> Yoqilgan
            </span>
          ) : (
            <span className="inline-flex items-center gap-1 rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-500">
              <XCircle className="h-3.5 w-3.5" /> O'chiq
            </span>
          )}
        </div>
        <p className="mb-4 text-sm text-slate-400">
          Avtobus GPS trackeri joylashuvni quyidagi manzilga yuboradi. Natija{' '}
          <Link to="/admin/boshqaruv/gps" className="font-medium text-brand-600 hover:underline">Boshqaruv → GPS</Link>{' '}
          bo'limida — xarita, izlar va to'xtashlar — ko'rinadi.
        </p>

        <label className="mb-4 inline-flex cursor-pointer items-center gap-2 text-sm font-medium text-slate-700">
          <input type="checkbox" checked={cfg.enabled} onChange={(e) => set('enabled', e.target.checked)}
            className="h-4 w-4 rounded border-slate-300 accent-brand-600" />
          Integratsiyani yoqish
        </label>

        <div className="mb-4 rounded-lg bg-slate-50 px-3 py-2.5 text-xs text-slate-600">
          <div className="mb-1 font-medium text-slate-500">Webhook (tracker shu yerga POST qiladi):</div>
          <code className="break-all text-slate-700">POST {webhookUrl}</code>
          <pre className="mt-2 overflow-x-auto rounded bg-white p-2 text-[11px] text-slate-600">{`{ "deviceId": "<tracker-id>", "lat": 41.31, "lng": 69.24,
  "speed": 35, "time": "2026-06-05T08:30:00", "token": "<token>" }`}</pre>
          <div className="mt-1 text-slate-400">deviceId — avtobusga biriktirilgan GPS qurilma ID'si (GPS bo'limida).</div>
        </div>

        <Input
          label="Webhook tokeni (ixtiyoriy — bo'sh bo'lsa tekshirilmaydi)"
          placeholder="maxfiy kalit"
          value={cfg.ingestToken}
          onChange={(e) => set('ingestToken', e.target.value)}
          autoComplete="off"
        />
      </Card>

      <Card>
        <h3 className="mb-1 font-semibold text-slate-800">Hisoblash parametrlari</h3>
        <p className="mb-4 text-sm text-slate-400">
          "Onlayn" — oxirgi signal shu daqiqalar ichida bo'lsa. "To'xtash" — avtobus shu radius (metr)
          ichida shu daqiqadan ko'p turib qolsa.
        </p>
        <div className="flex flex-wrap items-end gap-4">
          <label className="flex flex-col gap-1 text-sm">
            <span className="font-medium text-slate-600">Onlayn (daqiqa)</span>
            <input type="number" min={1} value={cfg.onlineMinutes}
              onChange={(e) => set('onlineMinutes', Number(e.target.value) || 1)} className={`${control} w-28`} />
          </label>
          <label className="flex flex-col gap-1 text-sm">
            <span className="font-medium text-slate-600">To'xtash radiusi (m)</span>
            <input type="number" min={5} value={cfg.stopRadiusM}
              onChange={(e) => set('stopRadiusM', Number(e.target.value) || 5)} className={`${control} w-32`} />
          </label>
          <label className="flex flex-col gap-1 text-sm">
            <span className="font-medium text-slate-600">To'xtash (daqiqa)</span>
            <input type="number" min={1} value={cfg.stopMinMinutes}
              onChange={(e) => set('stopMinMinutes', Number(e.target.value) || 1)} className={`${control} w-28`} />
          </label>
        </div>
        <p className="mt-4 flex items-center gap-1.5 text-sm text-slate-400">
          <Info className="h-4 w-4" /> Hozircha {cfg.busCount} ta avtobus qo'shilgan.
        </p>
      </Card>

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
  )
}
