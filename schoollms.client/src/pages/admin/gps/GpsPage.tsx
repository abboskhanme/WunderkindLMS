import { useCallback, useEffect, useMemo, useState } from 'react'
import { MapContainer, TileLayer, Marker, Popup, Polyline, CircleMarker } from 'react-leaflet'
import L from 'leaflet'
import { Bus as BusIcon, Plus, Pencil, Trash2, MapPin, Navigation, Clock, Route as RouteIcon } from 'lucide-react'
import type { HubConnection } from '@microsoft/signalr'
import {
  getBuses, createBus, updateBus, deleteBus, getBusTrack,
  type BusLive, type Bus, type BusTrack, type SaveBusPayload,
} from '@/api/services/gps'
import { connectLiveTopic } from '@/api/services/live'
import { cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'

const defaultIcon = new L.Icon({
  iconUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon.png',
  iconRetinaUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon-2x.png',
  shadowUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-shadow.png',
  iconSize: [25, 41], iconAnchor: [12, 41], popupAnchor: [1, -34], shadowSize: [41, 41],
})

const today = () => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}
const hhmm = (iso: string) => (iso && iso.length >= 16 ? iso.slice(11, 16) : '—')
const TASHKENT: [number, number] = [41.2995, 69.2401]

export function GpsPage() {
  const [buses, setBuses] = useState<BusLive[]>([])
  const [loading, setLoading] = useState(true)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [date, setDate] = useState(today())
  const [track, setTrack] = useState<BusTrack | null>(null)
  const [trackLoading, setTrackLoading] = useState(false)
  // Xaritada avtobus yo'nalishini (iz) ko'rsatish/yashirish — test paytida qulay bo'lishi uchun.
  // Tanlov localStorage'da saqlanadi — sahifa yangilansa/qayta yuklansa default'ga qaytmaydi.
  const [showRoute, setShowRoute] = useState<boolean>(
    () => localStorage.getItem('gps:showRoute') !== '0',
  )
  useEffect(() => {
    localStorage.setItem('gps:showRoute', showRoute ? '1' : '0')
  }, [showRoute])
  const [modalOpen, setModalOpen] = useState(false)
  const [editing, setEditing] = useState<Bus | null>(null)

  const loadBuses = useCallback(() => {
    getBuses().then(setBuses).finally(() => setLoading(false))
  }, [])

  // Jonli holat — boshlang'ich yuklash + 30s fallback poll (onlayn/oflayn holatini yangilab turish uchun).
  useEffect(() => {
    loadBuses()
    const t = setInterval(loadBuses, 30000)
    return () => clearInterval(t)
  }, [loadBuses])

  // Real-time: GPS ping kelganda avtobus markerini BIR ZUMDA yangilaymiz (SignalR — kutmaymiz).
  useEffect(() => {
    let conn: HubConnection | null = null
    connectLiveTopic('gps', {
      busLocation: (...args) => {
        const p = args[0] as { busId: string; latitude: number; longitude: number; speed: number; recordedAt: string }
        setBuses((prev) =>
          prev.map((b) =>
            b.bus.id === p.busId
              ? { ...b, lat: p.latitude, lng: p.longitude, speed: p.speed, lastSeen: p.recordedAt, online: true }
              : b,
          ),
        )
      },
    })
      .then((c) => { conn = c })
      .catch(() => {})
    return () => { conn?.stop() }
  }, [])

  // Tanlangan avtobus + sana → iz.
  useEffect(() => {
    if (!selectedId) { setTrack(null); return }
    setTrackLoading(true)
    getBusTrack(selectedId, date).then(setTrack).finally(() => setTrackLoading(false))
  }, [selectedId, date])

  const selected = buses.find((b) => b.bus.id === selectedId) ?? null

  // Xarita uchun nuqtalar (polyline) va markaz.
  const path = useMemo<[number, number][]>(
    () => (track?.points ?? []).map((p) => [p.lat, p.lng]),
    [track],
  )
  const liveMarkers = useMemo(
    () => buses.filter((b) => b.lat != null && b.lng != null),
    [buses],
  )
  const center: [number, number] =
    path.length > 0 ? path[Math.floor(path.length / 2)]
    : liveMarkers.length > 0 ? [liveMarkers[0].lat!, liveMarkers[0].lng!]
    : TASHKENT
  const mapKey = `${selectedId ?? 'all'}-${date}-${path.length}-${liveMarkers.length}`

  const onSaved = () => { setModalOpen(false); setEditing(null); loadBuses() }
  const onDelete = async (b: Bus) => {
    if (!confirm(`"${b.name}" avtobusini o'chirasizmi? Barcha GPS izlari ham o'chadi.`)) return
    await deleteBus(b.id)
    if (selectedId === b.id) setSelectedId(null)
    loadBuses()
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">GPS — avtobus kuzatuvi</h1>
          <p className="text-sm text-slate-400">
            Avtobuslar qayerda yurgani, to'xtashlari (qayerda qancha turgani) va kunlik izi.
          </p>
        </div>
        <Button onClick={() => { setEditing(null); setModalOpen(true) }}>
          <Plus className="h-4 w-4" /> Avtobus qo'shish
        </Button>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
          {/* Avtobuslar ro'yxati */}
          <div className="space-y-2 lg:col-span-1">
            {buses.length === 0 && (
              <Card>
                <div className="py-8 text-center text-sm text-slate-400">
                  <BusIcon className="mx-auto mb-2 h-8 w-8 text-slate-300" />
                  Hali avtobus qo'shilmagan.
                </div>
              </Card>
            )}
            {buses.map((b) => {
              const sel = b.bus.id === selectedId
              return (
                <button
                  key={b.bus.id}
                  type="button"
                  onClick={() => setSelectedId(sel ? null : b.bus.id)}
                  className={cn(
                    'w-full rounded-xl border p-3 text-left transition-colors',
                    sel ? 'border-brand-300 bg-brand-50/50 ring-1 ring-brand-200' : 'border-slate-200 bg-white hover:bg-slate-50',
                  )}
                >
                  <div className="flex items-center justify-between gap-2">
                    <div className="flex items-center gap-2">
                      <span className={cn('h-2.5 w-2.5 rounded-full', b.online ? 'bg-emerald-500' : 'bg-slate-300')} />
                      <span className="font-semibold text-slate-800">{b.bus.name}</span>
                    </div>
                    <div className="flex items-center gap-1">
                      <span
                        role="button"
                        tabIndex={0}
                        onClick={(e) => { e.stopPropagation(); setEditing(b.bus); setModalOpen(true) }}
                        className="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-600"
                        title="Tahrirlash"
                      >
                        <Pencil className="h-3.5 w-3.5" />
                      </span>
                      <span
                        role="button"
                        tabIndex={0}
                        onClick={(e) => { e.stopPropagation(); onDelete(b.bus) }}
                        className="rounded p-1 text-slate-400 hover:bg-red-50 hover:text-red-600"
                        title="O'chirish"
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </span>
                    </div>
                  </div>
                  <div className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-0.5 text-xs text-slate-500">
                    {b.bus.plateNumber && <span>{b.bus.plateNumber}</span>}
                    {b.bus.driverName && <span>{b.bus.driverName}</span>}
                    {b.online && b.speed != null && (
                      <span className="text-emerald-600">{Math.round(b.speed)} km/s</span>
                    )}
                  </div>
                  <div className="mt-0.5 text-[11px] text-slate-400">
                    {b.lastSeen ? `Oxirgi signal: ${b.lastSeen.slice(0, 10)} ${hhmm(b.lastSeen)}` : 'Signal yo\'q'}
                    {!b.bus.deviceId && <span className="ml-1 text-amber-500">· qurilma ID yo'q</span>}
                  </div>
                </button>
              )
            })}
          </div>

          {/* Xarita + tafsilot */}
          <div className="space-y-4 lg:col-span-2">
            <Card className="p-0 overflow-hidden">
              <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-100 px-4 py-2.5">
                <span className="text-sm font-medium text-slate-700">
                  {selected ? `${selected.bus.name} — izi` : 'Barcha avtobuslar (jonli)'}
                </span>
                {selected && (
                  <div className="flex flex-wrap items-center gap-2">
                    <button
                      type="button"
                      onClick={() => setShowRoute((v) => !v)}
                      title="Xaritada avtobus yo'nalishini (iz) ko'rsatish yoki yashirish"
                      className={cn(
                        'inline-flex items-center gap-1.5 rounded-lg border px-3 py-1.5 text-sm font-medium transition-colors',
                        showRoute
                          ? 'border-brand-300 bg-brand-50 text-brand-700'
                          : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50',
                      )}
                    >
                      <RouteIcon className="h-3.5 w-3.5" />
                      {showRoute ? "Yo'nalishni yashirish" : "Yo'nalishni ko'rsatish"}
                    </button>
                    <input
                      type="date"
                      value={date}
                      onChange={(e) => setDate(e.target.value)}
                      className="rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-sm font-medium text-slate-700 outline-none focus:border-brand-400"
                    />
                  </div>
                )}
              </div>
              <div className="isolate" style={{ height: 460 }}>
                <MapContainer key={mapKey} center={center} zoom={13} style={{ height: '100%', width: '100%' }}>
                  <TileLayer
                    attribution='&copy; OpenStreetMap'
                    url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
                  />
                  {/* Tanlanmagan — barcha avtobuslarning jonli joylashuvi */}
                  {!selected && liveMarkers.map((b) => (
                    <Marker key={b.bus.id} position={[b.lat!, b.lng!]} icon={defaultIcon}>
                      <Popup>
                        <div className="text-sm">
                          <p className="font-semibold text-slate-800">{b.bus.name}</p>
                          {b.bus.driverName && <p className="text-slate-500">{b.bus.driverName}</p>}
                          <p className="text-xs text-slate-400">{b.online ? 'Onlayn' : 'Oflayn'} · {hhmm(b.lastSeen ?? '')}</p>
                        </div>
                      </Popup>
                    </Marker>
                  ))}
                  {/* Tanlangan — iz (polyline) + boshlanish/tugash + to'xtashlar (tugma bilan yashiriladi) */}
                  {selected && showRoute && path.length > 0 && (
                    <>
                      <Polyline positions={path} pathOptions={{ color: '#3b82f6', weight: 4, opacity: 0.8 }} />
                      <Marker position={path[0]} icon={defaultIcon}>
                        <Popup>Boshlanish · {hhmm(track!.points[0].time)}</Popup>
                      </Marker>
                      <Marker position={path[path.length - 1]} icon={defaultIcon}>
                        <Popup>Oxirgi · {hhmm(track!.points[track!.points.length - 1].time)}</Popup>
                      </Marker>
                      {track!.stops.map((s, i) => (
                        <CircleMarker
                          key={i}
                          center={[s.lat, s.lng]}
                          radius={9}
                          pathOptions={{ color: '#dc2626', fillColor: '#ef4444', fillOpacity: 0.7 }}
                        >
                          <Popup>
                            <div className="text-sm">
                              <p className="font-semibold text-slate-800">To'xtash #{i + 1}</p>
                              <p className="text-slate-600">{s.durationMin} daqiqa</p>
                              <p className="text-xs text-slate-400">{hhmm(s.arrivedAt)} — {hhmm(s.departedAt)}</p>
                              <a
                                href={`https://www.google.com/maps?q=${s.lat},${s.lng}`}
                                target="_blank" rel="noopener noreferrer"
                                className="mt-1 inline-block text-xs text-brand-600 hover:underline"
                              >
                                Xaritada ochish →
                              </a>
                            </div>
                          </Popup>
                        </CircleMarker>
                      ))}
                    </>
                  )}
                </MapContainer>
              </div>
            </Card>

            {/* Tanlangan avtobus jamlamasi + to'xtashlar */}
            {selected && (
              trackLoading ? (
                <Loader label="Iz yuklanmoqda..." />
              ) : track && track.points.length > 0 ? (
                <>
                  <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
                    <Summary icon={RouteIcon} label="Masofa" value={`${track.distanceKm} km`} />
                    <Summary icon={Navigation} label="Harakatda" value={`${track.movingMin} daq`} />
                    <Summary icon={MapPin} label="To'xtashlar" value={`${track.stops.length}`} />
                    <Summary icon={Clock} label="To'xtab turgan" value={`${track.stoppedMin} daq`} />
                  </div>
                  <Card className="p-0">
                    <div className="overflow-x-auto">
                      <table className="w-full text-left text-sm">
                        <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                          <tr>
                            <th className="px-4 py-2.5">#</th>
                            <th className="px-4 py-2.5">Kelgan</th>
                            <th className="px-4 py-2.5">Ketgan</th>
                            <th className="px-4 py-2.5">Davomiyligi</th>
                            <th className="px-4 py-2.5">Joy</th>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-slate-100">
                          {track.stops.map((s, i) => (
                            <tr key={i} className="hover:bg-slate-50/60">
                              <td className="px-4 py-2.5 text-slate-400">{i + 1}</td>
                              <td className="px-4 py-2.5 font-medium text-slate-700">{hhmm(s.arrivedAt)}</td>
                              <td className="px-4 py-2.5 text-slate-600">{hhmm(s.departedAt)}</td>
                              <td className="px-4 py-2.5 font-semibold text-slate-800">{s.durationMin} daq</td>
                              <td className="px-4 py-2.5">
                                <a
                                  href={`https://www.google.com/maps?q=${s.lat},${s.lng}`}
                                  target="_blank" rel="noopener noreferrer"
                                  className="text-xs font-medium text-brand-600 hover:underline"
                                >
                                  {s.lat.toFixed(5)}, {s.lng.toFixed(5)} →
                                </a>
                              </td>
                            </tr>
                          ))}
                          {track.stops.length === 0 && (
                            <tr><td colSpan={5} className="px-4 py-8 text-center text-slate-400">To'xtash qayd etilmagan</td></tr>
                          )}
                        </tbody>
                      </table>
                    </div>
                  </Card>
                </>
              ) : (
                <Card>
                  <p className="py-8 text-center text-sm text-slate-400">Bu kun uchun GPS izi yo'q</p>
                </Card>
              )
            )}
          </div>
        </div>
      )}

      <BusFormModal
        open={modalOpen}
        bus={editing}
        onClose={() => { setModalOpen(false); setEditing(null) }}
        onSaved={onSaved}
      />
    </div>
  )
}

function Summary({ icon: Icon, label, value }: { icon: typeof MapPin; label: string; value: string }) {
  return (
    <div className="rounded-xl border border-slate-100 bg-slate-50/60 px-4 py-3">
      <div className="flex items-center gap-1.5 text-xs text-slate-400">
        <Icon className="h-3.5 w-3.5" /> {label}
      </div>
      <div className="mt-0.5 text-xl font-bold text-slate-800">{value}</div>
    </div>
  )
}

function BusFormModal({
  open, bus, onClose, onSaved,
}: { open: boolean; bus: Bus | null; onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState<SaveBusPayload>({ name: '', isActive: true })
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    if (!open) return
    setForm(bus
      ? { name: bus.name, plateNumber: bus.plateNumber, driverName: bus.driverName,
          driverPhone: bus.driverPhone, deviceId: bus.deviceId, route: bus.route,
          isActive: bus.isActive, note: bus.note }
      : { name: '', isActive: true })
  }, [open, bus])

  const set = <K extends keyof SaveBusPayload>(k: K, v: SaveBusPayload[K]) =>
    setForm((p) => ({ ...p, [k]: v }))

  const submit = async () => {
    if (!form.name.trim()) { alert('Avtobus nomi shart'); return }
    setSaving(true)
    try {
      if (bus) await updateBus(bus.id, form)
      else await createBus(form)
      onSaved()
    } catch {
      alert('Saqlashda xatolik')
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={bus ? 'Avtobusni tahrirlash' : 'Yangi avtobus'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>Bekor</Button>
          <Button onClick={submit} disabled={saving}>{saving ? 'Saqlanmoqda...' : 'Saqlash'}</Button>
        </>
      }
    >
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <Input label="Nomi *" placeholder="1-avtobus" value={form.name} onChange={(e) => set('name', e.target.value)} />
        <Input label="Davlat raqami" placeholder="01 A 123 BC" value={form.plateNumber ?? ''} onChange={(e) => set('plateNumber', e.target.value)} />
        <Input label="Haydovchi" value={form.driverName ?? ''} onChange={(e) => set('driverName', e.target.value)} />
        <Input label="Haydovchi telefoni" value={form.driverPhone ?? ''} onChange={(e) => set('driverPhone', e.target.value)} />
        <Input label="GPS qurilma ID (IMEI)" placeholder="tracker id" value={form.deviceId ?? ''} onChange={(e) => set('deviceId', e.target.value)} />
        <Input label="Marshrut" placeholder="Chilonzor — Maktab" value={form.route ?? ''} onChange={(e) => set('route', e.target.value)} />
      </div>
      <div className="mt-4">
        <Input label="Izoh" value={form.note ?? ''} onChange={(e) => set('note', e.target.value)} />
      </div>
      <label className="mt-4 inline-flex cursor-pointer items-center gap-2 text-sm font-medium text-slate-700">
        <input type="checkbox" checked={form.isActive} onChange={(e) => set('isActive', e.target.checked)} className="h-4 w-4 rounded border-slate-300 accent-brand-600" />
        Faol
      </label>
    </Modal>
  )
}
