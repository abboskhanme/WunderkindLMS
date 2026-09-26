/**
 * Umumiy jadval tuzish — hamma sinf bitta jadvalda (mijoz, 2026-09-23: "jadval yaratish
 * bo'limini ham jadvalli qil — hamma sinf va soatlar table sifatida ko'rinib tursin, har
 * katakka bosganda kichik modal ochilib fan va o'qituvchini tahrirlash mumkin bo'lsin").
 *
 * MODEL O'ZGARMAGAN: jadval — har sinfning NOMLI shabloni (masalan "Asosiy"); bu ekran bir
 * xil nomdagi shablonlarni yonma-yon ko'rsatadi. Sinfda shu nomdagi shablon bo'lmasa, u
 * birinchi katak saqlanganda yaratiladi. Katak oynasi — sinf sahifasidagining o'zi
 * (`LessonSlotModal`): bo'linish, o'qituvchi bandligi tekshiruvi, "Tozalash".
 *
 * Qatorlar — sinflar, ustunlar — tanlangan kunning dars soatlari. Bir o'qituvchi bir soatda
 * ikki sinfda turgan katak qizil hoshiya bilan belgilanadi.
 *
 * QORALAMA → NASHR (mijoz, 2026-09-23: "yangi jadval yaratiladi, lekin sinflarga qo'shilmaydi;
 * bitta tugma — nashr qilish; tasdiqlangandan keyin jadval doimiy/faol bo'ladi"):
 *  - FAOL jadval (joriy haftada amal qilayotgani) bu yerda faqat ko'rinadi, tahrirlanmaydi;
 *  - "Yangi jadval" — qoralama (bo'sh yoki faolning nusxasi). Sinflarga ta'sir qilmaydi, chunki
 *    haftaga biriktirilmagan shablon hech qayerda ishlatilmaydi;
 *  - "Nashr qilish" — qoralama JORIY haftadan o'quv yili oxirigacha hamma sinfda qo'yiladi.
 *    O'tgan haftalar tegilmaydi (jurnal, davomat, maosh tarixi o'z jadvali bo'yicha qoladi).
 *
 * QATORLAR — DARS EGALARI (docs/modules/track-groups-as-classes.md): 1–8-sinflar, keyin
 * YO'NALISH guruhlari (9–11-sinflar o'rnida — ularning sinfi bu yerda yo'q), keyin (faqat
 * "Guruh darslari" yoqilganda) oddiy guruhlar. Tartib serverda (`/admin/schedule/owners`).
 */
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { AlertTriangle, CalendarRange, Plus } from 'lucide-react'
import type { LessonOwnerKind, ScheduleLesson, ScheduleTemplate, SchoolSettings, Subject, Teacher } from '@/types'
import { getLessonOwners } from '@/api/services/lessonOwners'
import { getSubjects } from '@/api/services/subjects'
import { getTeachers } from '@/api/services/teachers'
import { getSettings } from '@/api/services/settings'
import {
  clearTemplateSlot,
  createTemplate,
  getOccupiedSlots,
  getTemplates,
  setTemplateCell,
} from '@/api/services/scheduleTemplates'
import { getWeekAssignments, saveWeekAssignments } from '@/api/services/weekAssignments'
import { weekDays, schedulePeriods } from '@/config/constants'
import { getCurrentQuarterAndWeek, getQuarterWeeks } from '@/lib/weeks'
import { periodTimeMap } from '@/components/schedule/periodTimes'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Toast } from '@/components/ui/Toast'
import { cn } from '@/lib/utils'
import { LessonSlotModal, type OccupiedSlots, type SlotTarget } from '../classes/LessonSlotModal'

const DEFAULT_NAME = 'Asosiy'

/** Jadval qatori — sinf yoki guruh (yo'nalish guruhi sinf kabi). */
interface OwnerRow {
  id: string
  name: string
  kind: LessonOwnerKind
  isTrack: boolean
}

const errText = (e: unknown, fallback: string) =>
  (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback

export function MasterScheduleGrid() {
  const [classes, setClasses] = useState<OwnerRow[]>([])
  /** Sinf id → uning hamma shablonlari. */
  const [templates, setTemplates] = useState<Record<string, ScheduleTemplate[]>>({})
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [settings, setSettings] = useState<SchoolSettings | null>(null)
  /** Hamma shablonlardagi band soatlar (hech biri chiqarilmagan) — katak oynasida filtrlanadi. */
  const [occupied, setOccupied] = useState<OccupiedSlots>({})
  const [loading, setLoading] = useState(true)

  const [name, setName] = useState('')
  const [day, setDay] = useState(0)
  const [editing, setEditing] = useState<{ cls: OwnerRow; slot: SlotTarget } | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [toast, setToast] = useState<string | null>(null)
  const closeToast = useCallback(() => setToast(null), [])

  const [newOpen, setNewOpen] = useState(false)
  const [newName, setNewName] = useState('')
  /** Yangi qoralama faol jadvaldan nusxa bilan boshlansinmi. */
  const [copyActive, setCopyActive] = useState(true)
  const [creating, setCreating] = useState(false)
  const [assignOpen, setAssignOpen] = useState(false)
  const [assigning, setAssigning] = useState(false)
  /** Sinf id → joriy haftada amal qilayotgan shablon id'si ("faol" jadval). */
  const [activeNow, setActiveNow] = useState<Record<string, string | null>>({})

  useEffect(() => {
    Promise.all([getLessonOwners(), getSubjects(), getTeachers(), getSettings(), getOccupiedSlots('')])
      .then(async ([owners, subs, tchs, st, occ]) => {
        // Arxivsiz, tartiblangan — server qoidasi (sinflar, yo'nalishlar, oddiy guruhlar).
        const active: OwnerRow[] = owners.map((o) => ({
          id: o.id,
          name: o.name,
          kind: o.kind,
          isTrack: o.isTrack,
        }))
        const tpls = await Promise.all(active.map((c) => getTemplates(c.id).catch(() => [])))
        const map: Record<string, ScheduleTemplate[]> = {}
        active.forEach((c, i) => (map[c.id] = tpls[i]))
        setClasses(active)
        setTemplates(map)
        setSubjects(subs)
        setTeachers(tchs)
        setSettings(st)
        setOccupied(occ)
        // Eng ko'p sinfda uchraydigan nom — sukut bo'yicha ochiladi.
        const counts = new Map<string, number>()
        for (const list of tpls) for (const t of list) counts.set(t.name, (counts.get(t.name) ?? 0) + 1)
        const top = [...counts.entries()].sort((a, b) => b[1] - a[1])[0]?.[0]
        setName(top ?? DEFAULT_NAME)
        // Joriy haftada qaysi shablon amal qilyapti — "Faol" belgisi shundan.
        const { quarter: cq, week: cw } = getCurrentQuarterAndWeek(st.quarters)
        const wa = await Promise.all(active.map((c) => getWeekAssignments(c.id, cq).catch(() => [])))
        const now: Record<string, string | null> = {}
        active.forEach((c, i) => (now[c.id] = wa[i].find((a) => a.week === cw)?.templateId ?? null))
        setActiveNow(now)
      })
      .catch((e) => setError(errText(e, "Ma'lumotni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [])

  /** Hamma sinflardagi shablon nomlari (+ hozir yaratilayotgani). */
  const names = useMemo(() => {
    const set = new Set<string>()
    Object.values(templates).forEach((l) => l.forEach((t) => set.add(t.name)))
    if (name) set.add(name)
    return [...set].sort((a, b) => a.localeCompare(b, 'uz'))
  }, [templates, name])

  const templateOf = (classId: string) => templates[classId]?.find((t) => t.name === name) ?? null
  const lessonsAt = (classId: string, period: number) =>
    (templateOf(classId)?.lessons ?? []).filter((l) => l.day === day && l.period === period)

  // Ko'rsatiladigan soatlar: sozlamadagi dars vaqtlari; bo'lmasa — 1..8, band bo'lgan eng katta soatgacha.
  const periodTimes = useMemo(() => periodTimeMap(settings), [settings])
  const periods = useMemo(() => {
    const used = Math.max(
      0,
      ...Object.values(templates).flatMap((l) => l.flatMap((t) => t.lessons.map((x) => x.period))),
    )
    const configured = Math.max(0, ...periodTimes.keys())
    const n = Math.min(schedulePeriods.length, Math.max(configured || 8, used))
    return schedulePeriods.slice(0, n)
  }, [templates, periodTimes])
  const timeLabel = (p: number) => {
    const t = periodTimes.get(p)
    return t ? `${t.start}–${t.end}` : null
  }

  const subjectOf = (id: string) => subjects.find((s) => s.id === id)
  const teacherName = (id: string) => teachers.find((t) => t.id === id)?.fullName ?? ''
  const shortName = (full: string) => {
    const [last, first] = full.split(' ')
    return first ? `${last} ${first[0]}.` : last ?? ''
  }

  /** (soat → o'qituvchi → nechta sinf) — shu kun, shu jadval bo'yicha to'qnashuvlar. */
  const clashes = useMemo(() => {
    const m = new Map<string, number>()
    for (const c of classes)
      for (const l of templates[c.id]?.find((t) => t.name === name)?.lessons ?? [])
        if (l.day === day && l.teacherId) m.set(`${l.period}|${l.teacherId}`, (m.get(`${l.period}|${l.teacherId}`) ?? 0) + 1)
    return m
  }, [classes, templates, name, day])

  /**
   * Katak oynasi uchun band soatlar — FAQAT shu jadvalning boshqa sinflaridagi darslar.
   * Qoralama hozirgi faol jadval bilan bir vaqtda ishlamaydi: o'qituvchi faol jadvalda shu
   * soatda dars bersa ham, bu qoralama uchun u band EMAS (aks holda soxta ogohlantirish).
   */
  const occupiedFor = (cls: OwnerRow): OccupiedSlots => {
    const out: OccupiedSlots = {}
    for (const [tid, list] of Object.entries(occupied))
      out[tid] = list.filter((o) => o.templateName === name && o.className !== cls.name)
    return out
  }

  const replaceLocal = (classId: string, tpl: ScheduleTemplate) =>
    setTemplates((prev) => ({
      ...prev,
      [classId]: [...(prev[classId] ?? []).filter((t) => t.id !== tpl.id), tpl],
    }))

  const refreshOccupied = () => getOccupiedSlots('').then(setOccupied).catch(() => undefined)

  const handleSave = async (d: number, period: number, next: ScheduleLesson[]) => {
    if (!editing) return
    const cls = editing.cls
    setEditing(null)
    setError(null)
    try {
      // Sinfda shu nomdagi shablon yo'q bo'lsa — birinchi katakda yaratiladi.
      let tpl = templateOf(cls.id)
      if (!tpl) tpl = await createTemplate(cls.id, name)
      await setTemplateCell(cls.id, tpl.id, d, period, next)
      replaceLocal(cls.id, {
        ...tpl,
        lessons: [...tpl.lessons.filter((l) => !(l.day === d && l.period === period)), ...next],
      })
      setToast(`${cls.name} · ${weekDays[d]} · ${period}-soat saqlandi`)
      void refreshOccupied()
    } catch (e) {
      setError(errText(e, "Darsni saqlab bo'lmadi"))
    }
  }

  const handleClear = async (d: number, period: number) => {
    if (!editing) return
    const cls = editing.cls
    const tpl = templateOf(cls.id)
    setEditing(null)
    if (!tpl) return
    setError(null)
    try {
      await clearTemplateSlot(cls.id, tpl.id, d, period)
      replaceLocal(cls.id, { ...tpl, lessons: tpl.lessons.filter((l) => !(l.day === d && l.period === period)) })
      setToast(`${cls.name} · ${weekDays[d]} · ${period}-soat tozalandi`)
      void refreshOccupied()
    } catch (e) {
      setError(errText(e, "Darsni o'chirib bo'lmadi"))
    }
  }

  /**
   * Yangi qoralama. Nusxa tanlansa — har sinfning FAOL shablonidagi darslar yangi nomdagi
   * shablonga ko'chiriladi (katakma-katak). Sinflarga ta'sir qilmaydi: haftaga biriktirilmagan.
   */
  const createDraft = async () => {
    const draft = newName.trim()
    if (!draft) return
    setCreating(true)
    setError(null)
    try {
      if (copyActive) {
        await Promise.all(
          classes.map(async (c) => {
            const src = (templates[c.id] ?? []).find((t) => t.id === activeNow[c.id])
            if (!src || src.lessons.length === 0) return
            const tpl = await createTemplate(c.id, draft)
            const cells = new Map<string, ScheduleLesson[]>()
            for (const l of src.lessons) {
              const k = `${l.day}|${l.period}`
              cells.set(k, [...(cells.get(k) ?? []), { ...l }])
            }
            for (const [k, lessons] of cells) {
              const [d, p] = k.split('|').map(Number)
              await setTemplateCell(c.id, tpl.id, d, p, lessons)
            }
            replaceLocal(c.id, { ...tpl, lessons: [...cells.values()].flat() })
          }),
        )
      }
      setName(draft)
      setNewOpen(false)
      setToast(`"${draft}" qoralamasi yaratildi — sinflarga hali ta'sir qilmaydi`)
    } catch (e) {
      setError(errText(e, "Yangi jadvalni yaratib bo'lmadi"))
    } finally {
      setCreating(false)
    }
  }

  /**
   * "Nashr qilish" (mijoz, 2026-09-23: "chorakka bo'lib o'tirishi shart emas — aktiv holatdagi
   * jadval sifatida qabul qilinsa bo'ldi"). Jadval JORIY haftadan o'quv yili oxirigacha
   * hamma sinfda qo'yiladi; o'tgan haftalar TEGILMAYDI — jurnal, davomat va maosh tarixi
   * o'sha haftalarning jadvali bo'yicha qoladi. Ichkarida bu oddiy hafta biriktirish.
   */
  const makeActive = async () => {
    if (!settings) return
    const { quarter: cq, week: cw } = getCurrentQuarterAndWeek(settings.quarters)
    const plan = settings.quarters
      .filter((q) => q.quarter >= cq && q.startDate && q.endDate)
      .map((q) => ({
        quarter: q.quarter,
        weeks: getQuarterWeeks(q.startDate, q.endDate)
          .map((w) => w.week)
          .filter((w) => q.quarter > cq || w >= cw),
      }))
    const targets = classes.filter((c) => templateOf(c.id))
    setAssigning(true)
    setError(null)
    try {
      for (const c of targets) {
        const tplId = templateOf(c.id)!.id
        for (const { quarter, weeks } of plan) {
          if (weeks.length === 0) continue
          const current = await getWeekAssignments(c.id, quarter)
          const byWeek = new Map(current.map((a) => [a.week, a.templateId]))
          weeks.forEach((w) => byWeek.set(w, tplId))
          await saveWeekAssignments(
            c.id,
            quarter,
            [...byWeek.entries()].map(([week, templateId]) => ({ week, templateId })),
          )
        }
      }
      setActiveNow((prev) => {
        const next = { ...prev }
        targets.forEach((c) => (next[c.id] = templateOf(c.id)!.id))
        return next
      })
      setAssignOpen(false)
      setToast(`"${name}" nashr qilindi — endi faol jadval (${targets.length} ta sinf)`)
    } catch (e) {
      setError(errText(e, "Jadvalni nashr qilib bo'lmadi"))
    } finally {
      setAssigning(false)
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  const withTemplate = classes.filter((c) => templateOf(c.id)).length
  const activeCount = classes.filter((c) => {
    const t = templateOf(c.id)
    return t && activeNow[c.id] === t.id
  }).length
  const isActive = withTemplate > 0 && activeCount === withTemplate
  /** Faol jadval (qisman bo'lsa ham) bu yerda tahrirlanmaydi — o'zgarish darhol darslarga tushardi. */
  const locked = activeCount > 0
  const pickable = subjects.filter((s) => s.isActive !== false)

  return (
    <div className="space-y-4">
      {/* Boshqaruv qatori */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <select
            value={name}
            onChange={(e) => setName(e.target.value)}
            className="h-10 rounded-xl border border-slate-200 bg-white px-3 text-sm font-medium text-slate-700 outline-none focus:border-brand-400"
            aria-label="Jadval"
          >
            {names.map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
          </select>
          <Button variant="secondary" onClick={() => { setNewName(''); setNewOpen(true) }}>
            <Plus className="h-4 w-4" /> Yangi jadval
          </Button>
          {withTemplate > 0 && (
            <span
              className={cn(
                'rounded-full px-2.5 py-1 text-xs font-medium',
                isActive
                  ? 'bg-emerald-50 text-emerald-700'
                  : activeCount > 0
                    ? 'bg-amber-50 text-amber-700'
                    : 'bg-slate-100 text-slate-500',
              )}
              title={locked ? "Hozir amal qilayotgan jadval" : "Nashr qilinmagan — sinflarga ta'sir qilmaydi"}
            >
              {isActive ? 'Faol' : activeCount > 0 ? `Qisman faol · ${activeCount}/${withTemplate}` : 'Qoralama'}
            </span>
          )}
          <span className="text-xs text-slate-400">
            {withTemplate} / {classes.length} sinfda bor
          </span>
        </div>
        {!locked && (
          <Button onClick={() => setAssignOpen(true)} disabled={withTemplate === 0}>
            <CalendarRange className="h-4 w-4" /> Nashr qilish
          </Button>
        )}
      </div>

      {/* Hafta kunlari */}
      <div className="flex w-fit flex-wrap gap-1 rounded-xl bg-slate-100 p-1">
        {weekDays.map((d, i) => (
          <button
            key={d}
            type="button"
            onClick={() => setDay(i)}
            className={cn(
              'rounded-lg px-3 py-1.5 text-sm font-medium transition-colors',
              day === i ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
            )}
          >
            {d}
          </button>
        ))}
      </div>

      {error && (
        <div className="flex items-start gap-2 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" /> {error}
        </div>
      )}

      {locked ? (
        <div className="rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-800">
          Bu — <b>faol jadval</b>, hozir sinflarda amal qilyapti va bu yerda o'zgartirilmaydi.
          O'zgartirish uchun <b>"Yangi jadval"</b> yarating (faol jadvaldan nusxa olish mumkin),
          tahrirlang va tayyor bo'lgach <b>"Nashr qilish"</b>ni bosing.
        </div>
      ) : (
        withTemplate > 0 || name ? (
          <div className="rounded-xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-600">
            <b>Qoralama.</b> O'zgarishlar sinflarga ta'sir qilmaydi — "Nashr qilish" bosilmaguncha
            hech qayerda ko'rinmaydi.
          </div>
        ) : null
      )}

      <Card className="overflow-hidden p-0">
        <div className="overflow-x-auto">
          <table className="w-max min-w-full border-separate border-spacing-0 text-sm">
            <thead>
              <tr>
                <th className="sticky left-0 z-20 min-w-[7rem] border-b border-r border-slate-200 bg-slate-50 px-3 py-2 text-left text-xs font-medium uppercase tracking-wide text-slate-500">
                  Sinf
                </th>
                {periods.map((p) => (
                  <th key={p} className="min-w-[9.5rem] border-b border-l border-slate-100 bg-slate-50 px-2 py-2 text-center font-normal">
                    <div className="text-xs font-semibold text-slate-700">{p}-soat</div>
                    {timeLabel(p) && <div className="text-[10px] tabular-nums text-slate-400">{timeLabel(p)}</div>}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {classes.map((c) => (
                <tr key={c.id} className="group">
                  <td className="sticky left-0 z-10 border-b border-r border-slate-200 bg-white px-3 py-2 group-hover:bg-slate-50">
                    <Link
                      to={`/admin/schedule/manage/${c.id}`}
                      className="font-semibold text-slate-800 hover:text-brand-600"
                      title={c.kind === 'group' ? 'Guruh jadvali — shablonlar va haftalar' : 'Sinf jadvali — shablonlar va haftalar'}
                    >
                      {c.name}
                    </Link>
                    {c.kind === 'group' && (
                      <span className="block text-[10px] font-medium uppercase tracking-wide text-slate-400">
                        {c.isTrack ? "yo'nalish" : 'guruh'}
                      </span>
                    )}
                  </td>
                  {periods.map((p) => {
                    const lessons = lessonsAt(c.id, p)
                    const clash = lessons.some((l) => l.teacherId && (clashes.get(`${p}|${l.teacherId}`) ?? 0) > 1)
                    return (
                      <td key={p} className="border-b border-l border-slate-100 p-1">
                        <button
                          type="button"
                          disabled={locked}
                          onClick={() => setEditing({ cls: c, slot: { day, period: p, lessons } })}
                          title={clash ? "O'qituvchi shu soatda boshqa sinfda ham bor" : `${c.name} · ${p}-soat`}
                          className={cn(
                            'flex h-14 w-full flex-col items-stretch justify-center gap-0.5 rounded-lg px-2 text-left transition-colors',
                            locked
                              ? 'cursor-default text-slate-200'
                              : lessons.length === 0
                                ? 'text-slate-300 hover:bg-brand-50/60'
                                : 'hover:brightness-95',
                            clash && 'ring-2 ring-red-400',
                          )}
                        >
                          {lessons.length === 0 ? (
                            <span className="text-center text-lg leading-none">{locked ? '' : '+'}</span>
                          ) : (
                            [...lessons]
                              .sort((a, b) => (a.subGroup ?? 0) - (b.subGroup ?? 0))
                              .map((l, i) => {
                                const s = subjectOf(l.subjectId)
                                return (
                                  <span
                                    key={i}
                                    className="block truncate rounded-md px-1.5 py-0.5"
                                    style={{ backgroundColor: s?.color ? `${s.color}1F` : '#f1f5f9' }}
                                  >
                                    <span className="block truncate text-xs font-semibold text-slate-800" style={s?.color ? { color: s.color } : undefined}>
                                      {(l.subGroup ?? 0) > 0 && <span className="mr-1 text-[10px] text-slate-500">G{l.subGroup}</span>}
                                      {s?.name ?? '—'}
                                    </span>
                                    {lessons.length === 1 && l.teacherId && (
                                      <span className="block truncate text-[11px] text-slate-500">{shortName(teacherName(l.teacherId))}</span>
                                    )}
                                  </span>
                                )
                              })
                          )}
                        </button>
                      </td>
                    )
                  })}
                </tr>
              ))}
              {classes.length === 0 && (
                <tr>
                  <td colSpan={periods.length + 1} className="px-4 py-10 text-center text-slate-400">
                    Sinflar yo'q
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </Card>

      <LessonSlotModal
        slot={editing?.slot ?? null}
        subjects={pickable}
        teachers={teachers}
        occupiedSlots={editing ? occupiedFor(editing.cls) : {}}
        timeLabel={editing ? timeLabel(editing.slot.period) : null}
        ownerKind={editing?.cls.kind ?? 'class'}
        onClose={() => setEditing(null)}
        onSave={(d, p, lessons) => void handleSave(d, p, lessons)}
        onClear={(d, p) => void handleClear(d, p)}
      />

      <Modal
        open={newOpen}
        onClose={() => setNewOpen(false)}
        title="Yangi jadval"
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setNewOpen(false)}>
              Bekor qilish
            </Button>
            <Button
              disabled={!newName.trim() || names.includes(newName.trim()) || creating}
              onClick={() => void createDraft()}
            >
              {creating ? 'Yaratilmoqda...' : 'Yaratish'}
            </Button>
          </>
        }
      >
        <label className="block text-sm">
          <span className="mb-1 block font-medium text-slate-600">Jadval nomi</span>
          <input
            autoFocus
            value={newName}
            onChange={(e) => setNewName(e.target.value)}
            placeholder="Masalan: 2-chorak jadvali"
            className="h-10 w-full rounded-xl border border-slate-200 px-3 outline-none focus:border-brand-400"
          />
        </label>
        {names.includes(newName.trim()) && <p className="mt-1 text-xs text-amber-700">Bu nomdagi jadval bor</p>}
        <label className="mt-3 flex items-start gap-2 text-sm text-slate-700">
          <input
            type="checkbox"
            checked={copyActive}
            onChange={(e) => setCopyActive(e.target.checked)}
            className="mt-0.5 h-4 w-4 accent-brand-600"
          />
          <span>
            Faol jadvaldan nusxa olish
            <span className="block text-xs text-slate-400">
              Kichik o'zgarish uchun — hammasini qaytadan to'ldirish shart emas.
            </span>
          </span>
        </label>
        <p className="mt-3 text-xs text-slate-400">
          Yangi jadval qoralama bo'lib turadi — "Nashr qilish" bosilmaguncha sinflarga ta'sir qilmaydi.
        </p>
      </Modal>

      <Modal
        open={assignOpen}
        onClose={() => setAssignOpen(false)}
        title="Jadvalni nashr qilish"
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setAssignOpen(false)}>
              Bekor qilish
            </Button>
            <Button onClick={() => void makeActive()} disabled={assigning}>
              {assigning ? 'Nashr qilinmoqda...' : 'Nashr qilish'}
            </Button>
          </>
        }
      >
        <p className="text-sm text-slate-600">
          Nashr qilingach <b>"{name}"</b> doimiy (faol) jadvalga aylanadi va <b>shu haftadan boshlab</b>{' '}
          {withTemplate} ta sinfda amal qiladi: dars jadvali, jurnal, davomat va Mini App shu bo'yicha ishlaydi.
        </p>
        <p className="mt-2 text-xs text-slate-400">
          Hozirgi faol jadval o'tgan haftalar uchun saqlanadi — u haftalarning jurnali, davomati va maoshi
          o'zgarmaydi.
        </p>
      </Modal>

      <Toast message={toast} onClose={closeToast} />
    </div>
  )
}
