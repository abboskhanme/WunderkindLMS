import { useRef, useState } from 'react'
import { FileText, Paperclip } from 'lucide-react'
import { createApplicant, updateApplicant, type ApplicantDetail, type Vacancy } from '@/api/services/recruitment'
import { hrErrorMessage } from '@/api/services/hrTasks'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select, Textarea } from '@/components/ui/Input'

interface Props {
  vacancies: Vacancy[]
  /** Edit mode when given. */
  applicant?: ApplicantDetail
  onClose: () => void
  onSaved: (id: string) => void
}

/** A candidate added by hand (a call, a recommendation), or an applicant's details edited. */
export function ApplicantFormModal({ vacancies, applicant, onClose, onSaved }: Props) {
  const row = applicant?.row
  const [fullName, setFullName] = useState(row?.fullName ?? '')
  const [phone, setPhone] = useState(row?.phone ?? '+998 ')
  const [email, setEmail] = useState(row?.email ?? '')
  const [vacancyId, setVacancyId] = useState(row?.vacancyId ?? '')
  const [position, setPosition] = useState(row?.position ?? '')
  const [experience, setExperience] = useState(applicant?.experience ?? '')
  const [about, setAbout] = useState(applicant?.about ?? '')
  const [cv, setCv] = useState<File | null>(null)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const fileRef = useRef<HTMLInputElement>(null)

  const save = async () => {
    setSaving(true)
    setError('')
    const input = {
      fullName: fullName.trim(),
      phone: phone.trim(),
      email: email.trim() || null,
      vacancyId: vacancyId || null,
      position: position.trim() || null,
      experience: experience.trim() || null,
      about: about.trim() || null,
    }
    try {
      if (row) {
        await updateApplicant(row.id, input)
        onSaved(row.id)
      } else {
        onSaved((await createApplicant(input, cv)).row.id)
      }
    } catch (e) {
      setError(hrErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      size="md"
      title={row ? 'Nomzodni tahrirlash' : "Nomzod qo'shish"}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button onClick={save} disabled={saving || !fullName.trim()}>
            {saving ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Input label="F.I.Sh." required value={fullName} maxLength={200} onChange={(e) => setFullName(e.target.value)} />
        <div className="grid gap-3 sm:grid-cols-2">
          <Input label="Telefon" required value={phone} maxLength={20} onChange={(e) => setPhone(e.target.value)} placeholder="+998 90 123 45 67" />
          <Input label="Email" type="email" value={email} maxLength={200} onChange={(e) => setEmail(e.target.value)} />
        </div>
        <div className="grid gap-3 sm:grid-cols-2">
          <Select label="Vakansiya" value={vacancyId} onChange={(e) => setVacancyId(e.target.value)}>
            <option value="">— vakansiyasiz —</option>
            {vacancies.map((v) => (
              <option key={v.id} value={v.id}>
                {v.title}
                {v.status === 'closed' ? ' (yopilgan)' : ''}
              </option>
            ))}
          </Select>
          <Input label="Lavozim (vakansiyasiz bo'lsa)" value={position} maxLength={200} onChange={(e) => setPosition(e.target.value)} />
        </div>
        <Textarea label="Ish tajribasi" rows={2} value={experience} maxLength={2000} onChange={(e) => setExperience(e.target.value)} />
        <Textarea label="Qo'shimcha ma'lumot" rows={2} value={about} maxLength={4000} onChange={(e) => setAbout(e.target.value)} />
        {!row && (
          <div>
            <span className="mb-1 block text-sm font-medium text-slate-600">CV (ixtiyoriy)</span>
            <button
              type="button"
              onClick={() => fileRef.current?.click()}
              className="flex w-full items-center gap-2 rounded-lg border border-dashed border-slate-300 px-3 py-2.5 text-sm text-slate-600 hover:bg-slate-50"
            >
              {cv ? <FileText className="h-4 w-4 text-brand-600" /> : <Paperclip className="h-4 w-4" />}
              <span className="truncate">{cv ? cv.name : 'PDF, Word yoki rasm — 10 MB gacha'}</span>
            </button>
            <input
              ref={fileRef}
              type="file"
              accept=".pdf,.doc,.docx,.jpg,.jpeg,.png"
              className="hidden"
              onChange={(e) => setCv(e.target.files?.[0] ?? null)}
            />
          </div>
        )}
        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}
