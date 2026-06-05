import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Check, CheckCircle2, XCircle, Info } from 'lucide-react'
import {
  getTurnstileSettings,
  saveTurnstileSettings,
  type TurnstileConfig,
  type TeacherDeviceMap,
} from '@/api/services/settings'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input, Time24Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

/**
 * Turniket / FaceID integratsiya sozlamasi. Qurilma (Hikvision/ZKTeco) o'tish hodisalaridan
 * o'qituvchilar davomati AVTOMATIK yuklanadi (natija — "O'qituvchilar → Davomat" dashboardida).
 */
export function TurnstileSettings() {
  const [cfg, setCfg] = useState<TurnstileConfig | null>(null)
  const [password, setPassword] = useState('')
  const [loading, setLoading] = useState(true)
  const [status, setStatus] = useState<'idle' | 'saving' | 'saved'>('idle')

  useEffect(() => {
    getTurnstileSettings()
      .then(setCfg)
      .finally(() => setLoading(false))
  }, [])

  const set = <K extends keyof TurnstileConfig>(key: K, value: TurnstileConfig[K]) =>
    setCfg((prev) => (prev ? { ...prev, [key]: value } : prev))

  const setTeacherDevice = (teacherId: string, deviceUserId: string) =>
    setCfg((prev) =>
      prev
        ? { ...prev, teachers: prev.teachers.map((t) => (t.teacherId === teacherId ? { ...t, deviceUserId } : t)) }
        : prev,
    )

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (!cfg) return
    setStatus('saving')
    const saved = await saveTurnstileSettings({
      enabled: cfg.enabled,
      vendor: cfg.vendor,
      host: cfg.host.trim(),
      port: cfg.port,
      username: cfg.username.trim(),
      password: password || undefined,
      workStartTime: cfg.workStartTime,
      lateGraceMinutes: cfg.lateGraceMinutes,
      teachers: cfg.teachers,
    })
    setCfg(saved)
    setPassword('')
    setStatus('saved')
    setTimeout(() => setStatus('idle'), 2000)
  }

  if (loading || !cfg) return <Loader label="Yuklanmoqda..." />

  const mapped = cfg.teachers.filter((t: TeacherDeviceMap) => t.deviceUserId.trim()).length

  return (
    <form onSubmit={onSubmit} className="space-y-6">
      <Card>
        <div className="mb-1 flex items-center gap-2">
          <span className="font-semibold text-slate-800">Turniket integratsiya</span>
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
          Turniket/FaceID qurilmasidan o'qituvchilar davomati avtomatik yuklanadi. Natija{' '}
          <span className="font-medium text-slate-500">O'qituvchilar → Davomat</span> bo'limida (dashboard)
          ko'rinadi — kim keldi, soat nechada, kechikdimi.
        </p>

        <label className="mb-4 inline-flex cursor-pointer items-center gap-2 text-sm font-medium text-slate-700">
          <input
            type="checkbox"
            checked={cfg.enabled}
            onChange={(e) => set('enabled', e.target.checked)}
            className="h-4 w-4 rounded border-slate-300 accent-brand-600"
          />
          Integratsiyani yoqish
        </label>

        <div className="grid max-w-2xl grid-cols-1 gap-4 sm:grid-cols-2">
          <label className="flex flex-col gap-1 text-sm">
            <span className="font-medium text-slate-600">Qurilma turi</span>
            <select value={cfg.vendor} onChange={(e) => set('vendor', e.target.value)} className={control}>
              <option value="hikvision">Hikvision (ISAPI)</option>
              <option value="zkteco">ZKTeco</option>
            </select>
          </label>
          <Input
            label="Qurilma manzili (IP / host)"
            placeholder="192.168.1.64"
            value={cfg.host}
            onChange={(e) => set('host', e.target.value)}
            autoComplete="off"
          />
          <Input
            label="Port"
            type="number"
            placeholder="80"
            value={String(cfg.port)}
            onChange={(e) => set('port', Number(e.target.value) || 0)}
            autoComplete="off"
          />
          <Input
            label="Login"
            placeholder="admin"
            value={cfg.username}
            onChange={(e) => set('username', e.target.value)}
            autoComplete="off"
          />
          <Input
            label={cfg.hasPassword ? 'Parol (saqlangan — o\'zgartirish uchun yozing)' : 'Parol'}
            type="password"
            placeholder={cfg.hasPassword ? '••••••••' : 'Qurilma paroli'}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete="new-password"
          />
        </div>
      </Card>

      <Card>
        <h3 className="mb-1 font-semibold text-slate-800">Kechikish qoidasi</h3>
        <p className="mb-4 text-sm text-slate-400">
          Kelgan vaqt — <b>ish boshlanish vaqti</b> va o'sha kungi <b>birinchi dars</b>dan (qaysi biri erta
          bo'lsa) + grace dan keyin bo'lsa "kechikdi" deb belgilanadi. Umuman kelmasa — "kelmadi".
        </p>
        <div className="flex flex-wrap items-end gap-4">
          <label className="flex flex-col gap-1 text-sm">
            <span className="font-medium text-slate-600">Ish boshlanishi</span>
            <Time24Input value={cfg.workStartTime} onChange={(v) => set('workStartTime', v)} />
          </label>
          <label className="flex flex-col gap-1 text-sm">
            <span className="font-medium text-slate-600">Grace (daqiqa)</span>
            <input
              type="number"
              min={0}
              value={cfg.lateGraceMinutes}
              onChange={(e) => set('lateGraceMinutes', Number(e.target.value) || 0)}
              className={`${control} w-28`}
            />
          </label>
        </div>
      </Card>

      <Card>
        <div className="mb-1 flex items-center justify-between">
          <h3 className="font-semibold text-slate-800">O'qituvchi qurilma ID lari</h3>
          <span className="text-xs text-slate-400">
            Moslashtirilgan: {mapped} / {cfg.teachers.length}
          </span>
        </div>
        <p className="mb-3 flex items-start gap-1.5 text-sm text-slate-400">
          <Info className="mt-0.5 h-4 w-4 shrink-0" />
          Qurilmadagi xodim ID'sini (personId / employeeNo) shu yerga kiriting — davomat hodisalari shu ID
          orqali o'qituvchiga bog'lanadi.
        </p>
        <div className="max-h-80 space-y-1.5 overflow-y-auto">
          {cfg.teachers.map((t) => (
            <div key={t.teacherId} className="flex items-center gap-3">
              <span className="flex-1 truncate text-sm text-slate-700">{t.fullName}</span>
              <input
                value={t.deviceUserId}
                onChange={(e) => setTeacherDevice(t.teacherId, e.target.value)}
                placeholder="Qurilma ID"
                className={`${control} w-40`}
              />
            </div>
          ))}
          {cfg.teachers.length === 0 && <p className="text-sm text-slate-400">O'qituvchi yo'q</p>}
        </div>
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
