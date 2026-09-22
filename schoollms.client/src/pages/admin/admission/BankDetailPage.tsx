/**
 * BANK TAFSILOTI — one question bank
 * (`docs/modules/admission-and-testing.md` §3.1 screen 4, §6.2; unit C1).
 *
 * Top to bottom: the bank's name and server-derived badge; the settings strip
 * (three numbers + Saqlash); a warning when the bank holds fewer questions
 * than one sitting draws; then every question with its A–F options and the
 * correct one highlighted, a search box, "Savol qo'shish" and
 * "Excel'dan import".
 *
 * THE WHOLE BANK IS LOADED. §3.1 filters by question text in the browser, so
 * the screen walks every page of `GET /banks/{id}/questions` once
 * (`listAllQuestions`) — a bank is a few hundred rows.
 *
 * THE BADGE AND THE COUNT ARE THE SERVER'S (§10). After any question is
 * added, edited, deleted or imported the bank is re-read, so the badge and
 * the warning always show what the server derived, never a local guess.
 *
 * THE ANSWER KEY (§7.7). This screen shows `isCorrect`; that is why its reads
 * are gated behind `admission` (§4.3). Nothing here is shared with the public
 * exam page.
 *
 * PERMISSION (§3.7). Without `admission`: no Saqlash, no "Savol qo'shish",
 * no import, no edit or delete on a question.
 */
import { useEffect, useMemo, useState } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'
import {
  AlertTriangle,
  ArrowLeft,
  FileQuestion,
  FileSpreadsheet,
  Plus,
  RefreshCw,
  Search,
  X,
} from 'lucide-react'
import { useAuth } from '@/context/auth-context'
import {
  admissionBankError,
  deleteQuestion,
  getBank,
  listAllQuestions,
  type BankErrorInfo,
  type Question,
  type QuestionBank,
  type QuestionImportResult,
} from '@/api/services/admissionBanks'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { Toast } from '@/components/ui/Toast'
import { cn } from '@/lib/utils'
import {
  BANKS_PATH,
  bankStateLabel,
  bankStateTone,
  bankTitle,
  canManageAdmission,
  formatMinutes,
  formatPoints,
  noticeFromState,
} from './BankHelpers'
import { BankSettingsStrip } from './BankSettingsStrip'
import { QuestionCard } from './QuestionCard'
import { QuestionFormModal } from './QuestionFormModal'
import { QuestionImportModal } from './QuestionImportModal'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

interface Notice {
  message: string
  tone: 'success' | 'error'
}

/** One finished request, tagged with the load it answered. */
interface Loaded<T> {
  key: string
  data: T | null
  error: BankErrorInfo | null
}

function BackLink() {
  return (
    <Link
      to={BANKS_PATH}
      className="inline-flex items-center gap-1.5 text-sm font-medium text-slate-500 hover:text-slate-800"
    >
      <ArrowLeft className="h-4 w-4" /> Test bazasi
    </Link>
  )
}

export function BankDetailPage() {
  const { bankId = '' } = useParams<{ bankId: string }>()
  const location = useLocation()
  const navigate = useNavigate()
  const { user } = useAuth()
  const canWrite = canManageAdmission(user?.permissions)

  // "Baza yaratildi" arrives from the register in the navigation state.
  const [notice, setNotice] = useState<Notice | null>(() => {
    const message = noticeFromState(location.state)
    return message ? { message, tone: 'success' } : null
  })
  const arrivedWithNotice = noticeFromState(location.state) !== null
  useEffect(() => {
    // Clear it from history, so a page reload does not announce it again.
    if (arrivedWithNotice) navigate(location.pathname, { replace: true, state: null })
  }, [arrivedWithNotice, location.pathname, navigate])

  // ---- the bank --------------------------------------------------------------
  const [bankToken, setBankToken] = useState(0)
  const bankKey = `${bankId}#${bankToken}`
  const [bankLoad, setBankLoad] = useState<Loaded<QuestionBank> | null>(null)

  useEffect(() => {
    if (!bankId) return
    let cancelled = false
    getBank(bankId)
      .then((data) => {
        if (!cancelled) setBankLoad({ key: bankKey, data, error: null })
      })
      .catch((err: unknown) => {
        if (!cancelled) setBankLoad({ key: bankKey, data: null, error: admissionBankError(err, 'open') })
      })
    return () => {
      cancelled = true
    }
  }, [bankId, bankKey])

  const bankState = bankLoad?.key === bankKey ? bankLoad : null
  const bank = bankState?.data ?? null

  /** Re-read after a change — quietly: the screen stays, only the numbers move. */
  const refreshBank = async () => {
    try {
      const data = await getBank(bankId)
      setBankLoad((prev) => (prev && prev.key === bankKey ? { ...prev, data } : prev))
    } catch (err) {
      setNotice({ message: admissionBankError(err, 'open').message, tone: 'error' })
    }
  }

  // ---- the questions ---------------------------------------------------------
  const [questionsToken, setQuestionsToken] = useState(0)
  const questionsKey = `${bankId}#${questionsToken}`
  const [questionsLoad, setQuestionsLoad] = useState<Loaded<Question[]> | null>(null)

  useEffect(() => {
    if (!bankId) return
    let cancelled = false
    listAllQuestions(bankId)
      .then((data) => {
        if (!cancelled) setQuestionsLoad({ key: questionsKey, data, error: null })
      })
      .catch((err: unknown) => {
        if (!cancelled) {
          setQuestionsLoad({ key: questionsKey, data: null, error: admissionBankError(err, 'questions') })
        }
      })
    return () => {
      cancelled = true
    }
  }, [bankId, questionsKey])

  const questionsState = questionsLoad?.key === questionsKey ? questionsLoad : null
  const questions = useMemo(() => questionsState?.data ?? [], [questionsState])

  const patchQuestions = (update: (list: Question[]) => Question[]) =>
    setQuestionsLoad((prev) =>
      prev && prev.key === questionsKey && prev.data ? { ...prev, data: update(prev.data) } : prev,
    )

  const refreshQuestions = async () => {
    try {
      const data = await listAllQuestions(bankId)
      setQuestionsLoad((prev) => (prev && prev.key === questionsKey ? { ...prev, data } : prev))
    } catch (err) {
      setNotice({ message: admissionBankError(err, 'questions').message, tone: 'error' })
    }
  }

  // ---- search (client-side, §3.1) --------------------------------------------
  const [search, setSearch] = useState('')
  const term = search.trim().toLocaleLowerCase()
  const numbered = useMemo(
    () => questions.map((question, i) => ({ question, number: i + 1 })),
    [questions],
  )
  const visible = useMemo(
    () =>
      term
        ? numbered.filter(({ question }) => question.text.toLocaleLowerCase().includes(term))
        : numbered,
    [numbered, term],
  )

  // ---- modals and row actions ------------------------------------------------
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<Question | null>(null)
  const [importOpen, setImportOpen] = useState(false)
  const [busyId, setBusyId] = useState<string | null>(null)

  const openCreate = () => {
    setEditing(null)
    setFormOpen(true)
  }

  const openEdit = (question: Question) => {
    setEditing(question)
    setFormOpen(true)
  }

  const closeForm = () => {
    setFormOpen(false)
    setEditing(null)
  }

  const onQuestionSaved = (saved: Question, created: boolean) => {
    closeForm()
    patchQuestions((list) =>
      created ? [...list, saved] : list.map((q) => (q.id === saved.id ? saved : q)),
    )
    setNotice({ message: created ? "Savol qo'shildi" : 'Savol saqlandi', tone: 'success' })
    if (created) void refreshBank()
  }

  const onImported = (result: QuestionImportResult) => {
    setImportOpen(false)
    const skipped =
      result.errorCount > 0 ? `, ${result.errorCount} ta xato qator o'tkazib yuborildi` : ''
    setNotice({ message: `${result.imported} ta savol qo'shildi${skipped}`, tone: 'success' })
    void refreshQuestions()
    void refreshBank()
  }

  /** 409 when answered, or when the bank feeds a published exam (§6.2, §8.2). */
  const removeQuestion = async (question: Question, number: number) => {
    if (!confirm(`${number}-savolni o'chirasizmi? Bu amalni qaytarib bo'lmaydi.`)) return
    setBusyId(question.id)
    try {
      await deleteQuestion(question.id)
      patchQuestions((list) => list.filter((q) => q.id !== question.id))
      setNotice({ message: `${number}-savol o'chirildi`, tone: 'success' })
      void refreshBank()
    } catch (err) {
      setNotice({ message: admissionBankError(err, 'deleteQuestion').message, tone: 'error' })
    } finally {
      setBusyId(null)
    }
  }

  const toast = (
    <Toast
      message={notice?.message ?? null}
      tone={notice?.tone ?? 'success'}
      onClose={() => setNotice(null)}
    />
  )

  // ---- the bank's own states -------------------------------------------------
  if (!bankId) {
    return (
      <div className="space-y-4">
        <BackLink />
        <Card className="py-16 text-center text-slate-400">Baza topilmadi</Card>
      </div>
    )
  }

  if (!bankState) {
    return (
      <div className="space-y-4">
        <BackLink />
        <Loader label="Yuklanmoqda..." />
        {toast}
      </div>
    )
  }

  if (!bank) {
    const error = bankState.error
    const kind = error?.kind
    return (
      <div className="space-y-4">
        <BackLink />
        <Card className="flex flex-col items-center gap-3 py-14 text-center">
          <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-red-50 text-red-600">
            <AlertTriangle className="h-6 w-6" />
          </div>
          <div>
            <p className="font-medium text-slate-800">
              {kind === 'not_found'
                ? 'Baza topilmadi'
                : kind === 'forbidden'
                  ? "Bu bo'limga ruxsatingiz yo'q"
                  : "Bazani ochib bo'lmadi"}
            </p>
            <p className="mt-1 max-w-md text-sm text-slate-500">{error?.message}</p>
          </div>
          {kind !== 'not_found' && kind !== 'forbidden' && (
            <Button variant="secondary" onClick={() => setBankToken((t) => t + 1)}>
              <RefreshCw className="h-4 w-4" /> Qayta urinish
            </Button>
          )}
        </Card>
        {toast}
      </div>
    )
  }

  const settingsKey = `${bank.id}:${bank.questionsPerTest}:${bank.timeLimitMin}:${bank.pointsPerCorrect}`

  return (
    <div className="space-y-6">
      <BackLink />

      {/* ---------- Header ---------- */}
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <h1 className="text-xl font-semibold text-slate-800">{bankTitle(bank)}</h1>
            <span
              className={cn('rounded-full px-2 py-0.5 text-xs font-medium', bankStateTone(bank.state))}
            >
              {bankStateLabel(bank.state)}
            </span>
          </div>
          <p className="text-sm text-slate-400">
            Bazada {bank.questionsCount} ta savol
            {typeof bank.questionsPerTest === 'number' &&
              ` · testga ${bank.questionsPerTest} tasi tushadi`}
            {typeof bank.timeLimitMin === 'number' && ` · ${formatMinutes(bank.timeLimitMin)}`}
            {typeof bank.pointsPerCorrect === 'number' &&
              ` · to'g'ri javob ${formatPoints(bank.pointsPerCorrect)} ball`}
          </p>
        </div>
        {canWrite && (
          <div className="flex flex-wrap gap-2">
            <Button variant="secondary" onClick={() => setImportOpen(true)}>
              <FileSpreadsheet className="h-4 w-4" /> Excel'dan import
            </Button>
            <Button onClick={openCreate}>
              <Plus className="h-4 w-4" /> Savol qo'shish
            </Button>
          </div>
        )}
      </div>

      {/* ---------- Server-derived warnings ---------- */}
      {bank.state === 'notEnough' && (
        <div className="flex items-start gap-2 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <span>
            Savollar yetarli emas: bazada {bank.questionsCount} ta, bitta testga esa{' '}
            {bank.questionsPerTest ?? '—'} ta kerak. Savol qo'shilmaguncha bu bazadan imtihon
            e'lon qilib bo'lmaydi.
          </span>
        </div>
      )}
      {bank.state === 'unconfigured' && (
        <div className="flex items-start gap-2 rounded-xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-600">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-slate-400" />
          <span>
            «Testdagi savollar» soni kiritilmagan — baza hali imtihonga tayyor emas.
          </span>
        </div>
      )}

      <BankSettingsStrip
        key={settingsKey}
        bank={bank}
        canWrite={canWrite}
        onSaved={(saved) => {
          setBankLoad((prev) => (prev && prev.key === bankKey ? { ...prev, data: saved } : prev))
          setNotice({ message: 'Sozlamalar saqlandi', tone: 'success' })
        }}
      />

      {/* ---------- Questions ---------- */}
      <Card className="p-0">
        <div className="flex flex-wrap items-center gap-3 border-b border-slate-100 p-4">
          <h2 className="font-semibold text-slate-800">Savollar</h2>
          <div className="relative ml-auto w-full min-w-[200px] sm:w-72">
            <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Savol matni bo'yicha qidirish..."
              aria-label="Savol qidirish"
              disabled={!questionsState?.data?.length}
              className={cn(control, 'w-full pl-9 pr-8 disabled:bg-slate-50')}
            />
            {search && (
              <button
                type="button"
                onClick={() => setSearch('')}
                aria-label="Qidiruvni tozalash"
                className="absolute right-2 top-2 rounded p-0.5 text-slate-400 hover:text-slate-700"
              >
                <X className="h-4 w-4" />
              </button>
            )}
          </div>
        </div>

        {!questionsState ? (
          <Loader label="Savollar yuklanmoqda..." />
        ) : questionsState.error ? (
          <div className="flex flex-col items-center gap-3 px-4 py-14 text-center">
            <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-red-50 text-red-600">
              <AlertTriangle className="h-6 w-6" />
            </div>
            <div>
              <p className="font-medium text-slate-800">Savollarni yuklab bo'lmadi</p>
              <p className="mt-1 max-w-md text-sm text-slate-500">{questionsState.error.message}</p>
            </div>
            {questionsState.error.kind !== 'forbidden' && (
              <Button variant="secondary" onClick={() => setQuestionsToken((t) => t + 1)}>
                <RefreshCw className="h-4 w-4" /> Qayta urinish
              </Button>
            )}
          </div>
        ) : questions.length === 0 ? (
          <div className="flex flex-col items-center gap-3 px-4 py-14 text-center">
            <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-slate-100">
              <FileQuestion className="h-7 w-7 text-slate-400" />
            </div>
            <p className="font-medium text-slate-600">Bazada hali savol yo'q</p>
            <p className="max-w-md text-sm text-slate-400">
              Savollarni bittalab qo'shing yoki Excel shablonini to'ldirib, hammasini birdan
              yuklang. Har bir savolda 2 tadan 6 tagacha variant va bitta to'g'ri javob bo'ladi.
            </p>
            {canWrite && (
              <div className="flex flex-wrap justify-center gap-2">
                <Button variant="secondary" onClick={() => setImportOpen(true)}>
                  <FileSpreadsheet className="h-4 w-4" /> Excel'dan import
                </Button>
                <Button onClick={openCreate}>
                  <Plus className="h-4 w-4" /> Birinchi savolni qo'shish
                </Button>
              </div>
            )}
          </div>
        ) : visible.length === 0 ? (
          <div className="flex flex-col items-center gap-2 px-4 py-14 text-center">
            <Search className="h-8 w-8 text-slate-300" />
            <p className="font-medium text-slate-600">«{search.trim()}» bo'yicha savol topilmadi</p>
            <button
              type="button"
              onClick={() => setSearch('')}
              className="text-sm font-medium text-brand-600 transition-colors hover:text-brand-700"
            >
              Qidiruvni tozalash
            </button>
          </div>
        ) : (
          <div className="space-y-3 p-4">
            {term && (
              <p className="text-xs text-slate-400">
                {questions.length} ta savoldan {visible.length} tasi ko'rsatilmoqda
              </p>
            )}
            {visible.map(({ question, number }) => (
              <QuestionCard
                key={question.id}
                question={question}
                number={number}
                canWrite={canWrite}
                busy={busyId === question.id}
                onEdit={openEdit}
                onDelete={(q, n) => void removeQuestion(q, n)}
              />
            ))}
          </div>
        )}
      </Card>

      {formOpen && canWrite && (
        <QuestionFormModal
          bankId={bank.id}
          editing={editing}
          nextNumber={questions.length + 1}
          onClose={closeForm}
          onSaved={onQuestionSaved}
        />
      )}

      {importOpen && canWrite && (
        <QuestionImportModal
          bankId={bank.id}
          bankLabel={bankTitle(bank)}
          onClose={() => setImportOpen(false)}
          onImported={onImported}
        />
      )}

      {toast}
    </div>
  )
}
