import { useEffect, useState } from 'react'
import { CheckCircle2, FileSignature, XCircle } from 'lucide-react'
import type { StudentListRow } from '@/api/services/studentSearch'
import {
  attachContractsMany,
  type BulkAttachContractRow,
} from '@/api/services/studentContracts'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Textarea } from '@/components/ui/Input'

interface Props {
  open: boolean
  onClose: () => void
  students: StudentListRow[]
  /** Muvaffaqiyatli (qisman bo'lsa ham) yakunlangач — ro'yxat qayta yuklanadi. */
  onDone: () => void
}

const errorText = (e: unknown, fallback: string) =>
  (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback

/**
 * K-5 — bitta shartnoma raqami va sanasini tanlangan bir nechta o'quvchiga
 * birdaniga biriktiradi (EduSchool'dagi "Bulk attach contract").
 *
 * <b>NEGA ODATDA FAQAT BITTASI MUVAFFAQIYATLI BO'LADI.</b>
 * <c>student_contracts.number</c> — QISMAN UNIKAL: bitta raqam bitta yozuvda
 * (§2.10.1 — ikki farzandli oilaga bitta faylning ikkita nusxasi ketmaydi,
 * har bir bola O'Z yozuviga ega bo'lishi kerak). EduSchool'da bu maydon
 * oddiy matn ustuni bo'lgani uchun bir nechta o'quvchida bir xil qiymat
 * bo'lishi mumkin edi; bizda esa raqam — reyestrdagi yozuvning o'zi, shuning
 * uchun BIRINCHI tanlangan o'quvchi yozuvni oladi, qolganlari esa "raqam band"
 * sababi bilan qaytadi. Natija baribir FOYDALI: kimga qo'shilgani va kimga
 * qo'lda alohida raqam kerakligi bir ko'rishda ko'rinadi.
 */
export function AttachContractModal({ open, onClose, students, onDone }: Props) {
  const [number, setNumber] = useState('')
  const [signedOn, setSignedOn] = useState(new Date().toISOString().slice(0, 10))
  const [endsOn, setEndsOn] = useState('')
  const [comment, setComment] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [rows, setRows] = useState<BulkAttachContractRow[] | null>(null)

  useEffect(() => {
    if (open) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- oyna har ochilganda holat tozalanadi (maqsadli)
      setNumber('')
      setSignedOn(new Date().toISOString().slice(0, 10))
      setEndsOn('')
      setComment('')
      setError(null)
      setRows(null)
    }
  }, [open])

  const handleSubmit = async () => {
    const trimmed = number.trim()
    if (!trimmed) return
    setBusy(true)
    setError(null)
    try {
      const res = await attachContractsMany({
        studentIds: students.map((s) => s.id),
        number: trimmed,
        signedOn: signedOn || null,
        endsOn: endsOn || null,
        comment: comment.trim() || null,
      })
      setRows(res.results)
    } catch (e) {
      setError(errorText(e, "Biriktirib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const attached = rows?.filter((r) => r.ok).length ?? 0

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={`${students.length} ta o'quvchiga shartnoma biriktirish`}
      size="md"
      footer={
        rows ? (
          <Button onClick={() => { onClose(); onDone() }}>Yopish</Button>
        ) : (
          <>
            <Button variant="secondary" onClick={onClose} disabled={busy}>
              Bekor qilish
            </Button>
            <Button onClick={handleSubmit} disabled={busy || !number.trim()}>
              <FileSignature className="h-4 w-4" /> {busy ? 'Biriktirilmoqda…' : 'Biriktirish'}
            </Button>
          </>
        )
      }
    >
      {rows ? (
        <div className="space-y-3">
          <p className="text-sm font-medium text-slate-800">
            {attached} / {rows.length} ta o'quvchiga biriktirildi.
          </p>
          <ul className="max-h-72 space-y-1 overflow-y-auto rounded-lg border border-slate-100">
            {rows.map((r) => (
              <li
                key={r.studentId}
                className="flex items-start gap-2 border-b border-slate-50 px-3 py-2 text-sm last:border-0"
              >
                {r.ok ? (
                  <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-emerald-500" />
                ) : (
                  <XCircle className="mt-0.5 h-4 w-4 shrink-0 text-red-500" />
                )}
                <span className="flex-1">
                  <span className="font-medium text-slate-700">{r.studentName ?? r.studentId}</span>
                  {!r.ok && r.reason && <span className="ml-1 text-red-600">— {r.reason}</span>}
                </span>
              </li>
            ))}
          </ul>
        </div>
      ) : (
        <div className="space-y-4">
          <div className="max-h-28 overflow-y-auto rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-600">
            {students.map((s) => (
              <div key={s.id}>{s.fullName} <span className="text-slate-400">— {s.className || 'sinfsiz'}</span></div>
            ))}
          </div>
          <p className="text-xs text-slate-400">
            Raqam va sana BIR XIL bo'lib har bir o'quvchiga qo'llaniladi. Shartnoma raqami
            reyestrda TAKRORLANMAS bo'lgani uchun odatda birinchi o'quvchi yozuvni oladi,
            qolganlari esa aniq sababi bilan pastda ko'rsatiladi.
          </p>
          <Input
            label="Shartnoma raqami"
            required
            value={number}
            onChange={(e) => setNumber(e.target.value)}
            placeholder="masalan 142"
          />
          <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
            <Input
              label="Imzo sanasi"
              type="date"
              value={signedOn}
              onChange={(e) => setSignedOn(e.target.value)}
            />
            <Input
              label="Tugash sanasi (ixtiyoriy)"
              type="date"
              value={endsOn}
              onChange={(e) => setEndsOn(e.target.value)}
            />
          </div>
          <Textarea
            label="Izoh (ixtiyoriy)"
            rows={2}
            value={comment}
            onChange={(e) => setComment(e.target.value)}
          />
          {error && <p className="text-sm text-red-600">{error}</p>}
        </div>
      )}
    </Modal>
  )
}
