import { ShieldAlert } from 'lucide-react'
import type { DisciplinePoint } from '@/types'
import { cn, formatDate } from '@/lib/utils'
import { ProfileEmpty, ProfileSection } from './ProfileUi'

/**
 * Kartochkaning "Intizom" tab'i — docs/modules/students-parity.md §2.3
 * (S-10: *Intizom* — mavjud ball tarixi).
 *
 * Yozuvlar ikki manbadan keladi va ikkalasi ham shu ro'yxatda: qo'lda
 * qo'yilgan ball ("manual") va jurnaldagi davomat sababi ("attendance").
 * Ma'lumot shaxsiy daftar javobidan olinadi — yangi so'rov yo'q.
 */
export function DisciplineTab({ points }: { points: DisciplinePoint[] }) {
  return (
    <ProfileSection title="Intizomiy ball tarixi" icon={ShieldAlert}>
      {points.length === 0 ? (
        <ProfileEmpty>Yozuv yo'q</ProfileEmpty>
      ) : (
        <div className="space-y-2">
          {points.map((p) => (
            <div key={p.id} className="flex items-center gap-3 rounded-lg border border-slate-100 px-3 py-2">
              <span
                className={cn(
                  'rounded-md px-2 py-0.5 text-sm font-semibold',
                  p.points < 0 ? 'bg-red-50 text-red-600' : 'bg-emerald-50 text-emerald-600',
                )}
              >
                {p.points > 0 ? `+${p.points}` : p.points}
              </span>
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm font-medium text-slate-700">{p.reasonName}</p>
                {p.note && <p className="truncate text-xs text-slate-400">{p.note}</p>}
              </div>
              <span className="shrink-0 text-xs text-slate-400">{formatDate(p.createdAt)}</span>
            </div>
          ))}
        </div>
      )}
    </ProfileSection>
  )
}
