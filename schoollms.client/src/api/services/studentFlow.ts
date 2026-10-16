import type { StudentFlowReport, StudentFlowRow } from '@/types'
import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'

/* =========================================================================
 *  O'quvchilar oqimi — docs/modules/student-flow.md (client, 2026-10-15).
 *  Kelganlar, faollashganlar (birinchi to'lov), ketganlar va qaytganlar,
 *  ketish sabablari kesimida.
 * ========================================================================= */

const empty = (from: string, to: string): StudentFlowReport => ({
  from,
  to,
  classId: null,
  className: null,
  totals: { new: 0, activated: 0, left: 0, returned: 0, net: 0, headcountStart: 0, headcountEnd: 0 },
  byReason: [],
  byClass: [],
  newList: [],
  activatedList: [],
  leftList: [],
  returnedList: [],
})

export async function getStudentFlow(from: string, to: string, classId?: string): Promise<StudentFlowReport> {
  if (USE_MOCK) {
    await delay()
    return empty(from, to)
  }
  const { data } = await api.get<StudentFlowReport>('/admin/reports/student-flow', {
    params: { from, to, classId: classId || undefined },
  })
  return data
}

/** Hisobot Excel'da (server tuzadi): umumiy, sabablar, sinflar va to'rtta ro'yxat. */
export async function downloadStudentFlow(from: string, to: string, classId?: string): Promise<void> {
  if (USE_MOCK) {
    alert('Excel faqat real serverda ishlaydi (VITE_USE_MOCK=false).')
    return
  }
  const res = await api.get('/admin/reports/student-flow/export', {
    params: { from, to, classId: classId || undefined },
    responseType: 'blob',
  })
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `oquvchilar_oqimi_${from}_${to}.xlsx`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

/** Ketgan o'quvchining sababini keyin qo'yish / tuzatish. `reasonId` null — "Boshqa". */
export async function updateLeaveReason(
  eventId: string,
  reasonId: string | null,
  reasonText: string | null,
): Promise<StudentFlowRow> {
  const { data } = await api.put<StudentFlowRow>(`/admin/reports/student-flow/events/${eventId}/reason`, {
    reasonId,
    reasonText,
  })
  return data
}
