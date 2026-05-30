import { useEffect, useMemo, useState } from 'react'
import { MapContainer, TileLayer, Marker, Popup } from 'react-leaflet'
import L from 'leaflet'
import { MapPin } from 'lucide-react'
import type { SchoolClass, StudentLocationRow } from '@/types'
import { getStudentLocations } from '@/api/services/locations'
import { getClasses } from '@/api/services/classes'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { formatDate, cn } from '@/lib/utils'

// Leaflet default marker ikon Vite/bundler bilan to'g'ri yuklanmaydi — qo'lda CDN ko'rsatamiz.
// (Aks holda pin ko'rinmaydi.)
const defaultIcon = new L.Icon({
  iconUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon.png',
  iconRetinaUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon-2x.png',
  shadowUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-shadow.png',
  iconSize: [25, 41],
  iconAnchor: [12, 41],
  popupAnchor: [1, -34],
  shadowSize: [41, 41],
})

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

/**
 * Admin "Joylashuv" sahifasi — o'quvchilar mobil ilova orqali yuborgan uy joylashuvi xaritada.
 * Pin'lar bosilsa: F.I.SH, sinf, manzil, yangilangan vaqt. Sinfga ko'ra filtr.
 */
export function LocationPage() {
  const [rows, setRows] = useState<StudentLocationRow[]>([])
  const [classes, setClasses] = useState<SchoolClass[]>([])
  const [loading, setLoading] = useState(true)
  const [classFilter, setClassFilter] = useState('all')

  useEffect(() => {
    setLoading(true)
    Promise.all([getStudentLocations(), getClasses()])
      .then(([r, c]) => {
        setRows(r)
        setClasses(c)
      })
      .finally(() => setLoading(false))
  }, [])

  const filtered = useMemo(
    () => (classFilter === 'all' ? rows : rows.filter((r) => r.className === classFilter)),
    [rows, classFilter],
  )

  // Xarita markazi — birinchi pin yoki Toshkent (fallback).
  const center: [number, number] = filtered.length > 0
    ? [filtered[0].latitude, filtered[0].longitude]
    : [41.2995, 69.2401] // Toshkent

  // Sinflar bo'yicha hisob — pastdagi statistika uchun.
  const byClass = useMemo(() => {
    const map = new Map<string, number>()
    rows.forEach((r) => map.set(r.className, (map.get(r.className) ?? 0) + 1))
    return [...map.entries()].sort((a, b) => b[1] - a[1])
  }, [rows])

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Joylashuv</h1>
          <p className="text-sm text-slate-400">
            O'quvchilar mobil ilova orqali yuborgan uy joylashuvi —{' '}
            <b>{filtered.length}</b> ta pin
          </p>
        </div>
        <select
          value={classFilter}
          onChange={(e) => setClassFilter(e.target.value)}
          className={cn(control, 'min-w-[160px]')}
        >
          <option value="all">Barcha sinflar</option>
          {classes.map((c) => (
            <option key={c.id} value={c.name}>
              {c.name}
            </option>
          ))}
        </select>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : rows.length === 0 ? (
        <Card>
          <div className="py-12 text-center text-sm text-slate-400">
            <MapPin className="mx-auto mb-2 h-8 w-8 text-slate-300" />
            <p>Hozircha hech bir o'quvchi mobil ilova orqali joylashuv yubormagan.</p>
            <p className="mt-1 text-xs">
              O'quvchilar ilovaga kirib "Joylashuvni saqlash" bosgach, bu yerda pin ko'rinadi.
            </p>
          </div>
        </Card>
      ) : (
        <>
          <Card className="p-0 overflow-hidden">
            <div style={{ height: 500 }}>
              <MapContainer
                center={center}
                zoom={12}
                style={{ height: '100%', width: '100%' }}
                key={`${classFilter}-${filtered.length}`}
              >
                <TileLayer
                  attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
                  url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
                />
                {filtered.map((r) => (
                  <Marker
                    key={r.studentId}
                    position={[r.latitude, r.longitude]}
                    icon={defaultIcon}
                  >
                    <Popup>
                      <div className="text-sm">
                        <p className="font-semibold text-slate-800">{r.fullName}</p>
                        <p className="text-slate-500">{r.className}</p>
                        {r.address && <p className="mt-1 text-xs text-slate-600">{r.address}</p>}
                        {r.updatedAt && (
                          <p className="mt-1 text-[11px] text-slate-400">
                            Yangilangan: {formatDate(r.updatedAt.slice(0, 10))}
                          </p>
                        )}
                        <a
                          href={`https://www.google.com/maps?q=${r.latitude},${r.longitude}`}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="mt-1 inline-block text-xs text-brand-600 hover:underline"
                        >
                          Google Maps'da ochish →
                        </a>
                      </div>
                    </Popup>
                  </Marker>
                ))}
              </MapContainer>
            </div>
          </Card>

          {/* Sinf bo'yicha hisob */}
          {byClass.length > 0 && (
            <Card>
              <h2 className="mb-3 text-sm font-semibold text-slate-800">
                Sinf bo'yicha joylashuv ulushi
              </h2>
              <div className="flex flex-wrap gap-2">
                {byClass.map(([name, count]) => (
                  <button
                    key={name}
                    type="button"
                    onClick={() => setClassFilter(name)}
                    className={cn(
                      'rounded-full border px-3 py-1 text-xs font-medium transition-colors',
                      classFilter === name
                        ? 'border-brand-300 bg-brand-50 text-brand-700'
                        : 'border-slate-200 bg-white text-slate-600 hover:border-brand-200',
                    )}
                  >
                    {name} <b>· {count}</b>
                  </button>
                ))}
              </div>
            </Card>
          )}

          {/* Jadval */}
          <Card className="p-0">
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-4 py-3">F.I.SH</th>
                    <th className="px-4 py-3">Sinf</th>
                    <th className="px-4 py-3">Manzil</th>
                    <th className="px-4 py-3">Koordinata</th>
                    <th className="px-4 py-3">Yangilangan</th>
                    <th className="px-4 py-3"></th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {filtered.map((r) => (
                    <tr key={r.studentId} className="hover:bg-slate-50/60">
                      <td className="px-4 py-3 font-medium text-slate-800">{r.fullName}</td>
                      <td className="px-4 py-3">
                        <span className="rounded-md bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
                          {r.className}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-slate-600 max-w-[24rem] truncate" title={r.address ?? ''}>
                        {r.address || '—'}
                      </td>
                      <td className="px-4 py-3 text-xs text-slate-500">
                        {r.latitude.toFixed(5)}, {r.longitude.toFixed(5)}
                      </td>
                      <td className="px-4 py-3 text-xs text-slate-500">
                        {r.updatedAt ? formatDate(r.updatedAt.slice(0, 10)) : '—'}
                      </td>
                      <td className="px-4 py-3 text-right">
                        <a
                          href={`https://www.google.com/maps?q=${r.latitude},${r.longitude}`}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="text-xs font-medium text-brand-600 hover:text-brand-700"
                        >
                          Xaritada →
                        </a>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Card>
        </>
      )}
    </div>
  )
}
