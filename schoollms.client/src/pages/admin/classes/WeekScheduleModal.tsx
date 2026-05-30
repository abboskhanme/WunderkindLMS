import type { ScheduleLesson, Subject, Teacher } from '@/types'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { weekDays, schedulePeriods } from '@/config/constants'

interface Props {
  open: boolean
  title: string
  lessons: ScheduleLesson[]
  subjects: Subject[]
  teachers: Teacher[]
  onClose: () => void
}

export function WeekScheduleModal({ open, title, lessons, subjects, teachers, onClose }: Props) {
  const subjectName = (sid: string) => subjects.find((s) => s.id === sid)?.name ?? ''
  const teacherName = (tid: string) => teachers.find((t) => t.id === tid)?.fullName ?? ''
  const lessonFor = (day: number, period: number) =>
    lessons.find((l) => l.day === day && l.period === period) ?? null

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="xl"
      title={title}
      footer={
        <Button variant="secondary" onClick={onClose}>
          Yopish
        </Button>
      }
    >
      <div className="overflow-x-auto">
        <table className="w-full border-separate border-spacing-0 text-sm">
          <thead>
            <tr className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
              <th className="w-10 px-2 py-2 text-center">№</th>
              {weekDays.map((d) => (
                <th key={d} className="min-w-[110px] px-2 py-2 text-left font-medium">
                  {d}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {schedulePeriods.map((period) => (
              <tr key={period}>
                <td className="px-2 py-1.5 text-center align-top">
                  <div className="mt-1.5 inline-flex h-6 w-6 items-center justify-center rounded-md bg-slate-100 text-xs font-semibold text-slate-500">
                    {period}
                  </div>
                </td>
                {weekDays.map((_, day) => {
                  const lesson = lessonFor(day, period)
                  return (
                    <td key={day} className="px-1 py-1 align-top">
                      <div
                        className={
                          lesson
                            ? 'min-h-[52px] rounded-lg border border-brand-100 bg-brand-50 p-2'
                            : 'min-h-[52px] rounded-lg border border-dashed border-slate-200'
                        }
                      >
                        {lesson && (
                          <>
                            <p className="text-sm font-medium text-slate-800">
                              {subjectName(lesson.subjectId)}
                            </p>
                            {lesson.teacherId && (
                              <p className="mt-0.5 text-xs text-slate-500">
                                {teacherName(lesson.teacherId)}
                              </p>
                            )}
                          </>
                        )}
                      </div>
                    </td>
                  )
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Modal>
  )
}
