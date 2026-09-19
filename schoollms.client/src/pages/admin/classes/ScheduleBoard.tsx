import { useEffect, useMemo, useState } from 'react'
import { AlertTriangle, Plus } from 'lucide-react'
import type { ScheduleLesson, ScheduleTemplate, SchoolSettings, Subject, Teacher } from '@/types'
import { getSubjects } from '@/api/services/subjects'
import { getTeachers } from '@/api/services/teachers'
import { getSettings } from '@/api/services/settings'
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
import { Toast } from '@/components/ui/Toast'
import { PeriodCell, PeriodHead } from '@/components/schedule/PeriodCell'
import { periodTimeMap } from '@/components/schedule/periodTimes'
import { cn } from '@/lib/utils'
import { LessonSlotModal, type SlotTarget } from './LessonSlotModal'

interface Props {
  /** Eganing id'si — sinf id'si yoki o'quv guruhi id'si (§2.1.4) */
  classId: string
  template: ScheduleTemplate
}

/** O'qituvchi → band soatlar xaritasi. teacherId → [{day, period, className, templateName, ownerKind}] */
type OccupiedSlots = Record<string, OccupiedSlot[]>

/** `#RRGGBB` ni `rgba(...)` ga o'giradi — F-3 katak bo'yog'i uchun (noto'g'ri shakl — undefined). */
function tint(hex: string, alpha: number): string | undefined {
  const m = /^#([0-9a-fA-F]{6})$/.exec(hex)
  if (!m) return undefined
  const n = parseInt(m[1], 16)
  return `rgba(${(n >> 16) & 255}, ${(n >> 8) & 255}, ${n & 255}, ${alpha})`
}

/* ==========================================================================
   JADVAL YARATISH TO'RI

   Mijoz, 2026-09-19: "dars jadvali yaratish qismida ham boshqa dars
   jadvallari kabi jadval bo'lsin, katakchalarni bosganda modal window
   ochilib kerakli narsalar tanlansin."

   Shuning uchun bu to'r ko'rish ekranlaridagi to'rning AYNAN o'zi: chapda
   dars raqami va uning qo'ng'iroq vaqti (`PeriodCell`), tepada hafta
   kunlari, katakda fan va o'qituvchi. Yagona farqi — bu yerda katak
   bosiladi va tahrir oynasi ochiladi.

   NEGA O'NTA QATOR: ko'rish ekranlarida bo'sh qatorlar kesiladi, bu yerda
   esa YO'Q — bo'sh katak aynan "yangi dars qo'shish" tugmasi. Sozlamada 6
   ta dars vaqti turgan bo'lsa ham, 7-darsni shu yerdan qo'yib bo'ladi.
   ========================================================================== */
export function ScheduleBoard({ classId, template }: Props) {
  const ownerKind = template.ownerKind ?? 'class'
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [settings, setSettings] = useState<SchoolSettings | null>(null)
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
  /** Saqlandi/o'chirildi xabari — davomat ekranidagi kabi. */
  const [toast, setToast] = useState<string | null>(null)
  /**
   * Shu eganing o'quvchilari boshqa egada (guruhda) band bo'ladigan soatlar —
   * FAQAT KO'RSATISH. Guruh darslari o'chiq bo'lsa server bo'sh ro'yxat beradi.
   */
  const [overlay, setOverlay] = useState<PupilOverlaySlot[]>([])

  useEffect(() => {
    Promise.all([
      getSubjects(),
      getTeachers(),
      getSettings(),
      getOccupiedSlots(template.id),
      getPupilOverlay(classId).catch(() => [] as PupilOverlaySlot[]),
    ])
      .then(([s, t, st, occ, ov]) => {
        setSubjects(s)
        setTeachers(t)
        setSettings(st)
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

  /** Dars raqami -> qo'ng'iroq vaqti ("Sozlamalar → Dars vaqtlari"). */
  const periodTimes = useMemo(() => periodTimeMap(settings), [settings])

  const subjectName = (sid: string) => subjects.find((s) => s.id === sid)?.name ?? ''
  /** F-3: jadval katakchasini bo'yaydigan rang — fanda ko'rsatilmagan bo'lsa null. */
  const subjectColor = (sid: string) => subjects.find((s) => s.id === sid)?.color ?? null
  const teacherName = (tid: string) => teachers.find((t) => t.id === tid)?.fullName ?? ''
  /**
   * Tahrir oynasidagi fan tanlovi FAQAT faol fanlarni ko'rsatadi (F-3: "must
   * disappear from pickers for NEW rows") — lekin joriy katakda ALLAQACHON
   * tanlangan (endi faolsizlantirilgan bo'lishi mumkin) fan ro'yxatdan
   * TUSHIB QOLMAYDI, aks holda mavjud darsni tahrirlashda tanlov bo'sh
   * ko'rinib qolardi ("must not break existing... rows").
   */
  const pickableSubjects = (() => {
    const keep = new Set((selected?.lessons ?? []).map((l) => l.subjectId))
    return subjects.filter((s) => s.isActive !== false || keep.has(s.id))
  })()
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
    // Oyna yopiladi — mijoz aynan shuni so'radi: tanlandi, qo'yildi, ketdi.
    setSelected(null)
    setToast(`${weekDays[day]} · ${period}-dars saqlandi`)
    setTemplateCell(classId, template.id, day, period, next).catch((e) => {
      // Rad etildi — ekrandagi optimistik holatni QAYTARAMIZ, aks holda
      // foydalanuvchi saqlanmagan darsni saqlangan deb o'ylardi.
      setLessons(before)
      setToast(null)
      setSaveError(message(e, "Darsni saqlab bo'lmadi"))
    })
  }

  const handleClear = (day: number, period: number) => {
    const before = lessons
    setSaveError(null)
    setLessons((prev) => prev.filter((l) => !(l.day === day && l.period === period)))
    setSelected(null)
    setToast(`${weekDays[day]} · ${period}-dars tozalandi`)
    clearTemplateSlot(classId, template.id, day, period).catch((e) => {
      setLessons(before)
      setToast(null)
      setSaveError(message(e, "Darsni o'chirib bo'lmadi"))
    })
  }

  const timeLabel = (period: number) => {
    const t = periodTimes.get(period)
    return t ? `${t.start}–${t.end}` : null
  }

  return (
    <div className="space-y-3">
      {saveError && (
        <div className="flex items-start gap-2 rounded-xl border border-red-200 bg-red-50 px-4 py-3">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-red-500" />
          <p className="text-sm text-red-700">{saveError}</p>
        </div>
      )}

      <Card className="p-0">
        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : (
          <div className="overflow-x-auto p-4">
            <table className="w-full border-separate border-spacing-0 text-sm">
              <thead>
                <tr className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <PeriodHead />
                  {weekDays.map((d) => (
                    <th key={d} className="min-w-[120px] px-2 py-2 text-left font-medium">
                      {d}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {schedulePeriods.map((period) => (
                  <tr key={period}>
                    <PeriodCell period={period} time={periodTimes.get(period)} />
                    {weekDays.map((_, day) => {
                      const slotLessons = lessonsAt(day, period)
                      const slotOverlay = overlayAt(day, period)
                      const isSplit =
                        slotLessons.length > 1 ||
                        (slotLessons.length === 1 && (slotLessons[0].subGroup ?? 0) > 0)
                      // F-3: butun katak faqat BO'LINMAGAN darsda bo'yaladi — bo'lingan
                      // katakda G1/G2 nishonlari allaqachon o'z rangini tashiydi va katak
                      // foni ular bilan to'qnashib ketardi.
                      const soleColor =
                        !isSplit && slotLessons.length === 1
                          ? subjectColor(slotLessons[0].subjectId)
                          : null
                      const soleTint = soleColor ? tint(soleColor, 0.14) : undefined
                      return (
                        <td key={day} className="px-1 py-1 align-top">
                          <button
                            type="button"
                            title={
                              slotLessons.length > 0
                                ? 'Tahrirlash uchun bosing'
                                : 'Dars qo‘yish uchun bosing'
                            }
                            onClick={() => setSelected({ day, period, lessons: slotLessons })}
                            style={
                              soleTint
                                ? { backgroundColor: soleTint, borderColor: tint(soleColor!, 0.4) }
                                : undefined
                            }
                            className={cn(
                              'flex min-h-[52px] w-full flex-col justify-center rounded-lg border p-2 text-left transition-colors',
                              slotLessons.length > 0
                                ? cn(
                                    'hover:border-brand-300',
                                    !soleTint && 'border-brand-100 bg-brand-50',
                                  )
                                : 'border-dashed border-slate-200 text-slate-300 hover:border-brand-300 hover:bg-brand-50/40 hover:text-brand-400',
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
                                      <span className="flex items-center gap-1 text-xs font-medium text-slate-800">
                                        {subjectColor(l.subjectId) && (
                                          <span
                                            className="h-1.5 w-1.5 shrink-0 rounded-full"
                                            style={{ backgroundColor: subjectColor(l.subjectId)! }}
                                          />
                                        )}
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

      <LessonSlotModal
        slot={selected}
        subjects={pickableSubjects}
        teachers={teachers}
        occupiedSlots={occupiedSlots}
        timeLabel={selected ? timeLabel(selected.period) : null}
        ownerKind={ownerKind}
        onClose={() => setSelected(null)}
        onSave={handleSave}
        onClear={handleClear}
      />

      <Toast message={toast} onClose={() => setToast(null)} />
    </div>
  )
}
