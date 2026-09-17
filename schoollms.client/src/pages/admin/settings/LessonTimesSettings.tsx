import { useEffect, useState } from 'react'
import { Check, Plus, Trash2 } from 'lucide-react'
import type { LessonTime } from '@/types'
import { getSettings, saveLessonTimes } from '@/api/services/settings'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Time24Input } from '@/components/ui/Input'

type Status = 'idle' | 'saving' | 'saved'

/**
 * Dars vaqtlari — ilgari `SettingsPage`ning `lesson-times` bo'limi sifatida inline
 * yozilgan edi, endi alohida komponent: eski `/admin/settings/lesson-times`
 * marshrutida (SettingsPage orqali) va yangi "Umumiy sozlamalar" hub sahifasida
 * bir xil ishlatiladi. Forma va API chaqiruvlari o'zgarmagan.
 */
export function LessonTimesSettings() {
  const [lessonTimes, setLessonTimes] = useState<LessonTime[]>([])
  const [loading, setLoading] = useState(true)
  const [status, setStatus] = useState<Status>('idle')

  useEffect(() => {
    getSettings()
      .then((s) => setLessonTimes(s.lessonTimes))
      .finally(() => setLoading(false))
  }, [])

  const updateTime = (i: number, field: 'startTime' | 'endTime', value: string) =>
    setLessonTimes((prev) => prev.map((t, idx) => (idx === i ? { ...t, [field]: value } : t)))

  const addTime = () =>
    setLessonTimes((prev) => [...prev, { period: prev.length + 1, startTime: '', endTime: '' }])

  const removeTime = (i: number) =>
    setLessonTimes((prev) =>
      prev.filter((_, idx) => idx !== i).map((t, idx) => ({ ...t, period: idx + 1 })),
    )

  const onSave = async () => {
    setStatus('saving')
    await saveLessonTimes(lessonTimes)
    setStatus('saved')
    setTimeout(() => setStatus('idle'), 2000)
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <Card>
      <div className="mb-4 flex items-center justify-between">
        <h2 className="font-semibold text-slate-800">Dars vaqtlari</h2>
        <SaveButton status={status} onClick={onSave} />
      </div>
      <div className="space-y-2">
        {lessonTimes.map((t, i) => (
          <div key={i} className="flex flex-wrap items-center gap-2">
            <span className="w-20 text-sm font-medium text-slate-600">{t.period}-dars</span>
            <Time24Input value={t.startTime} onChange={(v) => updateTime(i, 'startTime', v)} />
            <span className="text-slate-400">—</span>
            <Time24Input value={t.endTime} onChange={(v) => updateTime(i, 'endTime', v)} />
            <button
              type="button"
              onClick={() => removeTime(i)}
              title="O'chirish"
              className="rounded-lg p-2 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
            >
              <Trash2 className="h-4 w-4" />
            </button>
          </div>
        ))}
        <button
          type="button"
          onClick={addTime}
          className="inline-flex items-center gap-1 text-sm font-medium text-brand-600 hover:text-brand-700"
        >
          <Plus className="h-4 w-4" /> Dars qo'shish
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
