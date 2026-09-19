import { useEffect, useState } from 'react'
import { FileUp, Search, X } from 'lucide-react'
import type { SaveStudentContractInput, StudentContract } from '@/api/services/studentContracts'
import { searchStudents } from '@/api/services/studentSearch'
import { uploadAdminFile } from '@/api/services/students'
import { Button } from '@/components/ui/Button'
import { Input, Textarea } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { cn } from '@/lib/utils'
import { DatePicker } from '@/components/ui/DatePicker'

interface Props {
  open: boolean
  /** null — yangi yozuv. */
  contract: StudentContract | null
  /**
   * Kartochkadan ochilganda o'quvchi oldindan belgilanadi va o'zgartirilmaydi.
   * ODDIY QIYMATLAR bilan uzatiladi, obyekt bilan emas: obyekt har render'da
   * yangi havola bo'lardi va quyidagi effekt cheksiz qayta ishlardi.
   */
  fixedStudentId?: string
  fixedStudentName?: string
  onClose: () => void
  onSubmit: (input: SaveStudentContractInput) => Promise<void>
}

interface Candidate {
  id: string
  fullName: string
  className: string
}

/**
 * O'quvchi shartnomasi yozuvi — docs/modules/students-parity.md §2.10
 * (K-1 yozuv, K-3 imzolangan nusxa).
 *
 * SUMMA MAYDONI YO'Q va bo'lmaydi: shartnoma yozuvi — hujjat (raqam, sana,
 * fayl). O'quvchi nima to'lashini obuna hal qiladi (Moliya), va narx ikkinchi
 * joyda saqlansa bir kun birinchisi bilan ziddiyatga tushardi.
 *
 * FAYL mavjud yuklash yo'li bilan boradi (`/api/admin/uploads` + UploadGuard);
 * bu yerda faqat qaytgan manzil saqlanadi.
 */
export function StudentContractFormModal({
  open,
  contract,
  fixedStudentId,
  fixedStudentName,
  onClose,
  onSubmit,
}: Props) {
  const [studentId, setStudentId] = useState('')
  const [studentName, setStudentName] = useState('')
  const [query, setQuery] = useState('')
  const [candidates, setCandidates] = useState<Candidate[]>([])
  const [searching, setSearching] = useState(false)
  const [number, setNumber] = useState('')
  const [signedOn, setSignedOn] = useState('')
  const [endsOn, setEndsOn] = useState('')
  const [fileUrl, setFileUrl] = useState('')
  const [fileName, setFileName] = useState('')
  const [comment, setComment] = useState('')
  const [uploading, setUploading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda forma tanlangan yozuv bilan to'ldiriladi (maqsadli)
    setStudentId(contract?.studentId ?? fixedStudentId ?? '')
    setStudentName(contract?.studentName ?? fixedStudentName ?? '')
    setQuery('')
    setCandidates([])
    setNumber(contract?.number ?? '')
    setSignedOn(contract?.signedOn ?? '')
    setEndsOn(contract?.endsOn ?? '')
    setFileUrl(contract?.fileUrl ?? '')
    setFileName(contract?.fileUrl ? contract.fileUrl.split('/').pop()! : '')
    setComment(contract?.comment ?? '')
    setError(null)
  }, [open, contract, fixedStudentId, fixedStudentName])

  const runSearch = async () => {
    const term = query.trim()
    if (term.length < 2) return
    setSearching(true)
    try {
      const page = await searchStudents({ search: term, state: 'all', pageSize: 20 })
      setCandidates(
        page.items.map((s) => ({ id: s.id, fullName: s.fullName, className: s.className })),
      )
    } finally {
      setSearching(false)
    }
  }

  const upload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (!file) return
    setUploading(true)
    setError(null)
    try {
      const up = await uploadAdminFile(file)
      setFileUrl(up.url)
      setFileName(file.name)
    } catch (err) {
      setError(errorText(err, "Faylni yuklab bo'lmadi"))
    } finally {
      setUploading(false)
      e.target.value = ''
    }
  }

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!studentId) {
      setError("O'quvchini tanlang")
      return
    }
    setBusy(true)
    setError(null)
    try {
      await onSubmit({
        studentId,
        number: number.trim(),
        signedOn: signedOn.trim(),
        endsOn: endsOn.trim(),
        fileUrl: fileUrl.trim(),
        comment: comment.trim(),
        // Qo'lda kiritilgan yozuv — "yuklangan"; tizim hosil qilgani alohida
        // oqimdan (Andozadan hosil qilish) keladi va bu yerda o'zgarmaydi.
        source: contract?.source ?? 'uploaded',
      })
    } catch (err) {
      setError(errorText(err, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const lockedStudent = Boolean(contract || fixedStudentId)

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={contract ? 'Shartnomani tahrirlash' : 'Yangi shartnoma yozuvi'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button type="submit" form="student-contract-form" disabled={busy || uploading}>
            {busy ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <form id="student-contract-form" onSubmit={submit} className="space-y-4">
        {/* O'quvchi */}
        {lockedStudent ? (
          <div>
            <span className="mb-1 block text-sm font-medium text-slate-600">O'quvchi</span>
            <p className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-700">
              {studentName || studentId}
            </p>
          </div>
        ) : (
          <div className="space-y-2">
            <div className="flex items-end gap-2">
              <div className="flex-1">
                <Input
                  label="O'quvchi"
                  required
                  value={studentId ? studentName : query}
                  onChange={(e) => {
                    setStudentId('')
                    setQuery(e.target.value)
                  }}
                  onKeyDown={(e) => {
                    if (e.key !== 'Enter') return
                    e.preventDefault()
                    runSearch()
                  }}
                  placeholder="Familiya yoki ism (kamida 2 harf)"
                />
              </div>
              <Button type="button" variant="secondary" onClick={runSearch} disabled={searching}>
                <Search className="h-4 w-4" />
                {searching ? '...' : 'Qidirish'}
              </Button>
            </div>

            {studentId && (
              <p className="flex items-center gap-2 text-xs text-emerald-700">
                Tanlandi: {studentName}
                <button
                  type="button"
                  onClick={() => {
                    setStudentId('')
                    setStudentName('')
                  }}
                  className="rounded p-0.5 hover:bg-emerald-50"
                >
                  <X className="h-3 w-3" />
                </button>
              </p>
            )}

            {!studentId && candidates.length > 0 && (
              <ul className="max-h-40 divide-y divide-slate-100 overflow-y-auto rounded-lg border border-slate-200">
                {candidates.map((c) => (
                  <li key={c.id}>
                    <button
                      type="button"
                      onClick={() => {
                        setStudentId(c.id)
                        setStudentName(c.fullName)
                        setCandidates([])
                      }}
                      className="flex w-full items-center justify-between px-3 py-2 text-left text-sm hover:bg-slate-50"
                    >
                      <span className="text-slate-700">{c.fullName}</span>
                      <span className="text-xs text-slate-400">{c.className}</span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
        )}

        <div className="grid gap-4 sm:grid-cols-3">
          <Input
            label="Shartnoma raqami"
            value={number}
            onChange={(e) => setNumber(e.target.value)}
            placeholder="raqamsiz — qoralama"
          />
          <DatePicker
            label="Imzo sanasi"
            value={signedOn}
            onChange={(value: string) => setSignedOn(value)}
          />
          <DatePicker
            label="Tugash sanasi"
            value={endsOn}
            onChange={(value: string) => setEndsOn(value)}
          />
        </div>

        {/* Fayl */}
        <div>
          <span className="mb-1 block text-sm font-medium text-slate-600">
            Imzolangan nusxa (skaner yoki PDF)
          </span>
          <div className="flex flex-wrap items-center gap-2">
            <label
              className={cn(
                'inline-flex cursor-pointer items-center gap-2 rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 transition-colors hover:bg-slate-50',
                uploading && 'pointer-events-none opacity-60',
              )}
            >
              <FileUp className="h-4 w-4" />
              {uploading ? 'Yuklanmoqda...' : 'Fayl yuklash'}
              <input type="file" hidden onChange={upload} />
            </label>
            {fileUrl && (
              <span className="flex items-center gap-2 text-sm text-slate-600">
                <a
                  href={fileUrl}
                  target="_blank"
                  rel="noreferrer"
                  className="text-brand-600 hover:underline"
                >
                  {fileName || 'Fayl'}
                </a>
                <button
                  type="button"
                  title="Olib tashlash"
                  onClick={() => {
                    setFileUrl('')
                    setFileName('')
                  }}
                  className="rounded p-0.5 text-slate-400 hover:bg-slate-100 hover:text-slate-600"
                >
                  <X className="h-3.5 w-3.5" />
                </button>
              </span>
            )}
          </div>
        </div>

        <Textarea
          label="Izoh"
          rows={2}
          value={comment}
          onChange={(e) => setComment(e.target.value)}
          placeholder="Qog'oz nusxa qayerda, kim imzoladi..."
        />

        {error && <p className="text-sm text-red-600">{error}</p>}
      </form>
    </Modal>
  )
}

function errorText(e: unknown, fallback: string) {
  return (
    (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback
  )
}
