import type { LmsMaterial, LmsProgressReport, LmsSubject, LmsTopic, LmsUnlockMode } from '@/types'
import { api } from '../client'

/* ─── Fanlar ─────────────────────────────────────────────── */

/** Barcha fanlar yoki bitta sinf fanlari */
export async function getLmsSubjects(classId?: string): Promise<LmsSubject[]> {
  const { data } = await api.get<LmsSubject[]>('/admin/lms/subjects', {
    params: classId ? { classId } : undefined,
  })
  return data
}

export async function createLmsSubject(payload: SaveSubjectPayload): Promise<LmsSubject> {
  const { data } = await api.post<LmsSubject>('/admin/lms/subjects', payload)
  return data
}

export async function updateLmsSubject(id: string, payload: Omit<SaveSubjectPayload, 'classId'>): Promise<void> {
  await api.put(`/admin/lms/subjects/${id}`, payload)
}

export async function deleteLmsSubject(id: string): Promise<void> {
  await api.delete(`/admin/lms/subjects/${id}`)
}

/* ─── Mavzular ───────────────────────────────────────────── */

export async function getLmsTopics(subjectId: string): Promise<LmsTopic[]> {
  const { data } = await api.get<LmsTopic[]>(`/admin/lms/subjects/${subjectId}/topics`)
  return data
}

export async function createLmsTopic(subjectId: string, payload: SaveTopicPayload): Promise<LmsTopic> {
  const { data } = await api.post<LmsTopic>(`/admin/lms/subjects/${subjectId}/topics`, payload)
  return data
}

export async function updateLmsTopic(id: string, payload: SaveTopicPayload): Promise<void> {
  await api.put(`/admin/lms/topics/${id}`, payload)
}

export async function deleteLmsTopic(id: string): Promise<void> {
  await api.delete(`/admin/lms/topics/${id}`)
}

export async function reorderLmsTopics(subjectId: string, topicIds: string[]): Promise<void> {
  await api.put(`/admin/lms/subjects/${subjectId}/topics/reorder`, { topicIds })
}

/* ─── O'quvchilar progressi ──────────────────────────────── */

/** Sinf o'quvchilari × mavzular progress matritsasi (kim qaysi mavzuni tugatgan). */
export async function getLmsProgress(subjectId: string): Promise<LmsProgressReport> {
  const { data } = await api.get<LmsProgressReport>(`/admin/lms/subjects/${subjectId}/progress`)
  return data
}

/* ─── Fayl yuklash ───────────────────────────────────────── */

export async function uploadLmsMaterial(file: File): Promise<LmsMaterial> {
  const form = new FormData()
  form.append('file', file)
  // Server upload javobi `id` qaytarmaydi — modal `key`/o'chirish uchun klient id beramiz.
  const { data } = await api.post<Omit<LmsMaterial, 'id'>>('/admin/lms/uploads', form, {
    headers: { 'Content-Type': 'multipart/form-data' },
  })
  return { ...data, id: crypto.randomUUID() }
}

/* ─── Payload turlari ────────────────────────────────────── */

export interface SaveSubjectPayload {
  classId: string
  title: string
  description: string
  unlockMode: LmsUnlockMode
  batchSize: number
}

export interface MaterialInput {
  id: string
  name: string
  url: string
  size: number
  contentType: string
}

export interface SaveTopicPayload {
  title: string
  description: string
  videoUrl: string
  textContent: string
  materials: MaterialInput[]
}
