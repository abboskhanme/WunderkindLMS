import type { TeacherReportRow, TeacherReportDetail } from '@/types'
import { api, USE_MOCK } from '../client'

/** Barcha o'qituvchilar faollik hisoboti. quarter=0 (yoki berilmasa) — barcha choraklar. */
export async function getTeacherReport(quarter = 0): Promise<TeacherReportRow[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<TeacherReportRow[]>('/admin/teacher-reports', {
    params: { quarter },
  })
  return data
}

/** Bitta o'qituvchining batafsil hisoboti (sinf/fan yoyilmasi). */
export async function getTeacherReportDetail(
  id: string,
  quarter = 0,
): Promise<TeacherReportDetail> {
  const { data } = await api.get<TeacherReportDetail>(`/admin/teacher-reports/${id}`, {
    params: { quarter },
  })
  return data
}
