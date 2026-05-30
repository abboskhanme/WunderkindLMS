import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import { Check, Plus, Trash2 } from 'lucide-react'
import type { AbsenceReason, AssignmentType, LessonTime, QuarterPeriod } from '@/types'
import {
  getSettings,
  saveQuarters,
  saveLessonTimes,
  saveAbsenceReasons,
} from '@/api/services/settings'
import { getAssignmentTypes, saveAssignmentTypes } from '@/api/services/assignments'
import { uid, cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Time24Input } from '@/components/ui/Input'
import { SchoolSettings } from './SchoolSettings'
import { TelegramSettings } from './TelegramSettings'

type Status = 'idle' | 'saving' | 'saved'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

const sectionTitles: Record<string, string> = {
  quarters: 'Choraklar sanalari',
  'lesson-times': 'Dars vaqtlari',
  reasons: 'Davomat sabablari',
  school: "Maktab ma'lumotlari",
  telegram: 'Telegram bot',
  'assignment-types': 'Topshiriq turlari',
}

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

export function SettingsPage() {
  const { section = 'quarters' } = useParams()
  const [quarters, setQuarters] = useState<QuarterPeriod[]>([])
  const [lessonTimes, setLessonTimes] = useState<LessonTime[]>([])
  const [reasons, setReasons] = useState<AbsenceReason[]>([])
  const [loading, setLoading] = useState(true)
  const [qStatus, setQStatus] = useState<Status>('idle')
  const [tStatus, setTStatus] = useState<Status>('idle')
  const [rStatus, setRStatus] = useState<Status>('idle')
  const [types, setTypes] = useState<AssignmentType[]>([])
  const [atStatus, setAtStatus] = useState<Status>('idle')

  useEffect(() => {
    getSettings()
      .then((s) => {
        setQuarters(normalizeQuarters(s.quarters))
        setLessonTimes(s.lessonTimes)
        setReasons(s.absenceReasons)
      })
      .finally(() => setLoading(false))
  }, [])

  useEffect(() => {
    getAssignmentTypes().then(setTypes)
  }, [])

  const updateQuarter = (i: number, field: 'startDate' | 'endDate', value: string) =>
    setQuarters((prev) => prev.map((q, idx) => (idx === i ? { ...q, [field]: value } : q)))

  const toggleQuarterOpen = (i: number) =>
    setQuarters((prev) => prev.map((q, idx) => (idx === i ? { ...q, gradesOpen: !q.gradesOpen } : q)))

  const updateTime = (i: number, field: 'startTime' | 'endTime', value: string) =>
    setLessonTimes((prev) => prev.map((t, idx) => (idx === i ? { ...t, [field]: value } : t)))

  const addTime = () =>
    setLessonTimes((prev) => [...prev, { period: prev.length + 1, startTime: '', endTime: '' }])

  const removeTime = (i: number) =>
    setLessonTimes((prev) =>
      prev.filter((_, idx) => idx !== i).map((t, idx) => ({ ...t, period: idx + 1 })),
    )

  const onSaveQuarters = async () => {
    setQStatus('saving')
    // Faqat ikkala sanasi to'ldirilgan choraklarni saqlaymiz.
    await saveQuarters(quarters.filter((q) => q.startDate && q.endDate))
    setQStatus('saved')
    setTimeout(() => setQStatus('idle'), 2000)
  }

  const onSaveTimes = async () => {
    setTStatus('saving')
    await saveLessonTimes(lessonTimes)
    setTStatus('saved')
    setTimeout(() => setTStatus('idle'), 2000)
  }

  const updateReason = (i: number, field: 'name' | 'short', value: string) =>
    setReasons((prev) => prev.map((r, idx) => (idx === i ? { ...r, [field]: value } : r)))

  const toggleReasonLate = (i: number) =>
    setReasons((prev) => prev.map((r, idx) => (idx === i ? { ...r, isLate: !r.isLate } : r)))

  const addReason = () =>
    setReasons((prev) => [...prev, { id: uid(), name: '', short: '', isLate: false }])

  const removeReason = (i: number) =>
    setReasons((prev) => prev.filter((_, idx) => idx !== i))

  const onSaveReasons = async () => {
    setRStatus('saving')
    await saveAbsenceReasons(reasons.filter((r) => r.name.trim()))
    setRStatus('saved')
    setTimeout(() => setRStatus('idle'), 2000)
  }

  const updateType = (i: number, value: string) =>
    setTypes((prev) => prev.map((t, idx) => (idx === i ? { ...t, name: value } : t)))

  const addType = () => setTypes((prev) => [...prev, { id: uid(), name: '' }])

  const removeType = (i: number) => setTypes((prev) => prev.filter((_, idx) => idx !== i))

  const onSaveTypes = async () => {
    setAtStatus('saving')
    await saveAssignmentTypes(types.filter((t) => t.name.trim()))
    setAtStatus('saved')
    setTimeout(() => setAtStatus('idle'), 2000)
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Sozlamalar</h1>
        <p className="text-sm text-slate-400">{sectionTitles[section] ?? 'Sozlamalar'}</p>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="max-w-3xl space-y-6">
          {/* Choraklar sanalari */}
          {section === 'quarters' && (
          <Card>
            <div className="mb-1 flex items-center justify-between">
              <h2 className="font-semibold text-slate-800">Choraklar</h2>
              <SaveButton status={qStatus} onClick={onSaveQuarters} />
            </div>
            <p className="mb-4 text-sm text-slate-400">
              Chorak sanalari va o'qituvchilarga chorak bahosini kiritishni ochish. "Baho ochiq"
              belgilanmagan chorakka o'qituvchi chorak bahosini qo'ya olmaydi (administrator baribir qo'ya oladi).
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
          )}

          {/* Dars vaqtlari */}
          {section === 'lesson-times' && (
          <Card>
            <div className="mb-4 flex items-center justify-between">
              <h2 className="font-semibold text-slate-800">Dars vaqtlari</h2>
              <SaveButton status={tStatus} onClick={onSaveTimes} />
            </div>
            <div className="space-y-2">
              {lessonTimes.map((t, i) => (
                <div key={i} className="flex flex-wrap items-center gap-2">
                  <span className="w-20 text-sm font-medium text-slate-600">{t.period}-dars</span>
                  <Time24Input
                    value={t.startTime}
                    onChange={(v) => updateTime(i, 'startTime', v)}
                  />
                  <span className="text-slate-400">—</span>
                  <Time24Input
                    value={t.endTime}
                    onChange={(v) => updateTime(i, 'endTime', v)}
                  />
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
          )}

          {/* Davomat sabablari */}
          {section === 'reasons' && (
          <Card>
            <div className="mb-4 flex items-center justify-between">
              <h2 className="font-semibold text-slate-800">Davomat sabablari</h2>
              <SaveButton status={rStatus} onClick={onSaveReasons} />
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
          )}

          {/* Maktab ma'lumotlari */}
          {section === 'school' && <SchoolSettings />}

          {/* Telegram bot */}
          {section === 'telegram' && <TelegramSettings />}

          {/* Topshiriq turlari */}
          {section === 'assignment-types' && (
          <Card>
            <div className="mb-1 flex items-center justify-between">
              <h2 className="font-semibold text-slate-800">Topshiriq turlari</h2>
              <SaveButton status={atStatus} onClick={onSaveTypes} />
            </div>
            <p className="mb-4 text-sm text-slate-400">
              Qo'shimcha topshiriqlarni turlash uchun (Uy vazifasi, Mustaqil ish, Test ...).
            </p>
            <div className="space-y-2">
              {types.map((t, i) => (
                <div key={t.id} className="flex items-center gap-2">
                  <input
                    value={t.name}
                    onChange={(e) => updateType(i, e.target.value)}
                    placeholder="Tur nomi (masalan: Uy vazifasi)"
                    className={`${control} flex-1 min-w-[180px]`}
                  />
                  <button
                    type="button"
                    onClick={() => removeType(i)}
                    title="O'chirish"
                    className="rounded-lg p-2 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                  >
                    <Trash2 className="h-4 w-4" />
                  </button>
                </div>
              ))}
              <button
                type="button"
                onClick={addType}
                className="inline-flex items-center gap-1 text-sm font-medium text-brand-600 hover:text-brand-700"
              >
                <Plus className="h-4 w-4" /> Tur qo'shish
              </button>
            </div>
          </Card>
          )}
        </div>
      )}
    </div>
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
