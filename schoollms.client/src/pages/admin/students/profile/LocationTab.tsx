import { useState } from 'react'
import { MapContainer, Marker, TileLayer, useMapEvents } from 'react-leaflet'
import L from 'leaflet'
import { MapPin, Trash2 } from 'lucide-react'
import type { StudentCard } from '@/api/services/studentProfile'
import { saveStudentLocation } from '@/api/services/studentProfile'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { ProfileError, ProfileSection } from './ProfileUi'

/**
 * Kartochkaning "Manzil" tab'i — docs/modules/students-parity.md §2.8 (L-1).
 *
 * Bugungacha `latitude`/`longitude` ustunlarini faqat eski mobil ilova
 * yozardi, ya'ni amalda hech kim: xarita ekrani faqat qadimgi ma'lumotni
 * ko'rsatardi. Endi xodim nuqtani XARITADAN BOSIB qo'yadi va manzilni yozadi.
 *
 * MANZIL QIDIRUVI YO'Q: avtomatik to'ldirish va teskari geokodlash tashqi
 * provayder talab qiladi (§2.8 — "Declined", Q6). Manzil qo'lda yoziladi.
 */

// Leaflet default marker ikon Vite/bundler bilan to'g'ri yuklanmaydi — qo'lda
// CDN ko'rsatamiz (xarita ekranidagi bilan bir xil).
const pinIcon = new L.Icon({
  iconUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon.png',
  iconRetinaUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon-2x.png',
  shadowUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-shadow.png',
  iconSize: [25, 41],
  iconAnchor: [12, 41],
  popupAnchor: [1, -34],
  shadowSize: [41, 41],
})

/** Toshkent — joylashuvi yo'q o'quvchida xarita shu yerdan ochiladi. */
const fallbackCenter: [number, number] = [41.2995, 69.2401]

const errorText = (e: unknown, fallback: string) =>
  (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback

/** Xaritadagi bosishni ushlaydi (react-leaflet'da bu faqat xarita ichidan mumkin). */
function ClickCatcher({ onPick }: { onPick: (lat: number, lng: number) => void }) {
  useMapEvents({
    click: (e) => onPick(e.latlng.lat, e.latlng.lng),
  })
  return null
}

interface Props {
  card: StudentCard
  /** Saqlangach yangilangan kartochka — sarlavha ham yangilansin. */
  onSaved: (card: StudentCard) => void
}

export function LocationTab({ card, onSaved }: Props) {
  const [lat, setLat] = useState<number | null>(card.latitude)
  const [lng, setLng] = useState<number | null>(card.longitude)
  const [address, setAddress] = useState(card.locationAddress ?? '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  const center: [number, number] = lat != null && lng != null ? [lat, lng] : fallbackCenter

  const pick = (nextLat: number, nextLng: number) => {
    setLat(Number(nextLat.toFixed(6)))
    setLng(Number(nextLng.toFixed(6)))
    setSaved(false)
  }

  const save = async (clear = false) => {
    setBusy(true)
    setError(null)
    try {
      const updated = await saveStudentLocation(card.student.id, {
        latitude: clear ? null : lat,
        longitude: clear ? null : lng,
        address: clear ? null : address.trim() || null,
      })
      if (clear) {
        setLat(null)
        setLng(null)
        setAddress('')
      }
      setSaved(true)
      onSaved(updated)
    } catch (err) {
      setError(errorText(err, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <ProfileSection title="Uy joylashuvi" icon={MapPin}>
      <p className="mb-3 text-sm text-slate-500">
        Nuqtani xaritadan bosib belgilang, manzilni esa qo'lda yozing.
        {card.locationUpdatedAt && (
          <span className="text-slate-400">
            {' '}
            Oxirgi yangilanish: {card.locationUpdatedAt.slice(0, 16).replace('T', ' ')}.
          </span>
        )}
      </p>

      <div className="overflow-hidden rounded-xl border border-slate-200">
        <MapContainer
          center={center}
          zoom={lat != null ? 16 : 12}
          style={{ height: 320, width: '100%' }}
          scrollWheelZoom
        >
          <TileLayer
            attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
            url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
          />
          <ClickCatcher onPick={pick} />
          {lat != null && lng != null && <Marker position={[lat, lng]} icon={pinIcon} />}
        </MapContainer>
      </div>

      <div className="mt-4 grid gap-4 sm:grid-cols-2">
        <Input
          label="Manzil"
          placeholder="masalan: Chilonzor 9-kvartal, 12-uy"
          value={address}
          onChange={(e) => {
            setAddress(e.target.value)
            setSaved(false)
          }}
        />
        <div className="flex items-end gap-2">
          <div className="flex-1 rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-600">
            {lat != null && lng != null ? (
              <>
                <span className="text-xs text-slate-400">Koordinata</span>
                <p className="font-medium">
                  {lat}, {lng}
                </p>
              </>
            ) : (
              <span className="text-slate-400">Nuqta belgilanmagan</span>
            )}
          </div>
        </div>
      </div>

      {error && (
        <div className="mt-3">
          <ProfileError message={error} />
        </div>
      )}
      {saved && !error && (
        <p className="mt-3 text-sm text-emerald-600">Joylashuv saqlandi</p>
      )}

      <div className="mt-4 flex flex-wrap gap-2">
        <Button onClick={() => save(false)} disabled={busy}>
          Saqlash
        </Button>
        {(card.latitude != null || card.locationAddress) && (
          <Button variant="secondary" onClick={() => save(true)} disabled={busy}>
            <Trash2 className="h-4 w-4" /> Tozalash
          </Button>
        )}
      </div>
    </ProfileSection>
  )
}
