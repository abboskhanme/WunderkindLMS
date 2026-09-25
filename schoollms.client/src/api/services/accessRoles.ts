import type { AccessRole } from '@/types'
import { api, USE_MOCK } from '../client'

export interface AccessRolePayload {
  name: string
  description: string
  permissions: string[]
}

export async function getAccessRoles(): Promise<AccessRole[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<AccessRole[]>('/admin/roles')
  return data
}

export async function createAccessRole(payload: AccessRolePayload): Promise<AccessRole> {
  const { data } = await api.post<AccessRole>('/admin/roles', payload)
  return data
}

export async function updateAccessRole(id: string, payload: AccessRolePayload): Promise<AccessRole> {
  const { data } = await api.put<AccessRole>(`/admin/roles/${id}`, payload)
  return data
}

export async function deleteAccessRole(id: string): Promise<void> {
  await api.delete(`/admin/roles/${id}`)
}
