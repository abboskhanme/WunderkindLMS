import type { StudentStatus } from '@/api/services/attendance'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'

interface Props {
  open: boolean
  title: string
  subtitle: string
  students: StudentStatus[]
  loading: boolean
  onClose: () => void
}

export function SubjectAttendanceModal({
  open,
  title,
  subtitle,
  students,
  loading,
  onClose,
}: Props) {
  const present = students.filter((s) => !s.absent).length
  const absent = students.length - present

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={title}
      footer={
        <Button variant="secondary" onClick={onClose}>
          Yopish
        </Button>
      }
    >
      <p className="mb-3 text-sm text-slate-400">{subtitle}</p>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <>
          <div className="mb-3 flex gap-4 text-sm">
            <span className="font-medium text-emerald-600">Keldi: {present}</span>
            <span className="font-medium text-red-600">Kelmadi: {absent}</span>
          </div>
          <div className="space-y-1.5">
            {students.map((s, i) => (
              <div
                key={s.student.id}
                className="flex items-center gap-3 rounded-lg border border-slate-100 px-3 py-2"
              >
                <span className="w-5 text-xs text-slate-400">{i + 1}</span>
                <span className="flex-1 font-medium text-slate-800">{s.student.fullName}</span>
                <span
                  className={cn(
                    'rounded-md px-2 py-0.5 text-xs font-medium',
                    s.absent ? 'bg-red-50 text-red-600' : 'bg-emerald-50 text-emerald-600',
                  )}
                >
                  {s.absent ? `Kelmadi${s.reasonName ? ` · ${s.reasonName}` : ''}` : 'Keldi'}
                </span>
              </div>
            ))}
            {students.length === 0 && (
              <p className="py-6 text-center text-slate-400">O'quvchilar yo'q</p>
            )}
          </div>
        </>
      )}
    </Modal>
  )
}
