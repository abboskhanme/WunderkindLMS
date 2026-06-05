import { api } from '../client'

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api'

export interface Camera {
  id: string
  name: string
  location: string
  rtspUrl: string
  rtspSubUrl: string
  /** Yozuv necha kun saqlansin (0 = cheksiz) */
  retentionDays: number
  isActive: boolean
  note: string
}

export interface SaveCameraPayload {
  name: string
  location?: string
  rtspUrl: string
  rtspSubUrl?: string
  retentionDays: number
  isActive: boolean
  note?: string
}

export async function getCameras(): Promise<Camera[]> {
  const { data } = await api.get<Camera[]>('/admin/cameras')
  return data
}

export async function createCamera(payload: SaveCameraPayload): Promise<Camera> {
  const { data } = await api.post<Camera>('/admin/cameras', payload)
  return data
}

export async function updateCamera(id: string, payload: SaveCameraPayload): Promise<Camera> {
  const { data } = await api.put<Camera>(`/admin/cameras/${id}`, payload)
  return data
}

export async function deleteCamera(id: string): Promise<void> {
  await api.delete(`/admin/cameras/${id}`)
}

/** Jonli HLS pleylist manzili (hls.js shu manzilni autentifikatsiya bilan oladi). */
export function cameraLiveUrl(id: string): string {
  return `${API_BASE}/admin/cameras/${id}/index.m3u8`
}

/** Playback/clip — yozuvdan MP4 (blob). start "YYYY-MM-DDTHH:mm:ss", duration soniyada. */
export async function getClipBlob(id: string, start: string, durationSec: number): Promise<Blob> {
  const { data } = await api.get(`/admin/cameras/${id}/clip`, {
    params: { start, duration: durationSec },
    responseType: 'blob',
  })
  return data as Blob
}
