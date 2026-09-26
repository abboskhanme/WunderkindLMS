import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { GraduationCap, School, Trash2 } from 'lucide-react'
import type { StudentMemberships } from '@/api/services/classMemberships'
import { getStudentMemberships } from '@/api/services/classMemberships'
import { removeGroupMember } from '@/api/services/groups'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'

interface Props {
  studentId: string
}

/**
 * O'quvchi kartochkasining "Sinf va guruhlar" tab'i —
 * `docs/modules/students-parity.md` §2.1.6 (G-10).
 *
 * Joriy sinf va o'quv guruhlari, ularning TARIXI bilan: qachon qo'shilgan,
 * qachon va NEGA chiqqan, necha kun turgan. A'zolik hech qachon
 * o'chirilmaydi — yopiladi, shuning uchun bu ro'yxat bolaning butun yo'lini
 * ko'rsatadi.
 *
 * Sinf a'zoligini bu yerdan o'zgartirib bo'lmaydi: sinf — `students.class_name`
 * bilan bog'langan haqiqat manbai va uni o'quvchi formasi yoki sinf ro'yxati
 * sahifasi boshqaradi. Guruhdan chiqarish esa shu yerda mumkin (§2.1.1).
 */
export function MembershipsTab({ studentId }: Props) {
  const [data, setData] = useState<StudentMemberships | null>(null)
  const [loading, setLoading] = useState(true)

  const load = useCallback(() => {
    setLoading(true)
    getStudentMemberships(studentId)
      .then(setData)
      .finally(() => setLoading(false))
  }, [studentId])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- ma'lumot kelganda formani to'ldiramiz (maqsadli, loyihadagi mavjud naqsh)
    load()
  }, [load])

  const handleRemoveGroup = (groupId: string, memberId: string, groupName: string) => {
    const reason = prompt(`"${groupName}" guruhidan chiqarasizmi?\nSabab (ixtiyoriy):`, '')
    if (reason === null) return
    removeGroupMember(groupId, memberId, reason.trim() || undefined)
      .then(load)
      .catch((e) =>
        alert(
          (e as { response?: { data?: { message?: string } } })?.response?.data?.message ??
            "Chiqarib bo'lmadi",
        ),
      )
  }

  if (loading && !data) return <Loader label="Yuklanmoqda..." />
  if (!data) return null

  const activeClass = data.classes.find((c) => !c.leftOn)

  return (
    <div className="space-y-6">
      {/* ---------- Sinf ---------- */}
      <Card className="space-y-3">
        <h3 className="flex items-center gap-2 text-sm font-semibold text-slate-700">
          <School className="h-4 w-4 text-brand-600" /> Sinf
        </h3>

        {!activeClass && data.className && (
          // `class_name` to'la, lekin sanali a'zolik yozuvi yo'q: migratsiya
          // backfill'i faqat nomi sinf katalogiga MOS tushganlarni qamragan.
          // Buni yashirmaymiz — administrator kartochkadan sinfni qayta saqlasa
          // yozuv o'z-o'zidan paydo bo'ladi.
          <p className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-800">
            Joriy sinf: <b>{data.className}</b> — lekin sanali a'zolik yozuvi yo'q. Sinfni
            o'quvchi kartochkasidan qayta saqlasangiz, yozuv paydo bo'ladi.
          </p>
        )}

        {data.classes.length === 0 && !data.className ? (
          <p className="text-sm text-slate-400">O'quvchi hech qaysi sinfda emas</p>
        ) : (
          <div className="space-y-1.5">
            {data.classes.map((c) => (
              <Row
                key={c.id}
                active={!c.leftOn}
                title={c.className}
                subtitle={`${c.grade}-daraja`}
                period={`${c.joinedOn} — ${c.leftOn ?? 'hozirgacha'} · ${c.days} kun`}
                reason={c.leaveReason}
              />
            ))}
          </div>
        )}
      </Card>

      {/* ---------- Guruhlar ---------- */}
      <Card className="space-y-3">
        <h3 className="flex items-center gap-2 text-sm font-semibold text-slate-700">
          <GraduationCap className="h-4 w-4 text-brand-600" /> O'quv guruhlari
        </h3>

        {data.groups.length === 0 ? (
          <p className="text-sm text-slate-400">O'quvchi hech qaysi guruhda emas</p>
        ) : (
          <div className="space-y-1.5">
            {data.groups.map((g) => (
              <Row
                key={g.id}
                active={!g.leftOn}
                title={g.groupName}
                subtitle={(g.subjectName || "Yo'nalish guruhi") + (g.groupIsArchived ? ' · arxivda' : '')}
                period={`${g.joinedOn} — ${g.leftOn ?? 'hozirgacha'} · ${g.days} kun`}
                reason={g.leaveReason}
                link={`/admin/groups/${g.groupId}/students`}
                onRemove={
                  !g.leftOn && !g.groupIsArchived
                    ? () => handleRemoveGroup(g.groupId, g.id, g.groupName)
                    : undefined
                }
              />
            ))}
          </div>
        )}
      </Card>
    </div>
  )
}

function Row({
  active,
  title,
  subtitle,
  period,
  reason,
  link,
  onRemove,
}: {
  active: boolean
  title: string
  subtitle: string
  period: string
  reason: string | null
  link?: string
  onRemove?: () => void
}) {
  return (
    <div
      className={cn(
        'flex items-start justify-between gap-3 rounded-lg border px-3 py-2',
        active ? 'border-brand-200 bg-brand-50/40' : 'border-slate-100 bg-white',
      )}
    >
      <div className="min-w-0">
        <p className={cn('truncate text-sm font-medium', active ? 'text-slate-800' : 'text-slate-500')}>
          {link ? (
            <Link to={link} className="hover:text-brand-600 hover:underline">
              {title}
            </Link>
          ) : (
            title
          )}
          {active && (
            <span className="ml-2 rounded-md bg-brand-600 px-1.5 py-0.5 text-[11px] font-medium text-white">
              faol
            </span>
          )}
        </p>
        <p className="text-xs text-slate-400">{subtitle}</p>
        <p className="text-xs text-slate-400">{period}</p>
        {reason && <p className="text-xs text-slate-500">Sabab: {reason}</p>}
      </div>
      {onRemove && (
        <button
          type="button"
          title="Guruhdan chiqarish"
          onClick={onRemove}
          className="shrink-0 rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
        >
          <Trash2 className="h-4 w-4" />
        </button>
      )}
    </div>
  )
}
