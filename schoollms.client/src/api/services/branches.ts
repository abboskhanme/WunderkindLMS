import type { Branch } from '@/types'
import { api, USE_MOCK } from '../client'

export type BranchPayload = Omit<Branch, 'id' | 'createdAt'>

export async function getBranches(): Promise<Branch[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<Branch[]>('/admin/branches')
  return data
}

export async function createBranch(payload: BranchPayload): Promise<Branch> {
  const { data } = await api.post<Branch>('/admin/branches', payload)
  return data
}

export async function updateBranch(id: string, payload: BranchPayload): Promise<Branch> {
  const { data } = await api.put<Branch>(`/admin/branches/${id}`, payload)
  return data
}

export async function deleteBranch(id: string): Promise<void> {
  await api.delete(`/admin/branches/${id}`)
}
