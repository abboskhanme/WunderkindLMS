import { useEffect, useState } from 'react'
import type { BulkRoomsInput, BulkRoomsResult, RoomKind } from '@/api/services/rooms'
import { MAX_BULK_ROOMS, roomKindOptions } from '@/api/services/rooms'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'

interface Props {
  open: boolean
  buildings: string[]
  onClose: () => void
  onSubmit: (input: BulkRoomsInput) => Promise<BulkRoomsResult>
}

/**
 * Ommaviy yaratish — EduSchool'dagi "Xonalar qo'shish" (`rooms/multiple`,
 * §2.6.1). Yangi bino ochilganda 30 ta xonani bittalab kiritish o'rniga
 * bitta forma: nechta, qaysi raqamdan, qanday prefiks bilan.
 *
 * Nomi band bo'lgan xonalar YARATILMAYDI va natijada alohida ko'rsatiladi —
 * butun so'rov bitta takroriy nom tufayli bekor bo'lmaydi.
 */
export function BulkRoomsModal({ open, buildings, onClose, onSubmit }: Props) {
  const [count, setCount] = useState('10')
  const [startFrom, setStartFrom] = useState('')
  const [prefix, setPrefix] = useState('')
  const [building, setBuilding] = useState('')
  const [floor, setFloor] = useState('')
  const [capacity, setCapacity] = useState('30')
  const [kind, setKind] = useState<RoomKind>('classroom')
  const [result, setResult] = useState<BulkRoomsResult | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal har ochilganda toza forma (maqsadli)
    setCount('10')
    setStartFrom('')
    setPrefix('')
    setBuilding('')
    setFloor('')
    setCapacity('30')
    setKind('classroom')
    setResult(null)
    setError(null)
  }, [open])

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    const n = Number(count)
    if (!Number.isInteger(n) || n < 1 || n > MAX_BULK_ROOMS) {
      setError(`Nechta xona: 1 dan ${MAX_BULK_ROOMS} gacha`)
      return
    }
    setBusy(true)
    setError(null)
    try {
      setResult(
        await onSubmit({
          count: n,
          startFrom: startFrom.trim() === '' ? null : Number(startFrom),
          prefix: prefix.trim(),
          building: building.trim() || null,
          floor: floor.trim() === '' ? null : Number(floor),
          capacity: capacity.trim() === '' ? null : Number(capacity),
          kind,
        }),
      )
    } catch (err) {
      setError(
        (err as { response?: { data?: { message?: string } } })?.response?.data?.message ??
          "Yaratib bo'lmadi",
      )
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Bir nechta xona qo'shish"
      footer={
        result ? (
          <Button onClick={onClose}>Yopish</Button>
        ) : (
          <>
            <Button variant="secondary" onClick={onClose} disabled={busy}>
              Bekor qilish
            </Button>
            <Button type="submit" form="bulk-rooms-form" disabled={busy}>
              {busy ? 'Yaratilmoqda...' : 'Yaratish'}
            </Button>
          </>
        )
      }
    >
      {result ? (
        <div className="space-y-3">
          <p className="text-sm font-medium text-emerald-700">
            {result.created.length} ta xona yaratildi
          </p>
          {result.created.length > 0 && (
            <p className="text-sm text-slate-500">
              {result.created.map((r) => r.name).join(', ')}
            </p>
          )}
          {result.skipped.length > 0 && (
            <div className="rounded-lg bg-amber-50 p-3">
              <p className="text-xs font-medium text-amber-700">
                {result.skipped.length} ta nom band edi — o'tkazib yuborildi:
              </p>
              <p className="mt-1 text-xs text-amber-600">{result.skipped.join(', ')}</p>
            </div>
          )}
        </div>
      ) : (
        <form id="bulk-rooms-form" onSubmit={submit} className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-3">
            <Input
              label="Nechta"
              required
              type="number"
              min={1}
              max={MAX_BULK_ROOMS}
              value={count}
              onChange={(e) => setCount(e.target.value)}
            />
            <Input
              label="Qaysi raqamdan"
              type="number"
              min={0}
              value={startFrom}
              onChange={(e) => setStartFrom(e.target.value)}
              placeholder="avtomatik"
            />
            <Input
              label="Prefiks"
              value={prefix}
              onChange={(e) => setPrefix(e.target.value)}
              placeholder="A-"
            />
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <div>
              <Input
                label="Bino"
                value={building}
                onChange={(e) => setBuilding(e.target.value)}
                list="bulk-room-buildings"
              />
              <datalist id="bulk-room-buildings">
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
            />
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <Input
              label="Sig'imi"
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

          <p className="rounded-lg bg-slate-50 p-3 text-xs text-slate-500">
            Nomlar prefiks va ketma-ket raqamdan hosil bo'ladi: <code>A-101</code>,{' '}
            <code>A-102</code>, ... Raqam ko'rsatilmasa — mavjud eng katta raqamdan keyingisi.
          </p>

          {error && <p className="text-sm text-red-600">{error}</p>}
        </form>
      )}
    </Modal>
  )
}
