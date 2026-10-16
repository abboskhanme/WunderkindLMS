/**
 * OMMAVIY YUKLASH — instead of typing test questions one by one (client, 2026-10-15: "testlarni
 * bittalab kiritish majburiyati olib tashlansin … shablon fayl orqali ommaviy yuklash").
 * docs/modules/test-import.md.
 *
 * Three ways in, one result: paste text (primary — an AI answer goes here too), a file (.xlsx template,
 * .docx, .txt, .json), and "AI uchun so'rov" — a ready prompt for ChatGPT/Gemini. The server only READS
 * (`/api/teacher/assignments/questions/parse`): nothing is saved, the questions land in the form and the
 * teacher saves the assignment as usual.
 */
import { useRef, useState } from 'react'
import { Check, ChevronLeft, ClipboardCopy, Download, FileUp, Send, Sparkles, X } from 'lucide-react'
import { teacherApi } from '../../lib/teacherApi'
import { haptic, hapticNotify, inTelegram } from '../../lib/telegram'
import { MAX_TEST_QUESTIONS, aiQuestionPrompt, copyText, importReasonLabel, importRowsText } from '../../lib/assignments'

const INPUT =
  'w-full rounded-xl border border-slate-200 bg-slate-50 px-3 py-2.5 text-[15px] outline-none focus:border-brand focus:bg-white'

const LETTERS = 'ABCDEF'
const MAX_FILE_BYTES = 10 * 1024 * 1024
const FILE_TYPES = /\.(xlsx|docx|txt|json)$/i

const EXAMPLE = `1. Savol matni
A) variant
*B) to'g'ri variant
C) variant
D) variant`

/**
 * @param existing  filled questions already in the form (Qo'shish / Almashtirish and the cap)
 * @param subject   the selected subject's name — for the AI prompt
 * @param grade     the selected class's grade ("7") — for the AI prompt
 * @param onApply   (questions, 'append' | 'replace') — puts the questions into the form
 */
export function QuestionImportSheet({ existing, subject, grade, onApply, onClose }) {
  const [text, setText] = useState('')
  const [result, setResult] = useState(null)
  const [source, setSource] = useState('')
  const [busy, setBusy] = useState(null)
  const [error, setError] = useState(null)
  const [note, setNote] = useState(null)
  const [aiOpen, setAiOpen] = useState(false)
  const [topic, setTopic] = useState('')
  const [count, setCount] = useState('10')
  const [copied, setCopied] = useState(false)
  const [confirmReplace, setConfirmReplace] = useState(false)
  const fileRef = useRef(null)

  const parse = async (kind, request, label) => {
    setBusy(kind)
    setError(null)
    setNote(null)
    try {
      const r = await request()
      setResult(r)
      setSource(label)
      setConfirmReplace(false)
      hapticNotify(r.validCount > 0 ? 'success' : 'error')
    } catch (e) {
      hapticNotify('error')
      setError(e?.message || "O'qib bo'lmadi")
    } finally {
      setBusy(null)
    }
  }

  const parseText = () => {
    if (!text.trim()) {
      setError('Avval savollarni joylang yoki yozing.')
      return
    }
    parse('text', () => teacherApi.parseQuestionsText(text), 'Matn')
  }

  const onPickFile = (e) => {
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
    parse('file', () => teacherApi.parseQuestionsFile(file), file.name)
  }

  const template = async () => {
    setBusy('template')
    setError(null)
    setNote(null)
    try {
      if (inTelegram) {
        await teacherApi.questionTemplateToTelegram()
        hapticNotify('success')
        setNote("Excel shablon Telegram'dagi bot chatingizga yuborildi.")
      } else {
        await teacherApi.downloadQuestionTemplate()
      }
    } catch (e) {
      hapticNotify('error')
      setError(e?.message || "Shablonni olib bo'lmadi")
    } finally {
      setBusy(null)
    }
  }

  const prompt = aiQuestionPrompt({ grade, subject, topic, count })
  const copyPrompt = async () => {
    const ok = await copyText(prompt)
    setCopied(ok)
    if (ok) {
      hapticNotify('success')
      window.setTimeout(() => setCopied(false), 2500)
    } else setError("Nusxa olinmadi — matnni bosib turib, o'zingiz nusxalang.")
  }

  const valid = result?.questions ?? []
  const overCap = existing + valid.length > MAX_TEST_QUESTIONS
  const unitWord = result?.unit === 'row' ? 'qator' : 'savol'

  const apply = (mode) => {
    haptic('light')
    onApply(valid, mode)
  }

  return (
    <div className="fixed inset-0 z-40 flex items-end bg-black/40" onClick={onClose}>
      <div
        role="dialog"
        aria-label="Ommaviy yuklash"
        className="max-h-[92vh] w-full overflow-y-auto rounded-t-[26px] bg-white px-4 pb-[calc(16px+var(--tg-safe-bottom))] pt-4"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center gap-3">
          {result && (
            <button
              type="button"
              onClick={() => setResult(null)}
              aria-label="Orqaga"
              className="flex h-9 w-9 items-center justify-center rounded-full bg-slate-100"
            >
              <ChevronLeft className="h-5 w-5" />
            </button>
          )}
          <div className="min-w-0 flex-1">
            <p className="truncate text-[17px] font-bold">{result ? 'Tekshiruv natijasi' : 'Savollarni ommaviy yuklash'}</p>
            <p className="truncate text-[13px] text-slate-500">
              {result ? source : 'Matn, Word, Excel yoki AI orqali — birdaniga'}
            </p>
          </div>
          <button type="button" onClick={onClose} aria-label="Yopish" className="flex h-9 w-9 items-center justify-center rounded-full bg-slate-100">
            <X className="h-5 w-5" />
          </button>
        </div>

        {!result ? (
          <>
            <label className="mt-3 block">
              <span className="mb-1 block text-[13px] font-semibold text-slate-500">Savollarni shu yerga joylang</span>
              <textarea
                value={text}
                rows={8}
                onChange={(e) => setText(e.target.value)}
                placeholder={EXAMPLE}
                className={INPUT + ' resize-y font-mono text-[13px] leading-snug'}
              />
            </label>
            <p className="mt-1 text-[12px] text-slate-400">
              To'g'ri variant oldiga <b>*</b> qo'ying yoki pastiga «Javob: B» deb yozing. Savollar orasida bo'sh qator qoldiring.
            </p>
            <button
              type="button"
              onClick={parseText}
              disabled={busy !== null}
              className="mt-3 w-full rounded-2xl bg-brand py-3 text-[16px] font-bold text-brand-ink disabled:opacity-50"
            >
              {busy === 'text' ? 'Tekshirilmoqda…' : 'Tekshirish'}
            </button>

            <div className="mt-3 grid grid-cols-2 gap-2">
              <button
                type="button"
                onClick={() => fileRef.current?.click()}
                disabled={busy !== null}
                className="flex flex-col items-center gap-1 rounded-xl bg-slate-100 px-2 py-2.5 text-center disabled:opacity-50"
              >
                <FileUp className="h-5 w-5 text-slate-600" />
                <span className="text-[13px] font-semibold text-slate-700">{busy === 'file' ? 'Tekshirilmoqda…' : 'Fayl tanlash'}</span>
                <span className="text-[11px] text-slate-400">.xlsx · .docx · .txt · .json</span>
              </button>
              <button
                type="button"
                onClick={template}
                disabled={busy !== null}
                className="flex flex-col items-center gap-1 rounded-xl bg-slate-100 px-2 py-2.5 text-center disabled:opacity-50"
              >
                {inTelegram ? <Send className="h-5 w-5 text-slate-600" /> : <Download className="h-5 w-5 text-slate-600" />}
                <span className="text-[13px] font-semibold text-slate-700">{busy === 'template' ? 'Tayyorlanmoqda…' : 'Excel shablon'}</span>
                <span className="text-[11px] text-slate-400">{inTelegram ? "Telegram'ga yuboriladi" : 'Yuklab olish'}</span>
              </button>
            </div>
            <input
              ref={fileRef}
              type="file"
              hidden
              accept=".xlsx,.docx,.txt,.json,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,application/vnd.openxmlformats-officedocument.wordprocessingml.document,text/plain,application/json"
              onChange={onPickFile}
            />

            <div className="mt-3 rounded-2xl border border-slate-200">
              <button
                type="button"
                onClick={() => setAiOpen((v) => !v)}
                aria-expanded={aiOpen}
                className="flex w-full items-center gap-2.5 px-3 py-2.5 text-left"
              >
                <Sparkles className="h-5 w-5 shrink-0 text-brand-ink" />
                <span className="min-w-0 flex-1">
                  <span className="block text-[14px] font-semibold">AI uchun so'rov</span>
                  <span className="block text-[12px] text-slate-500">ChatGPT yoki Gemini'ga bering, javobini yuqoriga joylang</span>
                </span>
                <span className="text-[13px] font-semibold text-slate-400">{aiOpen ? 'Yopish' : 'Ochish'}</span>
              </button>
              {aiOpen && (
                <div className="space-y-2 border-t border-slate-100 px-3 pb-3 pt-2">
                  <div className="grid grid-cols-[1fr_88px] gap-2">
                    <input value={topic} maxLength={120} onChange={(e) => setTopic(e.target.value)} placeholder="Mavzu (masalan: Kasrlar)" className={INPUT + ' py-2 text-[14px]'} />
                    <input
                      type="number"
                      inputMode="numeric"
                      min={1}
                      max={MAX_TEST_QUESTIONS}
                      value={count}
                      onChange={(e) => setCount(e.target.value)}
                      aria-label="Savollar soni"
                      className={INPUT + ' py-2 text-[14px]'}
                    />
                  </div>
                  <pre className="max-h-48 overflow-y-auto whitespace-pre-wrap break-words rounded-xl bg-slate-50 px-3 py-2 text-[12px] leading-snug text-slate-700">{prompt}</pre>
                  <p className="text-[12px] text-slate-400">[Qavs] ichidagilarni AI oynasida o'zingiz to'ldiring. Javobni tekshirib, keyin joylang.</p>
                  <button
                    type="button"
                    onClick={copyPrompt}
                    className="flex w-full items-center justify-center gap-2 rounded-xl bg-brand-ink py-2.5 text-[14px] font-semibold text-brand"
                  >
                    {copied ? <Check className="h-4 w-4" /> : <ClipboardCopy className="h-4 w-4" />}
                    {copied ? 'Nusxalandi' : 'Nusxa olish'}
                  </button>
                </div>
              )}
            </div>
          </>
        ) : (
          <>
            <div className="mt-3 rounded-2xl bg-slate-50 px-3 py-3">
              <p className="text-[15px] font-bold">
                {valid.length > 0 ? `${valid.length} ta savol tayyor` : "Qo'shiladigan savol yo'q"}
                {result.errorCount > 0 && <span className="font-semibold text-red-600"> · {result.errorCount} ta xato</span>}
              </p>
              <p className="mt-0.5 text-[12px] text-slate-500">
                {result.totalRows === 0
                  ? "Savol topilmadi. Ko'rinishni tekshiring: raqam, savol, A) B) C) variantlar va to'g'ri javob belgisi."
                  : result.errorCount > 0
                    ? `Xato ${unitWord}lar tashlab ketiladi. Hammasini olish uchun ularni tuzatib, qayta tekshiring.`
                    : "Hammasi to'g'ri o'qildi. Pastda ko'rib chiqing."}
              </p>
            </div>

            {result.errors.length > 0 && (
              <ul className="mt-2 space-y-1.5">
                {result.errors.map((g) => (
                  <li key={g.reason} className="rounded-xl border border-red-100 px-3 py-2">
                    <span className="mr-2 rounded-full bg-red-50 px-2 py-0.5 text-[12px] font-semibold text-red-700">{importReasonLabel(g.reason)}</span>
                    <span className="text-[12px] text-slate-500">
                      {unitWord}: {importRowsText(g.rows)}
                    </span>
                  </li>
                ))}
              </ul>
            )}

            {valid.length > 0 && (
              <div className="mt-3 space-y-2">
                {valid.map((q, i) => (
                  <div key={i} className="rounded-2xl border border-slate-200 p-3">
                    <p className="break-words text-[14px] font-semibold">
                      <span className="text-slate-400">{i + 1}. </span>
                      {q.text}
                    </p>
                    <div className="mt-1.5 space-y-1">
                      {q.options.map((o, k) => {
                        const correct = k === q.correctIndex
                        return (
                          <p key={k} className={'flex items-start gap-1.5 text-[13px] ' + (correct ? 'font-semibold text-emerald-700' : 'text-slate-600')}>
                            <span className="w-5 shrink-0">{LETTERS[k]})</span>
                            <span className="min-w-0 flex-1 break-words">{o}</span>
                            {correct && <Check className="h-4 w-4 shrink-0" />}
                          </p>
                        )
                      })}
                    </div>
                  </div>
                ))}
              </div>
            )}

            {valid.length > 0 && (
              <div className="sticky bottom-0 -mx-4 mt-3 border-t border-slate-100 bg-white px-4 pt-3">
                {overCap && existing > 0 && (
                  <p className="mb-2 text-[12px] font-medium text-red-600">
                    Formada {existing} ta savol bor — qo'shilsa {MAX_TEST_QUESTIONS} tadan oshadi. Almashtiring yoki savollarni kamaytiring.
                  </p>
                )}
                {confirmReplace ? (
                  <div className="flex items-center gap-2">
                    <p className="flex-1 text-[13px] font-semibold text-red-700">Formadagi {existing} ta savol o'chiriladi.</p>
                    <button type="button" onClick={() => setConfirmReplace(false)} className="rounded-xl bg-slate-100 px-3 py-2.5 text-[13px] font-semibold text-slate-600">
                      Yo'q
                    </button>
                    <button type="button" onClick={() => apply('replace')} className="rounded-xl bg-red-600 px-3 py-2.5 text-[13px] font-semibold text-white">
                      Almashtirish
                    </button>
                  </div>
                ) : (
                  <div className="flex gap-2">
                    <button
                      type="button"
                      onClick={() => apply('append')}
                      disabled={overCap}
                      className="flex-1 rounded-2xl bg-brand py-3 text-[15px] font-bold text-brand-ink disabled:opacity-50"
                    >
                      {existing > 0 ? `Qo'shish (${valid.length})` : `Formaga qo'shish (${valid.length})`}
                    </button>
                    {existing > 0 && (
                      <button
                        type="button"
                        onClick={() => setConfirmReplace(true)}
                        className="rounded-2xl bg-slate-100 px-4 py-3 text-[15px] font-semibold text-slate-700"
                      >
                        Almashtirish
                      </button>
                    )}
                  </div>
                )}
              </div>
            )}
          </>
        )}

        {note && <p className="mt-3 rounded-xl bg-emerald-50 px-3 py-2 text-[13px] font-medium text-emerald-700">{note}</p>}
        {error && <p className="mt-3 rounded-xl bg-red-50 px-3 py-2 text-[13px] font-medium text-red-600">{error}</p>}
      </div>
    </div>
  )
}
