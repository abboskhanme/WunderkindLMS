import type { WeekAssignment } from '@/types'
import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'
import { weekAssignmentsMock } from '../mock/weekAssignments'

const key = (classId: string, quarter: number) => `${classId}-${quarter}`

export async function getWeekAssignments(
  classId: string,
  quarter: number,
): Promise<WeekAssignment[]> {
  if (USE_MOCK) {
    await delay()
    return weekAssignmentsMock[key(classId, quarter)] ?? []
  }
  const { data } = await api.get<WeekAssignment[]>(
    `/admin/classes/${classId}/week-assignments`,
    { params: { quarter } },
  )
  return data
}

export async function saveWeekAssignments(
  classId: string,
  quarter: number,
  assignments: WeekAssignment[],
): Promise<void> {
  if (USE_MOCK) {
    await delay(150)
    weekAssignmentsMock[key(classId, quarter)] = assignments
    return
  }
  await api.put(`/admin/classes/${classId}/week-assignments`, { quarter, assignments })
}
