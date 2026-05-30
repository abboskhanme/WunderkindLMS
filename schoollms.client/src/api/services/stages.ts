import type { Stage, StageColor } from '@/types'
import { delay, uid } from '@/lib/utils'
import { api, USE_MOCK } from '../client'
import { stagesMock } from '../mock/stages'

export interface StagePayload {
  title: string
  color: StageColor
}

export async function getStages(): Promise<Stage[]> {
  if (USE_MOCK) {
    await delay()
    return stagesMock
  }
  const { data } = await api.get<Stage[]>('/admin/lead-stages')
  return data
}

export async function createStage(payload: StagePayload): Promise<Stage> {
  if (USE_MOCK) {
    await delay(200)
    return { id: uid(), ...payload }
  }
  const { data } = await api.post<Stage>('/admin/lead-stages', payload)
  return data
}

export async function updateStage(id: string, payload: StagePayload): Promise<Stage> {
  if (USE_MOCK) {
    await delay(200)
    return { id, ...payload }
  }
  const { data } = await api.put<Stage>(`/admin/lead-stages/${id}`, payload)
  return data
}

export async function deleteStage(id: string): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.delete(`/admin/lead-stages/${id}`)
}

/** Ustunlar tartibini saqlash */
export async function reorderStages(ids: string[]): Promise<void> {
  if (USE_MOCK) {
    await delay(150)
    return
  }
  await api.patch('/admin/lead-stages/reorder', { ids })
}
