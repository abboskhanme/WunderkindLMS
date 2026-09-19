import { useMemo, useRef, useState } from 'react'
import { FileUp, Paperclip, X } from 'lucide-react'
import type { Student, Subject, Teacher } from '@/types'
import {
  createCertificate,
  updateCertificate,
  certificateError,
  type Certificate,
  type CertificateType,
} from '@/api/services/certificates'
import { uploadAdminFile } from '@/api/services/students'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { cn } from '@/lib/utils'
import { DatePicker } from '@/components/ui/DatePicker'

interface Props {
  editing: Certificate | null
  types: CertificateType[]
  students: Student[]
  subjects: Subject[]
  teachers: Teacher[]
  /** Ro'yxat ekranida tanlangan o'quvchi — yangi hujjatda oldindan qo'yiladi. */
  defaultStudentId?: string
  onClose: () => void
  onSaved: (saved: Certificate) => void
}

/**
 * Sertifikat formasi (§2.3).
 *
 * <b>Ball maydoni turga qarab OCHILADI.</b> Tur "ball qo'yiladi" bo'lmasa maydon
 * umuman ko'rinmaydi va yuborilmaydi — server ham aynan shuni talab qiladi.
 * Ya'ni xodim taqiqlangan qiymatni kiritib, keyin xato o'qiydigan holatga
 * tushmaydi; xato faqat zaxira chorasi bo'lib qoladi.
 *
 * <b>Fayl mavjud yuklash yo'li bilan ketadi</b> — `uploadAdminFile`
 * (`POST /api/admin/uploads`, UploadGuard tekshiruvi bilan). Ikkinchi yuklash
 * yo'li ochilmaydi: allowlist bir joyda turishi kerak.
 *
 * Har ochilganda QAYTA ULANADI (ota-komponent shartli render qiladi), shuning
 * uchun boshlang'ich qiymatlar `useState` da — effekt ichida `setState` yo'q.
 */
export function CertificateFormModal({
  editing,
  types,
  students,
  subjects,
  teachers,
  defaultStudentId,
  onClose,
  onSaved,
}: Props) {
  const [studentId, setStudentId] = useState(editing?.studentId ?? defaultStudentId ?? '')
  const [typeId, setTypeId] = useState(editing?.typeId ?? '')
  // Z-3: bir nechta fan. Eski, bitta-fanli hujjatlarda ham `subjectIds` backfill orqali
  // to'ldirilgan (docs/modules/students-parity.md §2.7 Z-3); shunga qaramay `subjectId`
  // ham zaxira sifatida tekshiriladi — hech qanday holatda fan "yo'qolib qolmasin".
  const [subjectIds, setSubjectIds] = useState<string[]>(
    editing?.subjectIds?.length ? editing.subjectIds : editing?.subjectId ? [editing.subjectId] : [],
  )
  const [teacherId, setTeacherId] = useState(editing?.teacherId ?? '')
  const [number, setNumber] = useState(editing?.number ?? '')
  const [score, setScore] = useState(editing?.score != null ? String(editing.score) : '')
  const [issuedOn, setIssuedOn] = useState(editing?.issuedOn ?? '')
  const [expiresOn, setExpiresOn] = useState(editing?.expiresOn ?? '')
  const [fileUrl, setFileUrl] = useState(editing?.fileUrl ?? '')
  const [comment, setComment] = useState(editing?.comment ?? '')
  const [studentSearch, setStudentSearch] = useState('')
  const [uploading, setUploading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const fileRef = useRef<HTMLInputElement>(null)

  /**
   * Tanlanadigan turlar: faol turlar, ustiga tahrirlanayotgan hujjatning turi
   * (u keyinchalik arxivlangan bo'lishi mumkin — ro'yxatdan tushib qolmasin).
   */
  const selectableTypes = useMemo(() => {
    const list = types.filter((t) => t.isActive)
    if (editing && !list.some((t) => t.id === editing.typeId)) {
      const current = types.find((t) => t.id === editing.typeId)
      if (current) list.push(current)
    }
    return list
  }, [types, editing])

  const selectedType = types.find((t) => t.id === typeId) ?? null
  const scored = selectedType?.isScored ?? false

  const visibleStudents = useMemo(() => {
    const q = studentSearch.trim().toLowerCase()
    const list = q
      ? students.filter((s) => s.fullName.toLowerCase().includes(q))
      : students
    // Tanlangan o'quvchi qidiruvdan tushib qolmasin, aks holda select o'zini bo'shatadi.
    if (studentId && !list.some((s) => s.id === studentId)) {
      const current = students.find((s) => s.id === studentId)
      if (current) return [current, ...list]
    }
    return list
  }, [students, studentSearch, studentId])

  const toggleSubject = (id: string) =>
    setSubjectIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]))

  const pickFile = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    e.target.value = ''
    if (!file) return
    setUploading(true)
    setError('')
    try {
      const uploaded = await uploadAdminFile(file)
      setFileUrl(uploaded.url)
    } catch (err) {
      setError(certificateError(err))
    } finally {
      setUploading(false)
    }
  }

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!studentId || !typeId || !issuedOn) return
    setSaving(true)
    setError('')
    try {
      const payload = {
        studentId,
        typeId,
        subjectIds,
        teacherId: teacherId || null,
        number: number.trim() || null,
        // Ballsiz turda maydon yashirin — qiymat ham yuborilmaydi.
        score: scored && score.trim() !== '' ? Number(score) : null,
        issuedOn,
        expiresOn: expiresOn || null,
        fileUrl: fileUrl || null,
        comment: comment.trim() || null,
      }
      const saved = editing
        ? await updateCertificate(editing.id, payload)
        : await createCertificate(payload)
      onSaved(saved)
    } catch (err) {
      setError(certificateError(err))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title={editing ? 'Sertifikatni tahrirlash' : 'Yangi sertifikat'}
      size="lg"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button
            type="submit"
            form="certificate-form"
            disabled={saving || uploading || !studentId || !typeId || !issuedOn}
          >
            Saqlash
          </Button>
        </>
      }
    >
      <form id="certificate-form" onSubmit={submit} className="space-y-4">
        <div className="grid gap-4 sm:grid-cols-2">
          <div className="space-y-2">
            <Input
              label="O'quvchini qidirish"
              placeholder="F.I.SH bo'yicha"
              value={studentSearch}
              onChange={(e) => setStudentSearch(e.target.value)}
            />
            <Select
              label="O'quvchi"
              required
              value={studentId}
              onChange={(e) => setStudentId(e.target.value)}
            >
              <option value="">— tanlang —</option>
              {visibleStudents.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.fullName} {s.className ? `(${s.className})` : ''}
                </option>
              ))}
            </Select>
          </div>

          <div className="space-y-2">
            <Select
              label="Sertifikat turi"
              required
              value={typeId}
              onChange={(e) => setTypeId(e.target.value)}
            >
              <option value="">— tanlang —</option>
              {selectableTypes.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.name}
                  {t.isScored ? ' (ballik)' : ''}
                </option>
              ))}
            </Select>

            {scored ? (
              <Input
                label="Ball"
                type="number"
                step="0.01"
                min="0"
                placeholder="masalan: 7.5"
                value={score}
                onChange={(e) => setScore(e.target.value)}
              />
            ) : (
              <p className="pt-1 text-xs text-slate-400">
                Ball faqat "ball qo'yiladi" deb belgilangan turlarda kiritiladi.
              </p>
            )}
          </div>

          <div className="sm:col-span-2">
            {/* Z-3: bitta hujjat bir nechta fanni qamrab olishi mumkin — masalan
                "Matematika + Fizika olimpiadasi". Birinchi belgilangan fan ASOSIY
                bo'lib qoladi (ro'yxatdagi "Fan" ustuni, eksport). */}
            <span className="mb-2 block text-sm font-medium text-slate-600">Fan(lar)</span>
            <div className="flex flex-wrap gap-2">
              {subjects.map((s) => {
                const active = subjectIds.includes(s.id)
                return (
                  <button
                    key={s.id}
                    type="button"
                    onClick={() => toggleSubject(s.id)}
                    className={cn(
                      'rounded-full border px-3 py-1 text-sm transition-colors',
                      active
                        ? 'border-brand-500 bg-brand-50 text-brand-700'
                        : 'border-slate-200 text-slate-600 hover:bg-slate-50',
                    )}
                  >
                    {s.name}
                  </button>
                )
              })}
              {subjects.length === 0 && (
                <p className="text-sm text-slate-400">Avval fan qo'shing</p>
              )}
            </div>
          </div>

          <Select
            label="O'qituvchi"
            value={teacherId}
            onChange={(e) => setTeacherId(e.target.value)}
          >
            <option value="">— tanlanmagan —</option>
            {teachers.map((t) => (
              <option key={t.id} value={t.id}>
                {t.fullName}
              </option>
            ))}
          </Select>

          <Input
            label="Hujjat raqami"
            placeholder="blank / sertifikat kodi"
            value={number}
            onChange={(e) => setNumber(e.target.value)}
          />

          <div />

          <DatePicker
            label="Berilgan sana"
            required
            value={issuedOn}
            onChange={(value: string) => setIssuedOn(value)}
          />

          <DatePicker
            label="Amal qilish muddati"
            value={expiresOn}
            onChange={(value: string) => setExpiresOn(value)}
          />
        </div>

        <div>
          <span className="mb-1 block text-sm font-medium text-slate-600">Fayl (skan)</span>
          <div className="flex flex-wrap items-center gap-2">
            <Button
              type="button"
              variant="secondary"
              disabled={uploading}
              onClick={() => fileRef.current?.click()}
            >
              <FileUp className="h-4 w-4" /> {uploading ? 'Yuklanmoqda…' : 'Fayl yuklash'}
            </Button>
            <input
              ref={fileRef}
              type="file"
              accept=".jpg,.jpeg,.png,.webp,.heic,.pdf,.doc,.docx"
              className="hidden"
              onChange={pickFile}
            />
            {fileUrl && (
              <span className="inline-flex items-center gap-2 rounded-lg bg-slate-50 px-2.5 py-1.5 text-sm text-slate-600">
                <Paperclip className="h-4 w-4 text-slate-400" />
                <a
                  href={fileUrl}
                  target="_blank"
                  rel="noreferrer"
                  className="font-medium text-brand-600 hover:text-brand-700"
                >
                  Faylni ochish
                </a>
                <button
                  type="button"
                  title="Faylni olib tashlash"
                  onClick={() => setFileUrl('')}
                  className="rounded p-0.5 text-slate-400 hover:text-red-600"
                >
                  <X className="h-3.5 w-3.5" />
                </button>
              </span>
            )}
          </div>
        </div>

        <Textarea
          label="Izoh"
          rows={3}
          placeholder="qo'shimcha ma'lumot"
          value={comment}
          onChange={(e) => setComment(e.target.value)}
        />

        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
      </form>
    </Modal>
  )
}
