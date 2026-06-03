import { useEffect, useRef, useState } from 'react'
import { Upload, X, FileText, Loader2 } from 'lucide-react'
import type { Student } from '@/types'
import type { StudentPayload } from '@/api/services/students'
import { uploadAdminFile } from '@/api/services/students'
import { getClasses } from '@/api/services/classes'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { genderOptions } from '@/config/constants'
import { randomPassword, cn } from '@/lib/utils'

interface Props {
  open: boolean
  onClose: () => void
  onSubmit: (values: StudentPayload) => void
  /** Tahrirlash uchun mavjud o'quvchi, qo'shish uchun null */
  initial?: Student | null
}

const empty: StudentPayload = {
  fullName: '',
  lastName: '',
  firstName: '',
  middleName: '',
  birthDate: '',
  birthCertificateUrl: null,
  address: '',
  gender: 'male',
  parentFullName: '',
  parentLastName: '',
  parentFirstName: '',
  parentMiddleName: '',
  parentPhone: '',
  parentPassportUrl: null,
  className: '',
  enrollmentDate: new Date().toISOString().slice(0, 10),
  discountPct: 0,
  discountAmount: 0,
  discountNote: '',
  subGroup: 0,
}

/** "Familiya Ism Sharifi" stringidan parts. Eski yozuvlarni tahrirda taqsimlaymiz. */
function splitFullName(full: string): { last: string; first: string; middle: string } {
  const parts = (full ?? '').trim().split(/\s+/).filter(Boolean)
  return {
    last: parts[0] ?? '',
    first: parts[1] ?? '',
    middle: parts.slice(2).join(' '),
  }
}

function joinName(last?: string, first?: string, middle?: string): string {
  return [last, first, middle]
    .map((p) => (p ?? '').trim())
    .filter((p) => p !== '')
    .join(' ')
}

export function StudentFormModal({ open, onClose, onSubmit, initial }: Props) {
  const [form, setForm] = useState<StudentPayload>(empty)
  const [classNames, setClassNames] = useState<string[]>([])
  /** Fayl yuklash holatlari (har maydon uchun alohida). */
  const [uploading, setUploading] = useState<{ birth?: boolean; passport?: boolean }>({})

  useEffect(() => {
    if (open) getClasses().then((cs) => setClassNames(cs.map((c) => c.name)))
  }, [open])

  useEffect(() => {
    if (!open) return
    if (initial) {
      // Tahrirda: agar parts saqlanmagan bo'lsa, FullName'dan parse qilamiz (eski o'quvchilar).
      const sParts = initial.lastName || initial.firstName || initial.middleName
        ? { last: initial.lastName ?? '', first: initial.firstName ?? '', middle: initial.middleName ?? '' }
        : splitFullName(initial.fullName)
      const pParts = initial.parentLastName || initial.parentFirstName || initial.parentMiddleName
        ? { last: initial.parentLastName ?? '', first: initial.parentFirstName ?? '', middle: initial.parentMiddleName ?? '' }
        : splitFullName(initial.parentFullName)
      // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda formani initial bilan sinxronlash (maqsadli)
      setForm({
        fullName: initial.fullName,
        lastName: sParts.last,
        firstName: sParts.first,
        middleName: sParts.middle,
        birthDate: initial.birthDate,
        birthCertificateUrl: initial.birthCertificateUrl ?? null,
        address: initial.address,
        gender: initial.gender,
        parentFullName: initial.parentFullName,
        parentLastName: pParts.last,
        parentFirstName: pParts.first,
        parentMiddleName: pParts.middle,
        parentPhone: initial.parentPhone,
        parentPassportUrl: initial.parentPassportUrl ?? null,
        className: initial.className,
        enrollmentDate: initial.enrollmentDate,
        discountPct: initial.discountPct,
        discountAmount: initial.discountAmount,
        discountNote: initial.discountNote,
        subGroup: initial.subGroup,
      })
    } else {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- yangi forma boshlash (maqsadli)
      setForm(empty)
    }
  }, [open, initial])

  // Yangi o'quvchida sinf tanlanmagan bo'lsa, birinchi sinfni standart qilamiz
  useEffect(() => {
    if (open && !initial && classNames.length) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- sinflar yuklangach standart sinfni o'rnatish (maqsadli)
      setForm((f) => (f.className ? f : { ...f, className: classNames[0] }))
    }
  }, [open, initial, classNames])

  const update = <K extends keyof StudentPayload>(key: K, value: StudentPayload[K]) =>
    setForm((f) => ({ ...f, [key]: value }))

  /** Fayl yuklash — admin uploads endpoint'iga uzatib, qaytgan URL'ni formaga yozadi. */
  const handleUpload = async (key: 'birthCertificateUrl' | 'parentPassportUrl', file: File) => {
    const flag = key === 'birthCertificateUrl' ? 'birth' : 'passport'
    setUploading((u) => ({ ...u, [flag]: true }))
    try {
      const res = await uploadAdminFile(file)
      update(key, res.url)
    } catch {
      // mock yoki tarmoq xatosi — sukut bilan o'tkazamiz; istasangiz toast ko'rsatish mumkin
    } finally {
      setUploading((u) => ({ ...u, [flag]: false }))
    }
  }

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    const last = (form.lastName ?? '').trim()
    const first = (form.firstName ?? '').trim()
    const middle = (form.middleName ?? '').trim()
    if (!last && !first && !middle) return
    const fullName = joinName(last, first, middle)
    const parentFullName = joinName(form.parentLastName, form.parentFirstName, form.parentMiddleName)
    onSubmit({ ...form, fullName, parentFullName })
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="lg"
      title={initial ? "O'quvchini tahrirlash" : "Yangi o'quvchi"}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button type="submit" form="student-form">
            Saqlash
          </Button>
        </>
      }
    >
      <form id="student-form" onSubmit={handleSubmit} className="space-y-5">
        {/* ---------- O'quvchi ---------- */}
        <Section title="O'quvchi">
          <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
            <Input
              label="Familiya"
              required
              value={form.lastName ?? ''}
              onChange={(e) => update('lastName', e.target.value)}
            />
            <Input
              label="Ism"
              required
              value={form.firstName ?? ''}
              onChange={(e) => update('firstName', e.target.value)}
            />
            <Input
              label="Otasining ismi"
              value={form.middleName ?? ''}
              onChange={(e) => update('middleName', e.target.value)}
            />
          </div>
          <div className="mt-3 grid grid-cols-1 gap-3 md:grid-cols-2">
            <Input
              label="Tug'ilgan kun"
              type="date"
              value={form.birthDate}
              onChange={(e) => update('birthDate', e.target.value)}
            />
            <Select
              label="Jinsi"
              value={form.gender}
              onChange={(e) => update('gender', e.target.value as StudentPayload['gender'])}
            >
              {genderOptions.map((g) => (
                <option key={g.value} value={g.value}>
                  {g.label}
                </option>
              ))}
            </Select>
          </div>
          <div className="mt-3">
            <FileField
              label="O'quvchi rasmi"
              url={form.birthCertificateUrl ?? null}
              uploading={uploading.birth}
              onUpload={(f) => handleUpload('birthCertificateUrl', f)}
              onClear={() => update('birthCertificateUrl', null)}
            />
          </div>
        </Section>

        {/* ---------- Ota-ona ---------- */}
        <Section title="Ota-ona">
          <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
            <Input
              label="Familiya"
              value={form.parentLastName ?? ''}
              onChange={(e) => update('parentLastName', e.target.value)}
            />
            <Input
              label="Ism"
              value={form.parentFirstName ?? ''}
              onChange={(e) => update('parentFirstName', e.target.value)}
            />
            <Input
              label="Otasining ismi"
              value={form.parentMiddleName ?? ''}
              onChange={(e) => update('parentMiddleName', e.target.value)}
            />
          </div>
          <div className="mt-3">
            <Input
              label="Telefon raqami"
              placeholder="+998 90 123 45 67"
              value={form.parentPhone}
              onChange={(e) => update('parentPhone', e.target.value)}
            />
          </div>
          <div className="mt-3">
            <FileField
              label="Ota-ona rasmi"
              url={form.parentPassportUrl ?? null}
              uploading={uploading.passport}
              onUpload={(f) => handleUpload('parentPassportUrl', f)}
              onClear={() => update('parentPassportUrl', null)}
            />
          </div>
        </Section>

        {/* ---------- Boshqa ma'lumotlar ---------- */}
        <Section title="Boshqa ma'lumotlar">
          <Input
            label="Manzil"
            value={form.address}
            onChange={(e) => update('address', e.target.value)}
          />
          <div className="mt-3 grid grid-cols-1 gap-3 md:grid-cols-2">
            <Select
              label="Sinfga biriktirish"
              value={form.className}
              onChange={(e) => update('className', e.target.value)}
            >
              {classNames.map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </Select>
            <Input
              label="Maktabga kelgan sana"
              type="date"
              value={form.enrollmentDate}
              onChange={(e) => update('enrollmentDate', e.target.value)}
            />
          </div>
        </Section>

        {/* ---------- Chegirma ---------- */}
        <Section title="Chegirma">
          <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
            <Input
              label="Foiz (%)"
              type="number"
              min={0}
              max={100}
              value={form.discountPct ?? 0}
              onChange={(e) =>
                update('discountPct', Math.max(0, Math.min(100, Number(e.target.value) || 0)))
              }
            />
            <Input
              label="Aniq summa (so'm)"
              type="number"
              min={0}
              step={1000}
              value={form.discountAmount ?? 0}
              onChange={(e) =>
                update('discountAmount', Math.max(0, Number(e.target.value) || 0))
              }
            />
          </div>
          <div className="mt-3">
            <Input
              label="Izoh (sabab)"
              placeholder="masalan: Aka-uka chegirmasi"
              value={form.discountNote ?? ''}
              onChange={(e) => update('discountNote', e.target.value)}
            />
          </div>
          <p className="mt-1 text-xs text-slate-400">
            Avval foiz ayriladi, keyin aniq summa. Oylik 0 dan past bo'lmaydi.
          </p>
        </Section>

        {/* ---------- Parol almashtirish (faqat tahrirda) ---------- */}
        {initial && (
          <Section title="Parolni almashtirish">
            <div className="flex items-start gap-2">
              <div className="flex-1">
                <Input
                  type="text"
                  autoComplete="new-password"
                  placeholder="Bo'sh qoldirilsa — parol o'zgarmaydi"
                  value={form.newPassword ?? ''}
                  onChange={(e) => update('newPassword', e.target.value)}
                />
              </div>
              <Button
                type="button"
                variant="secondary"
                onClick={() => update('newPassword', randomPassword())}
              >
                Generatsiya
              </Button>
            </div>
            <p className="mt-1 text-xs text-slate-400">
              Login (username) o'zgarmaydi. Yangi parolni kiriting yoki generatsiya qiling — saqlangach
              o'quvchiga topshiring.
            </p>
          </Section>
        )}
      </form>
    </Modal>
  )
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="rounded-xl border border-slate-200 bg-slate-50/40 p-3">
      <h4 className="mb-3 text-xs font-semibold uppercase tracking-wide text-slate-500">{title}</h4>
      {children}
    </div>
  )
}

function FileField({
  label,
  url,
  uploading,
  onUpload,
  onClear,
}: {
  label: string
  url: string | null
  uploading: boolean | undefined
  onUpload: (file: File) => void
  onClear: () => void
}) {
  const ref = useRef<HTMLInputElement | null>(null)
  const isImage = url && /\.(png|jpe?g|webp|gif|bmp)$/i.test(url)
  return (
    <div>
      <span className="mb-1 block text-sm font-medium text-slate-600">{label}</span>
      <div className="flex items-start gap-3">
        <div
          className={cn(
            'flex h-20 w-20 shrink-0 items-center justify-center rounded-lg border border-dashed bg-white',
            url ? 'border-brand-200' : 'border-slate-200 text-slate-300',
          )}
        >
          {uploading ? (
            <Loader2 className="h-5 w-5 animate-spin text-brand-500" />
          ) : isImage ? (
            <img src={url} alt="" className="h-full w-full rounded-lg object-cover" />
          ) : url ? (
            <FileText className="h-7 w-7 text-brand-500" />
          ) : (
            <Upload className="h-5 w-5" />
          )}
        </div>
        <div className="flex flex-1 flex-col gap-2">
          <input
            ref={ref}
            type="file"
            accept="image/*"
            className="hidden"
            onChange={(e) => {
              const f = e.target.files?.[0]
              if (f) onUpload(f)
              if (ref.current) ref.current.value = ''
            }}
          />
          <div className="flex gap-2">
            <Button
              type="button"
              variant="secondary"
              onClick={() => ref.current?.click()}
              disabled={uploading}
            >
              <Upload className="h-4 w-4" />
              {url ? 'Yangilash' : 'Yuklash'}
            </Button>
            {url && (
              <>
                <Button
                  type="button"
                  variant="secondary"
                  onClick={() => window.open(url, '_blank', 'noopener,noreferrer')}
                >
                  Ko'rish
                </Button>
                <Button type="button" variant="danger" onClick={onClear}>
                  <X className="h-4 w-4" /> O'chirish
                </Button>
              </>
            )}
          </div>
          {url && <p className="text-xs text-slate-400 break-all">{url}</p>}
        </div>
      </div>
    </div>
  )
}
