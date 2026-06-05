import { useEffect, useState } from 'react'
import { getTeacherSalaryDetail, type TeacherSalaryDetail } from '@/api/services/salaryRates'
import { teacherCategoryLabel } from '@/config/constants'
import { formatDate, formatMoney, cn } from '@/lib/utils'
import { Modal } from '@/components/ui/Modal'
import { Loader } from '@/components/ui/Loader'

const WD = ['Yakshanba', 'Dushanba', 'Seshanba', 'Chorshanba', 'Payshanba', 'Juma', 'Shanba']
const weekday = (iso: string) => WD[new Date(iso).getDay()] ?? ''

const uzMonth = (m: string) => {
  const names = ['Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun', 'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr']
  if (!m || m.length < 7) return m
  return `${names[Number(m.slice(5, 7)) - 1] ?? m} ${m.slice(0, 4)}`
}

export function TeacherSalaryDetailModal({
  teacherId,
  month,
  onClose,
}: {
  teacherId: string | null
  month: string
  onClose: () => void
}) {
  const [data, setData] = useState<TeacherSalaryDetail | null>(null)
  const [loading, setLoading] = useState(false)

  useEffect(() => {
    if (!teacherId) return
    setLoading(true)
    setData(null)
    getTeacherSalaryDetail(teacherId, month)
      .then(setData)
      .finally(() => setLoading(false))
  }, [teacherId, month])

  return (
    <Modal open={!!teacherId} onClose={onClose} title={data?.fullName ?? 'Maosh tafsiloti'} size="md">
      {loading || !data ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-5">
          {/* Sarlavha — toifa, oy, narx */}
          <div className="flex flex-wrap items-center gap-2 text-sm">
            {data.category ? (
              <span className="rounded-md bg-brand-50 px-2 py-0.5 text-xs font-medium text-brand-700">
                {teacherCategoryLabel(data.category)}
              </span>
            ) : (
              <span className="rounded-md bg-amber-50 px-2 py-0.5 text-xs font-medium text-amber-700">Toifasiz</span>
            )}
            <span className="text-slate-400">·</span>
            <span className="font-medium text-slate-600">{uzMonth(data.month)}</span>
            <span className="text-slate-400">·</span>
            <span className="text-slate-500">Soat narxi: {formatMoney(data.hourlyRate)}</span>
          </div>

          {data.partialMonth && (
            <div className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-700">
              Ishga kirgan oy — {formatDate(data.startDate)} dan oy oxirigacha qisman hisoblangan.
            </div>
          )}

          {/* Statistika */}
          <div className="grid grid-cols-3 gap-3">
            <Stat label="Haftalik dars" value={String(data.weeklyLessons)} />
            <Stat label="Shu oy darslar" value={String(data.monthlyLessons)} />
            <Stat label="Kelmagan dars" value={data.missedLessons > 0 ? `−${data.missedLessons}` : '0'} red={data.missedLessons > 0} />
          </div>

          {/* Hisob-kitob */}
          <div className="rounded-2xl border border-slate-100 bg-slate-50/50 p-4">
            <Row label={`Reja oylik (${data.monthlyLessons} dars)`} value={formatMoney(data.plannedSalary)} />
            {data.deduction > 0 && (
              <Row
                label={`Kelmagan (${data.missedLessons} dars)`}
                value={`− ${formatMoney(data.deduction)}`}
                color="text-red-600"
              />
            )}
            {data.bonusPct > 0 && (
              <Row
                label={`Ustama (${data.bonusPct}%)`}
                value={`+ ${formatMoney(data.bonusAmount)}`}
                color="text-emerald-600"
              />
            )}
            <div className="my-2 border-t border-slate-200" />
            <div className="flex items-center justify-between">
              <span className="text-sm font-semibold text-slate-700">Jami oylik maosh</span>
              <span className="text-lg font-bold text-slate-900">{formatMoney(data.netSalary)}</span>
            </div>
            <div className="mt-3 space-y-1.5 border-t border-slate-200 pt-3">
              <Row label="Berilgan (shu oy)" value={formatMoney(data.paid)} color="text-emerald-600" />
              <Row
                label="Qoldiq (berilishi kerak)"
                value={formatMoney(data.remaining)}
                color={data.remaining > 0 ? 'text-slate-800 font-semibold' : 'text-slate-400'}
              />
            </div>
          </div>

          {/* Qachon kelmagan */}
          <div>
            <p className="mb-2 text-sm font-medium text-slate-600">
              Kelmagan kunlar {data.absentDays.length > 0 && `(${data.absentDays.length})`}
            </p>
            {data.absentDays.length === 0 ? (
              <p className="rounded-lg bg-emerald-50 px-3 py-2 text-sm text-emerald-700">
                Bu oyda kelmagan kun yo'q ✓
              </p>
            ) : (
              <div className="space-y-1.5">
                {data.absentDays.map((a) => (
                  <div
                    key={a.date}
                    className="flex items-center gap-3 rounded-lg border border-slate-100 px-3 py-2 text-sm"
                  >
                    <span className="font-medium text-slate-700">{formatDate(a.date)}</span>
                    <span className="text-slate-400">{weekday(a.date)}</span>
                    {a.note && <span className="truncate text-xs text-slate-400">— {a.note}</span>}
                    <span className={cn('ml-auto font-medium', a.lessons > 0 ? 'text-red-600' : 'text-slate-300')}>
                      {a.lessons > 0 ? `−${a.lessons} dars` : 'dars yo\'q'}
                    </span>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>
      )}
    </Modal>
  )
}

function Stat({ label, value, red }: { label: string; value: string; red?: boolean }) {
  return (
    <div className="rounded-xl border border-slate-100 bg-white p-3 text-center">
      <div className={cn('text-xl font-bold', red ? 'text-red-600' : 'text-slate-800')}>{value}</div>
      <div className="mt-0.5 text-xs text-slate-400">{label}</div>
    </div>
  )
}

function Row({ label, value, color }: { label: string; value: string; color?: string }) {
  return (
    <div className="flex items-center justify-between py-1 text-sm">
      <span className="text-slate-500">{label}</span>
      <span className={cn('font-medium text-slate-700', color)}>{value}</span>
    </div>
  )
}
