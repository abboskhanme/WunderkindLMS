import { useEffect, useMemo, useState } from 'react'
import { useParams, Link, useNavigate } from 'react-router-dom'
import { ArrowLeft, Plus, Pencil, Trash2, LayoutGrid, Eye } from 'lucide-react'
import type {
  SchoolClass,
  ScheduleTemplate,
  SchoolSettings,
  WeekAssignment,
  Subject,
  Teacher,
} from '@/types'
import { getClasses } from '@/api/services/classes'
import {
  getTemplates,
  createTemplate,
  renameTemplate,
  deleteTemplate,
} from '@/api/services/scheduleTemplates'
import { getSubjects } from '@/api/services/subjects'
import { getTeachers } from '@/api/services/teachers'
import { getSettings } from '@/api/services/settings'
import { getWeekAssignments, saveWeekAssignments } from '@/api/services/weekAssignments'
import { quarters } from '@/config/constants'
import { getQuarterWeeks } from '@/lib/weeks'
import { formatDate, cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { TemplateNameModal } from './TemplateNameModal'
import { WeekScheduleModal } from './WeekScheduleModal'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

export function ClassSchedulePage() {
  const { id = '' } = useParams()
  const navigate = useNavigate()

  const [cls, setCls] = useState<SchoolClass | null>(null)
  const [templates, setTemplates] = useState<ScheduleTemplate[]>([])
  const [settings, setSettings] = useState<SchoolSettings | null>(null)
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [loading, setLoading] = useState(true)
  const [viewing, setViewing] = useState<{ title: string; lessons: ScheduleTemplate['lessons'] } | null>(null)

  const [quarter, setQuarter] = useState(1)
  const [assignments, setAssignments] = useState<WeekAssignment[]>([])
  const [assignLoading, setAssignLoading] = useState(false)
  const [checked, setChecked] = useState<Set<number>>(new Set())
  const [selectedTemplateId, setSelectedTemplateId] = useState('')

  const [nameOpen, setNameOpen] = useState(false)
  const [editingTpl, setEditingTpl] = useState<ScheduleTemplate | null>(null)

  useEffect(() => {
    Promise.all([getClasses(), getTemplates(id), getSettings(), getSubjects(), getTeachers()])
      .then(([cl, tpls, st, subs, tchs]) => {
        setCls(cl.find((c) => c.id === id) ?? null)
        setTemplates(tpls)
        setSettings(st)
        setSubjects(subs)
        setTeachers(tchs)
        setSelectedTemplateId(tpls[0]?.id ?? '')
      })
      .finally(() => setLoading(false))
  }, [id])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- yangi so'rovdan oldin holatni tozalaymiz (maqsadli)
    setAssignLoading(true)
    setChecked(new Set())
    getWeekAssignments(id, quarter)
      .then(setAssignments)
      .finally(() => setAssignLoading(false))
  }, [id, quarter])

  const weeks = useMemo(() => {
    if (!settings) return []
    const q = settings.quarters.find((x) => x.quarter === quarter)
    return q ? getQuarterWeeks(q.startDate, q.endDate) : []
  }, [settings, quarter])

  const assignedId = (week: number) =>
    assignments.find((a) => a.week === week)?.templateId ?? null
  const templateName = (tid: string | null) =>
    tid ? (templates.find((t) => t.id === tid)?.name ?? null) : null

  const allChecked = weeks.length > 0 && weeks.every((w) => checked.has(w.week))

  const toggleWeek = (week: number) =>
    setChecked((prev) => {
      const next = new Set(prev)
      if (next.has(week)) next.delete(week)
      else next.add(week)
      return next
    })

  const toggleAll = () =>
    setChecked(allChecked ? new Set() : new Set(weeks.map((w) => w.week)))

  const openWeekView = (week: number, tid: string) => {
    const tpl = templates.find((t) => t.id === tid)
    if (!tpl) return
    setViewing({ title: `${week}-hafta — ${tpl.name}`, lessons: tpl.lessons })
  }

  const apply = (templateId: string | null) => {
    const next: WeekAssignment[] = weeks.map((w) => ({
      week: w.week,
      templateId: checked.has(w.week) ? templateId : assignedId(w.week),
    }))
    setAssignments(next)
    saveWeekAssignments(id, quarter, next)
    setChecked(new Set())
  }

  // --- Template CRUD ---
  const handleNameSubmit = (name: string) => {
    if (editingTpl) {
      const tid = editingTpl.id
      renameTemplate(id, tid, name)
      setTemplates((prev) => prev.map((t) => (t.id === tid ? { ...t, name } : t)))
    } else {
      createTemplate(id, name).then((tpl) => {
        setTemplates((prev) => [...prev, tpl])
        navigate(`/admin/schedule/manage/${id}/template/${tpl.id}`)
      })
    }
    setNameOpen(false)
    setEditingTpl(null)
  }

  const handleDeleteTpl = (t: ScheduleTemplate) => {
    if (!confirm(`"${t.name}" jadvalini o'chirasizmi?`)) return
    deleteTemplate(id, t.id)
    setTemplates((prev) => prev.filter((x) => x.id !== t.id))
    // shu jadval biriktirilgan haftalarni bo'shatamiz
    setAssignments((prev) => {
      const next = prev.map((a) => (a.templateId === t.id ? { ...a, templateId: null } : a))
      saveWeekAssignments(id, quarter, next)
      return next
    })
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <Link
            to="/admin/schedule/manage"
            className="rounded-lg p-2 text-slate-500 transition-colors hover:bg-slate-100"
          >
            <ArrowLeft className="h-5 w-5" />
          </Link>
          <div>
            <h1 className="text-xl font-semibold text-slate-800">
              Dars jadvali{cls ? ` — ${cls.name}` : ''}
            </h1>
            <p className="text-sm text-slate-400">Jadval yarating va haftalarga biriktiring</p>
          </div>
        </div>
        <Button
          onClick={() => {
            setEditingTpl(null)
            setNameOpen(true)
          }}
        >
          <Plus className="h-4 w-4" /> Yangi jadval
        </Button>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <>
          {/* Jadvallar ro'yxati */}
          <Card>
            <h2 className="mb-4 font-semibold text-slate-800">Jadvallar</h2>
            <div className="space-y-2">
              {templates.map((t) => (
                <div
                  key={t.id}
                  className="flex items-center justify-between gap-3 rounded-xl border border-slate-100 p-3"
                >
                  <div>
                    <p className="font-medium text-slate-800">{t.name}</p>
                    <p className="text-xs text-slate-400">{t.lessons.length} ta dars</p>
                  </div>
                  <div className="flex items-center gap-1">
                    <Button
                      variant="secondary"
                      onClick={() => navigate(`/admin/schedule/manage/${id}/template/${t.id}`)}
                    >
                      <LayoutGrid className="h-4 w-4" /> Ochish
                    </Button>
                    <IconBtn
                      icon={Pencil}
                      title="Nomini o'zgartirish"
                      onClick={() => {
                        setEditingTpl(t)
                        setNameOpen(true)
                      }}
                    />
                    <IconBtn
                      icon={Trash2}
                      title="O'chirish"
                      danger
                      onClick={() => handleDeleteTpl(t)}
                    />
                  </div>
                </div>
              ))}
              {templates.length === 0 && (
                <p className="py-6 text-center text-sm text-slate-400">
                  Hali jadval yaratilmagan
                </p>
              )}
            </div>
          </Card>

          {/* Haftalarga taqsimlash */}
          <Card>
            <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
              <h2 className="font-semibold text-slate-800">Haftalarga taqsimlash</h2>
              <div className="flex w-fit gap-1 rounded-lg bg-slate-100 p-1">
                {quarters.map((q) => (
                  <button
                    key={q}
                    type="button"
                    onClick={() => setQuarter(q)}
                    className={cn(
                      'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                      q === quarter
                        ? 'bg-white text-brand-700 shadow-sm'
                        : 'text-slate-500 hover:text-slate-700',
                    )}
                  >
                    {q}-chorak
                  </button>
                ))}
              </div>
            </div>

            {/* Amal paneli */}
            <div className="mb-3 flex flex-wrap items-center gap-2">
              <select
                value={selectedTemplateId}
                onChange={(e) => setSelectedTemplateId(e.target.value)}
                className={control}
              >
                <option value="">Jadvalni tanlang</option>
                {templates.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.name}
                  </option>
                ))}
              </select>
              <Button
                onClick={() => apply(selectedTemplateId)}
                disabled={!selectedTemplateId || checked.size === 0}
              >
                Tanlangan haftalarga belgilash
              </Button>
              <Button variant="secondary" onClick={() => apply(null)} disabled={checked.size === 0}>
                Bo'shatish
              </Button>
            </div>

            {assignLoading ? (
              <Loader label="Yuklanmoqda..." />
            ) : weeks.length === 0 ? (
              <p className="py-8 text-center text-sm text-slate-400">
                Bu chorak uchun sanalar kiritilmagan.{' '}
                <Link to="/admin/settings" className="text-brand-600 hover:underline">
                  Sozlamalarga o'ting
                </Link>
              </p>
            ) : (
              <div className="overflow-hidden rounded-xl border border-slate-100">
                <table className="w-full text-left text-sm">
                  <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                    <tr>
                      <th className="w-10 px-4 py-3">
                        <input
                          type="checkbox"
                          checked={allChecked}
                          onChange={toggleAll}
                          className="h-4 w-4 accent-brand-600"
                        />
                      </th>
                      <th className="px-4 py-3">Hafta</th>
                      <th className="px-4 py-3">Jadval</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {weeks.map((w) => {
                      const tid = assignedId(w.week)
                      const name = templateName(tid)
                      return (
                        <tr key={w.week} className="hover:bg-slate-50/60">
                          <td className="px-4 py-3">
                            <input
                              type="checkbox"
                              checked={checked.has(w.week)}
                              onChange={() => toggleWeek(w.week)}
                              className="h-4 w-4 accent-brand-600"
                            />
                          </td>
                          <td className="px-4 py-3">
                            <span className="font-medium text-slate-800">{w.week}-hafta</span>
                            <span className="ml-2 text-xs text-slate-400">
                              {formatDate(w.startISO)} – {formatDate(w.endISO)}
                            </span>
                          </td>
                          <td className="px-4 py-3">
                            {tid && name ? (
                              <div className="flex items-center gap-2">
                                <span className="rounded-md bg-brand-50 px-2 py-0.5 text-xs font-medium text-brand-700">
                                  {name}
                                </span>
                                <button
                                  type="button"
                                  title="Ko'rish"
                                  onClick={() => openWeekView(w.week, tid)}
                                  className="rounded-lg p-1 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                                >
                                  <Eye className="h-4 w-4" />
                                </button>
                              </div>
                            ) : (
                              <span className="text-slate-400">—</span>
                            )}
                          </td>
                        </tr>
                      )
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </Card>
        </>
      )}

      <TemplateNameModal
        open={nameOpen}
        initialName={editingTpl?.name}
        onClose={() => {
          setNameOpen(false)
          setEditingTpl(null)
        }}
        onSubmit={handleNameSubmit}
      />

      <WeekScheduleModal
        open={!!viewing}
        title={viewing?.title ?? ''}
        lessons={viewing?.lessons ?? []}
        subjects={subjects}
        teachers={teachers}
        onClose={() => setViewing(null)}
      />
    </div>
  )
}

interface IconBtnProps {
  icon: typeof Pencil
  title: string
  onClick: () => void
  danger?: boolean
}

function IconBtn({ icon: Icon, title, onClick, danger }: IconBtnProps) {
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      className={cn(
        'rounded-lg p-1.5 transition-colors',
        danger
          ? 'text-slate-400 hover:bg-red-50 hover:text-red-600'
          : 'text-slate-400 hover:bg-slate-100 hover:text-slate-700',
      )}
    >
      <Icon className="h-4 w-4" />
    </button>
  )
}
