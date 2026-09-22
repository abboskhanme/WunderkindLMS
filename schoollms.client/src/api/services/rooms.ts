import { api, USE_MOCK } from '../client'

/* =========================================================================
 *  Xonalar reyestri — docs/modules/students-parity.md §2.6 (R-1).
 *
 *  Bu KATALOG: nom, bino, qavat, sig'im, tur. Xonani darsga biriktirish va
 *  to'qnashuvlarni tekshirish — jadval modulining ishi; xona jihozlari —
 *  WareHouse. Sinf kartochkasidagi "Xona" erkin matni O'ZGARMAYDI.
 *
 *  DTO nomlari backend bilan AYNAN bir xil (camelCase).
 * ========================================================================= */

/** Xona turlari — backenddagi `RoomKind` bilan bir xil ro'yxat. */
export type RoomKind = 'classroom' | 'lab' | 'gym' | 'hall'

export const roomKindLabels: Record<RoomKind, string> = {
  classroom: 'Sinf xonasi',
  lab: 'Laboratoriya',
  gym: 'Sport zali',
  hall: 'Yig‘ilish zali',
}

export const roomKindOptions: { value: RoomKind; label: string }[] = [
  { value: 'classroom', label: roomKindLabels.classroom },
  { value: 'lab', label: roomKindLabels.lab },
  { value: 'gym', label: roomKindLabels.gym },
  { value: 'hall', label: roomKindLabels.hall },
]

/** EduSchool ham ommaviy yaratishda shu chegarada to'xtatadi (§2.6.1). */
export const MAX_BULK_ROOMS = 50

export interface Room {
  id: string
  name: string
  building: string | null
  floor: number | null
  capacity: number
  kind: RoomKind
  /** Nechta sinf shu xonani ko'rsatgan. 0 dan katta bo'lsa o'chirib bo'lmaydi. */
  usedByClasses: number
  /**
   * Faolmi (Batch C qo'shimchasi, R-1 dan keyin). `false` — "ishlatilmaydi,
   * lekin saqlanadi": yangi jadval/sinfda ko'rinmaydi, uni ko'rsatgan eski
   * sinf esa joyida qoladi.
   */
  isActive: boolean
}

export interface SaveRoomInput {
  name: string
  building?: string | null
  floor?: number | null
  capacity?: number | null
  kind?: RoomKind
  /** Berilmasa — joyida qoladi (yaratishda sukut — faol). */
  isActive?: boolean
}

export interface RoomFilter {
  search?: string
  building?: string
  kind?: RoomKind
  /** Berilmasa — hammasi (faol ham, faolsiz ham). */
  isActive?: boolean
}

export interface BulkRoomsInput {
  count: number
  startFrom?: number | null
  prefix?: string
  building?: string | null
  floor?: number | null
  capacity?: number | null
  kind?: RoomKind
}

export interface BulkRoomsResult {
  created: Room[]
  /** Nomi band bo'lgani uchun yaratilmagan xonalar. */
  skipped: string[]
}

export async function getRooms(filter: RoomFilter = {}): Promise<Room[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<Room[]>('/admin/rooms', { params: filter })
  return data
}

/** Binolar ro'yxati (filtr uchun). Alohida "binolar" katalogi yo'q — §2.6.3. */
export async function getRoomBuildings(): Promise<string[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<string[]>('/admin/rooms/buildings')
  return data
}

export async function createRoom(input: SaveRoomInput): Promise<Room> {
  const { data } = await api.post<Room>('/admin/rooms', input)
  return data
}

export async function updateRoom(id: string, input: SaveRoomInput): Promise<Room> {
  const { data } = await api.put<Room>(`/admin/rooms/${id}`, input)
  return data
}

export async function deleteRoom(id: string): Promise<void> {
  await api.delete(`/admin/rooms/${id}`)
}

/** Ketma-ket raqamlangan N ta xona (EduSchool `rooms/multiple`). */
export async function createRoomsBulk(input: BulkRoomsInput): Promise<BulkRoomsResult> {
  const { data } = await api.post<BulkRoomsResult>('/admin/rooms/multiple', input)
  return data
}
