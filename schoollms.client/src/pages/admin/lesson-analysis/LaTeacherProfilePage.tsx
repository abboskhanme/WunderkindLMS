import { useMemo } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft, Plus } from 'lucide-react'
import { getObservations, getTeacher } from '@/api/services/lessonAnalysis'
import type { LaObservationListItem } from '@/types/lessonAnalysis'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { UserAvatar } from '@/components/ui/UserAvatar'
import { fmtDate } from '@/components/lesson-analysis/format'
import { EmptyState, Notice, ObservationTable, Score, StatTile } from '@/components/lesson-analysis/ui'
import { useLoad } from '@/components/lesson-analysis/useLoad'
import { laPaths } from './paths'

const linkTo = (item: LaObservationListItem) =>
  item.status === 'draft' ? laPaths.edit(item.id) : laPaths.detail(item.id)

/** O'qituvchi profili — eski `TeacherProfile.tsx` (`oqituvchi/:teacherId`). */
export function LaTeacherProfilePage() {
  const { teacherId = '' } = useParams()
  const navigate = useNavigate()

  const teacher = useLoad(`teacher|${teacherId}`, () => getTeacher(teacherId))
  const list = useLoad(`teacher-obs|${teacherId}`, () => getObservations({ teacherId }))

  const stats = useMemo(() => {
    const items = list.data ?? []
    const completed = items.filter((item) => item.status === 'completed')
    const dates = completed.map((item) => item.lessonDate).sort()
    return {
      total: items.length,
      completed: completed.length,
      drafts: items.length - completed.length,
      last: dates.length ? dates[dates.length - 1] : (teacher.data?.lastObservationDate ?? null),
    }
  }, [list.data, teacher.data])

  const back = (
    <Button
      variant="secondary"
      className="px-3 py-1.5"
      onClick={() => (window.history.length > 1 ? navigate(-1) : navigate(laPaths.rating))}
    >
      <ArrowLeft className="h-4 w-4" />
      Orqaga
    </Button>
  )

  const error = teacher.error ?? list.error
  if (error) {
    return (
      <div className="space-y-3">
        {back}
        <Notice>{error}</Notice>
      </div>
    )
  }
  if (!teacher.data || !list.data) return <Loader label="O‘qituvchi ma’lumotlari yuklanmoqda" />
  const t = teacher.data
  const items = list.data

  const newButton = (
    <Link
      to={laPaths.create(t.id)}
      className="inline-flex items-center justify-center gap-2 rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-brand-700"
    >
      <Plus className="h-4 w-4" />
      Yangi dars tahlili
    </Link>
  )

  return (
    <div className="space-y-4">
      {back}

      <div className="flex flex-wrap items-center justify-between gap-4">
        <div className="flex min-w-0 items-center gap-3">
          <UserAvatar fullName={t.fullName} avatarUrl={t.photoUrl} className="h-12 w-12 text-base" />
          <div className="min-w-0">
            <h2 className="truncate text-lg font-semibold text-slate-800">{t.fullName}</h2>
            <p className="truncate text-sm text-slate-400">
              {[t.subject || 'Fan ko‘rsatilmagan', t.phone].filter(Boolean).join(' · ')}
            </p>
          </div>
        </div>
        {newButton}
      </div>

      <div className="grid gap-4 [grid-template-columns:repeat(auto-fit,minmax(min(200px,100%),1fr))]">
        <StatTile label="Jami tahlillar" value={stats.total} hint="Qoralamalar bilan birga" />
        <StatTile
          label="O‘rtacha reyting"
          value={t.rating != null ? <Score percent={t.rating} size="lg" /> : '—'}
          hint={
            t.rating != null
              ? `${t.observationsCount} ta yakunlangan tahlil bo‘yicha`
              : 'Hali yakunlangan tahlil yo‘q'
          }
        />
        <StatTile
          label="Oxirgi tahlil"
          value={<span className="text-xl">{fmtDate(stats.last)}</span>}
          hint="Oxirgi yakunlangan kuzatuv sanasi"
        />
        <StatTile label="Qoralamalar" value={stats.drafts} hint="Yakunlanmagan tahlillar" />
      </div>

      <section className="space-y-3">
        <h2 className="font-semibold text-slate-800">Dars tahlillari</h2>
        {items.length === 0 ? (
          <Card>
            <EmptyState
              title="Bu o‘qituvchiga hali tahlil yozilmagan"
              hint="Birinchi dars kuzatuvini shu yerdan boshlang"
              action={newButton}
            />
          </Card>
        ) : (
          <ObservationTable items={items} linkTo={linkTo} />
        )}
      </section>
    </div>
  )
}
