/**
 * Natijalarni Excel'dan yuklash — `POST /exams/{id}/entry-table/import`
 * (§6.3, §8.4). One endpoint with a dry run, posted twice (§6.2's rule, which
 * the result import mirrors): `dryRun=true` checks the file and writes
 * nothing; "Yozish" posts the same file with `dryRun=false`.
 *
 * The report is the server's — row numbers are 1-based sheet rows including
 * the header, so they match what the user sees in Excel. Nothing in the
 * browser reads the spreadsheet.
 */
import { useRef, useState } from 'react'
import type { ChangeEvent } from 'react'
import { FileDown, Loader2, Upload } from 'lucide-react'
import {
  downloadEntryTemplate,
  examsErrorMessage,
  importEntryTable,
  type ResultImportError,
  type ResultImportResult,
} from '@/api/services/exams'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'

interface Props {
  examId: string
  onClose: () => void
  /** Rows written — the grid reloads from the server. */
  onImported: (imported: number) => void
}

/**
 * Words for the reasons a result sheet can fail on. The server's own Uzbek
 * `message` wins when it sends one; these cover a bare `reason`.
 */
const REASON_LABELS: Record<string, string> = {
  unknownParticipant: 'Ishtirokchi topilmadi',
  duplicateParticipant: 'Ishtirokchi takrorlangan',
  outOfRange: "Ball ruxsat etilgan oraliqdan tashqarida",
  notANumber: 'Ball son emas',
  unknownSubject: "Noma'lum fan ustuni",
  badAbsent: '"Kelmadi" ustuni noto\'g\'ri',
}

/** How many row numbers one chip lists before "+N". */
const ROWS_SHOWN = 20

function reasonText(error: ResultImportError): string {
  const fromServer = error.message?.trim()
  if (fromServer) return fromServer
  return REASON_LABELS[error.reason] ?? 'Qatorda xato'
}

type Busy = 'template' | 'checking' | 'writing' | null

export function ResultImportModal({ examId, onClose, onImported }: Props) {
  const [file, setFile] = useState<File | null>(null)
  const [report, setReport] = useState<ResultImportResult | null>(null)
  const [busy, setBusy] = useState<Busy>(null)
  const [error, setError] = useState<string | null>(null)
  const inputRef = useRef<HTMLInputElement>(null)

  const template = async () => {
    setBusy('template')
    setError(null)
    try {
      await downloadEntryTemplate(examId)
    } catch (err) {
      setError(examsErrorMessage(err, 'entry.template'))
    } finally {
      setBusy(null)
    }
  }

  const pick = async (e: ChangeEvent<HTMLInputElement>) => {
    const picked = e.target.files?.[0]
    e.target.value = '' // the same file can be picked again after a fix
    if (!picked) return
    setReport(null)
    setError(null)
    if (!picked.name.toLowerCase().endsWith('.xlsx')) {
      setFile(null)
      setError('Faqat .xlsx (Excel) fayl qabul qilinadi')
      return
    }
    setFile(picked)
    setBusy('checking')
    try {
      setReport(await importEntryTable(examId, picked, true))
    } catch (err) {
      setError(examsErrorMessage(err, 'entry.import'))
    } finally {
      setBusy(null)
    }
  }

  const commit = async () => {
    if (!file) return
    setBusy('writing')
    setError(null)
    try {
      const result = await importEntryTable(examId, file, false)
      onImported(result.imported)
    } catch (err) {
      setError(examsErrorMessage(err, 'entry.import'))
      setBusy(null)
    }
  }

  const canWrite = Boolean(report && report.validCount > 0) && busy === null

  return (
    <Modal
      open
      onClose={busy ? () => undefined : onClose}
      title="Natijalarni Excel'dan yuklash"
      size="lg"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy !== null}>
            Yopish
          </Button>
          <Button onClick={() => void commit()} disabled={!canWrite}>
            {busy === 'writing' && <Loader2 className="h-4 w-4 animate-spin" />}
            {report && report.validCount > 0 ? `Yozish (${report.validCount} ta qator)` : 'Yozish'}
          </Button>
        </>
      }
    >
      <div className="space-y-4 text-sm">
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="secondary" onClick={() => void template()} disabled={busy !== null}>
            {busy === 'template' ? (
              <Loader2 className="h-4 w-4 animate-spin" />
            ) : (
              <FileDown className="h-4 w-4" />
            )}
            Shablon
          </Button>
          <Button variant="secondary" onClick={() => inputRef.current?.click()} disabled={busy !== null}>
            <Upload className="h-4 w-4" /> {file ? 'Boshqa fayl' : 'Fayl tanlash'}
          </Button>
          <input
            ref={inputRef}
            type="file"
            accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            className="hidden"
            onChange={(e) => void pick(e)}
          />
          {file && <span className="truncate text-slate-500">{file.name}</span>}
        </div>

        <p className="rounded-lg bg-slate-50 px-3 py-2 text-slate-500">
          Shablonni yuklab oling — unda shu imtihonning o'quvchilari va fanlari bor. Ballarni
          to'ldirib, faylni tanlang: u avval <b>tekshiriladi</b>, bazaga hech narsa yozilmaydi.
          "Yozish" faqat xatosiz qatorlarni saqlaydi.
        </p>

        {busy === 'checking' && (
          <p className="flex items-center gap-2 text-slate-500">
            <Loader2 className="h-4 w-4 animate-spin" /> Fayl tekshirilmoqda…
          </p>
        )}

        {error && (
          <p className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-red-700">{error}</p>
        )}

        {report && (
          <div className="space-y-3">
            <div className="flex flex-wrap gap-x-5 gap-y-1">
              <span className="text-slate-600">
                Jami qator: <b>{report.totalRows}</b>
              </span>
              <span className="text-emerald-700">
                Xatosiz: <b>{report.validCount}</b>
              </span>
              {report.errorCount > 0 && (
                <span className="text-red-600">
                  Xatoli: <b>{report.errorCount}</b>
                </span>
              )}
            </div>

            {report.validCount === 0 && (
              <p className="rounded-lg bg-red-50 px-3 py-2 text-red-700">
                Faylda yoziladigan qator yo'q — xatolarni tuzatib, qayta yuklang.
              </p>
            )}

            {report.errors.length > 0 && (
              <ul className="space-y-2">
                {report.errors.map((err, i) => {
                  const shown = err.rows.slice(0, ROWS_SHOWN)
                  const more = err.rows.length - shown.length
                  return (
                    <li
                      key={`${err.reason}-${i}`}
                      className="rounded-lg border border-red-100 bg-red-50/50 px-3 py-2"
                    >
                      <p className="font-medium text-red-700">{reasonText(err)}</p>
                      {shown.length > 0 && (
                        <p className="mt-1 text-xs text-slate-500">
                          Qatorlar: {shown.join(', ')}
                          {more > 0 && ` va yana ${more} ta`}
                        </p>
                      )}
                    </li>
                  )
                })}
              </ul>
            )}

            {report.errorCount === 0 && report.validCount > 0 && (
              <p className="rounded-lg bg-emerald-50 px-3 py-2 text-emerald-700">
                Fayl toza — "Yozish"ni bosing.
              </p>
            )}
          </div>
        )}
      </div>
    </Modal>
  )
}
