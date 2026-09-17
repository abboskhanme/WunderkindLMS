import { useRef, useState } from 'react'
import { FileDown, Upload } from 'lucide-react'
import {
  commitStudentImport,
  downloadStudentImportTemplateV2,
  validateStudentImport,
  type StudentImportPreview,
} from '@/api/services/studentImport'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'

/**
 * Ikki bosqichli import (§2.3, S-3): shablon → tekshirish → tasdiqlash.
 *
 * QOIDA: YARIM IMPORT BO'LMAYDI. Tekshirish bazaga hech narsa yozmaydi va
 * har xatoni QATOR raqami bilan ko'rsatadi; tasdiqlash esa bitta ham xato
 * bo'lsa hech narsa yozmaydi. Shu sababli "Yozish" tugmasi faqat fayl
 * butunlay toza bo'lgandagina faollashadi.
 */

interface Props {
  open: boolean
  onClose: () => void
  /** Yozib bo'lingach — ro'yxatni qayta o'qish uchun. */
  onImported: (created: number, updated: number) => void
}

const errorText = (e: unknown, fallback: string) =>
  (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback

export function StudentImportModal({ open, onClose, onImported }: Props) {
  const [file, setFile] = useState<File | null>(null)
  const [preview, setPreview] = useState<StudentImportPreview | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const inputRef = useRef<HTMLInputElement>(null)

  const reset = () => {
    setFile(null)
    setPreview(null)
    setError(null)
  }

  const close = () => {
    reset()
    onClose()
  }

  const pick = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const picked = e.target.files?.[0]
    e.target.value = '' // bir xil faylni qayta tanlash mumkin bo'lsin
    if (!picked) return
    setFile(picked)
    setPreview(null)
    setError(null)
    setBusy(true)
    try {
      setPreview(await validateStudentImport(picked))
    } catch (err) {
      setError(errorText(err, "Faylni tekshirib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const commit = async () => {
    if (!file) return
    setBusy(true)
    setError(null)
    try {
      const result = await commitStudentImport(file)
      onImported(result.created, result.updated)
      reset()
      onClose()
    } catch (err) {
      // Server tasdiqlashda ham qayta tekshiradi: oraliqda sinf o'chirilgan
      // yoki holat faolsizlantirilgan bo'lishi mumkin.
      const data = (err as { response?: { data?: StudentImportPreview } })?.response?.data
      if (data && Array.isArray(data.errors)) setPreview(data)
      else setError(errorText(err, "Yozib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const ready = !!preview?.ok && !busy

  return (
    <Modal
      open={open}
      onClose={close}
      title="Excel'dan o'quvchilarni yuklash"
      size="lg"
      footer={
        <>
          <Button variant="secondary" onClick={close} disabled={busy}>
            Yopish
          </Button>
          <Button onClick={commit} disabled={!ready}>
            {busy ? 'Yozilmoqda…' : 'Yozish'}
          </Button>
        </>
      }
    >
      <div className="space-y-4 text-sm">
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="secondary" onClick={() => downloadStudentImportTemplateV2()} disabled={busy}>
            <FileDown className="h-4 w-4" /> Shablon
          </Button>
          <Button variant="secondary" onClick={() => inputRef.current?.click()} disabled={busy}>
            <Upload className="h-4 w-4" /> {file ? 'Boshqa fayl' : 'Fayl tanlash'}
          </Button>
          <input
            ref={inputRef}
            type="file"
            accept=".xlsx"
            className="hidden"
            onChange={pick}
          />
          {file && <span className="text-slate-500">{file.name}</span>}
        </div>

        <p className="rounded-lg bg-slate-50 px-3 py-2 text-slate-500">
          Fayl avval <b>tekshiriladi</b> — bazaga hech narsa yozilmaydi. Bitta xato qator bo'lsa
          ham yozish boshlanmaydi. F.I.SH, tug'ilgan sana va sinf mos kelsa mavjud o'quvchi
          <b> yangilanadi</b>; bo'sh katak mavjud qiymatni o'chirmaydi.
        </p>

        {busy && !preview && <p className="text-slate-500">Tekshirilmoqda…</p>}
        {error && <p className="text-red-600">{error}</p>}

        {preview && (
          <div className="space-y-3">
            <div className="flex flex-wrap gap-x-5 gap-y-1">
              <span className="text-emerald-700">
                Yangi: <b>{preview.created}</b>
              </span>
              <span className="text-brand-700">
                Yangilanadi: <b>{preview.updated}</b>
              </span>
              {preview.skipped > 0 && (
                <span className="text-slate-500">
                  Bo'sh qator: <b>{preview.skipped}</b>
                </span>
              )}
              {preview.errors.length > 0 && (
                <span className="text-red-600">
                  Xato: <b>{preview.errors.length}</b>
                </span>
              )}
            </div>

            {preview.message && (
              <p className={cn('rounded-lg px-3 py-2', preview.ok ? 'bg-slate-50 text-slate-600' : 'bg-red-50 text-red-700')}>
                {preview.message}
              </p>
            )}

            {preview.errors.length > 0 && (
              <div className="max-h-72 overflow-auto rounded-lg border border-slate-100">
                <table className="w-full text-left text-sm">
                  <thead className="sticky top-0 bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                    <tr>
                      <th className="w-16 px-3 py-2">Qator</th>
                      <th className="px-3 py-2">Ustun</th>
                      <th className="px-3 py-2">Xato</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {preview.errors.map((e, i) => (
                      <tr key={`${e.row}-${e.column}-${i}`}>
                        <td className="px-3 py-2 text-slate-500">{e.row}</td>
                        <td className="px-3 py-2 text-slate-600">{e.column}</td>
                        <td className="px-3 py-2 text-red-600">{e.message}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            {preview.ok && preview.preview.length > 0 && (
              <div className="max-h-72 overflow-auto rounded-lg border border-slate-100">
                <table className="w-full text-left text-sm">
                  <thead className="sticky top-0 bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                    <tr>
                      <th className="w-16 px-3 py-2">Qator</th>
                      <th className="px-3 py-2">F.I.SH</th>
                      <th className="px-3 py-2">Sinf</th>
                      <th className="px-3 py-2">Amal</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {preview.preview.map((r) => (
                      <tr key={r.row}>
                        <td className="px-3 py-2 text-slate-400">{r.row}</td>
                        <td className="px-3 py-2 font-medium text-slate-800">{r.fullName}</td>
                        <td className="px-3 py-2 text-slate-600">{r.className}</td>
                        <td className="px-3 py-2">
                          <span
                            className={cn(
                              'rounded-md px-2 py-0.5 text-xs font-medium',
                              r.action === 'create'
                                ? 'bg-emerald-50 text-emerald-600'
                                : 'bg-amber-50 text-amber-700',
                            )}
                          >
                            {r.action === 'create' ? 'yangi' : 'yangilanadi'}
                          </span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                {preview.created + preview.updated > preview.preview.length && (
                  <p className="px-3 py-2 text-xs text-slate-400">
                    … yana {preview.created + preview.updated - preview.preview.length} ta qator
                  </p>
                )}
              </div>
            )}
          </div>
        )}
      </div>
    </Modal>
  )
}
