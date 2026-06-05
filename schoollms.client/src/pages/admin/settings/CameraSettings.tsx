import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Check, CheckCircle2, XCircle, Info } from 'lucide-react'
import { Link } from 'react-router-dom'
import { getCameraSettings, saveCameraSettings, type CameraConfig } from '@/api/services/settings'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'

/**
 * Kamera integratsiya sozlamasi. IP kameralar RTSP oqimi media-shlyuz (MediaMTX) orqali brauzerda
 * jonli (HLS) ko'rsatiladi, 24/7 yoziladi (playback + qirqib yuklab olish). Kameralar ro'yxati va
 * kuzatuv — "Boshqaruv → Kameralar" bo'limida.
 */
export function CameraSettings() {
  const [cfg, setCfg] = useState<CameraConfig | null>(null)
  const [loading, setLoading] = useState(true)
  const [status, setStatus] = useState<'idle' | 'saving' | 'saved'>('idle')

  useEffect(() => {
    getCameraSettings().then(setCfg).finally(() => setLoading(false))
  }, [])

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (!cfg) return
    setStatus('saving')
    const saved = await saveCameraSettings({ enabled: cfg.enabled })
    setCfg(saved)
    setStatus('saved')
    setTimeout(() => setStatus('idle'), 2000)
  }

  if (loading || !cfg) return <Loader label="Yuklanmoqda..." />

  return (
    <form onSubmit={onSubmit} className="space-y-6">
      <Card>
        <div className="mb-1 flex items-center gap-2">
          <span className="font-semibold text-slate-800">Kamera integratsiya</span>
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
          IP kameralar (RTSP) media-shlyuz orqali brauzerda ko'rinadi. Kameralarni qo'shish va kuzatish —{' '}
          <Link to="/admin/boshqaruv/cameras" className="font-medium text-brand-600 hover:underline">Boshqaruv → Kameralar</Link>.
        </p>

        <label className="mb-4 inline-flex cursor-pointer items-center gap-2 text-sm font-medium text-slate-700">
          <input type="checkbox" checked={cfg.enabled} onChange={(e) => setCfg({ ...cfg, enabled: e.target.checked })}
            className="h-4 w-4 rounded border-slate-300 accent-brand-600" />
          Kamera kuzatuvini yoqish
        </label>

        <div className="rounded-lg bg-slate-50 px-3 py-2.5 text-xs text-slate-600">
          <div className="mb-1 font-medium text-slate-500">Qanday ishlaydi:</div>
          <ul className="list-inside list-disc space-y-1">
            <li>Har kameraga uning <b>RTSP manzili</b>ni (login/parol bilan) kiriting.</li>
            <li>Media-shlyuz (MediaMTX) RTSP'ni brauzer ko'radigan <b>HLS</b> ga o'giradi va 24/7 yozib boradi.</li>
            <li>Jonli kuzatuv, yozuvni orqaga qaytarish va istalgan bo'lakni <b>qirqib yuklab olish</b> — Kameralar bo'limida.</li>
          </ul>
        </div>

        <p className="mt-4 flex items-center gap-1.5 text-sm text-slate-400">
          <Info className="h-4 w-4" /> Hozircha {cfg.cameraCount} ta kamera qo'shilgan.
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
