import { useEffect, useRef, useState } from 'react'
import Hls from 'hls.js'
import { Video, Plus, Pencil, Trash2, ArrowLeft, Download, Play, VideoOff } from 'lucide-react'
import {
  getCameras, createCamera, updateCamera, deleteCamera, getClipBlob, cameraLiveUrl,
  type Camera, type SaveCameraPayload,
} from '@/api/services/cameras'
import { cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'

/** Jonli HLS pleyer — hls.js orqali (har so'rovga auth sarlavhasi qo'shiladi). */
function LivePlayer({ id, className }: { id: string; className?: string }) {
  const ref = useRef<HTMLVideoElement>(null)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    const video = ref.current
    if (!video) return
    setFailed(false)
    if (!Hls.isSupported()) { setFailed(true); return }
    const token = localStorage.getItem('token')
    const hls = new Hls({
      manifestLoadingTimeOut: 15000,
      xhrSetup: (xhr) => {
        if (token) xhr.setRequestHeader('Authorization', `Bearer ${token}`)
      },
    })
    hls.loadSource(cameraLiveUrl(id))
    hls.attachMedia(video)
    hls.on(Hls.Events.MANIFEST_PARSED, () => video.play().catch(() => {}))
    hls.on(Hls.Events.ERROR, (_e, data) => { if (data.fatal) setFailed(true) })
    return () => hls.destroy()
  }, [id])

  if (failed) {
    return (
      <div className={cn('flex flex-col items-center justify-center bg-slate-900 text-slate-400', className)}>
        <VideoOff className="mb-1 h-7 w-7" />
        <span className="text-xs">Oqim yo'q</span>
      </div>
    )
  }
  return <video ref={ref} className={cn('bg-black', className)} muted autoPlay playsInline controls />
}

export function CamerasPage() {
  const [cameras, setCameras] = useState<Camera[]>([])
  const [loading, setLoading] = useState(true)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [modalOpen, setModalOpen] = useState(false)
  const [editing, setEditing] = useState<Camera | null>(null)

  const load = () => getCameras().then(setCameras).finally(() => setLoading(false))
  useEffect(() => { load() }, [])

  const selected = cameras.find((c) => c.id === selectedId) ?? null

  const onSaved = () => { setModalOpen(false); setEditing(null); load() }
  const onDelete = async (c: Camera) => {
    if (!confirm(`"${c.name}" kamerasini o'chirasizmi?`)) return
    await deleteCamera(c.id)
    if (selectedId === c.id) setSelectedId(null)
    load()
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Kameralar</h1>
          <p className="text-sm text-slate-400">
            Maktab kameralarini real vaqtda kuzatish, yozuvni orqaga qaytarish va qirqib yuklab olish.
          </p>
        </div>
        {!selected && (
          <Button onClick={() => { setEditing(null); setModalOpen(true) }}>
            <Plus className="h-4 w-4" /> Kamera qo'shish
          </Button>
        )}
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : selected ? (
        <SingleCamera
          camera={selected}
          onBack={() => setSelectedId(null)}
          onEdit={() => { setEditing(selected); setModalOpen(true) }}
          onDelete={() => onDelete(selected)}
        />
      ) : cameras.length === 0 ? (
        <Card>
          <div className="py-12 text-center text-sm text-slate-400">
            <Video className="mx-auto mb-2 h-8 w-8 text-slate-300" />
            <p>Hali kamera qo'shilmagan.</p>
            <p className="mt-1 text-xs">"Kamera qo'shish" orqali RTSP manzilini kiriting.</p>
          </div>
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {cameras.map((c) => (
            <Card key={c.id} className="overflow-hidden p-0">
              <button type="button" onClick={() => setSelectedId(c.id)} className="block w-full">
                {c.isActive
                  ? <LivePlayer id={c.id} className="aspect-video w-full" />
                  : <div className="flex aspect-video w-full items-center justify-center bg-slate-100 text-slate-400">
                      <VideoOff className="h-7 w-7" />
                    </div>}
              </button>
              <div className="flex items-center justify-between gap-2 px-3 py-2">
                <div className="min-w-0">
                  <div className="truncate font-medium text-slate-800">{c.name}</div>
                  {c.location && <div className="truncate text-xs text-slate-400">{c.location}</div>}
                </div>
                <div className="flex shrink-0 items-center gap-1">
                  <button type="button" onClick={() => { setEditing(c); setModalOpen(true) }}
                    className="rounded p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-600" title="Tahrirlash">
                    <Pencil className="h-4 w-4" />
                  </button>
                  <button type="button" onClick={() => onDelete(c)}
                    className="rounded p-1.5 text-slate-400 hover:bg-red-50 hover:text-red-600" title="O'chirish">
                    <Trash2 className="h-4 w-4" />
                  </button>
                </div>
              </div>
            </Card>
          ))}
        </div>
      )}

      <CameraFormModal
        open={modalOpen}
        camera={editing}
        onClose={() => { setModalOpen(false); setEditing(null) }}
        onSaved={onSaved}
      />
    </div>
  )
}

/** Bitta kamera: katta jonli ko'rinish + playback (orqaga qaytarish / qirqib yuklab olish). */
function SingleCamera({
  camera, onBack, onEdit, onDelete,
}: { camera: Camera; onBack: () => void; onEdit: () => void; onDelete: () => void }) {
  const [start, setStart] = useState('')
  const [durationMin, setDurationMin] = useState(1)
  const [clipUrl, setClipUrl] = useState<string | null>(null)
  const [busy, setBusy] = useState<'view' | 'download' | null>(null)

  useEffect(() => () => { if (clipUrl) URL.revokeObjectURL(clipUrl) }, [clipUrl])

  const startIso = () => (start.length === 16 ? `${start}:00` : start)

  const view = async () => {
    if (!start) { alert('Vaqtni tanlang'); return }
    setBusy('view')
    try {
      const blob = await getClipBlob(camera.id, startIso(), durationMin * 60)
      if (clipUrl) URL.revokeObjectURL(clipUrl)
      setClipUrl(URL.createObjectURL(blob))
    } catch {
      alert('Yozuv topilmadi (bu vaqt uchun yozuv yo\'q bo\'lishi mumkin)')
    } finally { setBusy(null) }
  }

  const download = async () => {
    if (!start) { alert('Vaqtni tanlang'); return }
    setBusy('download')
    try {
      const blob = await getClipBlob(camera.id, startIso(), durationMin * 60)
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = `${camera.name}_${start.replace(/[:T]/g, '-')}_${durationMin}min.mp4`
      a.click()
      URL.revokeObjectURL(url)
    } catch {
      alert('Yozuv topilmadi')
    } finally { setBusy(null) }
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <button type="button" onClick={onBack}
          className="inline-flex items-center gap-1.5 text-sm font-medium text-slate-600 hover:text-slate-800">
          <ArrowLeft className="h-4 w-4" /> Orqaga
        </button>
        <div className="flex items-center gap-2">
          <Button variant="secondary" onClick={onEdit}><Pencil className="h-4 w-4" /> Tahrirlash</Button>
          <Button variant="danger" onClick={onDelete}><Trash2 className="h-4 w-4" /> O'chirish</Button>
        </div>
      </div>

      <div>
        <h2 className="font-semibold text-slate-800">{camera.name}</h2>
        {camera.location && <p className="text-sm text-slate-400">{camera.location}</p>}
      </div>

      <Card className="overflow-hidden p-0">
        <div className="border-b border-slate-100 bg-slate-50 px-3 py-1.5 text-xs font-medium text-emerald-600">
          ● Jonli
        </div>
        <LivePlayer id={camera.id} className="aspect-video w-full" />
      </Card>

      {/* Playback / qirqish */}
      <Card>
        <h3 className="mb-1 font-semibold text-slate-800">Yozuvni ko'rish / qirqib yuklab olish</h3>
        <p className="mb-4 text-sm text-slate-400">
          Boshlanish vaqti va davomiyligini tanlang — yozuvdan shu bo'lak MP4 sifatida ko'riladi yoki yuklab olinadi.
        </p>
        <div className="flex flex-wrap items-end gap-3">
          <label className="flex flex-col gap-1 text-sm">
            <span className="font-medium text-slate-600">Boshlanish vaqti</span>
            <input type="datetime-local" value={start} onChange={(e) => setStart(e.target.value)}
              className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400" />
          </label>
          <label className="flex flex-col gap-1 text-sm">
            <span className="font-medium text-slate-600">Davomiyligi (daqiqa)</span>
            <input type="number" min={1} max={60} value={durationMin}
              onChange={(e) => setDurationMin(Math.min(60, Math.max(1, Number(e.target.value) || 1)))}
              className="w-28 rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400" />
          </label>
          <Button variant="secondary" onClick={view} disabled={busy !== null}>
            <Play className="h-4 w-4" /> {busy === 'view' ? 'Yuklanmoqda...' : 'Ko\'rish'}
          </Button>
          <Button onClick={download} disabled={busy !== null}>
            <Download className="h-4 w-4" /> {busy === 'download' ? 'Yuklanmoqda...' : 'Yuklab olish'}
          </Button>
        </div>

        {clipUrl && (
          <video src={clipUrl} controls autoPlay className="mt-4 aspect-video w-full rounded-lg bg-black" />
        )}
      </Card>
    </div>
  )
}

function CameraFormModal({
  open, camera, onClose, onSaved,
}: { open: boolean; camera: Camera | null; onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState<SaveCameraPayload>({ name: '', rtspUrl: '', retentionDays: 7, isActive: true })
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    if (!open) return
    setForm(camera
      ? { name: camera.name, location: camera.location, rtspUrl: camera.rtspUrl,
          rtspSubUrl: camera.rtspSubUrl, retentionDays: camera.retentionDays,
          isActive: camera.isActive, note: camera.note }
      : { name: '', rtspUrl: '', retentionDays: 7, isActive: true })
  }, [open, camera])

  const set = <K extends keyof SaveCameraPayload>(k: K, v: SaveCameraPayload[K]) =>
    setForm((p) => ({ ...p, [k]: v }))

  const submit = async () => {
    if (!form.name.trim()) { alert('Nomi shart'); return }
    if (!form.rtspUrl.trim()) { alert('RTSP manzili shart'); return }
    setSaving(true)
    try {
      if (camera) await updateCamera(camera.id, form)
      else await createCamera(form)
      onSaved()
    } catch {
      alert('Saqlashda xatolik')
    } finally { setSaving(false) }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={camera ? 'Kamerani tahrirlash' : 'Yangi kamera'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>Bekor</Button>
          <Button onClick={submit} disabled={saving}>{saving ? 'Saqlanmoqda...' : 'Saqlash'}</Button>
        </>
      }
    >
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <Input label="Nomi *" placeholder="Kirish eshigi" value={form.name} onChange={(e) => set('name', e.target.value)} />
        <Input label="Joylashuvi" placeholder="1-qavat koridor" value={form.location ?? ''} onChange={(e) => set('location', e.target.value)} />
      </div>
      <div className="mt-4">
        <Input label="RTSP manzili *" placeholder="rtsp://login:parol@192.168.1.10:554/Streaming/Channels/101"
          value={form.rtspUrl} onChange={(e) => set('rtspUrl', e.target.value)} autoComplete="off" />
      </div>
      <div className="mt-4">
        <Input label="Sub-oqim RTSP (ixtiyoriy — grid uchun past sifat)" placeholder="rtsp://.../Channels/102"
          value={form.rtspSubUrl ?? ''} onChange={(e) => set('rtspSubUrl', e.target.value)} autoComplete="off" />
      </div>
      <div className="mt-4 grid grid-cols-1 gap-4 sm:grid-cols-2">
        <label className="flex flex-col gap-1 text-sm">
          <span className="font-medium text-slate-600">Yozuv saqlash muddati</span>
          <select
            value={form.retentionDays}
            onChange={(e) => set('retentionDays', Number(e.target.value))}
            className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
          >
            <option value={3}>3 kun</option>
            <option value={7}>7 kun</option>
            <option value={14}>14 kun</option>
            <option value={30}>30 kun</option>
            <option value={60}>60 kun</option>
            <option value={90}>90 kun</option>
            <option value={0}>Cheksiz (o'chirilmaydi)</option>
          </select>
          <span className="text-[11px] text-slate-400">Bu muddatdan eski yozuvlar avtomatik o'chiriladi.</span>
        </label>
        <Input label="Izoh" value={form.note ?? ''} onChange={(e) => set('note', e.target.value)} />
      </div>
      <label className="mt-4 inline-flex cursor-pointer items-center gap-2 text-sm font-medium text-slate-700">
        <input type="checkbox" checked={form.isActive} onChange={(e) => set('isActive', e.target.checked)}
          className="h-4 w-4 rounded border-slate-300 accent-brand-600" />
        Faol
      </label>
    </Modal>
  )
}
