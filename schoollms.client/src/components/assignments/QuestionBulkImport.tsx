/**
 * "Ommaviy yuklash" — instead of typing test questions one by one (client, 2026-10-15).
 * docs/modules/test-import.md.
 *
 * A panel in the assignment wizard's questions step: paste text (an AI answer too), a file
 * (.xlsx template, .docx, .txt, .json), the Excel template and a ready AI prompt. The server only
 * READS — the result is shown, "Qo'shish" / "Almashtirish" put the questions into the form, and
 * the assignment is saved with "Saqlash" as usual.
 *
 * Same rules and wording as the Mini App's `QuestionImportSheet.jsx` (a separate code base).
 */
import { useRef, useState } from 'react'
import type { ChangeEvent } from 'react'
import { Check, ClipboardCopy, FileDown, Loader2, Upload, X } from 'lucide-react'
import type {
  AssignmentImportReason,
  AssignmentQuestionParseResult,
  QuestionInput,
} from '@/api/services/assignments'
import { Button } from '@/components/ui/Button'

/** Questions per test — the server's `AssignmentService.MaxQuestions`. */
export const MAX_TEST_QUESTIONS = 200

const LETTERS = 'ABCDEF'
const MAX_FILE_BYTES = 10 * 1024 * 1024
const FILE_TYPES = /\.(xlsx|docx|txt|json)$/i
const ROWS_SHOWN = 30

const REASON_LABELS: Record<AssignmentImportReason, string> = {
  emptyText: "Savol matni bo'sh",
  tooFewOptions: 'Variant 2 tadan kam',
  tooManyOptions: "Variant 6 tadan ko'p",
  noCorrect: "To'g'ri javob belgilanmagan",
  badCorrect: "To'g'ri javob variantlarda yo'q",
  manyCorrect: "Bir nechta to'g'ri javob",
  duplicateOption: 'Bir xil variant',
}

const EXAMPLE = `1. Savol matni
A) variant
*B) to'g'ri variant
C) variant
D) variant`

function rowsText(rows: number[]): string {
  const sorted = [...rows].sort((a, b) => a - b)
  const shown = sorted.slice(0, ROWS_SHOWN).join(', ')
  return sorted.length > ROWS_SHOWN ? `${shown} … (+${sorted.length - ROWS_SHOWN})` : shown
}

function messageOf(err: unknown, fallback: string): string {
  return (err as { response?: { data?: { message?: string } } })?.response?.data?.message ?? fallback
}

/** The prompt for ChatGPT / Gemini — its answer comes back in this panel's text format. */
function aiPrompt(subjectName: string): string {
  return [
    `[sinf]-sinf o'quvchilari uchun «${subjectName || '[fan]'}» fanidan [mavzu] mavzusida 10 ta test savoli tuz.`,
    "Har bir savolda 4 ta javob varianti bo'lsin, ulardan faqat bittasi to'g'ri.",
    '',
    "Javobni FAQAT quyidagi ko'rinishda, oddiy matn bilan yoz:",
    '',
    EXAMPLE,
    '',
    '2. Keyingi savol',
    '…',
    '',
    'Qoidalar:',
    "- To'g'ri variant harfi oldiga * belgisini qo'y (har savolda faqat bittasiga).",
    "- Savollar orasida bitta bo'sh qator qoldir.",
    "- Markdown, jadval, izoh, tushuntirish, kirish va xulosa so'zlarini yozma.",
  ].join('\n')
}

interface Props {
  /** Filled questions already in the form. */
  existing: number
  subjectName: string
  onParse: (input: File | string) => Promise<AssignmentQuestionParseResult>
  onDownloadTemplate: () => Promise<void>
  onApply: (questions: QuestionInput[], mode: 'append' | 'replace') => void
  onClose: () => void
}

type Phase = 'idle' | 'text' | 'file' | 'template'

export function QuestionBulkImport({ existing, subjectName, onParse, onDownloadTemplate, onApply, onClose }: Props) {
  const [text, setText] = useState('')
  const [result, setResult] = useState<AssignmentQuestionParseResult | null>(null)
  const [source, setSource] = useState('')
  const [phase, setPhase] = useState<Phase>('idle')
  const [error, setError] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)

  const busy = phase !== 'idle'
  const valid = result?.questions ?? []
  const overCap = existing + valid.length > MAX_TEST_QUESTIONS
  const unitWord = result?.unit === 'row' ? 'qator' : 'savol'

  const parse = async (kind: Phase, input: File | string, label: string) => {
    setPhase(kind)
    setError(null)
    try {
      setResult(await onParse(input))
      setSource(label)
    } catch (err) {
      setError(messageOf(err, "O'qib bo'lmadi"))
    } finally {
      setPhase('idle')
    }
  }

  const check = () => {
    if (!text.trim()) {
      setError('Avval savollarni joylang yoki yozing.')
      return
    }
    void parse('text', text, 'Matn')
  }

  const pick = (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    e.target.value = '' // the same file may be picked again after fixing it
    if (!file) return
    if (!FILE_TYPES.test(file.name)) {
      setError('Faqat .xlsx, .docx, .txt yoki .json fayl qabul qilinadi')
      return
    }
    if (file.size > MAX_FILE_BYTES) {
      setError('Fayl 10 MB dan katta')
      return
    }
    void parse('file', file, file.name)
  }

  const template = async () => {
    setPhase('template')
    setError(null)
    try {
      await onDownloadTemplate()
    } catch {
      setError("Shablonni yuklab bo'lmadi")
    } finally {
      setPhase('idle')
    }
  }

  const copyPrompt = async () => {
    try {
      await navigator.clipboard.writeText(aiPrompt(subjectName))
      setCopied(true)
      window.setTimeout(() => setCopied(false), 2500)
    } catch {
      setError("Nusxa olinmadi — brauzer ruxsat bermadi")
    }
  }

  return (
    <div className="mb-3 space-y-3 rounded-xl border border-brand-200 bg-brand-50/40 p-3 text-sm">
      <div className="flex items-center justify-between gap-2">
        <span className="font-medium text-slate-700">Savollarni ommaviy yuklash</span>
        <button
          type="button"
          title="Yopish"
          aria-label="Yopish"
          onClick={onClose}
          className="rounded p-1 text-slate-400 hover:bg-white hover:text-slate-600"
        >
          <X className="h-4 w-4" />
        </button>
      </div>

      {!result ? (
        <>
          <textarea
            value={text}
            rows={8}
            onChange={(e) => setText(e.target.value)}
            placeholder={EXAMPLE}
            className="w-full resize-y rounded-lg border border-slate-200 bg-white px-3 py-2 font-mono text-xs leading-snug text-slate-700 outline-none focus:border-brand-400"
          />
          <p className="text-xs text-slate-500">
            To'g'ri variant oldiga <b>*</b> qo'ying yoki pastiga «Javob: B» deb yozing; savollar orasida bo'sh
            qator. Yoki fayl: Excel shablon, Word (.docx — xuddi shu matn), .txt yoki .json.
          </p>
          <div className="flex flex-wrap gap-2">
            <Button type="button" onClick={check} disabled={busy}>
              {phase === 'text' && <Loader2 className="h-4 w-4 animate-spin" />}
              Tekshirish
            </Button>
            <Button type="button" variant="secondary" onClick={() => inputRef.current?.click()} disabled={busy}>
              {phase === 'file' ? <Loader2 className="h-4 w-4 animate-spin" /> : <Upload className="h-4 w-4" />}
              Fayl tanlash
            </Button>
            <Button type="button" variant="secondary" onClick={() => void template()} disabled={busy}>
              {phase === 'template' ? <Loader2 className="h-4 w-4 animate-spin" /> : <FileDown className="h-4 w-4" />}
              Excel shablon
            </Button>
            <Button type="button" variant="secondary" onClick={() => void copyPrompt()} disabled={busy}>
              {copied ? <Check className="h-4 w-4" /> : <ClipboardCopy className="h-4 w-4" />}
              {copied ? 'Nusxalandi' : "AI uchun so'rov"}
            </Button>
            <input
              ref={inputRef}
              type="file"
              accept=".xlsx,.docx,.txt,.json"
              className="hidden"
              onChange={pick}
            />
          </div>
        </>
      ) : (
        <>
          <div className="flex flex-wrap items-baseline gap-x-4 gap-y-1">
            <span className="text-slate-500">{source}</span>
            <span className="text-emerald-700">
              Tayyor: <b>{valid.length}</b>
            </span>
            {result.errorCount > 0 && (
              <span className="text-red-600">
                Xato: <b>{result.errorCount}</b>
              </span>
            )}
          </div>

          {result.totalRows === 0 ? (
            <p className="rounded-lg bg-amber-50 px-3 py-2 text-amber-700">
              Savol topilmadi. Ko'rinishni tekshiring: raqam, savol, A) B) C) variantlar va to'g'ri javob belgisi.
            </p>
          ) : result.errorCount > 0 ? (
            <p className="rounded-lg bg-amber-50 px-3 py-2 text-amber-700">
              Xato {unitWord}lar tashlab ketiladi. Hammasini olish uchun ularni tuzatib, qayta tekshiring.
            </p>
          ) : null}

          {result.errors.length > 0 && (
            <ul className="space-y-1.5">
              {result.errors.map((g) => (
                <li
                  key={g.reason}
                  className="flex flex-wrap items-baseline gap-x-2 gap-y-1 rounded-lg border border-red-100 bg-white px-3 py-1.5"
                >
                  <span className="rounded-full bg-red-50 px-2 py-0.5 text-xs font-medium text-red-700">
                    {REASON_LABELS[g.reason] ?? g.reason}
                  </span>
                  <span className="text-xs text-slate-500">
                    {unitWord}: {rowsText(g.rows)}
                  </span>
                </li>
              ))}
            </ul>
          )}

          {valid.length > 0 && (
            <ol className="max-h-80 space-y-2 overflow-y-auto rounded-lg bg-white p-2">
              {valid.map((q, i) => (
                <li key={i} className="rounded-lg border border-slate-100 px-3 py-2">
                  <p className="font-medium text-slate-800">
                    <span className="text-slate-400">{i + 1}. </span>
                    {q.text}
                  </p>
                  <div className="mt-1 grid gap-x-4 gap-y-0.5 sm:grid-cols-2">
                    {q.options.map((o, k) => (
                      <span
                        key={k}
                        className={
                          k === q.correctIndex ? 'flex items-center gap-1 font-medium text-emerald-700' : 'text-slate-600'
                        }
                      >
                        {LETTERS[k]}) {o}
                        {k === q.correctIndex && <Check className="h-3.5 w-3.5" />}
                      </span>
                    ))}
                  </div>
                </li>
              ))}
            </ol>
          )}

          {overCap && existing > 0 && valid.length > 0 && (
            <p className="text-xs text-red-600">
              Formada {existing} ta savol bor — qo'shilsa {MAX_TEST_QUESTIONS} tadan oshadi. Almashtiring yoki
              savollarni kamaytiring.
            </p>
          )}

          <div className="flex flex-wrap gap-2">
            {valid.length > 0 && (
              <Button type="button" onClick={() => onApply(valid, 'append')} disabled={overCap}>
                Qo'shish ({valid.length})
              </Button>
            )}
            {valid.length > 0 && existing > 0 && (
              <Button
                type="button"
                variant="secondary"
                onClick={() => {
                  if (window.confirm(`Formadagi ${existing} ta savol o'chirilib, ${valid.length} ta yangisi yoziladi. Davom etasizmi?`))
                    onApply(valid, 'replace')
                }}
              >
                Almashtirish
              </Button>
            )}
            <Button type="button" variant="secondary" onClick={() => setResult(null)}>
              Orqaga
            </Button>
          </div>
        </>
      )}

      {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-red-600">{error}</p>}
    </div>
  )
}
