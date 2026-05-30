import { useEffect, useState } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import {
  ArrowLeft, Video, FileText, Paperclip, Users, BookOpen,
} from 'lucide-react'
import type { LmsSubject, LmsTopic, LmsProgressReport } from '@/types'
import {
  getTeacherLmsSubjects,
  getTeacherLmsTopics,
  getTeacherLmsProgress,
} from '@/api/services/teacher'
import { Loader } from '@/components/ui/Loader'
import { Card } from '@/components/ui/Card'
import { cn } from '@/lib/utils'

type Tab = 'topics' | 'progress'

export function TeacherLmsSubjectPage() {
  const { subjectId } = useParams<{ subjectId: string }>()
  const navigate = useNavigate()

  const [subject, setSubject] = useState<LmsSubject | null>(null)
  const [topics, setTopics] = useState<LmsTopic[]>([])
  const [progress, setProgress] = useState<LmsProgressReport | null>(null)
  const [loading, setLoading] = useState(true)
  const [progressLoading, setProgressLoading] = useState(false)
  const [tab, setTab] = useState<Tab>('topics')

  useEffect(() => {
    if (!subjectId) return
    Promise.all([
      getTeacherLmsSubjects(),
      getTeacherLmsTopics(subjectId),
    ])
      .then(([subs, tops]) => {
        setSubject(subs.find((s) => s.id === subjectId) ?? null)
        setTopics(tops)
      })
      .finally(() => setLoading(false))
  }, [subjectId])

  const loadProgress = () => {
    if (!subjectId || progress) return
    setProgressLoading(true)
    getTeacherLmsProgress(subjectId)
      .then(setProgress)
      .finally(() => setProgressLoading(false))
  }

  const switchTab = (t: Tab) => {
    setTab(t)
    if (t === 'progress') loadProgress()
  }

  if (loading) return <Loader label="Yuklanmoqda..." />
  if (!subject) return <p className="py-12 text-center text-slate-400">Fan topilmadi</p>

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <div className="mb-1 flex items-center gap-2 text-sm text-slate-400">
            <button
              type="button"
              onClick={() => navigate('/teacher/lms')}
              className="flex items-center gap-1 hover:text-brand-600"
            >
              <ArrowLeft className="h-3.5 w-3.5" />
              Ta'lim
            </button>
            <span>/</span>
            <span className="text-slate-600">{subject.className}-sinf</span>
            <span>/</span>
            <span className="text-slate-800">{subject.title}</span>
          </div>
          <h1 className="text-xl font-semibold text-slate-800">{subject.title}</h1>
          {subject.description && (
            <p className="mt-0.5 text-sm text-slate-400">{subject.description}</p>
          )}
        </div>

        {/* Mavzular soni badge */}
        <div className="flex items-center gap-2 rounded-xl bg-slate-100 px-3 py-1.5 text-sm text-slate-600">
          <BookOpen className="h-4 w-4" />
          {topics.length} ta mavzu
        </div>
      </div>

      {/* Tab */}
      <div className="inline-flex rounded-lg border border-slate-200 bg-white p-1">
        <TabBtn active={tab === 'topics'} onClick={() => switchTab('topics')}>
          Mavzular
        </TabBtn>
        <TabBtn active={tab === 'progress'} onClick={() => switchTab('progress')}>
          <Users className="h-3.5 w-3.5" />
          O'quvchilar progressi
        </TabBtn>
      </div>

      {/* ─── MAVZULAR ─── */}
      {tab === 'topics' && (
        topics.length === 0 ? (
          <Card className="py-12 text-center text-slate-400">Mavzular yo'q</Card>
        ) : (
          <div className="space-y-2">
            {topics.map((t) => (
              <div
                key={t.id}
                className="flex items-start gap-4 rounded-xl border border-slate-200 bg-white px-4 py-3.5 shadow-sm"
              >
                {/* Tartib */}
                <div className="mt-0.5 flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-brand-50 text-sm font-bold text-brand-700">
                  {t.order}
                </div>

                {/* Asosiy ma'lumot */}
                <div className="min-w-0 flex-1">
                  <p className="font-medium text-slate-800">{t.title}</p>
                  {t.description && (
                    <p className="mt-0.5 text-xs text-slate-400">{t.description}</p>
                  )}

                  {/* Kontent indikatorlari */}
                  <div className="mt-2 flex flex-wrap items-center gap-3">
                    {t.videoUrl && (
                      <span className="flex items-center gap-1 text-xs font-medium text-brand-600">
                        <Video className="h-3.5 w-3.5" />
                        Video
                      </span>
                    )}
                    {t.textContent && (
                      <span className="flex items-center gap-1 text-xs text-slate-500">
                        <FileText className="h-3.5 w-3.5" />
                        Matn
                      </span>
                    )}
                    {t.materials.length > 0 && (
                      <span className="flex items-center gap-1 text-xs text-slate-500">
                        <Paperclip className="h-3.5 w-3.5" />
                        {t.materials.length} fayl
                      </span>
                    )}
                    {t.materials.map((m) => (
                      <a
                        key={m.id}
                        href={m.url}
                        target="_blank"
                        rel="noreferrer"
                        className="truncate text-xs text-slate-400 underline hover:text-brand-600"
                      >
                        {m.name}
                      </a>
                    ))}
                  </div>
                </div>

                {/* O'quvchilar tugatladi */}
                {t.completedCount > 0 && (
                  <div className="shrink-0 rounded-lg bg-emerald-50 px-2.5 py-1 text-center">
                    <p className="text-sm font-bold text-emerald-700">{t.completedCount}</p>
                    <p className="text-[10px] text-emerald-600">tugaldi</p>
                  </div>
                )}
              </div>
            ))}
          </div>
        )
      )}

      {/* ─── PROGRESS MATRITSASI ─── */}
      {tab === 'progress' && (
        progressLoading ? (
          <Loader label="Progress yuklanmoqda..." />
        ) : !progress ? null : progress.students.length === 0 ? (
          <Card className="py-12 text-center text-slate-400">O'quvchilar yo'q</Card>
        ) : (
          <Card className="p-0">
            <div className="overflow-x-auto">
              <table className="w-full border-separate border-spacing-0 text-sm">
                <thead>
                  <tr className="bg-slate-50">
                    {/* O'quvchi ismi ustuni */}
                    <th className="sticky left-0 z-10 min-w-[180px] bg-slate-50 px-4 py-2.5 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
                      O'quvchi
                    </th>
                    {/* Mavzular ustunlari */}
                    {progress.topics.map((topic) => (
                      <th
                        key={topic.id}
                        title={topic.title}
                        className="min-w-[60px] px-2 py-2.5 text-center"
                      >
                        <div className="mx-auto flex h-6 w-6 items-center justify-center rounded-md bg-slate-200 text-xs font-semibold text-slate-600">
                          {topic.order}
                        </div>
                        <p className="mt-1 max-w-[60px] truncate text-[10px] font-normal text-slate-400">
                          {topic.title}
                        </p>
                      </th>
                    ))}
                    {/* Jami */}
                    <th className="px-3 py-2.5 text-center text-xs font-semibold uppercase tracking-wide text-slate-500">
                      Jami
                    </th>
                  </tr>
                </thead>

                <tbody>
                  {progress.students.map((student, idx) => {
                    const doneSet = new Set(student.completedTopicIds)
                    const pct =
                      student.totalCount > 0
                        ? Math.round((student.completedCount / student.totalCount) * 100)
                        : 0
                    return (
                      <tr
                        key={student.studentId}
                        className={cn(
                          'border-t border-slate-100 hover:bg-slate-50/60',
                          idx % 2 === 1 && 'bg-slate-50/30',
                        )}
                      >
                        {/* O'quvchi ismi */}
                        <td className="sticky left-0 z-10 bg-white px-4 py-2 font-medium text-slate-800">
                          {student.fullName}
                        </td>

                        {/* Har mavzu uchun hujayra */}
                        {progress.topics.map((topic) => (
                          <td key={topic.id} className="px-2 py-2 text-center">
                            {doneSet.has(topic.id) ? (
                              <span className="inline-flex h-6 w-6 items-center justify-center rounded-full bg-emerald-100 text-emerald-700">
                                ✓
                              </span>
                            ) : (
                              <span className="inline-block h-6 w-6 rounded-full border-2 border-slate-200" />
                            )}
                          </td>
                        ))}

                        {/* Jami */}
                        <td className="px-3 py-2 text-center">
                          <div className="mx-auto w-14">
                            <p
                              className={cn(
                                'text-sm font-bold',
                                pct === 100
                                  ? 'text-emerald-600'
                                  : pct >= 50
                                    ? 'text-brand-600'
                                    : 'text-slate-500',
                              )}
                            >
                              {pct}%
                            </p>
                            <p className="text-[10px] text-slate-400">
                              {student.completedCount}/{student.totalCount}
                            </p>
                          </div>
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>

            {/* Izoh */}
            <div className="border-t border-slate-100 px-4 py-2.5 text-xs text-slate-400">
              <span className="mr-4 inline-flex items-center gap-1">
                <span className="inline-flex h-4 w-4 items-center justify-center rounded-full bg-emerald-100 text-[10px] text-emerald-700">✓</span>
                Tugallagan
              </span>
              <span className="inline-flex items-center gap-1">
                <span className="inline-block h-4 w-4 rounded-full border-2 border-slate-200" />
                Tugallamagan
              </span>
            </div>
          </Card>
        )
      )}
    </div>
  )
}

function TabBtn({
  active,
  onClick,
  children,
}: {
  active: boolean
  onClick: () => void
  children: React.ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'inline-flex items-center gap-1.5 rounded-md px-4 py-1.5 text-sm font-medium transition-colors',
        active ? 'bg-brand-600 text-white' : 'text-slate-600 hover:bg-slate-50',
      )}
    >
      {children}
    </button>
  )
}
