import { api } from '../client'

/** `GET /api/admin/search` — yuqori paneldagi umumiy qidiruv. */
export type GlobalSearchKind = 'student' | 'teacher' | 'class' | 'group' | 'parent' | 'lead'

export interface GlobalSearchHit {
  kind: GlobalSearchKind
  id: string
  title: string
  subtitle: string
  /** Bosilganda ochiladigan admin sahifasi. */
  url: string
  archived: boolean
}

export async function globalSearch(q: string, signal?: AbortSignal): Promise<GlobalSearchHit[]> {
  const { data } = await api.get<GlobalSearchHit[]>('/admin/search', { params: { q }, signal })
  return data
}
