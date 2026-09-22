/**
 * "Excel'dan import" — questions into one bank from `.xlsx`
 * (§3.1 screen 4; §6.2 `import/template`, `import`; §13 Q9).
 *
 * ONE ENDPOINT, TWO POSTS (§6.2). Picking a file posts it with
 * `dryRun=true`: the server validates, writes nothing, and reports. "Yozish"
 * posts the SAME file with `dryRun=false`: the server validates again and
 * writes the valid rows. No staging token, so no "session expired" step.
 *
 * PARTIAL IMPORT IS BY DESIGN HERE. A row with any error is skipped whole;
 * the valid rows in the same file still import (§6.2). The preview says so
 * before the button is pressed, and names every skipped row.
 *
 * ERRORS ARE CHIPS. `errors[]` is grouped by `reason`, one of a closed set of
 * seven (§6.2); each renders as one chip with the offending row numbers —
 * 1-based sheet rows including the header, i.e. what the user sees in Excel.
 *
 * `.xlsx` ONLY (§13 Q9). The extension is checked before any upload, with the
 * server's own sentence; the server checks again.
 *
 * Mounted conditionally by the parent — every opening starts clean.
 */
import { useRef, useState } from 'react'
import type { ChangeEvent } from 'react'
import { FileDown, FileSpreadsheet, Loader2, Upload } from 'lucide-react'
import {
  XLSX_ONLY_MESSAGE,
  admissionBankError,
  downloadQuestionTemplate,
  importQuestions,
  isXlsx,
  type QuestionImportResult,
} from '@/api/services/admissionBanks'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { importReasonLabel } from './BankHelpers'

interface Props {
  bankId: string
  /** "5-sinf · Matematika" — so the user sees which bank they are filling. */
  bankLabel: string
  onClose: () => void
  onImported: (result: QuestionImportResult) => void
}

type Phase = 'idle' | 'template' | 'checking' | 'importing'

/** Long row lists are cut here — the chip must stay one readable line or two. */
const ROWS_SHOWN = 30

const TEMPLATE_COLUMNS = [
  'Savol matni',
  'A',
  'B',
  'C',
  'D',
  'E',
  'F',
  "To'g'ri javob (A-F)",
  'Rasm havolasi',
]

function rowsText(rows: number[]): string {
  const sorted = [...rows].sort((a, b) => a - b)
  const shown = sorted.slice(0, ROWS_SHOWN).join(', ')
  return sorted.length > ROWS_SHOWN ? `${shown} … (+${sorted.length - ROWS_SHOWN})` : shown
}

export function QuestionImportModal({ bankId, bankLabel, onClose, onImported }: Props) {
  const [file, setFile] = useState<File | null>(null)
  const [preview, setPreview] = useState<QuestionImportResult | null>(null)
  const [phase, setPhase] = useState<Phase>('idle')
  const [error, setError] = useState<string | null>(null)
  const inputRef = useRef<HTMLInputElement>(null)

  const busy = phase !== 'idle'
  const canImport = Boolean(file && preview && preview.validCount > 0) && !busy

  const template = async () => {
    setPhase('template')
    setError(null)
    try {
      await downloadQuestionTemplate(bankId)
    } catch (err) {
      setError(admissionBankError(err, 'template').message)
    } finally {
      setPhase('idle')
    }
  }

  const pick = async (e: ChangeEvent<HTMLInputElement>) => {
    const picked = e.target.files?.[0]
    e.target.value = '' // the same file may be picked again after fixing it
    if (!picked) return
    setPreview(null)
    setError(null)
    if (!isXlsx(picked)) {
      setFile(null)
      setError(XLSX_ONLY_MESSAGE)
      return
    }
    setFile(picked)
    setPhase('checking')
    try {
      setPreview(await importQuestions(bankId, picked, true))
    } catch (err) {
      setError(admissionBankError(err, 'import').message)
    } finally {
      setPhase('idle')
    }
  }

  const commit = async () => {
    if (!file || !canImport) return
    setPhase('importing')
    setError(null)
    try {
      const result = await importQuestions(bankId, file, false)
      if (result.imported > 0) {
        onImported(result)
        return
      }
      // Nothing was written — the file changed its verdict between the two posts.
      setPreview(result)
      setError("Hech narsa yozilmadi — xatolarni tuzatib, faylni qayta tanlang")
    } catch (err) {
      setError(admissionBankError(err, 'import').message)
    } finally {
      setPhase('idle')
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Excel'dan savollarni yuklash"
      size="lg"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={phase === 'importing'}>
            Yopish
          </Button>
          <Button onClick={() => void commit()} disabled={!canImport}>
            {phase === 'importing' && <Loader2 className="h-4 w-4 animate-spin" />}
            {phase === 'importing'
              ? 'Yozilmoqda…'
              : preview && preview.validCount > 0
                ? `${preview.validCount} ta savolni yozish`
                : 'Yozish'}
          </Button>
        </>
      }
    >
      <div className="space-y-4 text-sm">
        <p className="text-slate-500">
          Baza: <span className="font-medium text-slate-800">{bankLabel}</span>
        </p>

        <div className="flex flex-wrap items-center gap-2">
          <Button variant="secondary" onClick={() => void template()} disabled={busy}>
            {phase === 'template' ? (
              <Loader2 className="h-4 w-4 animate-spin" />
            ) : (
              <FileDown className="h-4 w-4" />
            )}
            Shablon
          </Button>
          <Button variant="secondary" onClick={() => inputRef.current?.click()} disabled={busy}>
            <Upload className="h-4 w-4" /> {file ? 'Boshqa fayl' : 'Fayl tanlash'}
          </Button>
          <input
            ref={inputRef}
            type="file"
            accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            className="hidden"
            onChange={(e) => void pick(e)}
          />
          {file && (
            <span className="inline-flex min-w-0 items-center gap-1.5 text-slate-500">
              <FileSpreadsheet className="h-4 w-4 shrink-0 text-emerald-600" />
              <span className="truncate" title={file.name}>
                {file.name}
              </span>
            </span>
          )}
        </div>

        <div className="space-y-2 rounded-xl bg-slate-50 px-4 py-3 text-slate-500">
          <p>
            Faqat <b>.xlsx</b> fayl. «Savollar» varag'ida bitta qator — bitta savol, ustunlar shu
            tartibda:
          </p>
          <div className="flex flex-wrap gap-1">
            {TEMPLATE_COLUMNS.map((column) => (
              <span
                key={column}
                className="rounded-md border border-slate-200 bg-white px-1.5 py-0.5 text-xs text-slate-600"
              >
                {column}
              </span>
            ))}
          </div>
          <p className="text-xs">
            2 tadan 6 tagacha variant; to'g'ri javob — A–F harfi; rasm havolasi bo'lsa,{' '}
            <code className="rounded bg-white px-1">/uploads/</code> bilan boshlanadi. Fayl avval{' '}
            <b>tekshiriladi</b> — bazaga hech narsa yozilmaydi. Xato qator butunlay o'tkazib
            yuboriladi, to'g'ri qatorlar esa «Yozish»dan keyin qo'shiladi.
          </p>
        </div>

        {phase === 'checking' && (
          <p className="flex items-center gap-2 text-slate-500">
            <Loader2 className="h-4 w-4 animate-spin" /> Tekshirilmoqda…
          </p>
        )}

        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-red-600">{error}</p>}

        {preview && (
          <div className="space-y-3">
            <div className="flex flex-wrap gap-x-5 gap-y-1">
              <span className="text-slate-600">
                Jami qator: <b>{preview.totalRows}</b>
              </span>
              <span className="text-emerald-700">
                Yoziladi: <b>{preview.validCount}</b>
              </span>
              {preview.errorCount > 0 && (
                <span className="text-red-600">
                  Xato: <b>{preview.errorCount}</b>
                </span>
              )}
            </div>

            {preview.totalRows === 0 ? (
              <p className="rounded-lg bg-amber-50 px-3 py-2 text-amber-700">
                Faylda savol topilmadi — «Savollar» varag'i bo'sh. Shablonni yuklab, to'ldirib
                qayta tanlang.
              </p>
            ) : preview.validCount === 0 ? (
              <p className="rounded-lg bg-amber-50 px-3 py-2 text-amber-700">
                Yoziladigan savol yo'q — quyidagi xatolarni tuzatib, faylni qayta tanlang.
              </p>
            ) : preview.errorCount > 0 ? (
              <p className="rounded-lg bg-amber-50 px-3 py-2 text-amber-700">
                {preview.errorCount} ta xato qator o'tkazib yuboriladi, qolgan{' '}
                {preview.validCount} tasi yoziladi. Hammasini yozish uchun xatolarni tuzatib,
                faylni qayta tanlang.
              </p>
            ) : (
              <p className="rounded-lg bg-emerald-50 px-3 py-2 text-emerald-700">
                Fayl toza — {preview.validCount} ta savol yozishga tayyor.
              </p>
            )}

            {preview.errors.length > 0 && (
              <ul className="space-y-2">
                {preview.errors.map((group) => (
                  <li
                    key={group.reason}
                    className="flex flex-wrap items-baseline gap-x-2 gap-y-1 rounded-xl border border-red-100 bg-white px-3 py-2"
                  >
                    <span className="rounded-full bg-red-50 px-2 py-0.5 text-xs font-medium text-red-700">
                      {importReasonLabel(group.reason)}
                    </span>
                    <span className="text-xs text-slate-500">
                      {group.rows.length === 1 ? 'qator' : 'qatorlar'}: {rowsText(group.rows)}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </div>
        )}
      </div>
    </Modal>
  )
}
