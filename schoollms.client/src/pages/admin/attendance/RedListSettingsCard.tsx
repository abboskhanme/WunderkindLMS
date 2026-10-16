/**
 * Qizil ro'yxat mezonlari — qachon o'quvchi ro'yxatga tushadi, qachon holat rahbariyatga chiqadi va kim
 * Telegram'da xabar oladi. Saqlash — `attendanceCases` kaliti (server tekshiradi).
 */
import { useState } from 'react'
import { Save } from 'lucide-react'
import type { AxiosError } from 'axios'
import {
  saveRedListSettings,
  type RedListSettings,
  type SaveRedListSettings,
} from '@/api/services/attendanceRedList'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Switch } from '@/components/hr-tasks/ui'

function errorText(err: unknown, fallback: string): string {
  return (err as AxiosError<{ message?: string }>)?.response?.data?.message ?? fallback
}

export function RedListSettingsCard({
  settings,
  onSaved,
}: {
  settings: RedListSettings
  onSaved: (next: RedListSettings) => void
}) {
  const [form, setForm] = useState<SaveRedListSettings>({
    consecutiveDays: settings.consecutiveDays,
    windowDays: settings.windowDays,
    windowUnexcused: settings.windowUnexcused,
    countExcused: settings.countExcused,
    escalateAfterDays: settings.escalateAfterDays,
    notifyTeachers: settings.notifyTeachers,
    notifyManagement: settings.notifyManagement,
  })
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null)

  const num = (key: 'consecutiveDays' | 'windowDays' | 'windowUnexcused' | 'escalateAfterDays') =>
    (e: React.ChangeEvent<HTMLInputElement>) => setForm((f) => ({ ...f, [key]: Number(e.target.value) || 0 }))

  const dirty = (Object.keys(form) as (keyof SaveRedListSettings)[]).some((k) => form[k] !== settings[k])

  const save = async () => {
    setBusy(true)
    setMsg(null)
    try {
      const next = await saveRedListSettings(form)
      onSaved(next)
      setMsg({ ok: true, text: "Saqlandi. Yangi mezon keyingi tekshiruvdan qo'llanadi." })
    } catch (e) {
      setMsg({ ok: false, text: errorText(e, "Saqlab bo'lmadi.") })
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <div className="mb-4">
        <h2 className="font-semibold text-slate-800">Mezonlar</h2>
        <p className="text-xs text-slate-400">
          Tizim har kuni {settings.scanTimes.join(' va ')} da tekshiradi (yakshanba va bayramlardan tashqari). Kun bo'yi
          kelmagan kunlar sanaladi; qisman kelgan kun seriyani uzadi, darsi yo'q yoki davomati olinmagan kun hisobga kirmaydi.
        </p>
      </div>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <Input
          label="Ketma-ket kunlar"
          type="number"
          min={2}
          max={10}
          value={form.consecutiveDays}
          onChange={num('consecutiveDays')}
        />
        <Input label="Davr (kun)" type="number" min={3} max={60} value={form.windowDays} onChange={num('windowDays')} />
        <Input
          label="Davrdagi kunlar soni"
          type="number"
          min={2}
          max={30}
          value={form.windowUnexcused}
          onChange={num('windowUnexcused')}
        />
        <Input
          label="Rahbariyatga (kundan keyin)"
          type="number"
          min={1}
          max={14}
          value={form.escalateAfterDays}
          onChange={num('escalateAfterDays')}
        />
      </div>
      <p className="mt-2 text-xs text-slate-500">
        Ro'yxatga tushadi: {form.consecutiveDays} kun ketma-ket yoki so'nggi {form.windowDays} kunda {form.windowUnexcused} kun
        {form.countExcused ? ' kelmasa' : ' sababsiz kelmasa'}. Holat {form.escalateAfterDays} kun amalsiz qolsa yoki "Bog'lanib
        bo'lmadi" bo'lsa — rahbariyat e'tiboriga chiqadi.
      </p>
      <div className="mt-4 grid gap-3 sm:grid-cols-3">
        <Switch
          checked={form.countExcused}
          onChange={(v) => setForm((f) => ({ ...f, countExcused: v }))}
          label="Sababli kunlar ham sanalsin"
          hint="O'chiq: faqat sababsiz kunlar"
        />
        <Switch
          checked={form.notifyTeachers}
          onChange={(v) => setForm((f) => ({ ...f, notifyTeachers: v }))}
          label="Sinf rahbariga Telegram xabar"
          hint="Yangi holat ochilganda"
        />
        <Switch
          checked={form.notifyManagement}
          onChange={(v) => setForm((f) => ({ ...f, notifyManagement: v }))}
          label="Rahbariyatga kunlik xabar"
          hint="«Qizil ro'yxat» ruxsati bor xodimlarga"
        />
      </div>
      <div className="mt-4 flex items-center justify-end gap-3">
        {msg && <p className={msg.ok ? 'text-sm text-emerald-700' : 'text-sm text-red-600'}>{msg.text}</p>}
        <Button onClick={save} disabled={busy || !dirty}>
          <Save className="h-4 w-4" />
          {busy ? 'Saqlanmoqda...' : 'Saqlash'}
        </Button>
      </div>
    </Card>
  )
}
