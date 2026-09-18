import { useEffect, useState } from 'react'
import { Check } from 'lucide-react'
import type { QuarterPeriod } from '@/types'
import { getSettings, saveQuarters } from '@/api/services/settings'
import { cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'

type Status = 'idle' | 'saving' | 'saved'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

/**
 * O'quv yilida har doim 4 ta chorak bo'ladi — bazada yo'q (yoki kam) bo'lsa ham
 * 1-4 choraklar uchun (bo'sh sanali) qatorlarni ko'rsatamiz, mavjud sanalarni saqlab.
 */
function normalizeQuarters(loaded: QuarterPeriod[]): QuarterPeriod[] {
  return [1, 2, 3, 4].map(
    (quarter) =>
      loaded.find((q) => q.quarter === quarter) ?? {
        quarter,
        startDate: '',
        endDate: '',
        gradesOpen: false,
      },
  )
}

/**
 * Choraklar sanalari — ilgari `SettingsPage`ning `quarters` bo'limi sifatida inline
 * yozilgan edi, endi alohida komponent: eski `/admin/settings/quarters` marshrutida
 * (SettingsPage orqali) va yangi "Umumiy sozlamalar" hub sahifasida bir xil ishlatiladi.
 * Forma va API chaqiruvlari o'zgarmagan.
 */
export function QuartersSettings() {
  const [quarters, setQuarters] = useState<QuarterPeriod[]>([])
  const [loading, setLoading] = useState(true)
  const [status, setStatus] = useState<Status>('idle')

  useEffect(() => {
    getSettings()
      .then((s) => setQuarters(normalizeQuarters(s.quarters)))
      .finally(() => setLoading(false))
  }, [])

  const updateQuarter = (i: number, field: 'startDate' | 'endDate', value: string) =>
    setQuarters((prev) => prev.map((q, idx) => (idx === i ? { ...q, [field]: value } : q)))

  const toggleQuarterOpen = (i: number) =>
    setQuarters((prev) => prev.map((q, idx) => (idx === i ? { ...q, gradesOpen: !q.gradesOpen } : q)))

  const onSave = async () => {
    setStatus('saving')
    // Faqat ikkala sanasi to'ldirilgan choraklarni saqlaymiz.
    await saveQuarters(quarters.filter((q) => q.startDate && q.endDate))
    setStatus('saved')
    setTimeout(() => setStatus('idle'), 2000)
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <Card>
      <div className="mb-1 flex items-center justify-between">
        <h2 className="font-semibold text-slate-800">Choraklar</h2>
        <SaveButton status={status} onClick={onSave} />
      </div>
      <p className="mb-4 text-sm text-slate-400">
        Chorak sanalari va o'qituvchilarga chorak bahosini kiritishni ochish. "Baho ochiq"
        belgilanmagan chorakka o'qituvchi chorak bahosini qo'ya olmaydi (administrator baribir
        qo'ya oladi).
      </p>
      <div className="space-y-3">
        {quarters.map((q, i) => {
          const hasDates = !!q.startDate && !!q.endDate
          return (
            <div
              key={q.quarter}
              className="flex flex-wrap items-center gap-2 rounded-lg border border-slate-100 p-2"
            >
              <span className="w-20 text-sm font-medium text-slate-600">{q.quarter}-chorak</span>
              <input
                type="date"
                value={q.startDate}
                onChange={(e) => updateQuarter(i, 'startDate', e.target.value)}
                className={control}
              />
              <span className="text-slate-400">—</span>
              <input
                type="date"
                value={q.endDate}
                onChange={(e) => updateQuarter(i, 'endDate', e.target.value)}
                className={control}
              />
              <label
                className={cn(
                  'ml-auto inline-flex items-center gap-1.5 whitespace-nowrap text-sm',
                  hasDates ? 'cursor-pointer text-slate-600' : 'cursor-not-allowed text-slate-300',
                )}
                title={
                  hasDates
                    ? "Ochiq bo'lsa, o'qituvchilar shu chorak bahosini kirita oladi"
                    : 'Avval chorak sanalarini kiriting'
                }
              >
                <input
                  type="checkbox"
                  checked={q.gradesOpen}
                  disabled={!hasDates}
                  onChange={() => toggleQuarterOpen(i)}
                  className="h-4 w-4 rounded border-slate-300 accent-brand-600"
                />
                Baho ochiq
              </label>
            </div>
          )
        })}
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
