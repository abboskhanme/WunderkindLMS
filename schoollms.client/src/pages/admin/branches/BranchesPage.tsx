import { useEffect, useState } from 'react'
import { MapContainer, TileLayer, Marker, Circle, useMapEvents } from 'react-leaflet'
import L from 'leaflet'
import { Plus, Pencil, Trash2, MapPin } from 'lucide-react'
import type { Branch } from '@/types'
import {
  getBranches,
  createBranch,
  updateBranch,
  deleteBranch,
  type BranchPayload,
} from '@/api/services/branches'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Input } from '@/components/ui/Input'

// Leaflet marker ikoni Vite bilan to'g'ri yuklanmaydi — CDN'dan ko'rsatamiz (LocationPage'dagidek).
const markerIcon = new L.Icon({
  iconUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon.png',
  iconRetinaUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon-2x.png',
  shadowUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-shadow.png',
  iconSize: [25, 41],
  iconAnchor: [12, 41],
})

// Toshkent markazi — yangi filial uchun standart ko'rinish.
const DEFAULT_CENTER: [number, number] = [41.311081, 69.279737]

const empty: BranchPayload = { name: '', address: '', latitude: 0, longitude: 0, radiusMeters: 100 }

export function BranchesPage() {
  const [branches, setBranches] = useState<Branch[]>([])
  const [loading, setLoading] = useState(true)
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<Branch | null>(null)
  const [form, setForm] = useState<BranchPayload>(empty)

  useEffect(() => {
    getBranches()
      .then(setBranches)
      .finally(() => setLoading(false))
  }, [])

  const openCreate = () => {
    setEditing(null)
    setForm(empty)
    setFormOpen(true)
  }
  const openEdit = (b: Branch) => {
    setEditing(b)
    setForm({
      name: b.name,
      address: b.address,
      latitude: b.latitude,
      longitude: b.longitude,
      radiusMeters: b.radiusMeters,
    })
    setFormOpen(true)
  }

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!form.name.trim()) return
    if (editing) {
      updateBranch(editing.id, form).then((u) =>
        setBranches((p) => p.map((x) => (x.id === u.id ? u : x))),
      )
    } else {
      createBranch(form).then((c) => setBranches((p) => [...p, c]))
    }
    setFormOpen(false)
  }

  const handleDelete = (b: Branch) => {
    if (!confirm(`"${b.name}" filialini o'chirasizmi?`)) return
    deleteBranch(b.id).then(() => setBranches((p) => p.filter((x) => x.id !== b.id)))
  }

  const hasPoint = form.latitude !== 0 || form.longitude !== 0

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Filiallar</h1>
          <p className="text-sm text-slate-400">Filial nomi, manzil, joylashuv (xarita) va radius</p>
        </div>
        <Button onClick={openCreate}>
          <Plus className="h-4 w-4" /> Yangi filial
        </Button>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : branches.length === 0 ? (
        <Card>
          <p className="py-10 text-center text-slate-400">Hali filial qo'shilmagan</p>
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
          {branches.map((b) => (
            <Card key={b.id} className="flex flex-col gap-2">
              <div className="flex items-start justify-between gap-2">
                <div>
                  <p className="font-semibold text-slate-800">{b.name}</p>
                  <p className="text-sm text-slate-500">{b.address || '—'}</p>
                </div>
                <div className="flex items-center gap-0.5">
                  <button
                    type="button"
                    title="Tahrirlash"
                    onClick={() => openEdit(b)}
                    className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                  >
                    <Pencil className="h-4 w-4" />
                  </button>
                  <button
                    type="button"
                    title="O'chirish"
                    onClick={() => handleDelete(b)}
                    className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                  >
                    <Trash2 className="h-4 w-4" />
                  </button>
                </div>
              </div>
              <div className="flex items-center gap-2 text-xs text-slate-400">
                <MapPin className="h-3.5 w-3.5" />
                {b.latitude.toFixed(5)}, {b.longitude.toFixed(5)} · radius {b.radiusMeters} m
              </div>
              <div className="h-40 overflow-hidden rounded-lg">
                <MapContainer
                  center={[b.latitude || DEFAULT_CENTER[0], b.longitude || DEFAULT_CENTER[1]]}
                  zoom={14}
                  scrollWheelZoom={false}
                  style={{ height: '100%', width: '100%' }}
                >
                  <TileLayer url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png" />
                  {(b.latitude !== 0 || b.longitude !== 0) && (
                    <>
                      <Marker position={[b.latitude, b.longitude]} icon={markerIcon} />
                      <Circle center={[b.latitude, b.longitude]} radius={b.radiusMeters} />
                    </>
                  )}
                </MapContainer>
              </div>
            </Card>
          ))}
        </div>
      )}

      <Modal
        open={formOpen}
        onClose={() => setFormOpen(false)}
        size="lg"
        title={editing ? 'Filialni tahrirlash' : 'Yangi filial'}
        footer={
          <>
            <Button variant="secondary" onClick={() => setFormOpen(false)}>
              Bekor qilish
            </Button>
            <Button type="submit" form="branch-form">
              Saqlash
            </Button>
          </>
        }
      >
        <form id="branch-form" onSubmit={handleSubmit} className="space-y-4">
          <Input
            label="Filial nomi"
            required
            value={form.name}
            onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))}
          />
          <Input
            label="Manzil"
            value={form.address}
            onChange={(e) => setForm((f) => ({ ...f, address: e.target.value }))}
          />
          <Input
            label="Radius (metr)"
            type="number"
            min={10}
            step={10}
            value={form.radiusMeters}
            onChange={(e) => setForm((f) => ({ ...f, radiusMeters: Number(e.target.value) }))}
          />
          <div>
            <label className="mb-1 block text-sm font-medium text-slate-600">
              Joylashuv — xaritani bosing yoki markerni suring
            </label>
            <div className="h-72 overflow-hidden rounded-lg border border-slate-200">
              <MapContainer
                key={editing?.id ?? 'new'}
                center={hasPoint ? [form.latitude, form.longitude] : DEFAULT_CENTER}
                zoom={14}
                style={{ height: '100%', width: '100%' }}
              >
                <TileLayer url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png" />
                <ClickPicker onPick={(lat, lng) => setForm((f) => ({ ...f, latitude: lat, longitude: lng }))} />
                {hasPoint && (
                  <>
                    <Marker
                      position={[form.latitude, form.longitude]}
                      icon={markerIcon}
                      draggable
                      eventHandlers={{
                        dragend: (e) => {
                          const m = e.target.getLatLng()
                          setForm((f) => ({ ...f, latitude: m.lat, longitude: m.lng }))
                        },
                      }}
                    />
                    <Circle center={[form.latitude, form.longitude]} radius={form.radiusMeters} />
                  </>
                )}
              </MapContainer>
            </div>
            <p className="mt-1 text-xs text-slate-400">
              {hasPoint
                ? `Tanlangan: ${form.latitude.toFixed(5)}, ${form.longitude.toFixed(5)}`
                : 'Joylashuv tanlanmagan — xaritani bosing.'}
            </p>
          </div>
        </form>
      </Modal>
    </div>
  )
}

function ClickPicker({ onPick }: { onPick: (lat: number, lng: number) => void }) {
  useMapEvents({
    click(e) {
      onPick(e.latlng.lat, e.latlng.lng)
    },
  })
  return null
}
