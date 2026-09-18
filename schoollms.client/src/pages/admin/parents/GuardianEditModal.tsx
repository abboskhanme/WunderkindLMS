import { useEffect, useState } from 'react'
import { Link2Off, Plus, Search } from 'lucide-react'
import type { GuardianRelation, GuardianRow } from '@/types'
import {
  attachGuardianChild,
  detachGuardianChild,
  saveGuardian,
} from '@/api/services/guardians'
import { guardianRelations, relationLabel } from '@/api/services/studentGuardians'
import { searchStudents, type StudentListRow } from '@/api/services/studentSearch'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { cn } from '@/lib/utils'

interface Props {
  open: boolean
  guardian: GuardianRow | null
  onClose: () => void
  onSaved: () => void
}

const control =
  'w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

/**
 * "Ilova → Ota-onalar" ekranidan vasiyni tahrirlash va unga FARZAND
 * biriktirish (§2.9, P-4).
 *
 * Serverda yangi narsa yo'q: `AdminGuardiansController` allaqachon shu
 * amallarni bajarardi, unga ekran yo'q edi. Vasiyning telefoni shu yerda
 * ham IDENTIFIKATOR — boshqa vasiyga tegishli raqamni yozib bo'lmaydi
 * (server 400 qaytaradi).
 */
export function GuardianEditModal({ open, guardian, onClose, onSaved }: Props) {
  const [fullName, setFullName] = useState('')
  const [phone, setPhone] = useState('')
  const [busy, setBusy] = useState(false)

  /* ---- Farzand biriktirish ---- */
  const [term, setTerm] = useState('')
  const [found, setFound] = useState<StudentListRow[]>([])
  const [relation, setRelation] = useState<GuardianRelation>('father')
  const [searching, setSearching] = useState(false)

  useEffect(() => {
    if (!open || !guardian) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oyna ochilganda formani tanlangan vasiy bilan sinxronlash (maqsadli)
    setFullName(guardian.fullName)
    setPhone(guardian.phone)
    setTerm('')
    setFound([])
    setRelation('father')
  }, [open, guardian])

  if (!guardian) return null

  const handleSave = async () => {
    if (fullName.trim().length === 0) { alert('F.I.SH kerak'); return }
    setBusy(true)
    try {
      await saveGuardian(guardian.guardianId, { fullName: fullName.trim(), phone: phone.trim() })
      onSaved()
    } catch {
      alert("Saqlab bo'lmadi — telefon raqami boshqa vasiyga tegishli bo'lishi mumkin")
    } finally {
      setBusy(false)
    }
  }

  const handleSearch = async () => {
    const search = term.trim()
    if (search.length < 2) return
    setSearching(true)
    try {
      const page = await searchStudents({ search, pageSize: 10 })
      setFound(page.items)
    } catch {
      setFound([])
    } finally {
      setSearching(false)
    }
  }

  const handleAttach = async (studentId: string) => {
    setBusy(true)
    try {
      await attachGuardianChild(guardian.guardianId, { studentId, relation, isPrimary: false })
      onSaved()
    } catch {
      alert("Farzandni biriktirib bo'lmadi")
      setBusy(false)
    }
  }

  const handleDetach = async (studentId: string, name: string) => {
    if (!confirm(`${name} shu vasiydan uzilsinmi?`)) return
    setBusy(true)
    try {
      await detachGuardianChild(guardian.guardianId, studentId)
      onSaved()
    } catch {
      alert("Uzib bo'lmadi")
      setBusy(false)
    }
  }

  const attached = new Set(guardian.children.map((c) => c.studentId))

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="lg"
      title="Vasiyni tahrirlash"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Yopish
          </Button>
          <Button onClick={handleSave} disabled={busy}>
            Saqlash
          </Button>
        </>
      }
    >
      <div className="space-y-5">
        <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
          <Input label="F.I.SH" value={fullName} onChange={(e) => setFullName(e.target.value)} />
          <Input
            label="Telefon raqami"
            placeholder="+998 90 123 45 67"
            value={phone}
            onChange={(e) => setPhone(e.target.value)}
          />
        </div>

        <div className="rounded-xl border border-slate-200 bg-slate-50/40 p-3">
          <h4 className="mb-3 text-xs font-semibold uppercase tracking-wide text-slate-500">
            Farzandlari
          </h4>
          {guardian.children.length === 0 ? (
            <p className="text-sm text-slate-400">Biriktirilgan farzand yo'q</p>
          ) : (
            <ul className="space-y-2">
              {guardian.children.map((c) => (
                <li
                  key={c.studentId}
                  className="flex items-center gap-2 rounded-lg border border-slate-200 bg-white px-3 py-2"
                >
                  <div className="flex-1">
                    <div className="text-sm font-medium text-slate-800">{c.fullName}</div>
                    <div className="text-xs text-slate-500">
                      {c.className} · {relationLabel(c.relation, c.relationNote)}
                      {c.isPrimary && ' · asosiy vasiy'}
                    </div>
                  </div>
                  <Button
                    type="button"
                    variant="danger"
                    disabled={busy}
                    onClick={() => handleDetach(c.studentId, c.fullName)}
                  >
                    <Link2Off className="h-4 w-4" /> Uzish
                  </Button>
                </li>
              ))}
            </ul>
          )}
        </div>

        <div className="rounded-xl border border-slate-200 bg-slate-50/40 p-3">
          <h4 className="mb-3 text-xs font-semibold uppercase tracking-wide text-slate-500">
            Yangi farzand biriktirish
          </h4>
          <div className="flex flex-wrap items-end gap-2">
            <div className="relative min-w-[200px] flex-1">
              <span className="mb-1 block text-sm font-medium text-slate-600">O'quvchi</span>
              <Search className="pointer-events-none absolute left-3 top-9 h-4 w-4 text-slate-400" />
              <input
                value={term}
                onChange={(e) => setTerm(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') { e.preventDefault(); void handleSearch() }
                }}
                placeholder="F.I.SH bo'yicha qidirish..."
                className={cn(control, 'pl-9')}
              />
            </div>
            <div className="min-w-[160px]">
              <Select
                label="Kimi bo'ladi"
                value={relation}
                onChange={(e) => setRelation(e.target.value as GuardianRelation)}
              >
                {guardianRelations.map((r) => (
                  <option key={r.value} value={r.value}>
                    {r.label}
                  </option>
                ))}
              </Select>
            </div>
            <Button type="button" variant="secondary" onClick={handleSearch} disabled={searching}>
              <Search className="h-4 w-4" /> Qidirish
            </Button>
          </div>

          {found.length > 0 && (
            <ul className="mt-3 space-y-2">
              {found.map((s) => (
                <li
                  key={s.id}
                  className="flex items-center gap-2 rounded-lg border border-slate-200 bg-white px-3 py-2"
                >
                  <div className="flex-1">
                    <div className="text-sm font-medium text-slate-800">{s.fullName}</div>
                    <div className="text-xs text-slate-500">{s.className}</div>
                  </div>
                  <Button
                    type="button"
                    variant="secondary"
                    disabled={busy || attached.has(s.id)}
                    onClick={() => handleAttach(s.id)}
                  >
                    <Plus className="h-4 w-4" />
                    {attached.has(s.id) ? 'Biriktirilgan' : 'Biriktirish'}
                  </Button>
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </Modal>
  )
}
