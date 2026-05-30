import { useParams } from 'react-router-dom'
import { ComingSoon } from '@/pages/ComingSoon'
import { SchoolGradesReport } from './SchoolGradesReport'
import { ClassGradesReport } from './ClassGradesReport'
import { StudentGradesReport } from './StudentGradesReport'

/**
 * Baholar hisoboti bo'limi. Sub-bo'limlar (`:section`):
 *  - school   — Maktab bo'yicha
 *  - class    — Sinf bo'yicha
 *  - student  — O'quvchi bo'yicha
 */
export function GradesReportPage() {
  const { section = 'school' } = useParams()

  if (section === 'school') return <SchoolGradesReport />
  if (section === 'class') return <ClassGradesReport />
  if (section === 'student') return <StudentGradesReport />

  return <ComingSoon title="Baholar hisoboti" />
}
