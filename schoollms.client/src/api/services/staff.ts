import type { Staff, Credentials } from '@/types'
import { api, USE_MOCK } from '../client'

export interface StaffPayload {
  fullName: string
  position: string
  newPassword?: string
}

export async function getStaff(): Promise<Staff[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<Staff[]>('/admin/staff')
  return data
}

export async function createStaff(payload: StaffPayload): Promise<Staff> {
  const { data } = await api.post<Staff>('/admin/staff', payload)
  return data
}

export async function updateStaff(id: string, payload: StaffPayload): Promise<Staff> {
  const { data } = await api.put<Staff>(`/admin/staff/${id}`, payload)
  return data
}

export async function deleteStaff(id: string): Promise<void> {
  await api.delete(`/admin/staff/${id}`)
}

export async function getStaffCredentials(id: string): Promise<Credentials> {
  const { data } = await api.get<Credentials>(`/admin/staff/${id}/credentials`)
  return data
}

/** Xodim bo'lim ruxsatlarini saqlash (faqat superadmin) */
export async function setStaffPermissions(id: string, permissions: string[]): Promise<Staff> {
  const { data } = await api.put<Staff>(`/admin/staff/${id}/permissions`, { permissions })
  return data
}
