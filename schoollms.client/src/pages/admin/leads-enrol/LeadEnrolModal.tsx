import { useEffect, useMemo, useState } from 'react'
import type { Lead } from '@/types'
import type { StudentPayload } from '@/api/services/students'
import { enrolLead } from '@/api/services/leads'
import { getClasses } from '@/api/services/classes'
import { StudentFormModal } from '@/pages/admin/students/StudentFormModal'

interface Props {
  /** Aylantirilayotgan lid; `null` — modal yopiq. */
  lead: Lead | null
  onClose: () => void
  /** Muvaffaqiyatli — lid serverda o'chirildi, doska uni ro'yxatdan oladi. */
  onEnrolled: (leadId: string) => void
}

const errorText = (e: unknown, fallback: string) =>
  (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback

/**
 * Lid → o'quvchi, to'g'ridan-to'g'ri sinfga (admission-and-testing.md §6.1).
 *
 * Alohida forma YO'Q: oddiy "Yangi o'quvchi" formasi (`StudentFormModal`) lid
 * ma'lumotlari bilan oldindan to'ldirib ochiladi — ya'ni sinf, vasiylar va
 * hujjatlar o'sha joyda, o'sha qoidalar bilan. Sinf lidning "nechinchi sinfga"
 * darajasidagi birinchi sinf bilan tanlanadi; xodim uni o'zgartira oladi.
 *
 * Saqlangach lid O'CHIRILADI (mijoz qarori, 2026-09-22) — ma'lumotlari endi
 * o'quvchida; voronka esa uni "o'quvchiga aylandi" sonida sanaydi.
 *
 * Lidlar doskasining fayllari dizayn bo'yicha muzlatilgan (CLAUDE.md), shuning
 * uchun bu komponent o'sha papkadan TASHQARIDA turadi.
 */
export function LeadEnrolModal({ lead, onClose, onEnrolled }: Props) {
  const [classForGrade, setClassForGrade] = useState<string | null>(null)

  useEffect(() => {
    if (!lead) return
    let active = true
    getClasses()
      .then((cs) => {
        if (!active) return
        const match = cs
          .filter((c) => c.grade === lead.targetGrade)
          .sort((a, b) => a.name.localeCompare(b.name))[0]
        setClassForGrade(match?.name ?? '')
      })
      .catch(() => active && setClassForGrade(''))
    return () => {
      active = false
    }
  }, [lead])

  // Memo: `StudentFormModal` prefill o'zgarsa formani qaytadan to'ldiradi.
  const prefill = useMemo<Partial<StudentPayload> | null>(() => {
    if (!lead || classForGrade === null) return null
    // Familiya / Ism / Sharifi qismlarini forma o'zi to'liq ismdan yig'adi.
    return {
      fullName: lead.fullName,
      birthDate: lead.birthDate,
      gender: lead.gender,
      parentFullName: lead.parentFullName,
      parentPhone: lead.parentPhone,
      className: classForGrade,
    }
  }, [lead, classForGrade])

  const submit = (values: StudentPayload) => {
    if (!lead) return
    enrolLead(lead.id, values)
      .then(() => {
        onEnrolled(lead.id)
        onClose()
      })
      .catch((err) => alert(errorText(err, "Lidni o'quvchiga aylantirib bo'lmadi")))
  }

  return (
    <StudentFormModal
      open={!!lead && prefill !== null}
      onClose={onClose}
      onSubmit={submit}
      prefill={prefill}
      title="Lidni o'quvchiga aylantirish"
    />
  )
}
