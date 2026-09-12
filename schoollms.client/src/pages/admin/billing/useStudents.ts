/**
 * O'quvchilar ro'yxati — obuna va chegirma formalaridagi tanlov uchun.
 *
 * Moliya DTO'lari faqat `studentId` + `studentName` beradi (sinf nomi yo'q),
 * shuning uchun tanlov ro'yxati o'quvchilar bo'limidan olinadi va sinf nomi
 * shu yerdan qo'shiladi — "Abdullayev Amir (9-A)" ko'rinishida bir xil ismli
 * o'quvchilarni ajratish mumkin bo'lsin.
 *
 * Arxivlangan o'quvchi ro'yxatga tushmaydi: server ham unga obuna/chegirma
 * ochishni `student_archived` bilan rad etadi.
 */
import { useCallback, useEffect, useState } from 'react'
import type { Student } from '@/types'
import { getStudents } from '@/api/services/students'
import { billingErrorMessage } from '@/api/services/billingError'

export interface StudentOption {
  id: string
  fullName: string
  className: string
  /** Qidiruv uchun: "abdullayev amir 9-a" */
  search: string
}

export interface StudentsIndex {
  options: StudentOption[]
  byId: Record<string, StudentOption>
  loading: boolean
  error: string | null
  reload: () => void
}

function toOption(s: Student): StudentOption {
  return {
    id: s.id,
    fullName: s.fullName,
    className: s.className,
    search: `${s.fullName} ${s.className}`.toLocaleLowerCase('uz-Latn-UZ'),
  }
}

export function useStudentsIndex(): StudentsIndex {
  const [options, setOptions] = useState<StudentOption[]>([])
  const [byId, setById] = useState<Record<string, StudentOption>>({})
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(() => {
    setLoading(true)
    setError(null)
    getStudents()
      .then((list) => {
        const mapped = list
          .filter((s) => !s.isArchived)
          .map(toOption)
          .sort((a, b) => a.fullName.localeCompare(b.fullName, 'uz'))
        setOptions(mapped)
        setById(Object.fromEntries(mapped.map((o) => [o.id, o])))
      })
      .catch((e: unknown) =>
        setError(billingErrorMessage(e, "O'quvchilar ro'yxatini yuklab bo'lmadi")),
      )
      .finally(() => setLoading(false))
  }, [])

  // eslint-disable-next-line react-hooks/set-state-in-effect -- birinchi yuklash (loyihadagi umumiy naqsh)
  useEffect(() => reload(), [reload])

  return { options, byId, loading, error, reload }
}

/** Ro'yxatni matn bo'yicha filtrlash (ism yoki sinf). */
export function filterStudents(options: StudentOption[], query: string): StudentOption[] {
  const q = query.trim().toLocaleLowerCase('uz-Latn-UZ')
  if (!q) return options
  return options.filter((o) => o.search.includes(q))
}

/** "Abdullayev Amir (9-A)" */
export function studentLabel(o: StudentOption): string {
  return o.className ? `${o.fullName} (${o.className})` : o.fullName
}
