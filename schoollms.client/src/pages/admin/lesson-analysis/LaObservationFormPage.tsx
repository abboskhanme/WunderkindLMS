import { useCallback, useMemo, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { ArrowLeft, CalendarClock, Check, Dices, MessageSquareText, Sparkles, X } from 'lucide-react'
import {
  completeObservation,
  createObservation,
  getCriteria,
  getObservation,
  getRandomTeacher,
  getTeacherLessons,
  getTeachers,
  getTemplates,
  laErrorMessage,
  updateObservation,
} from '@/api/services/lessonAnalysis'
import type {
  LaObservation,
  LaObservationInput,
  LaSection,
  LaTeacherLesson,
  LaTeacherSummary,
  LaTemplate,
} from '@/types/lessonAnalysis'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { DatePicker } from '@/components/ui/DatePicker'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Toast } from '@/components/ui/Toast'
import { todayIso } from '@/components/lesson-analysis/format'
import { Notice } from '@/components/lesson-analysis/ui'
import { useLoad } from '@/components/lesson-analysis/useLoad'
import { cn } from '@/lib/utils'
import { laPaths } from './paths'

type Answer = { isDone: boolean | null; comment: string }

const fieldCls =
  'w-full rounded-lg border border-slate-200 bg-white px-3 py-2.5 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100'

/** "08:30:00" → "08:30" (TimeSpan yoki "HH:mm"). */
const hhmm = (t: string | null) => (t ? t.slice(0, 5) : '')

/** Jadvaldagi dars → "Dars vaqti" maydoni ("09:00 – 09:45", vaqt bo'lmasa "3-dars"). */
function lessonTimeText(lesson: LaTeacherLesson): string {
  const start = hhmm(lesson.startTime)
  const end = hhmm(lesson.endTime)
  if (start && end) return `${start} – ${end}`
  return `${lesson.period}-dars`
}

/**
 * Yangi tahlil / tahrirlash — `tahlillar/yangi?teacherId=`, `tahlillar/:id/tahrir`.
 * Ma'lumot yuklangach forma `key` bilan bir marta quriladi — boshlang'ich qiymatlar
 * effekt orqali emas, to'g'ridan-to'g'ri `useState` ga beriladi.
 */
export function LaObservationFormPage() {
  const { id } = useParams()
  const [searchParams] = useSearchParams()
  const presetTeacher = searchParams.get('teacherId')

  const templates = useLoad('la-templates', () => getTemplates())
  const teachers = useLoad('teachers', () => getTeachers())
  const observation = useLoad(id ? `obs|${id}` : null, () => getObservation(id!))
  // Tahlil varag'i (mijoz, 2026-10-07): yangi tahlilda standart varaq, mavjudida o'zining varag'i.
  const [chosenSheet, setChosenSheet] = useState<string | null>(null)
  const sheetId =
    chosenSheet ??
    observation.data?.templateId ??
    templates.data?.find((t) => t.isDefault)?.id ??
    null
  const ready = templates.data && (!id || observation.data)
  const criteria = useLoad(ready && sheetId ? `criteria|${sheetId}` : null, () => getCriteria(false, sheetId ?? undefined))

  const error = criteria.error ?? teachers.error ?? observation.error ?? templates.error
  if (error) {
    return (
      <div className="space-y-3">
        <BackButton />
        <Notice>{error}</Notice>
      </div>
    )
  }
  if (!criteria.data || !teachers.data || !templates.data || (id && !observation.data)) return <Loader label="Yuklanmoqda..." />

  if (observation.data && observation.data.status === 'completed' && !observation.data.canEdit) {
    return (
      <div className="space-y-3">
        <BackButton />
        <Notice>Yakunlangan tahlilni faqat uning kuzatuvchisi yoki administrator tahrirlay oladi.</Notice>
        <Link to={laPaths.detail(observation.data.id)} className="text-sm font-medium text-brand-600 hover:underline">
          Tahlilni ko‘rish
        </Link>
      </div>
    )
  }

  return (
    <ObservationForm
      key={id ?? 'new'}
      sections={criteria.data}
      teachers={teachers.data}
      initial={observation.data}
      presetTeacherId={id ? null : presetTeacher}
      templates={templates.data.filter((t) => t.isActive || t.id === sheetId)}
      templateId={sheetId ?? ''}
      // Another sheet is another checklist: only before the first answer (the server agrees).
      onTemplateChange={observation.data && observation.data.details.length > 0 ? undefined : setChosenSheet}
    />
  )
}

function BackButton() {
  const navigate = useNavigate()
  return (
    <Button
      variant="secondary"
      className="shrink-0 px-3 py-1.5"
      onClick={() => (window.history.length > 1 ? navigate(-1) : navigate(laPaths.list))}
    >
      <ArrowLeft className="h-4 w-4" />
      Orqaga
    </Button>
  )
}

function ObservationForm({
  sections,
  teachers,
  initial,
  presetTeacherId,
  templates,
  templateId,
  onTemplateChange,
}: {
  sections: LaSection[]
  teachers: LaTeacherSummary[]
  initial: LaObservation | null
  presetTeacherId: string | null
  templates: LaTemplate[]
  templateId: string
  onTemplateChange?: (id: string) => void
}) {
  const navigate = useNavigate()
  const preset = presetTeacherId ? teachers.find((t) => t.id === presetTeacherId) : undefined
  const singleSubject = (s: string | null | undefined) => (s && !s.includes(',') ? s : '')

  const [answers, setAnswers] = useState<Record<string, Answer>>(() =>
    Object.fromEntries(
      (initial?.details ?? []).map((d) => [d.criterionId, { isDone: d.isDone, comment: d.comment ?? '' }]),
    ),
  )
  const [openComment, setOpenComment] = useState<string | null>(null)

  const [teacherId, setTeacherId] = useState(initial?.teacherId ?? preset?.id ?? '')
  const [lessonDate, setLessonDate] = useState(() => initial?.lessonDate ?? todayIso())
  const [lessonTime, setLessonTime] = useState(initial?.lessonTime ?? '')
  const [className, setClassName] = useState(initial?.className ?? '')
  const [subject, setSubject] = useState(initial?.subject ?? singleSubject(preset?.subject))
  const [strengths, setStrengths] = useState(initial?.strengths ?? '')
  const [growthAreas, setGrowthAreas] = useState(initial?.growthAreas ?? '')
  const [advice, setAdvice] = useState(initial?.nextLessonAdvice ?? '')

  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [saving, setSaving] = useState<'draft' | 'complete' | null>(null)
  const [observationId, setObservationId] = useState<string | null>(initial?.id ?? null)
  // yakunlangan tahlil xatoni tuzatish uchun tahrirlanadi — faqat tugmalar o'zgaradi
  const completed = initial?.status === 'completed'

  const clearError = useCallback(() => setError(null), [])
  const clearNotice = useCallback(() => setNotice(null), [])

  const lessons = useLoad(
    teacherId && lessonDate ? `lessons|${teacherId}|${lessonDate}` : null,
    () => getTeacherLessons(teacherId, lessonDate),
  )

  const allCriteria = useMemo(() => sections.flatMap((s) => s.criteria), [sections])
  // qog'oz shakl 1..N ni bo'limlar bo'yicha qayta boshlamay raqamlaydi
  const numbers = useMemo(() => new Map(allCriteria.map((c, index) => [c.id, index + 1])), [allCriteria])

  const answered = allCriteria.filter((c) => answers[c.id]?.isDone != null).length
  const doneCount = allCriteria.filter((c) => answers[c.id]?.isDone === true).length
  const progress = allCriteria.length ? Math.round((answered / allCriteria.length) * 100) : 0

  function setAnswer(criterionId: string, patch: Partial<Answer>) {
    setAnswers((prev) => ({
      ...prev,
      [criterionId]: {
        isDone: prev[criterionId]?.isDone ?? null,
        comment: prev[criterionId]?.comment ?? '',
        ...patch,
      },
    }))
  }

  function markAllDone() {
    setAnswers((prev) => {
      const next = { ...prev }
      for (const criterion of allCriteria) {
        next[criterion.id] = { isDone: true, comment: prev[criterion.id]?.comment ?? '' }
      }
      return next
    })
  }

  function pickTeacher(value: string) {
    setTeacherId(value)
    const teacher = teachers.find((t) => t.id === value)
    const own = singleSubject(teacher?.subject)
    if (own && !subject) setSubject(own)
  }

  function applyLesson(lesson: LaTeacherLesson) {
    setClassName(lesson.className)
    setSubject(lesson.subjectName)
    setLessonTime(lessonTimeText(lesson))
  }

  function payload(): LaObservationInput {
    return {
      teacherId,
      lessonDate,
      lessonTime: lessonTime.trim() || null,
      className: className.trim() || null,
      subject: subject.trim() || null,
      strengths: strengths.trim() || null,
      growthAreas: growthAreas.trim() || null,
      nextLessonAdvice: advice.trim() || null,
      templateId: templateId || null,
      // ko'rsatilgan har bir mezon yuboriladi, tozalangani null bilan — server eski belgini tashlaydi
      details: allCriteria.map((c) => ({
        criterionId: c.id,
        isDone: answers[c.id]?.isDone ?? null,
        comment: answers[c.id]?.comment.trim() || null,
      })),
    }
  }

  async function save(): Promise<string> {
    if (!teacherId) throw new Error('O‘qituvchini tanlang')
    if (!lessonDate) throw new Error('Dars sanasini tanlang')
    if (observationId) {
      await updateObservation(observationId, payload())
      return observationId
    }
    const created = await createObservation(payload())
    setObservationId(created.id)
    return created.id
  }

  async function handleSaveDraft() {
    setError(null)
    setSaving('draft')
    try {
      await save()
      setNotice('Qoralama saqlandi — keyinroq davom ettirishingiz mumkin.')
    } catch (err) {
      setError(laErrorMessage(err, 'Saqlashda xatolik'))
    } finally {
      setSaving(null)
    }
  }

  async function handleSaveCompleted() {
    setError(null)
    if (answered === 0) {
      setError('Kamida bitta mezonni belgilang')
      return
    }
    setSaving('complete')
    try {
      const savedId = await save()
      navigate(laPaths.detail(savedId), { replace: true })
    } catch (err) {
      setError(laErrorMessage(err, 'Saqlashda xatolik'))
    } finally {
      setSaving(null)
    }
  }

  async function handleComplete() {
    setError(null)
    if (answered === 0) {
      setError('Kamida bitta mezonni belgilang')
      return
    }
    setSaving('complete')
    try {
      const savedId = await save()
      await completeObservation(savedId)
      navigate(laPaths.detail(savedId), { replace: true })
    } catch (err) {
      setError(laErrorMessage(err, 'Yakunlashda xatolik'))
    } finally {
      setSaving(null)
    }
  }

  async function pickRandomTeacher() {
    setError(null)
    try {
      const teacher = await getRandomTeacher()
      setTeacherId(teacher.id)
      const own = singleSubject(teacher.subject)
      if (own) setSubject(own)
      setNotice(`Tasodifiy tanlandi: ${teacher.fullName}`)
    } catch (err) {
      setError(laErrorMessage(err, 'Tanlashda xatolik'))
    }
  }

  const lessonList = lessons.data ?? []
  const commentCriterion = openComment ? allCriteria.find((c) => c.id === openComment) : undefined

  return (
    <div className="space-y-4">
      <div className="flex items-start justify-between gap-4">
        <div className="min-w-0">
          <h2 className="text-lg font-semibold text-slate-800">
            {initial ? 'Dars tahlilini tahrirlash' : 'Yangi dars tahlili'}
          </h2>
          <p className="text-sm text-slate-400">Har bir mezonni «Bajarildi» yoki «Bajarilmadi» deb belgilang</p>
        </div>
        <BackButton />
      </div>

      {completed && (
        <Notice kind="info">
          Bu tahlil yakunlangan. O‘zgartirsangiz AI xulosani qayta yaratish kerak bo‘ladi.
        </Notice>
      )}

      {/* sarlavha maydonlari — qog'oz shakldagidek */}
      <Card className="space-y-4">
        {templates.length > 1 && (
          <label className="block max-w-md">
            <span className="mb-1 block text-sm font-medium text-slate-600">Tahlil varag‘i</span>
            <select
              className={fieldCls}
              value={templateId}
              disabled={!onTemplateChange}
              onChange={(e) => onTemplateChange?.(e.target.value)}
              aria-label="Tahlil varag‘i"
            >
              {templates.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.name}
                  {t.isDefault ? ' (standart)' : ''}
                </option>
              ))}
            </select>
            {!onTemplateChange && (
              <span className="mt-1 block text-xs text-slate-400">Javob berilgan tahlilning varag‘i almashmaydi.</span>
            )}
          </label>
        )}
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-6">
          <div className="md:col-span-2">
            <span className="mb-1 block text-sm font-medium text-slate-600">
              O‘qituvchi<span className="text-red-500"> *</span>
            </span>
            <div className="flex gap-2">
              <select
                className={fieldCls}
                value={teacherId}
                onChange={(e) => pickTeacher(e.target.value)}
                aria-label="O‘qituvchi"
              >
                <option value="">— tanlang —</option>
                {teachers.map((teacher) => (
                  <option key={teacher.id} value={teacher.id}>
                    {teacher.fullName}
                  </option>
                ))}
              </select>
              <button
                type="button"
                onClick={pickRandomTeacher}
                title="Tasodifiy o‘qituvchi"
                className="inline-flex shrink-0 items-center gap-2 rounded-lg border border-slate-200 bg-white px-3 text-sm font-medium text-slate-600 transition-colors hover:bg-slate-50"
              >
                <Dices className="h-5 w-5" />
                <span className="hidden sm:inline">Tasodifiy tanlash</span>
                <span className="sr-only sm:hidden">Tasodifiy tanlash</span>
              </button>
            </div>
          </div>

          <DatePicker label="Sana" required value={lessonDate} onChange={setLessonDate} />

          <label className="block">
            <span className="mb-1 block text-sm font-medium text-slate-600">Dars vaqti</span>
            <input
              className={fieldCls}
              value={lessonTime}
              onChange={(e) => setLessonTime(e.target.value)}
              placeholder="09:00 – 09:45"
              maxLength={100}
            />
          </label>

          <label className="block">
            <span className="mb-1 block text-sm font-medium text-slate-600">Sinf</span>
            <input
              className={fieldCls}
              value={className}
              onChange={(e) => setClassName(e.target.value)}
              placeholder="5-A"
              list="la-class-options"
              maxLength={100}
            />
          </label>
          <label className="block">
            <span className="mb-1 block text-sm font-medium text-slate-600">Fan</span>
            <input
              className={fieldCls}
              value={subject}
              onChange={(e) => setSubject(e.target.value)}
              placeholder="Matematika"
              list="la-subject-options"
              maxLength={200}
            />
          </label>
        </div>

        <datalist id="la-class-options">
          {[...new Set(lessonList.map((l) => l.className))].map((name) => (
            <option key={name} value={name} />
          ))}
        </datalist>
        <datalist id="la-subject-options">
          {[...new Set(lessonList.map((l) => l.subjectName))].map((name) => (
            <option key={name} value={name} />
          ))}
        </datalist>

        {/* tanlangan o'qituvchining shu kungi darslari — jadvaldan (SPEC §7) */}
        {teacherId && (
          <div className="rounded-xl bg-slate-50 p-3">
            <p className="flex items-center gap-2 text-xs font-medium uppercase tracking-wide text-slate-400">
              <CalendarClock className="h-4 w-4" />
              Shu kungi darslari
            </p>
            {lessons.loading ? (
              <p className="mt-2 text-sm text-slate-400">Jadval yuklanmoqda…</p>
            ) : lessons.error ? (
              <p className="mt-2 text-sm text-slate-400">
                Jadvaldan darslar olinmadi — sinf, fan va vaqtni qo‘lda kiriting.
              </p>
            ) : lessonList.length === 0 ? (
              <p className="mt-2 text-sm text-slate-400">
                Bu kunda jadvalda darsi yo‘q — sinf, fan va vaqtni qo‘lda kiriting.
              </p>
            ) : (
              <div className="mt-2 flex flex-wrap gap-2">
                {lessonList.map((lesson, index) => {
                  const active =
                    className === lesson.className &&
                    subject === lesson.subjectName &&
                    lessonTime === lessonTimeText(lesson)
                  return (
                    <button
                      key={`${lesson.period}-${index}`}
                      type="button"
                      onClick={() => applyLesson(lesson)}
                      className={cn(
                        'min-h-11 rounded-xl border px-3 py-2 text-left text-sm transition-colors',
                        active
                          ? 'border-brand-400 bg-brand-50 text-brand-800'
                          : 'border-slate-200 bg-white text-slate-700 hover:border-brand-300',
                      )}
                    >
                      <span className="font-semibold">{lesson.period}-dars</span>
                      <span className="text-slate-400"> · </span>
                      {lesson.className} · {lesson.subjectName}
                      {lesson.startTime && (
                        <span className="ml-1 tabular-nums text-slate-400">{lessonTimeText(lesson)}</span>
                      )}
                    </button>
                  )
                })}
              </div>
            )}
          </div>
        )}
      </Card>

      {/* jarayon */}
      <Card className="sticky top-0 z-20 p-4">
        <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1">
          <span className="text-sm font-medium tabular-nums text-slate-800">
            {answered} / {allCriteria.length} belgilandi
          </span>
          <span className="text-xs tabular-nums text-slate-500">
            Bajarildi <strong className="text-emerald-600">{doneCount}</strong> · Bajarilmadi{' '}
            <strong className="text-red-600">{answered - doneCount}</strong>
          </span>
        </div>
        <div className="mt-2.5 h-1.5 overflow-hidden rounded-full bg-slate-100">
          <div className="h-full rounded-full bg-brand-500 transition-all" style={{ width: `${progress}%` }} />
        </div>
        {!completed && answered === 0 && (
          <button type="button" onClick={markAllDone} className="mt-2.5 text-sm font-medium text-brand-600 hover:underline">
            Hammasini «Bajarildi» deb belgilash
          </button>
        )}
      </Card>

      {/* mezonlar */}
      {sections.map((section) => (
        <Card key={section.id} className="overflow-hidden p-0">
          <header
            className="px-5 py-3 text-[13px] font-semibold uppercase tracking-wide text-white"
            style={{ backgroundColor: section.color }}
          >
            {section.title}
          </header>
          <ul className="divide-y divide-slate-100">
            {section.criteria.map((criterion) => {
              const answer = answers[criterion.id]
              return (
                <li key={criterion.id} className="flex flex-col gap-2.5 px-4 py-3 sm:flex-row sm:items-start sm:gap-3 sm:px-5">
                  <div className="flex min-w-0 flex-1 items-start gap-3">
                    <span className="mt-0.5 w-6 shrink-0 text-xs font-medium tabular-nums text-slate-400">
                      {numbers.get(criterion.id)}
                    </span>
                    <div className="min-w-0 flex-1">
                      <p className="text-[15px] leading-snug text-slate-800">{criterion.text}</p>
                      {answer?.comment && (
                        <p className="mt-1.5 flex items-start gap-1.5 rounded-lg bg-slate-50 px-2.5 py-1.5 text-xs text-slate-600">
                          <MessageSquareText className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                          {answer.comment}
                        </p>
                      )}
                    </div>
                  </div>
                  <div className="flex shrink-0 gap-2 pl-9 sm:pl-0">
                    <button
                      type="button"
                      onClick={() => setAnswer(criterion.id, { isDone: answer?.isDone === true ? null : true })}
                      aria-label="Bajarildi"
                      aria-pressed={answer?.isDone === true}
                      title="Bajarildi"
                      className={cn(
                        'flex h-11 flex-1 items-center justify-center gap-1.5 rounded-xl px-3 text-sm font-medium transition-colors sm:w-11 sm:flex-none sm:px-0',
                        answer?.isDone === true
                          ? 'bg-emerald-500 text-white'
                          : 'bg-slate-100 text-slate-500 hover:bg-emerald-50 hover:text-emerald-600',
                      )}
                    >
                      <Check className="h-5 w-5" />
                      <span className="sm:hidden">Bajarildi</span>
                    </button>
                    <button
                      type="button"
                      onClick={() => setAnswer(criterion.id, { isDone: answer?.isDone === false ? null : false })}
                      aria-label="Bajarilmadi"
                      aria-pressed={answer?.isDone === false}
                      title="Bajarilmadi"
                      className={cn(
                        'flex h-11 flex-1 items-center justify-center gap-1.5 rounded-xl px-3 text-sm font-medium transition-colors sm:w-11 sm:flex-none sm:px-0',
                        answer?.isDone === false
                          ? 'bg-red-500 text-white'
                          : 'bg-slate-100 text-slate-500 hover:bg-red-50 hover:text-red-600',
                      )}
                    >
                      <X className="h-5 w-5" />
                      <span className="sm:hidden">Bajarilmadi</span>
                    </button>
                    <button
                      type="button"
                      onClick={() => setOpenComment(criterion.id)}
                      aria-label="Izoh"
                      title="Izoh"
                      className={cn(
                        'grid h-11 w-11 shrink-0 place-items-center rounded-xl transition-colors',
                        answer?.comment
                          ? 'bg-brand-50 text-brand-600'
                          : 'bg-slate-100 text-slate-500 hover:bg-brand-50 hover:text-brand-600',
                      )}
                    >
                      <MessageSquareText className="h-5 w-5" />
                    </button>
                  </div>
                </li>
              )
            })}
          </ul>
        </Card>
      ))}

      {/* kuzatuv xulosasi */}
      <Card className="overflow-hidden p-0">
        <header className="border-b border-slate-100 px-5 py-3.5">
          <h2 className="text-sm font-semibold text-slate-800">Kuzatuv xulosasi</h2>
        </header>
        <div className="grid gap-5 p-5 xl:grid-cols-2">
          <label className="block">
            <span className="mb-1 block text-sm font-medium text-emerald-600">Kuchli tomonlar</span>
            <textarea
              className={cn(fieldCls, 'min-h-[120px]')}
              maxLength={5000}
              placeholder="Dars davomida qo‘llanilgan samarali metodlar va ijobiy jihatlar…"
              value={strengths}
              onChange={(e) => setStrengths(e.target.value)}
            />
          </label>
          <label className="block">
            <span className="mb-1 block text-sm font-medium text-red-600">Rivojlanish sohalari</span>
            <textarea
              className={cn(fieldCls, 'min-h-[120px]')}
              maxLength={5000}
              placeholder="Kamchiliklar, xatolar va tavsiyalar…"
              value={growthAreas}
              onChange={(e) => setGrowthAreas(e.target.value)}
            />
          </label>
          <label className="block xl:col-span-2">
            <span className="mb-1 block text-sm font-medium text-brand-600">Kelgusi dars uchun bitta konkret tavsiya</span>
            <textarea
              className={cn(fieldCls, 'min-h-[80px]')}
              maxLength={5000}
              value={advice}
              onChange={(e) => setAdvice(e.target.value)}
            />
          </label>
        </div>
      </Card>

      {/* pastki amallar — sahifa bo'ylab yopishib turadi */}
      <div className="sticky bottom-0 z-20 -mx-6 -mb-6 border-t border-slate-200 bg-white/95 px-6 py-3 pb-[max(0.75rem,env(safe-area-inset-bottom))] backdrop-blur">
        <div className="flex gap-2.5">
          {completed ? (
            <Button onClick={handleSaveCompleted} disabled={saving !== null} className="h-11 flex-1">
              {saving ? 'Saqlanmoqda…' : 'O‘zgarishlarni saqlash'}
            </Button>
          ) : (
            <>
              <Button variant="secondary" onClick={handleSaveDraft} disabled={saving !== null} className="h-11 flex-1">
                {saving === 'draft' ? (
                  'Saqlanmoqda…'
                ) : (
                  <>
                    <span className="sm:hidden">Qoralama</span>
                    <span className="hidden sm:inline">Qoralama sifatida saqlash</span>
                  </>
                )}
              </Button>
              <Button onClick={handleComplete} disabled={saving !== null} className="h-11 flex-[2]">
                <Sparkles className="h-4 w-4" />
                {saving === 'complete' ? (
                  'Bajarilmoqda…'
                ) : (
                  <>
                    <span className="sm:hidden">Yakunlash</span>
                    <span className="hidden sm:inline">Yakunlash va AI xulosa olish</span>
                  </>
                )}
              </Button>
            </>
          )}
        </div>
      </div>

      <Modal
        open={openComment !== null}
        title="Mezon bo‘yicha izoh"
        size="sm"
        onClose={() => setOpenComment(null)}
        footer={
          <Button onClick={() => setOpenComment(null)} className="w-full sm:w-auto">
            Saqlash
          </Button>
        }
      >
        {openComment !== null && (
          <div className="space-y-3">
            {commentCriterion && <p className="text-sm text-slate-500">{commentCriterion.text}</p>}
            <textarea
              autoFocus
              className={cn(fieldCls, 'min-h-[130px]')}
              maxLength={2000}
              placeholder="Kuzatuvchi izohi (ixtiyoriy) — AI xulosada shu izohlar hisobga olinadi"
              value={answers[openComment]?.comment ?? ''}
              onChange={(e) => setAnswer(openComment, { comment: e.target.value })}
            />
          </div>
        )}
      </Modal>

      <Toast message={error} onClose={clearError} tone="error" duration={6000} />
      <Toast message={notice} onClose={clearNotice} duration={4000} />
    </div>
  )
}
