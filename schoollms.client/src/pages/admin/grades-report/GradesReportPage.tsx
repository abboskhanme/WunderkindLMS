import { useParams } from 'react-router-dom'
import { ComingSoon } from '@/pages/ComingSoon'
import { SchoolGradesReport } from './SchoolGradesReport'
import { ClassGradesReport } from './ClassGradesReport'
import { StudentGradesReport } from './StudentGradesReport'
import { SubjectAttainmentReport } from './SubjectAttainmentReport'

/**
 * Baholar hisoboti bo'limi. Sub-bo'limlar (`:section`):
 *  - school   — Maktab bo'yicha
 *  - class    — Sinf bo'yicha
 *  - student  — O'quvchi bo'yicha
 *  - subjects — Fanlar bo'yicha (sinf × fan pivoti)
 */
export function GradesReportPage() {
  const { section = 'school' } = useParams()

  if (section === 'school') return <SchoolGradesReport />
  if (section === 'class') return <ClassGradesReport />
  if (section === 'student') return <StudentGradesReport />
  if (section === 'subjects') return <SubjectAttainmentReport />

  return <ComingSoon title="Baholar hisoboti" />
}
