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
 * Tizimning "kech qoldi" yozuvi: nomida "kech" bor va `isLate`. U sozlamada KO'RSATILMAYDI
 * (mijoz, 2026-09-25: "kech qolishga sabab kerakmas") — jurnal va Mini App undan "kech qoldi"
 * belgisi sifatida foydalanadi, shuning uchun saqlashda o'z holicha qaytariladi.
 * Nomida "kech" bo'lmagan `isLate` sabab — xato qo'yilgan belgi: u oddiy yo'qlik sababi bo'lib
 * ko'rinadi va saqlashda tuzaladi.
 */
const isSystemLate = (r: AbsenceReason) => r.isLate && /kech/i.test(r.name)

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
 * Davomat sabablari — FAQAT kelmagan o'quvchi uchun (mijoz, 2026-09-25): nomi va
 * o'chirish, boshqa hech narsa. Qisqa belgi avtomatik, "kech qoldi" tizimda yashirin.
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

  const onSave = async () => {
    setStatus('saving')
    const late = hidden.length > 0 ? hidden : [{ ...DEFAULT_LATE, id: uid() }]
    const visible = reasons
      .filter((r) => r.name.trim())
      .map((r) => ({ ...r, name: r.name.trim(), isLate: false }))
    const taken = new Set(late.map((r) => r.short.toUpperCase()))
    await saveAbsenceReasons([...withShorts(visible, taken), ...late])
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
      <p className="mb-3 text-xs text-slate-400">Kelmagan o'quvchi uchun tanlanadigan sabablar.</p>
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
