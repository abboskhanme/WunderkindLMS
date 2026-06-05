import { api } from '../client'

export interface Bus {
  id: string
  name: string
  plateNumber: string
  driverName: string
  driverPhone: string
  deviceId: string
  route: string
  isActive: boolean
  note: string
}

export interface BusLive {
  bus: Bus
  lat?: number | null
  lng?: number | null
  speed?: number | null
  lastSeen?: string | null
  online: boolean
}

export interface TrackPoint {
  lat: number
  lng: number
  speed: number
  time: string
}

export interface BusStop {
  lat: number
  lng: number
  arrivedAt: string
  departedAt: string
  durationMin: number
}

export interface BusTrack {
  date: string
  points: TrackPoint[]
  stops: BusStop[]
  distanceKm: number
  movingMin: number
  stoppedMin: number
}

export interface SaveBusPayload {
  name: string
  plateNumber?: string
  driverName?: string
  driverPhone?: string
  deviceId?: string
  route?: string
  isActive: boolean
  note?: string
}

export async function getBuses(): Promise<BusLive[]> {
  const { data } = await api.get<BusLive[]>('/admin/gps/buses')
  return data
}

export async function createBus(payload: SaveBusPayload): Promise<Bus> {
  const { data } = await api.post<Bus>('/admin/gps/buses', payload)
  return data
}

export async function updateBus(id: string, payload: SaveBusPayload): Promise<Bus> {
  const { data } = await api.put<Bus>(`/admin/gps/buses/${id}`, payload)
  return data
}

export async function deleteBus(id: string): Promise<void> {
  await api.delete(`/admin/gps/buses/${id}`)
}

export async function getBusTrack(id: string, date: string): Promise<BusTrack> {
  const { data } = await api.get<BusTrack>(`/admin/gps/buses/${id}/track`, { params: { date } })
  return data
}
