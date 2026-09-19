import { useEffect, useState } from 'react'
import { FileText } from 'lucide-react'
import type { ContractTemplate } from '@/types'
import { getTemplates } from '@/api/services/contracts'
import type { StudentContract, StudentContractPreview } from '@/api/services/studentContracts'
import { generateStudentContract, previewStudentContract } from '@/api/services/studentContracts'
import { Button } from '@/components/ui/Button'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { DatePicker } from '@/components/ui/DatePicker'

interface Props {
  open: boolean
  /**
   * O'quvchi ODDIY QIYMATLAR bilan uzatiladi, obyekt bilan emas: obyekt har
   * render'da yangi havola bo'lardi va quyidagi effekt cheksiz qayta
   * ishlardi (u setState chaqiradi).
   */
  studentId: string | null
  studentName: string
  onClose: () => void
  onCreated: (contract: StudentContract) => void
}

/**
 * Bitta o'quvchi uchun Word andozadan shartnoma hosil qilish —
 * docs/modules/students-parity.md §2.10 (K-2).
 *
 * MAVJUD YUBORISH OQIMI TEGILMAGAN. "Shartnomalar → Xodimlar / Ota-onalar"
 * tab'i avvalgidek ishlaydi: andozani to'ldirib Telegram orqali yuboradi.
 * Bu yerda esa hujjat HOSIL QILINADI va reyestrga yoziladi — hech kimga
 * yuborilmaydi. Yuborish kerak bo'lsa, fayl reyestrdan yuklab olinadi.
 *
 * Raqam avtomatik taklif qilinadi (keyingi bo'sh raqam), lekin qo'lda
 * o'zgartirilishi mumkin — sozlama sifatida qat'iy rejim (K-6) keyingi
 * to'lqinda.
 */
export function GenerateContractModal({
  open,
  studentId,
  studentName,
  onClose,
  onCreated,
}: Props) {
  const [templates, setTemplates] = useState<ContractTemplate[]>([])
  const [preview, setPreview] = useState<StudentContractPreview | null>(null)
  const [templateId, setTemplateId] = useState('')
  const [number, setNumber] = useState('')
  const [signedOn, setSignedOn] = useState('')
  const [endsOn, setEndsOn] = useState('')
  const [comment, setComment] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (!open || !studentId) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda andozalar va keyingi raqam so'raladi (maqsadli)
    setLoading(true)
    setError(null)
    setComment('')
    setEndsOn('')
    Promise.all([getTemplates('parent'), previewStudentContract(studentId)])
      .then(([list, data]) => {
        setTemplates(list)
        setTemplateId(list[0]?.id ?? '')
        setPreview(data)
        setNumber(data.nextNumber)
        setSignedOn(data.today)
      })
      .catch((err) => setError(errorText(err, "Ma'lumotni olib bo'lmadi")))
      .finally(() => setLoading(false))
  }, [open, studentId])

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!studentId || !templateId) {
      setError('Andozani tanlang')
      return
    }
    setBusy(true)
    setError(null)
    try {
      onCreated(
        await generateStudentContract({
          studentId,
          templateId,
          number: number.trim(),
          signedOn: signedOn.trim(),
          endsOn: endsOn.trim(),
          comment: comment.trim(),
        }),
      )
    } catch (err) {
      setError(errorText(err, "Hosil qilib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="lg"
      title={`Andozadan shartnoma — ${studentName}`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button type="submit" form="generate-contract-form" disabled={busy || loading}>
            {busy ? 'Hosil qilinmoqda...' : 'Hosil qilish'}
          </Button>
        </>
      }
    >
      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <form id="generate-contract-form" onSubmit={submit} className="space-y-4">
          {templates.length === 0 ? (
            <p className="rounded-lg bg-amber-50 p-3 text-sm text-amber-700">
              Word andoza yuklanmagan. "Shartnomalar → Ota-onalar" tab'ida andoza yuklang.
            </p>
          ) : (
            <Select
              label="Andoza"
              required
              value={templateId}
              onChange={(e) => setTemplateId(e.target.value)}
            >
              {templates.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.name || t.fileName}
                </option>
              ))}
            </Select>
          )}

          <div className="grid gap-4 sm:grid-cols-3">
            <Input
              label="Shartnoma raqami"
              value={number}
              onChange={(e) => setNumber(e.target.value)}
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

          <Textarea
            label="Izoh"
            rows={2}
            value={comment}
            onChange={(e) => setComment(e.target.value)}
          />

          {/* Andozaga nima tushishi — oldindan ko'rinadi (§2.10.1 "review-and-edit"). */}
          {preview && (
            <div className="rounded-lg bg-slate-50 p-3">
              <p className="mb-2 flex items-center gap-2 text-xs font-medium text-slate-500">
                <FileText className="h-3.5 w-3.5" />
                Andozadagi o'rinbosarlar shu qiymatlar bilan almashtiriladi:
              </p>
              <div className="grid gap-x-4 gap-y-1 sm:grid-cols-2">
                {preview.tokens.map((t) => (
                  <div key={t.token} className="flex items-baseline justify-between gap-2 text-xs">
                    <code className="rounded bg-white px-1.5 py-0.5 font-medium text-brand-700 ring-1 ring-slate-200">
                      {t.token}
                    </code>
                    <span className="truncate text-slate-600">{t.value || '—'}</span>
                  </div>
                ))}
              </div>
              <p className="mt-2 text-xs text-slate-400">
                Raqam va sanalar yuqoridagi maydonlardan olinadi.
              </p>
            </div>
          )}

          {error && <p className="text-sm text-red-600">{error}</p>}
        </form>
      )}
    </Modal>
  )
}

function errorText(e: unknown, fallback: string) {
  return (
    (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback
  )
}
