import { useEffect, useState } from 'react'
import { AlertTriangle, Plus } from 'lucide-react'
import type { ScheduleLesson, ScheduleTemplate, Subject, Teacher } from '@/types'
import { getSubjects } from '@/api/services/subjects'
import { getTeachers } from '@/api/services/teachers'
import {
  setTemplateCell,
  clearTemplateSlot,
  getOccupiedSlots,
  getPupilOverlay,
  type OccupiedSlot,
  type PupilOverlaySlot,
} from '@/api/services/scheduleTemplates'
import { weekDays, schedulePeriods } from '@/config/constants'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import { LessonEditorPanel, type SlotTarget } from './LessonEditorPanel'

interface Props {
  /** Eganing id'si — sinf id'si yoki o'quv guruhi id'si (§2.1.4) */
  classId: string
  template: ScheduleTemplate
}

/** O'qituvchi → band soatlar xaritasi. teacherId → [{day, period, className, templateName, ownerKind}] */
type OccupiedSlots = Record<string, OccupiedSlot[]>

/** Bitta jadval variantining (template) haftalik gridi + yonidagi inline tahrir paneli */
export function ScheduleBoard({ classId, template }: Props) {
  const ownerKind = template.ownerKind ?? 'class'
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [occupiedSlots, setOccupiedSlots] = useState<OccupiedSlots>({})
  const [lessons, setLessons] = useState<ScheduleLesson[]>(template.lessons)
  const [loading, setLoading] = useState(true)
  const [selected, setSelected] = useState<SlotTarget | null>(null)
  /**
   * Server rad etgan saqlashning sababi (G-11 o'quvchi ziddiyati — 409).
   * Ilgari saqlash javobiga UMUMAN qaralmasdi: rad etilgan katak ekranda
   * saqlanganday ko'rinib qolardi.
   */
  const [saveError, setSaveError] = useState<string | null>(null)
  /**
   * Shu eganing o'quvchilari boshqa egada (guruhda) band bo'ladigan soatlar —
   * FAQAT KO'RSATISH. Guruh darslari o'chiq bo'lsa server bo'sh ro'yxat beradi.
   */
  const [overlay, setOverlay] = useState<PupilOverlaySlot[]>([])

  useEffect(() => {
    Promise.all([
      getSubjects(),
      getTeachers(),
      getOccupiedSlots(template.id),
      getPupilOverlay(classId).catch(() => [] as PupilOverlaySlot[]),
    ])
      .then(([s, t, occ, ov]) => {
        setSubjects(s)
        setTeachers(t)
        setOccupiedSlots(occ)
        setOverlay(ov)
      })
      .finally(() => setLoading(false))
    // eslint-disable-next-line react-hooks/exhaustive-deps -- template.id mount vaqtida qat'iy
  }, [])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- shablon o'zgarganda darslar nusxasini state'ga olamiz (maqsadli)
    setLessons(template.lessons)
    setSelected(null)
  }, [template.id, template.lessons])

  const subjectName = (sid: string) => subjects.find((s) => s.id === sid)?.name ?? ''
  const teacherName = (tid: string) => teachers.find((t) => t.id === tid)?.fullName ?? ''
  /** Bir (day, period) katakdagi BARCHA darslar (0..2 ta). */
  const lessonsAt = (day: number, period: number) =>
    lessons.filter((l) => l.day === day && l.period === period)
  /** Shu katakda o'quvchilarning boshqa egadagi darslari (faqat ko'rsatish). */
  const overlayAt = (day: number, period: number) =>
    overlay.filter((o) => o.day === day && o.period === period)

  const message = (e: unknown, fallback: string) =>
    (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback

  const handleSave = (day: number, period: number, next: ScheduleLesson[]) => {
    const before = lessons
    setSaveError(null)
    setLessons((prev) => [...prev.filter((l) => !(l.day === day && l.period === period)), ...next])
    // Panel ochiqligicha qoladi — saqlangan holatni ko'rsatib turamiz.
    setSelected({ day, period, lessons: next })
    setTemplateCell(classId, template.id, day, period, next).catch((e) => {
      // Rad etildi — ekrandagi optimistik holatni QAYTARAMIZ, aks holda
      // foydalanuvchi saqlanmagan darsni saqlangan deb o'ylardi.
      setLessons(before)
      setSelected({ day, period, lessons: before.filter((l) => l.day === day && l.period === period) })
      setSaveError(message(e, "Darsni saqlab bo'lmadi"))
    })
  }

  const handleClear = (day: number, period: number) => {
    const before = lessons
    setSaveError(null)
    setLessons((prev) => prev.filter((l) => !(l.day === day && l.period === period)))
    setSelected({ day, period, lessons: [] })
    clearTemplateSlot(classId, template.id, day, period).catch((e) => {
      setLessons(before)
      setSelected({ day, period, lessons: before.filter((l) => l.day === day && l.period === period) })
      setSaveError(message(e, "Darsni o'chirib bo'lmadi"))
    })
  }

  return (
    <div className="space-y-3">
      {saveError && (
        <div className="flex items-start gap-2 rounded-xl border border-red-200 bg-red-50 px-4 py-3">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-red-500" />
          <p className="text-sm text-red-700">{saveError}</p>
        </div>
      )}
      <div className="flex flex-col gap-4 xl:flex-row xl:items-start">
      <Card className="min-w-0 flex-1 p-0">
        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full border-separate border-spacing-0 text-sm">
              <thead>
                <tr className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <th className="w-12 px-3 py-3 text-center">№</th>
                  {weekDays.map((d) => (
                    <th key={d} className="min-w-[120px] px-3 py-3 text-left font-medium">
                      {d}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {schedulePeriods.map((period) => (
                  <tr key={period}>
                    <td className="px-3 py-1.5 text-center align-top">
                      <div className="mt-2 inline-flex h-7 w-7 items-center justify-center rounded-lg bg-slate-100 text-sm font-semibold text-slate-500">
                        {period}
                      </div>
                    </td>
                    {weekDays.map((_, day) => {
                      const slotLessons = lessonsAt(day, period)
                      const slotOverlay = overlayAt(day, period)
                      const isSplit =
                        slotLessons.length > 1 ||
                        (slotLessons.length === 1 && (slotLessons[0].subGroup ?? 0) > 0)
                      const isSelected = selected?.day === day && selected?.period === period
                      return (
                        <td key={day} className="px-1.5 py-1.5 align-top">
                          <button
                            type="button"
                            onClick={() => setSelected({ day, period, lessons: slotLessons })}
                            className={cn(
                              'flex min-h-[60px] w-full flex-col justify-center rounded-lg p-2 text-left transition-colors',
                              slotLessons.length > 0
                                ? 'border border-brand-100 bg-brand-50 hover:border-brand-300'
                                : 'border border-dashed border-slate-200 text-slate-300 hover:bg-slate-50',
                              isSelected && 'ring-2 ring-brand-400 ring-offset-1',
                            )}
                          >
                            {slotLessons.length === 0 ? (
                              <Plus className="mx-auto h-4 w-4" />
                            ) : isSplit ? (
                              <div className="space-y-1">
                                {slotLessons
                                  .slice()
                                  .sort((a, b) => (a.subGroup ?? 0) - (b.subGroup ?? 0))
                                  .map((l, i) => (
                                    <div key={i} className="flex flex-col">
                                      <span
                                        className={cn(
                                          'inline-block w-fit rounded px-1 text-[10px] font-semibold',
                                          l.subGroup === 1
                                            ? 'bg-sky-200 text-sky-800'
                                            : 'bg-violet-200 text-violet-800',
                                        )}
                                      >
                                        G{l.subGroup}
                                      </span>
                                      <span className="text-xs font-medium text-slate-800">
                                        {subjectName(l.subjectId)}
                                      </span>
                                      {l.teacherId && (
                                        <span className="text-[11px] text-slate-500">
                                          {teacherName(l.teacherId)}
                                        </span>
                                      )}
                                    </div>
                                  ))}
                              </div>
                            ) : (
                              <>
                                <span className="text-sm font-medium text-slate-800">
                                  {subjectName(slotLessons[0].subjectId)}
                                </span>
                                {slotLessons[0].teacherId && (
                                  <span className="mt-0.5 text-xs text-slate-500">
                                    {teacherName(slotLessons[0].teacherId)}
                                  </span>
                                )}
                              </>
                            )}
                          </button>
                          {slotOverlay.length > 0 && (
                            <div className="mt-1 space-y-0.5">
                              {slotOverlay.map((o) => (
                                <p
                                  key={o.ownerId}
                                  title={`${o.studentCount} ta o'quvchi shu soatda "${o.ownerName}" da band — bu yerga dars qo'yib bo'lmaydi`}
                                  className="truncate rounded bg-violet-50 px-1.5 py-0.5 text-[10px] text-violet-700"
                                >
                                  {o.ownerName} · {o.studentCount} ta
                                </p>
                              ))}
                            </div>
                          )}
                        </td>
                      )
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <div className="w-full shrink-0 xl:sticky xl:top-4 xl:w-[340px]">
        <LessonEditorPanel
          slot={selected}
          subjects={subjects}
          teachers={teachers}
          occupiedSlots={occupiedSlots}
          ownerKind={ownerKind}
          onSave={handleSave}
          onClear={handleClear}
        />
      </div>
      </div>
    </div>
  )
}
