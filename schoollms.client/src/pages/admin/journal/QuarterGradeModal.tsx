import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { cn } from '@/lib/utils'

interface Props {
  open: boolean
  studentName: string
  /** Joriy chorak bahosi (o'qituvchi qo'ygan), yo'q bo'lsa null */
  grade: number | null
  /** Tavsiya etilgan baho (kunlik baholar o'rtachasi), yo'q bo'lsa null */
  recommended: number | null
  onClose: () => void
  onSetGrade: (grade: number) => void
  onClear: () => void
}

const grades = [2, 3, 4, 5]

export function QuarterGradeModal({
  open,
  studentName,
  grade,
  recommended,
  onClose,
  onSetGrade,
  onClear,
}: Props) {
  const suggested = recommended != null ? Math.round(recommended) : null

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="sm"
      title={studentName}
      footer={
        <>
          {grade != null && (
            <Button variant="danger" className="mr-auto" onClick={onClear}>
              Tozalash
            </Button>
          )}
          <Button variant="secondary" onClick={onClose}>
            Yopish
          </Button>
        </>
      }
    >
      <p className="mb-1 text-sm font-medium text-slate-600">Chorak bahosi</p>
      <p className="mb-4 text-sm text-slate-400">
        {recommended != null ? (
          <>
            Tavsiya etilgan baho: <b className="text-slate-600">{suggested}</b> (o'rtacha{' '}
            {recommended.toFixed(1)})
          </>
        ) : (
          'Kunlik baholar yo\'q — tavsiya hisoblanmadi'
        )}
      </p>

      <div className="flex gap-2">
        {grades.map((g) => (
          <button
            key={g}
            type="button"
            onClick={() => onSetGrade(g)}
            className={cn(
              'flex h-11 w-11 items-center justify-center rounded-lg border text-base font-semibold transition-colors',
              grade === g
                ? 'border-brand-500 bg-brand-600 text-white'
                : 'border-slate-200 text-slate-700 hover:bg-slate-50',
            )}
          >
            {g}
          </button>
        ))}
      </div>

      {suggested != null && grade !== suggested && (
        <Button variant="secondary" className="mt-4 w-full" onClick={() => onSetGrade(suggested)}>
          Tavsiyani qo'llash ({suggested})
        </Button>
      )}
    </Modal>
  )
}
