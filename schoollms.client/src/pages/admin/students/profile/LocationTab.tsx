import { useEffect, useState } from 'react'
import { MapContainer, Marker, TileLayer, useMapEvents } from 'react-leaflet'
import L from 'leaflet'
import { Bus, Home, School, Trash2 } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import type { StudentCard } from '@/api/services/studentProfile'
import {
  deleteTypedLocation,
  getStudentCard,
  getStudentTypedLocations,
  saveTypedLocation,
} from '@/api/services/studentProfile'
import type { StudentLocationKind } from '@/types'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { ProfileError, ProfileSection } from './ProfileUi'

/**
 * Kartochkaning "Manzil" tab'i — docs/modules/students-parity.md §2.8 (L-1, L-2).
 *
 * Uchtagacha turdagi joylashuv: uy, maktab, olib ketish nuqtasi — har biri
 * o'z kartasida, xaritadan nuqta bosib va manzilni qo'lda yozib to'ldiriladi
 * (`pickup` uchun vaqt oralig'i ham). Manba `student_locations` (L-2).
 *
 * <b>ESKI (L-1) UY MANZILI BILAN SINXRONLIK.</b> `home` kartasi saqlanganda
 * server ESKI `students.latitude/longitude/location_address` ustunlarini
 * ham yangilaydi (`StudentProfileController.MirrorLegacyHome`) — mobil
 * ilova va ota-ona Mini App'i shu ustunlarni o'qishda davom etadi.
 * Xodim hali bu tab'dan bir marta ham saqlamagan bo'lsa-yu, o'quvchida
 * ESKI ustunlarda qiymat bo'lsa (L-1 orqali yoki eski mobil ilovadan) —
 * "Uy" kartasi o'sha qiymat bilan OLDINDAN TO'LDIRILGAN holda ochiladi
 * (belgisi — pastdagi "eski ma'lumot" yozuvi); birinchi saqlashda haqiqiy
 * `student_locations` qatoriga aylanadi.
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

const KIND_META: Record<StudentLocationKind, { title: string; icon: LucideIcon; hint: string }> = {
  home: { title: 'Uy joylashuvi', icon: Home, hint: 'Nuqtani xaritadan bosib belgilang, manzilni qo\'lda yozing.' },
  school: {
    title: "Maktab / boshqa o'quv joyi",
    icon: School,
    hint: "Bolaning ikkinchi o'quv joyi bo'lsa (masalan musiqa maktabi) shu yerga belgilang.",
  },
  pickup: {
    title: 'Olib ketish nuqtasi',
    icon: Bus,
    hint: "Avtobus bekati yoki uchrashuv nuqtasi — vaqt oralig'i bilan.",
  },
}

const KIND_ORDER: StudentLocationKind[] = ['home', 'school', 'pickup']

interface KindState {
  lat: number | null
  lng: number | null
  name: string
  pickupFrom: string
  pickupTo: string
  isLegacy: boolean
  dirty: boolean
}

const emptyKindState: KindState = {
  lat: null,
  lng: null,
  name: '',
  pickupFrom: '',
  pickupTo: '',
  isLegacy: false,
  dirty: false,
}

function emptyAll(): Record<StudentLocationKind, KindState> {
  return { home: { ...emptyKindState }, school: { ...emptyKindState }, pickup: { ...emptyKindState } }
}

interface Props {
  card: StudentCard
  /** `home` saqlangach/tozalangach — kartochka ham yangilansin (ESKI ustunlar). */
  onSaved: (card: StudentCard) => void
}

export function LocationTab({ card, onSaved }: Props) {
  const studentId = card.student.id
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [busyKind, setBusyKind] = useState<StudentLocationKind | null>(null)
  const [kindError, setKindError] = useState<Partial<Record<StudentLocationKind, string>>>({})
  const [savedFlash, setSavedFlash] = useState<StudentLocationKind | null>(null)
  const [state, setState] = useState<Record<StudentLocationKind, KindState>>(emptyAll())

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setLoadError(null)
    getStudentTypedLocations(studentId)
      .then((rows) => {
        if (cancelled) return
        const next = emptyAll()
        rows.forEach((r) => {
          next[r.kind] = {
            lat: r.lat,
            lng: r.lng,
            name: r.name ?? '',
            pickupFrom: r.pickupFrom ?? '',
            pickupTo: r.pickupTo ?? '',
            isLegacy: r.isLegacy,
            dirty: false,
          }
        })
        setState(next)
      })
      .catch((err) => !cancelled && setLoadError(errorText(err, "Joylashuvlarni yuklab bo'lmadi")))
      .finally(() => !cancelled && setLoading(false))
    return () => {
      cancelled = true
    }
  }, [studentId])

  const patch = (kind: StudentLocationKind, next: Partial<KindState>) => {
    setSavedFlash(null)
    setState((prev) => ({ ...prev, [kind]: { ...prev[kind], ...next, dirty: true } }))
  }

  const save = async (kind: StudentLocationKind) => {
    const s = state[kind]
    if (s.lat == null || s.lng == null) {
      setKindError((prev) => ({ ...prev, [kind]: 'Avval xaritadan nuqta belgilang' }))
      return
    }
    if (kind === 'pickup' && (!s.pickupFrom || !s.pickupTo)) {
      setKindError((prev) => ({
        ...prev,
        [kind]: "Olib ketish nuqtasida vaqt oralig'i (boshi va oxiri) majburiy",
      }))
      return
    }
    setBusyKind(kind)
    setKindError((prev) => ({ ...prev, [kind]: undefined }))
    try {
      await saveTypedLocation(studentId, kind, {
        lat: s.lat,
        lng: s.lng,
        name: s.name.trim() || null,
        pickupFrom: s.pickupFrom || null,
        pickupTo: s.pickupTo || null,
      })
      setState((prev) => ({ ...prev, [kind]: { ...prev[kind], isLegacy: false, dirty: false } }))
      setSavedFlash(kind)
      if (kind === 'home') onSaved(await getStudentCard(studentId))
    } catch (err) {
      setKindError((prev) => ({ ...prev, [kind]: errorText(err, "Saqlab bo'lmadi") }))
    } finally {
      setBusyKind(null)
    }
  }

  const clear = async (kind: StudentLocationKind) => {
    setBusyKind(kind)
    setKindError((prev) => ({ ...prev, [kind]: undefined }))
    try {
      await deleteTypedLocation(studentId, kind)
      setState((prev) => ({ ...prev, [kind]: { ...emptyKindState } }))
      setSavedFlash(null)
      if (kind === 'home') onSaved(await getStudentCard(studentId))
    } catch (err) {
      setKindError((prev) => ({ ...prev, [kind]: errorText(err, "O'chirib bo'lmadi") }))
    } finally {
      setBusyKind(null)
    }
  }

  if (loading) {
    return (
      <ProfileSection title="Manzil" icon={Home}>
        <p className="text-sm text-slate-400">Yuklanmoqda...</p>
      </ProfileSection>
    )
  }

  if (loadError) {
    return (
      <ProfileSection title="Manzil" icon={Home}>
        <ProfileError message={loadError} />
      </ProfileSection>
    )
  }

  return (
    <div className="space-y-6">
      {KIND_ORDER.map((kind) => {
        const meta = KIND_META[kind]
        const s = state[kind]
        const center: [number, number] = s.lat != null && s.lng != null ? [s.lat, s.lng] : fallbackCenter
        const busy = busyKind === kind

        return (
          <ProfileSection key={kind} title={meta.title} icon={meta.icon}>
            <p className="mb-3 text-sm text-slate-500">
              {meta.hint}
              {s.isLegacy && (
                <span className="text-amber-600">
                  {' '}
                  Bu — eski tizimdan qolgan ma'lumot, hali bu ekrandan saqlanmagan. "Saqlash"
                  bossangiz yangi joylashuv sifatida tasdiqlanadi.
                </span>
              )}
            </p>

            <div className="overflow-hidden rounded-xl border border-slate-200">
              <MapContainer
                center={center}
                zoom={s.lat != null ? 16 : 12}
                style={{ height: 260, width: '100%' }}
                scrollWheelZoom
              >
                <TileLayer
                  attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
                  url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
                />
                <ClickCatcher onPick={(lat, lng) => patch(kind, { lat: Number(lat.toFixed(6)), lng: Number(lng.toFixed(6)) })} />
                {s.lat != null && s.lng != null && <Marker position={[s.lat, s.lng]} icon={pinIcon} />}
              </MapContainer>
            </div>

            <div className="mt-4 grid gap-4 sm:grid-cols-2">
              <Input
                label="Manzil (ixtiyoriy)"
                placeholder="masalan: Chilonzor 9-kvartal, 12-uy"
                value={s.name}
                onChange={(e) => patch(kind, { name: e.target.value })}
              />
              <div className="flex items-end gap-2">
                <div className="flex-1 rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-600">
                  {s.lat != null && s.lng != null ? (
                    <>
                      <span className="text-xs text-slate-400">Koordinata</span>
                      <p className="font-medium">
                        {s.lat}, {s.lng}
                      </p>
                    </>
                  ) : (
                    <span className="text-slate-400">Nuqta belgilanmagan</span>
                  )}
                </div>
              </div>
            </div>

            {kind === 'pickup' && (
              <div className="mt-4 grid gap-4 sm:grid-cols-2">
                <Input
                  label="Vaqt boshi"
                  type="time"
                  step={300}
                  required
                  value={s.pickupFrom}
                  onChange={(e) => patch(kind, { pickupFrom: e.target.value })}
                />
                <Input
                  label="Vaqt oxiri"
                  type="time"
                  step={300}
                  required
                  value={s.pickupTo}
                  onChange={(e) => patch(kind, { pickupTo: e.target.value })}
                />
              </div>
            )}

            {kindError[kind] && (
              <div className="mt-3">
                <ProfileError message={kindError[kind]!} />
              </div>
            )}
            {savedFlash === kind && !kindError[kind] && (
              <p className="mt-3 text-sm text-emerald-600">Joylashuv saqlandi</p>
            )}

            <div className="mt-4 flex flex-wrap gap-2">
              <Button onClick={() => save(kind)} disabled={busy}>
                Saqlash
              </Button>
              {(s.lat != null || s.name) && (
                <Button variant="secondary" onClick={() => clear(kind)} disabled={busy}>
                  <Trash2 className="h-4 w-4" /> Tozalash
                </Button>
              )}
            </div>
          </ProfileSection>
        )
      })}
    </div>
  )
}
