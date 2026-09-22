import { useCallback, useEffect, useState } from 'react'
import { Building2, DoorOpen, Eye, EyeOff, Pencil, Plus, Trash2, Users } from 'lucide-react'
import type { BulkRoomsInput, Room, RoomKind, SaveRoomInput } from '@/api/services/rooms'
import {
  createRoom,
  createRoomsBulk,
  deleteRoom,
  getRoomBuildings,
  getRooms,
  roomKindLabels,
  updateRoom,
} from '@/api/services/rooms'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'
import { BulkRoomsModal } from './BulkRoomsModal'
import { RoomFormModal } from './RoomFormModal'

/**
 * Xonalar reyestri — docs/modules/students-parity.md §2.6 (R-1).
 * "O'quv bo'limi → Xonalar" (ruxsat: `students`, F-4 bilan bir vaqtda
 * to'g'irlandi — ilgari `schedule` edi va menyu bilan mos kelmasdi).
 *
 * BU KATALOG, JADVAL EMAS. Xonani darsga biriktirish, bandlikni ko'rish va
 * to'qnashuvlarni tekshirish jadval modulida qo'shiladi; bu yerda faqat
 * xonaning o'zi bor. Sinf kartochkasidagi "Xona" erkin matni tegilmagan.
 * Filtrlar va "Sinflardan ko'chirish" olib tashlandi (mijoz, 2026-09-23).
 *
 * O'CHIRISH va FAOLSIZLANTIRISH — ikki xil amal. Sinf ko'rsatgan xona hamon
 * o'chirilmaydi (server 400 qaytaradi). Endi "faol emas" bayrog'i bor
 * (Batch C): ishlatilgan xonani ham FAOLSIZLANTIRISH mumkin — sinf ekranlari
 * buzilmaydi, faqat yangi tanlovda ko'rinmay qoladi.
 */

const errorText = (e: unknown, fallback: string) =>
  (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback

const kindStyles: Record<RoomKind, string> = {
  classroom: 'bg-slate-100 text-slate-600',
  lab: 'bg-violet-50 text-violet-700',
  gym: 'bg-emerald-50 text-emerald-700',
  hall: 'bg-amber-50 text-amber-700',
}

export function RoomsPage() {
  const [rooms, setRooms] = useState<Room[]>([])
  const [buildings, setBuildings] = useState<string[]>([])
  const [loading, setLoading] = useState(true)
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<Room | null>(null)
  const [bulkOpen, setBulkOpen] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const [list, buildingList] = await Promise.all([
        // Filtrlar olib tashlandi (mijoz, 2026-09-23) — hamma xona, faol ham, faolsiz ham.
        getRooms({}),
        getRoomBuildings(),
      ])
      setRooms(list)
      setBuildings(buildingList)
    } finally {
      setLoading(false)
    }
  }, [])

  // setState TO'G'RIDAN-TO'G'RI chaqirilmaydi (taymer orqali) — `set-state-in-effect` izohi kerak emas.
  useEffect(() => {
    const timer = setTimeout(load, 0)
    return () => clearTimeout(timer)
  }, [load])

  const openCreate = () => {
    setEditing(null)
    setFormOpen(true)
  }

  const openEdit = (room: Room) => {
    setEditing(room)
    setFormOpen(true)
  }

  const submitForm = async (input: SaveRoomInput) => {
    const saved = editing ? await updateRoom(editing.id, input) : await createRoom(input)
    setRooms((prev) =>
      editing
        ? prev.map((r) => (r.id === saved.id ? saved : r))
        : [...prev, saved].sort(compareRooms),
    )
    if (saved.building && !buildings.includes(saved.building)) {
      setBuildings((prev) => [...prev, saved.building!].sort())
    }
    setFormOpen(false)
  }

  const submitBulk = async (input: BulkRoomsInput) => {
    const result = await createRoomsBulk(input)
    if (result.created.length > 0) await load()
    return result
  }

  const remove = async (room: Room) => {
    if (!confirm(`"${room.name}" xonasini o'chirasizmi?`)) return
    try {
      await deleteRoom(room.id)
      setRooms((prev) => prev.filter((r) => r.id !== room.id))
    } catch (err) {
      alert(errorText(err, "O'chirib bo'lmadi"))
    }
  }

  /**
   * Faollikni bir bosishda almashtirish — O'CHIRISHdan farqli, ishlatilgan
   * xona uchun ham ishlaydi (sinf ekranlari buzilmaydi). Boshqa maydonlar
   * TO'LIQ qayta yuboriladi — `SaveRoomInput` PUT'ning "berilmasa joyida
   * qoladi" qismi faqat sig'im/tur/faollikka tegishli, nom va binoga emas.
   */
  const toggleActive = async (room: Room) => {
    try {
      const saved = await updateRoom(room.id, {
        name: room.name,
        building: room.building,
        floor: room.floor,
        capacity: room.capacity,
        kind: room.kind,
        isActive: !room.isActive,
      })
      setRooms((prev) => prev.map((r) => (r.id === saved.id ? saved : r)))
    } catch (err) {
      alert(errorText(err, "Holatni almashtirib bo'lmadi"))
    }
  }

  const totalSeats = rooms.reduce((sum, r) => sum + r.capacity, 0)

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Xonalar</h1>
        <p className="text-sm text-slate-400">
          Maktab xonalari katalogi — nomi, binosi, qavati, sig'imi va turi. Dars jadvali xonani
          shu ro'yxatdan tanlaydi.
        </p>
      </div>

      {loading && rooms.length === 0 ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <Card className="p-0">
          <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 p-4">
            <p className="text-sm text-slate-500">
              {rooms.length} ta xona · jami {totalSeats} o'rin
            </p>
            <div className="flex flex-wrap gap-2">
              <Button variant="secondary" onClick={() => setBulkOpen(true)}>
                <Building2 className="h-4 w-4" /> Bir nechta
              </Button>
              <Button onClick={openCreate}>
                <Plus className="h-4 w-4" /> Yangi xona
              </Button>
            </div>
          </div>

          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-3">Nomi</th>
                  <th className="px-4 py-3">Bino</th>
                  <th className="px-4 py-3 text-center">Qavat</th>
                  <th className="px-4 py-3 text-center">Sig'imi</th>
                  <th className="px-4 py-3">Turi</th>
                  <th className="px-4 py-3 text-center">Sinflar</th>
                  <th className="px-4 py-3">Holat</th>
                  <th className="w-28 px-4 py-3" />
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rooms.map((room) => (
                  <tr key={room.id} className={cn('hover:bg-slate-50/60', !room.isActive && 'opacity-60')}>
                    <td className="px-4 py-3">
                      <span className="flex items-center gap-2 font-medium text-slate-800">
                        <DoorOpen className="h-4 w-4 text-slate-400" />
                        {room.name}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-slate-500">{room.building ?? '—'}</td>
                    <td className="px-4 py-3 text-center text-slate-500">{room.floor ?? '—'}</td>
                    <td className="px-4 py-3 text-center">
                      <span className="inline-flex items-center gap-1 text-slate-600">
                        <Users className="h-3.5 w-3.5 text-slate-400" />
                        {room.capacity}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      <span
                        className={cn(
                          'rounded px-2 py-0.5 text-xs font-medium',
                          kindStyles[room.kind],
                        )}
                      >
                        {roomKindLabels[room.kind]}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-center text-slate-500">
                      {room.usedByClasses > 0 ? room.usedByClasses : '—'}
                    </td>
                    <td className="px-4 py-3">
                      <span
                        className={cn(
                          'rounded-md px-2 py-0.5 text-xs font-medium',
                          room.isActive ? 'bg-emerald-50 text-emerald-600' : 'bg-slate-100 text-slate-400',
                        )}
                      >
                        {room.isActive ? 'Faol' : 'Faol emas'}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex justify-end gap-1">
                        <button
                          type="button"
                          title={room.isActive ? 'Faolsizlantirish' : 'Faollashtirish'}
                          onClick={() => toggleActive(room)}
                          className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600"
                        >
                          {room.isActive ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                        </button>
                        <button
                          type="button"
                          title="Tahrirlash"
                          onClick={() => openEdit(room)}
                          className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600"
                        >
                          <Pencil className="h-4 w-4" />
                        </button>
                        <button
                          type="button"
                          title={
                            room.usedByClasses > 0
                              ? "Sinf ko'rsatgan xona — avval sinfni bo'shating"
                              : "O'chirish"
                          }
                          onClick={() => remove(room)}
                          className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                        >
                          <Trash2 className="h-4 w-4" />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
                {rooms.length === 0 && (
                  <tr>
                    <td colSpan={8} className="px-4 py-12 text-center text-slate-400">
                      Hali xona yo'q — "Yangi xona" yoki "Bir nechta" bilan qo'shing.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </Card>
      )}

      <RoomFormModal
        open={formOpen}
        room={editing}
        buildings={buildings}
        onClose={() => setFormOpen(false)}
        onSubmit={submitForm}
      />
      <BulkRoomsModal
        open={bulkOpen}
        buildings={buildings}
        onClose={() => setBulkOpen(false)}
        onSubmit={submitBulk}
      />
    </div>
  )
}

function compareRooms(a: Room, b: Room) {
  return (a.building ?? '').localeCompare(b.building ?? '') || a.name.localeCompare(b.name)
}
