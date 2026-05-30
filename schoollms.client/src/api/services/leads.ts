import type { Lead } from '@/types'
import { delay, uid } from '@/lib/utils'
import { api, USE_MOCK } from '../client'
import { leadsMock } from '../mock/leads'

export type LeadPayload = Omit<Lead, 'id' | 'stage'>

export async function getLeads(): Promise<Lead[]> {
  if (USE_MOCK) {
    await delay()
    return leadsMock
  }
  const { data } = await api.get<Lead[]>('/admin/leads')
  return data
}

export async function createLead(payload: LeadPayload, stage: string): Promise<Lead> {
  if (USE_MOCK) {
    await delay(300)
    return { ...payload, id: uid(), stage }
  }
  const { data } = await api.post<Lead>('/admin/leads', { ...payload, stage })
  return data
}

export async function updateLead(id: string, payload: LeadPayload): Promise<void> {
  if (USE_MOCK) {
    await delay(300)
    return
  }
  await api.put(`/admin/leads/${id}`, payload)
}

export async function updateLeadStage(id: string, stage: string): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.patch(`/admin/leads/${id}`, { stage })
}

export async function deleteLead(id: string): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.delete(`/admin/leads/${id}`)
}
