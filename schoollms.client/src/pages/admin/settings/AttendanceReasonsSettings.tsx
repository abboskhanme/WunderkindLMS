import { useEffect, useState } from 'react'
import { Check, Plus, Trash2 } from 'lucide-react'
import type { AbsenceReason } from '@/types'
import { getSettings, saveAbsenceReasons } from '@/api/services/settings'
import { uid } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'

type Status = 'idle' | 'saving' | 'saved'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

/**
 * Ikki ro'yxat (mijoz, 2026-09-26): davomatda K (keldi) ostida — kech keldi sabablari
 * (`isLate`), Y (kelmadi) ostida — kelmaganlik sabablari. Kechikish ro'yxati bo'sh qolsa ham
 * "Kech qoldi" saqlanadi: o'qituvchi jurnali va Mini App undan kechikish belgisi sifatida
 * foydalanadi.
 */
const isSystemLate = (r: AbsenceReason) => r.isLate

const DEFAULT_LATE: Omit<AbsenceReason, 'id'> = { name: 'Kech qoldi', short: 'KQ', isLate: true }

/**
 * Jurnal katagidagi qisqa belgi nomdan hosil qilinadi — foydalanuvchi uni kiritmaydi.
 * Borini saqlaymiz (jurnal ko'rinishi o'zgarmasin); yangisi — birinchi harf, band bo'lsa
 * ikki harf, u ham band bo'lsa raqam qo'shiladi.
 */
function withShorts(list: AbsenceReason[], taken: Set<string>): AbsenceReason[] {
  const used = new Set(taken)
  for (const r of list) if (r.short.trim()) used.add(r.short.trim().toUpperCase())
  return list.map((r) => {
    if (r.short.trim()) return r
    const letters = r.name.replace(/[^\p{L}]/gu, '').toUpperCase() || 'X'
    let short = [letters.slice(0, 1), letters.slice(0, 2)].find((c) => !used.has(c)) ?? ''
    for (let n = 2; !short; n++) if (!used.has(`${letters[0]}${n}`)) short = `${letters[0]}${n}`
    used.add(short)
    return { ...r, short }
  })
}

/**
 * Davomat sabablari — ikki ro'yxat (mijoz, 2026-09-26): "Kelmadi sabablari" (Y ostida) va
 * "Kech keldi sabablari" (K ostida). Har birida faqat nom va o'chirish; qisqa belgi avtomatik.
 *
 * DIQQAT: bu "Arxivlash sabablari" (`ArchiveReasonsSettings.tsx`, §2.2) dan
 * boshqa katalog — u o'quvchini arxivlashda, bu esa kunlik davomatda ishlatiladi.
 */
export function AttendanceReasonsSettings() {
  const [reasons, setReasons] = useState<AbsenceReason[]>([])
  const [hidden, setHidden] = useState<AbsenceReason[]>([])
  const [loading, setLoading] = useState(true)
  const [status, setStatus] = useState<Status>('idle')

  useEffect(() => {
    getSettings()
      .then((s) => {
        setHidden(s.absenceReasons.filter(isSystemLate))
        setReasons(s.absenceReasons.filter((r) => !isSystemLate(r)).map((r) => ({ ...r, isLate: false })))
      })
      .finally(() => setLoading(false))
  }, [])

  const updateName = (i: number, value: string) =>
    setReasons((prev) => prev.map((r, idx) => (idx === i ? { ...r, name: value } : r)))
  const addReason = () => setReasons((prev) => [...prev, { id: uid(), name: '', short: '', isLate: false }])
  const removeReason = (i: number) => setReasons((prev) => prev.filter((_, idx) => idx !== i))

  const updateLateName = (i: number, value: string) =>
    setHidden((prev) => prev.map((r, idx) => (idx === i ? { ...r, name: value } : r)))
  const addLate = () => setHidden((prev) => [...prev, { id: uid(), name: '', short: '', isLate: true }])
  const removeLate = (i: number) => setHidden((prev) => prev.filter((_, idx) => idx !== i))

  const onSave = async () => {
    setStatus('saving')
    const named = hidden.filter((r) => r.name.trim()).map((r) => ({ ...r, name: r.name.trim(), isLate: true }))
    const late = named.length > 0 ? named : [{ ...DEFAULT_LATE, id: uid() }]
    const visible = reasons
      .filter((r) => r.name.trim())
      .map((r) => ({ ...r, name: r.name.trim(), isLate: false }))
    const taken = new Set<string>()
    const visibleWithShorts = withShorts(visible, new Set())
    for (const r of visibleWithShorts) taken.add(r.short.toUpperCase())
    await saveAbsenceReasons([...visibleWithShorts, ...withShorts(late, taken)])
    setHidden(late)
    setStatus('saved')
    setTimeout(() => setStatus('idle'), 2000)
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <Card>
      <div className="mb-4 flex items-center justify-between">
        <h2 className="font-semibold text-slate-800">Davomat sabablari</h2>
        <SaveButton status={status} onClick={onSave} />
      </div>
      <h3 className="mb-1 text-sm font-medium text-slate-700">Kelmadi sabablari</h3>
      <p className="mb-3 text-xs text-slate-400">
        Davomatda Y (kelmadi) ostidan tanlanadi. Birinchi "Sababsiz" — sukut bo'yicha.
      </p>
      <div className="space-y-2">
        {reasons.map((r, i) => (
          <div key={r.id} className="flex items-center gap-2">
            <input
              value={r.name}
              onChange={(e) => updateName(i, e.target.value)}
              placeholder="Sabab nomi (masalan: Kasal)"
              className={`${control} min-w-0 flex-1`}
            />
            <button
              type="button"
              onClick={() => removeReason(i)}
              title="O'chirish"
              className="rounded-lg p-2 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
            >
              <Trash2 className="h-4 w-4" />
            </button>
          </div>
        ))}
        <button
          type="button"
          onClick={addReason}
          className="inline-flex items-center gap-1 text-sm font-medium text-brand-600 hover:text-brand-700"
        >
          <Plus className="h-4 w-4" /> Sabab qo'shish
        </button>
      </div>

      <h3 className="mb-1 mt-6 text-sm font-medium text-slate-700">Kech keldi sabablari</h3>
      <p className="mb-3 text-xs text-slate-400">
        Davomatda K (keldi) ostidan tanlanadi — o'quvchi darsda bor hisoblanadi. Bo'sh qolsa, "Kech qoldi"
        saqlanadi.
      </p>
      <div className="space-y-2">
        {hidden.map((r, i) => (
          <div key={r.id} className="flex items-center gap-2">
            <input
              value={r.name}
              onChange={(e) => updateLateName(i, e.target.value)}
              placeholder="Masalan: Kech keldi"
              className={`${control} min-w-0 flex-1`}
            />
            <button
              type="button"
              onClick={() => removeLate(i)}
              title="O'chirish"
              className="rounded-lg p-2 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
            >
              <Trash2 className="h-4 w-4" />
            </button>
          </div>
        ))}
        <button
          type="button"
          onClick={addLate}
          className="inline-flex items-center gap-1 text-sm font-medium text-brand-600 hover:text-brand-700"
        >
          <Plus className="h-4 w-4" /> Kechikish sababi qo'shish
        </button>
      </div>
    </Card>
  )
}

function SaveButton({ status, onClick }: { status: Status; onClick: () => void }) {
  if (status === 'saved') {
    return (
      <span className="inline-flex items-center gap-1 text-sm font-medium text-emerald-600">
        <Check className="h-4 w-4" /> Saqlandi
      </span>
    )
  }
  return (
    <Button onClick={onClick} disabled={status === 'saving'}>
      {status === 'saving' ? 'Saqlanmoqda...' : 'Saqlash'}
    </Button>
  )
}
