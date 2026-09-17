import { useEffect, useState } from 'react'
import type { Room, RoomKind, SaveRoomInput } from '@/api/services/rooms'
import { roomKindOptions } from '@/api/services/rooms'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'

interface Props {
  open: boolean
  /** null — yangi xona. */
  room: Room | null
  buildings: string[]
  onClose: () => void
  onSubmit: (input: SaveRoomInput) => Promise<void>
}

/**
 * Xona formasi — docs/modules/students-parity.md §2.6 (R-1).
 *
 * EduSchool formasida uchta maydon bor (nom, bino, sig'im); bizda yana ikkitasi:
 * qavat va TUR. Tur kerak, chunki jadval moduli "sport zali" bilan "laboratoriya"ni
 * ajrata olishi kerak, aks holda darsni noto'g'ri xonaga qo'yish hech narsa
 * bilan to'xtatilmasdi.
 */
export function RoomFormModal({ open, room, buildings, onClose, onSubmit }: Props) {
  const [name, setName] = useState('')
  const [building, setBuilding] = useState('')
  const [floor, setFloor] = useState('')
  const [capacity, setCapacity] = useState('30')
  const [kind, setKind] = useState<RoomKind>('classroom')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda forma tanlangan xona bilan to'ldiriladi (maqsadli)
    setName(room?.name ?? '')
    setBuilding(room?.building ?? '')
    setFloor(room?.floor != null ? String(room.floor) : '')
    setCapacity(room?.capacity != null ? String(room.capacity) : '30')
    setKind(room?.kind ?? 'classroom')
    setError(null)
  }, [open, room])

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!name.trim()) {
      setError('Xona nomini yozing')
      return
    }
    setBusy(true)
    setError(null)
    try {
      await onSubmit({
        name: name.trim(),
        building: building.trim() || null,
        floor: floor.trim() === '' ? null : Number(floor),
        capacity: capacity.trim() === '' ? null : Number(capacity),
        kind,
      })
    } catch (err) {
      setError(
        (err as { response?: { data?: { message?: string } } })?.response?.data?.message ??
          "Saqlab bo'lmadi",
      )
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={room ? 'Xonani tahrirlash' : 'Yangi xona'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button type="submit" form="room-form" disabled={busy}>
            {busy ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <form id="room-form" onSubmit={submit} className="space-y-4">
        <Input
          label="Nomi"
          required
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="101, Sport zali"
        />

        <div className="grid gap-4 sm:grid-cols-2">
          <div>
            <Input
              label="Bino"
              value={building}
              onChange={(e) => setBuilding(e.target.value)}
              placeholder="Bosh bino"
              list="room-buildings"
            />
            <datalist id="room-buildings">
              {buildings.map((b) => (
                <option key={b} value={b} />
              ))}
            </datalist>
          </div>
          <Input
            label="Qavat"
            type="number"
            value={floor}
            onChange={(e) => setFloor(e.target.value)}
            placeholder="2"
          />
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <Input
            label="Sig'imi (o'quvchi)"
            type="number"
            min={1}
            max={1000}
            value={capacity}
            onChange={(e) => setCapacity(e.target.value)}
          />
          <Select label="Turi" value={kind} onChange={(e) => setKind(e.target.value as RoomKind)}>
            {roomKindOptions.map((o) => (
              <option key={o.value} value={o.value}>
                {o.label}
              </option>
            ))}
          </Select>
        </div>

        {error && <p className="text-sm text-red-600">{error}</p>}
      </form>
    </Modal>
  )
}
