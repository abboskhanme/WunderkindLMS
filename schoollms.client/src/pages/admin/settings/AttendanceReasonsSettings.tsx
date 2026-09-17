import { useEffect, useState } from 'react'
import { Check, Plus, Trash2 } from 'lucide-react'
import type { AbsenceReason } from '@/types'
import { getSettings, saveAbsenceReasons } from '@/api/services/settings'
import { uid } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'

type Status = 'idle' | 'saving' | 'saved'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

/**
 * Davomat sabablari (o'quvchi yo'qligi sabablari) — ilgari `SettingsPage`ning
 * `reasons` bo'limi sifatida inline yozilgan edi, endi alohida komponent: eski
 * `/admin/settings/reasons` marshrutida (SettingsPage orqali) va yangi "Umumiy
 * sozlamalar" hub sahifasida bir xil ishlatiladi. Forma va API chaqiruvlari
 * o'zgarmagan.
 *
 * DIQQAT: bu "Arxivlash sabablari" (`ArchiveReasonsSettings.tsx`, §2.2) dan
 * boshqa katalog — u o'quvchini arxivlashda, bu esa kunlik davomatda ishlatiladi.
 */
export function AttendanceReasonsSettings() {
  const [reasons, setReasons] = useState<AbsenceReason[]>([])
  const [loading, setLoading] = useState(true)
  const [status, setStatus] = useState<Status>('idle')

  useEffect(() => {
    getSettings()
      .then((s) => setReasons(s.absenceReasons))
      .finally(() => setLoading(false))
  }, [])

  const updateReason = (i: number, field: 'name' | 'short', value: string) =>
    setReasons((prev) => prev.map((r, idx) => (idx === i ? { ...r, [field]: value } : r)))

  const toggleReasonLate = (i: number) =>
    setReasons((prev) => prev.map((r, idx) => (idx === i ? { ...r, isLate: !r.isLate } : r)))

  const addReason = () =>
    setReasons((prev) => [...prev, { id: uid(), name: '', short: '', isLate: false }])

  const removeReason = (i: number) => setReasons((prev) => prev.filter((_, idx) => idx !== i))

  const onSave = async () => {
    setStatus('saving')
    await saveAbsenceReasons(reasons.filter((r) => r.name.trim()))
    setStatus('saved')
    setTimeout(() => setStatus('idle'), 2000)
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <Card>
      <div className="mb-4 flex items-center justify-between">
        <h2 className="font-semibold text-slate-800">Davomat sabablari</h2>
        <SaveButton status={status} onClick={onSave} />
      </div>
      <div className="space-y-2">
        {reasons.map((r, i) => (
          <div key={r.id} className="flex flex-wrap items-center gap-2">
            <input
              value={r.name}
              onChange={(e) => updateReason(i, 'name', e.target.value)}
              placeholder="Sabab nomi (masalan: Kasal)"
              className={`${control} flex-1 min-w-[180px]`}
            />
            <input
              value={r.short}
              onChange={(e) => updateReason(i, 'short', e.target.value)}
              placeholder="Belgi"
              maxLength={3}
              className={`${control} w-20 text-center`}
            />
            <label
              className="inline-flex cursor-pointer items-center gap-1.5 whitespace-nowrap text-sm text-slate-600"
              title="Kech keldi — yo'qlik emas, baho qo'ysa bo'ladi"
            >
              <input
                type="checkbox"
                checked={r.isLate}
                onChange={() => toggleReasonLate(i)}
                className="h-4 w-4 rounded border-slate-300"
              />
              Kech qolish
            </label>
            <button
              type="button"
              onClick={() => removeReason(i)}
              title="O'chirish"
              className="rounded-lg p-2 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
            >
              <Trash2 className="h-4 w-4" />
            </button>
          </div>
        ))}
        <button
          type="button"
          onClick={addReason}
          className="inline-flex items-center gap-1 text-sm font-medium text-brand-600 hover:text-brand-700"
        >
          <Plus className="h-4 w-4" /> Sabab qo'shish
        </button>
      </div>
    </Card>
  )
}

function SaveButton({ status, onClick }: { status: Status; onClick: () => void }) {
  if (status === 'saved') {
    return (
      <span className="inline-flex items-center gap-1 text-sm font-medium text-emerald-600">
        <Check className="h-4 w-4" /> Saqlandi
      </span>
    )
  }
  return (
    <Button onClick={onClick} disabled={status === 'saving'}>
      {status === 'saving' ? 'Saqlanmoqda...' : 'Saqlash'}
    </Button>
  )
}
