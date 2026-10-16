/**
 * VAZIFALAR — o'qituvchi o'quvchilarga beradigan topshiriqlar (mijoz, 2026-10-12: "mini appdagi
 * topshiriqlar qism bu xodimlarniki, men o'quvchilar uchun alohidani nazarda tutyapman").
 *
 * Eski o'qituvchi veb-ilovasidagi "Topshiriqlar" bilan bir xil manba (`/api/teacher/assignments`):
 * test (variantli savollar, avtomatik baholanadi), yozma, fayl va video. O'quvchi vazifani Mini App'da
 * ko'radi va topshiradi, ota-ona holatini va balini ko'radi.
 *
 * Uch ko'rinish, bitta tab: ro'yxat → natijalar (kim topshirdi, baholash) → forma (yangi / tahrir).
 */
import { useEffect, useRef, useState } from 'react'
import {
  Check, ChevronLeft, ChevronRight, ClipboardList, Download, Paperclip, Pencil, Plus, Trash2, Upload, X,
} from 'lucide-react'
import { Badge, Card, EmptyState, Hero, HeroProgress, Row, Screen } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { teacherApi } from '../../lib/teacherApi'
import { fullDate, dateTime } from '../../lib/format'
import { todayISO } from '../../lib/weeks'
import { haptic, hapticNotify, showBackButton, useKeyboardOpen } from '../../lib/telegram'
import {
  FORMATS, MAX_TEST_QUESTIONS, daysLeft, dueInfo, fileNameOf, formatOf, gradeOf, isoDay, safeHref,
} from '../../lib/assignments'
import { AsyncBlock } from './shared'
import { QuestionImportSheet } from './QuestionImportSheet'

export function AssignmentsTab() {
  const list = useAsync(() => teacherApi.assignments(), [])
  /** null — ro'yxat; `{ kind: 'results' | 'form', item }`. */
  const [view, setView] = useState(null)

  useEffect(() => (view ? showBackButton(() => setView(null)) : undefined), [view])

  if (view?.kind === 'form') {
    return (
      <AssignmentForm
        item={view.item}
        onCancel={() => setView(view.item ? { kind: 'results', item: view.item } : null)}
        onSaved={() => {
          setView(null)
          list.reload()
        }}
      />
    )
  }
  if (view?.kind === 'results') {
    return (
      <AssignmentResults
        item={view.item}
        onBack={() => setView(null)}
        onEdit={() => setView({ kind: 'form', item: view.item })}
        onDeleted={() => {
          setView(null)
          list.reload()
        }}
      />
    )
  }

  const items = list.data ?? []
  const active = items.filter((a) => (daysLeft(a.dueDate) ?? 0) >= 0)
  const past = items.filter((a) => (daysLeft(a.dueDate) ?? 0) < 0)
  const newButton = (
    <button
      type="button"
      onClick={() => {
        haptic('light')
        setView({ kind: 'form', item: null })
      }}
      className="flex h-9 items-center gap-1 rounded-full bg-brand-ink px-3 text-[13px] font-semibold text-brand"
    >
      <Plus className="h-4 w-4" />
      Yangi
    </button>
  )

  return (
    <Screen>
      <Hero title="Vazifalar" subtitle="O'quvchilarga topshiriqlar" right={newButton}>
        {items.length > 0 ? (
          <p className="text-[14px] opacity-80">
            {active.length} ta faol{past.length > 0 ? ` · ${past.length} ta muddati o'tgan` : ''}
          </p>
        ) : null}
      </Hero>

      <AsyncBlock
        query={list}
        loadingLabel="Vazifalar yuklanmoqda…"
        isEmpty={(d) => !d || d.length === 0}
        empty={
          <Card>
            <EmptyState
              icon={<ClipboardList className="h-9 w-9" />}
              title="Hali vazifa yo'q"
              note="«Yangi» tugmasi bilan test, yozma, fayl yoki video vazifa bering. O'quvchilar uni Mini App'da ko'radi va topshiradi."
            />
          </Card>
        }
      >
        {() => (
          <>
            {active.length > 0 && <AssignmentList title="Faol" items={active} onOpen={(item) => setView({ kind: 'results', item })} />}
            {past.length > 0 && (
              <AssignmentList title="Muddati o'tgan" items={past} onOpen={(item) => setView({ kind: 'results', item })} />
            )}
          </>
        )}
      </AsyncBlock>
    </Screen>
  )
}

function FormatIcon({ format }) {
  const Icon = formatOf(format).icon
  return (
    <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-brand/25 text-brand-ink">
      <Icon className="h-5 w-5" />
    </div>
  )
}

function AssignmentList({ title, items, onOpen }) {
  return (
    <Card title={title}>
      <div className="pt-1">
        {items.map((a) => {
          const due = dueInfo(a.dueDate)
          return (
            <Row
              key={a.id}
              lead={<FormatIcon format={a.format} />}
              title={a.title}
              subtitle={[formatOf(a.format).label, a.subjectName, (a.classNames || []).join(', ')].filter(Boolean).join(' · ')}
              right={<Badge tone={due.tone}>{due.text}</Badge>}
              onClick={() => {
                haptic('light')
                onOpen(a)
              }}
            />
          )
        })}
      </div>
    </Card>
  )
}

/* ------------------------------------------------------------ natijalar */

function BackPill({ label, onClick }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label="Orqaga"
      className="flex h-9 items-center gap-1 rounded-full bg-brand-ink px-3 text-[13px] font-semibold text-brand"
    >
      <ChevronLeft className="h-4 w-4" />
      {label}
    </button>
  )
}

function AssignmentResults({ item, onBack, onEdit, onDeleted }) {
  const q = useAsync(() => teacherApi.assignmentResults(item.id), [item.id])
  const [tab, setTab] = useState('done')
  const [grading, setGrading] = useState(null)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [error, setError] = useState(null)

  const r = q.data
  const rows = r?.rows ?? []
  const done = rows.filter((x) => x.completed)
  const pending = rows.filter((x) => !x.completed)
  const shown = tab === 'done' ? done : pending
  const due = dueInfo(item.dueDate)
  const fmt = formatOf(item.format)

  const remove = async () => {
    setDeleting(true)
    setError(null)
    try {
      await teacherApi.deleteAssignment(item.id)
      hapticNotify('success')
      onDeleted()
    } catch (e) {
      setError(e?.message || "O'chirib bo'lmadi")
      setDeleting(false)
    }
  }

  return (
    <Screen>
      <Hero title={item.title} subtitle={`${fmt.label} · ${item.subjectName}`} right={<BackPill label="Vazifalar" onClick={onBack} />}>
        {r ? <HeroProgress value={r.completedCount} max={r.total} lead={`${r.completedCount}/${r.total}`} note="topshirdi" /> : null}
      </Hero>

      <Card>
        <div className="space-y-2 px-4 py-3 text-[14px]">
          <div className="flex flex-wrap items-center gap-2">
            <Badge tone={due.tone}>{item.dueDate ? `${fullDate(isoDay(item.dueDate))} · ${due.text}` : due.text}</Badge>
            <Badge>Maks. {item.maxScore} ball</Badge>
            {item.format === 'test' && <Badge>{(item.questions || []).length} ta savol</Badge>}
          </div>
          <p className="text-slate-500">{(item.classNames || []).join(', ')}</p>
          {item.description && <p className="whitespace-pre-wrap break-words text-slate-700">{item.description}</p>}
          {(item.materials || []).length > 0 && <Materials items={item.materials} />}
        </div>
        <div className="flex gap-2 border-t border-slate-100 px-4 py-3">
          {confirmDelete ? (
            <>
              <p className="flex-1 self-center text-[13px] font-semibold text-red-700">Vazifa va javoblar o'chirilsinmi?</p>
              <button type="button" onClick={() => setConfirmDelete(false)} className="rounded-xl bg-slate-100 px-3 py-2 text-[13px] font-semibold text-slate-600">
                Yo'q
              </button>
              <button
                type="button"
                disabled={deleting}
                onClick={remove}
                className="rounded-xl bg-red-600 px-3 py-2 text-[13px] font-semibold text-white disabled:opacity-50"
              >
                {deleting ? "O'chirilmoqda…" : "Ha, o'chirish"}
              </button>
            </>
          ) : (
            <>
              <button
                type="button"
                onClick={onEdit}
                className="flex flex-1 items-center justify-center gap-1.5 rounded-xl bg-slate-100 py-2.5 text-[14px] font-semibold text-slate-700"
              >
                <Pencil className="h-4 w-4" /> Tahrirlash
              </button>
              <button
                type="button"
                onClick={() => setConfirmDelete(true)}
                className="flex items-center justify-center gap-1.5 rounded-xl bg-red-50 px-4 py-2.5 text-[14px] font-semibold text-red-600"
              >
                <Trash2 className="h-4 w-4" /> O'chirish
              </button>
            </>
          )}
        </div>
        {error && <p className="px-4 pb-3 text-[13px] font-medium text-red-600">{error}</p>}
      </Card>

      <AsyncBlock query={q} loadingLabel="Natijalar yuklanmoqda…">
        {() => (
          <Card>
            <div className="flex gap-1.5 px-4 pb-1 pt-4">
              {[
                ['done', `Topshirgan (${done.length})`],
                ['pending', `Topshirmagan (${pending.length})`],
              ].map(([value, label]) => (
                <button
                  key={value}
                  type="button"
                  onClick={() => setTab(value)}
                  className={
                    'flex-1 rounded-full px-3 py-2 text-[13px] font-semibold ' +
                    (tab === value ? 'bg-brand-ink text-brand' : 'bg-slate-100 text-slate-500')
                  }
                >
                  {label}
                </button>
              ))}
            </div>
            {rows.length === 0 ? (
              <EmptyState title="O'quvchi yo'q" note="Tanlangan sinflarda faol o'quvchi topilmadi." />
            ) : shown.length === 0 ? (
              <EmptyState title={tab === 'done' ? 'Hali hech kim topshirmagan' : 'Hamma topshirdi'} />
            ) : (
              <div className="pt-1">
                {shown.map((row) => (
                  <Row
                    key={row.studentId}
                    title={row.studentName}
                    subtitle={[row.className, row.completed && row.submittedAt ? dateTime(row.submittedAt) : null].filter(Boolean).join(' · ')}
                    right={
                      row.score != null ? (
                        <span className="shrink-0 text-right">
                          <span className="text-[18px] font-extrabold">{row.score}</span>
                          <span className="text-[12px] text-slate-400"> / {r.maxScore}</span>
                        </span>
                      ) : row.completed ? (
                        <Badge tone="brand">Baholash</Badge>
                      ) : (
                        <ChevronRight className="h-5 w-5 text-slate-300" />
                      )
                    }
                    onClick={() => setGrading(row)}
                  />
                ))}
              </div>
            )}
          </Card>
        )}
      </AsyncBlock>

      {grading && r && (
        <GradeSheet
          assignmentId={item.id}
          format={item.format}
          maxScore={r.maxScore}
          row={grading}
          onClose={() => setGrading(null)}
          onSaved={() => {
            setGrading(null)
            q.reload()
          }}
        />
      )}
    </Screen>
  )
}

function Materials({ items }) {
  return (
    <div className="space-y-1.5">
      {items.map((m, i) => {
        const href = safeHref(m.url)
        return (
          <a
            key={`${m.url}-${i}`}
            href={href ?? undefined}
            target="_blank"
            rel="noreferrer"
            className="flex items-center gap-2 rounded-xl bg-slate-50 px-3 py-2 text-[13px] font-semibold text-slate-700"
          >
            <Paperclip className="h-4 w-4 shrink-0 text-slate-400" />
            <span className="min-w-0 flex-1 truncate">{m.name || fileNameOf(m.url)}</span>
            <Download className="h-4 w-4 shrink-0 text-slate-400" />
          </a>
        )
      })}
    </div>
  )
}

/** Pastdan chiqadigan varaq — o'quvchining javobi va baho. */
function GradeSheet({ assignmentId, format, maxScore, row, onClose, onSaved }) {
  const [completed, setCompleted] = useState(!!row.completed)
  const [score, setScore] = useState(row.score != null ? String(row.score) : '')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState(null)
  const answerHref = safeHref(row.fileUrl)

  const save = async () => {
    const parsed = score.trim() === '' ? null : Number(score)
    if (parsed !== null && (!Number.isInteger(parsed) || parsed < 0 || parsed > maxScore)) {
      setError(`Ball 0 dan ${maxScore} gacha butun son bo'lsin.`)
      return
    }
    setSaving(true)
    setError(null)
    try {
      await teacherApi.setSubmission(assignmentId, row.studentId, { completed, score: completed ? parsed : null })
      hapticNotify('success')
      onSaved()
    } catch (e) {
      setError(e?.message || "Saqlab bo'lmadi")
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-40 flex items-end bg-black/40" onClick={onClose}>
      <div
        role="dialog"
        aria-label={row.studentName}
        className="max-h-[90vh] w-full overflow-y-auto rounded-t-[26px] bg-white px-4 pb-[calc(16px+var(--tg-safe-bottom))] pt-4"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center gap-3">
          <div className="min-w-0 flex-1">
            <p className="truncate text-[17px] font-bold">{row.studentName}</p>
            <p className="text-[13px] text-slate-500">
              {row.className}
              {row.completed && row.submittedAt ? ` · ${dateTime(row.submittedAt)}` : ''}
            </p>
          </div>
          <button type="button" onClick={onClose} aria-label="Yopish" className="flex h-9 w-9 items-center justify-center rounded-full bg-slate-100">
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="mt-3 rounded-2xl bg-slate-50 px-3 py-3 text-[14px]">
          <p className="mb-1 text-[12px] font-bold uppercase tracking-wide text-slate-400">Javob</p>
          {!row.completed && !row.answerText && !row.fileUrl ? (
            <p className="text-slate-500">Hali topshirmagan. Daftarda topshirgan bo'lsa — «Topshirdi» deb belgilab, baho qo'ying.</p>
          ) : format === 'test' ? (
            <p className="text-slate-600">Test — ball avtomatik hisoblangan. Kerak bo'lsa o'zgartirishingiz mumkin.</p>
          ) : (
            <div className="space-y-2">
              {row.answerText && <p className="whitespace-pre-wrap break-words text-slate-800">{row.answerText}</p>}
              {row.fileUrl &&
                (answerHref ? (
                  <a href={answerHref} target="_blank" rel="noreferrer" className="flex items-center gap-2 font-semibold text-brand-ink underline">
                    <Paperclip className="h-4 w-4" />
                    <span className="min-w-0 truncate">{fileNameOf(row.fileUrl)}</span>
                  </a>
                ) : (
                  <p className="break-all text-slate-600">{row.fileUrl}</p>
                ))}
              {!row.answerText && !row.fileUrl && <p className="text-slate-500">Matn yoki fayl biriktirilmagan.</p>}
            </div>
          )}
        </div>

        <div className="mt-3 flex gap-2">
          {[
            [true, 'Topshirdi'],
            [false, 'Topshirmadi'],
          ].map(([value, label]) => (
            <button
              key={label}
              type="button"
              onClick={() => setCompleted(value)}
              className={
                'flex-1 rounded-xl py-2.5 text-[14px] font-bold ' +
                (completed === value ? (value ? 'bg-emerald-700 text-white' : 'bg-red-600 text-white') : 'bg-slate-100 text-slate-500')
              }
            >
              {label}
            </button>
          ))}
        </div>

        {completed && (
          <label className="mt-3 block">
            <span className="mb-1 block text-[13px] font-semibold text-slate-500">Ball (0–{maxScore})</span>
            <input
              type="number"
              inputMode="numeric"
              min={0}
              max={maxScore}
              value={score}
              onChange={(e) => setScore(e.target.value)}
              placeholder="—"
              className="w-full rounded-xl border border-slate-200 bg-slate-50 px-3 py-2.5 text-[18px] font-bold outline-none focus:border-brand focus:bg-white"
            />
          </label>
        )}

        {error && <p className="mt-2 text-[13px] font-medium text-red-600">{error}</p>}
        <button
          type="button"
          onClick={save}
          disabled={saving}
          className="mt-4 w-full rounded-2xl bg-brand py-3.5 text-[16px] font-bold text-brand-ink disabled:opacity-50"
        >
          {saving ? 'Saqlanmoqda…' : 'Saqlash'}
        </button>
      </div>
    </div>
  )
}

/* ---------------------------------------------------------------- forma */

const INPUT =
  'w-full rounded-xl border border-slate-200 bg-slate-50 px-3 py-2.5 text-[15px] outline-none focus:border-brand focus:bg-white'

const emptyQuestion = () => ({ text: '', options: ['', '', '', ''], correctIndex: 0 })

/** A question card of the form — at least 4 option inputs (a 5–6 option question shows them all). */
const toFormQuestion = (x) => ({
  text: x.text,
  options: [...x.options, '', '', '', ''].slice(0, Math.max(4, x.options.length)),
  correctIndex: x.correctIndex,
})

/** A card with nothing typed — the bulk import takes its place. */
const isBlankQuestion = (x) => !x.text.trim() && x.options.every((o) => !o.trim())

function Field({ label, children }) {
  return (
    <label className="block">
      <span className="mb-1 block text-[13px] font-semibold text-slate-500">{label}</span>
      {children}
    </label>
  )
}

function Chip({ on, onClick, children }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={on}
      className={
        'flex items-center gap-1 rounded-full px-3.5 py-2 text-[14px] font-semibold ' +
        (on ? 'bg-brand-ink text-brand' : 'bg-slate-100 text-slate-600')
      }
    >
      {on && <Check className="h-4 w-4" />}
      {children}
    </button>
  )
}

/**
 * Yangi vazifa yoki tahrir. Sana — faqat muddat: server boshlanish sanasiga qarab yashirmaydi, shuning
 * uchun formada so'ralmaydi (tahrirda eski qiymat saqlanadi). Kechikib topshirish — Mini App o'quvchiga
 * muddatdan keyin topshirish tugmasini shu belgiga qarab beradi.
 */
function AssignmentForm({ item, onCancel, onSaved }) {
  const owners = useAsync(() => teacherApi.classes(), [])
  const keyboardOpen = useKeyboardOpen()
  const fileRef = useRef(null)

  const [title, setTitle] = useState(item?.title ?? '')
  const [description, setDescription] = useState(item?.description ?? '')
  const [format, setFormat] = useState(item ? (item.format === 'text' ? 'written' : item.format) : 'test')
  const [subjectId, setSubjectId] = useState(item?.subjectId ?? '')
  const [kind, setKind] = useState(item?.ownerKind ?? 'class')
  const [ownerIds, setOwnerIds] = useState(item?.classIds ?? [])
  const [dueDate, setDueDate] = useState(isoDay(item?.dueDate))
  const [maxScore, setMaxScore] = useState(String(item?.maxScore ?? 10))
  const [lateAccept, setLateAccept] = useState(item?.lateAccept ?? false)
  const [materials, setMaterials] = useState(
    (item?.materials ?? []).map((m) => ({ name: m.name, url: m.url, size: m.size, contentType: m.contentType })),
  )
  const [questions, setQuestions] = useState(
    item?.questions?.length
      ? [...item.questions].sort((a, b) => (a.order ?? 0) - (b.order ?? 0)).map(toFormQuestion)
      : [emptyQuestion()],
  )
  const [uploading, setUploading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState(null)
  const [importOpen, setImportOpen] = useState(false)
  const [importNote, setImportNote] = useState(null)

  const all = owners.data ?? []
  const subjectMap = new Map(all.flatMap((o) => o.subjects || []).map((s) => [s.id, s]))
  if (item?.subjectId && !subjectMap.has(item.subjectId)) subjectMap.set(item.subjectId, { id: item.subjectId, name: item.subjectName })
  const subjects = [...subjectMap.values()]
  const teaching = (o) => !subjectId || (o.subjects || []).some((s) => s.id === subjectId)
  const visibleOwners = all.some(teaching) ? all.filter(teaching) : all

  // Bitta fan bo'lsa — darrov tanlanadi.
  const onlySubject = subjects.length === 1 ? subjects[0].id : null
  useEffect(() => {
    if (!item && !subjectId && onlySubject) setSubjectId(onlySubject)
  }, [item, subjectId, onlySubject])

  const pickSubject = (id) => {
    setSubjectId(id)
    setError(null)
    // Bu fanni o'tmaydigan sinflar tanlovdan tushadi — ekranda ko'rinmagan sinfga vazifa ketmasin.
    setOwnerIds((prev) => prev.filter((oid) => all.some((o) => o.classId === oid && (o.subjects || []).some((s) => s.id === id))))
  }

  const toggleOwner = (o) => {
    setError(null)
    const ownerKind = o.ownerKind || 'class'
    // Sinf va o'quv guruhi bitta vazifada aralashmaydi (server bitta egalik turini saqlaydi).
    if (ownerKind !== kind) {
      setKind(ownerKind)
      setOwnerIds([o.classId])
      return
    }
    setOwnerIds((prev) => (prev.includes(o.classId) ? prev.filter((x) => x !== o.classId) : [...prev, o.classId]))
  }

  const onPickFile = async (e) => {
    const file = e.target.files?.[0]
    e.target.value = ''
    if (!file) return
    setUploading(true)
    setError(null)
    try {
      const up = await teacherApi.uploadMaterial(file)
      setMaterials((prev) => [...prev, { name: up.name, url: up.url, size: up.size, contentType: up.contentType }])
    } catch (err) {
      setError(err?.message || "Faylni yuklab bo'lmadi")
    } finally {
      setUploading(false)
    }
  }

  /** Questions from the bulk import: appended, or written in place of the form's questions. */
  const filledQuestions = questions.filter((x) => !isBlankQuestion(x))
  const applyImport = (imported, mode) => {
    const incoming = imported.map(toFormQuestion)
    setQuestions(mode === 'replace' ? incoming : [...filledQuestions, ...incoming])
    setImportOpen(false)
    setError(null)
    setImportNote(
      `${imported.length} ta savol ${mode === 'replace' ? 'formaga yozildi' : "qo'shildi"}. Ko'rib chiqing va «${item ? 'Saqlash' : 'Vazifani berish'}» tugmasini bosing.`,
    )
    hapticNotify('success')
  }

  const setQuestion = (i, patch) => setQuestions((prev) => prev.map((x, j) => (j === i ? { ...x, ...patch } : x)))
  const setOption = (i, k, value) =>
    setQuestions((prev) => prev.map((x, j) => (j === i ? { ...x, options: x.options.map((o, n) => (n === k ? value : o)) } : x)))

  /** Bo'sh variantlarsiz savollar; to'g'ri javob indeksi qayta hisoblanadi. Xato bo'lsa — matn. */
  const cleanQuestions = () => {
    const out = []
    for (const [i, x] of questions.entries()) {
      if (!x.text.trim()) continue
      const kept = x.options.map((o, k) => ({ o: o.trim(), k })).filter((v) => v.o)
      if (kept.length < 2) return `${i + 1}-savolda kamida ikkita variant bo'lsin.`
      const correct = kept.findIndex((v) => v.k === x.correctIndex)
      if (correct < 0) return `${i + 1}-savolda to'g'ri javob bo'sh variantda — to'g'ri variantni belgilang.`
      out.push({ text: x.text.trim(), options: kept.map((v) => v.o), correctIndex: correct })
    }
    if (out.length === 0) return 'Testga kamida bitta savol yozing.'
    if (out.length > MAX_TEST_QUESTIONS) return `Bitta testda ko'pi bilan ${MAX_TEST_QUESTIONS} ta savol bo'lishi mumkin.`
    return out
  }

  const save = async () => {
    const max = Number(maxScore)
    let problem = null
    if (!title.trim()) problem = 'Vazifa nomini yozing.'
    else if (!subjectId) problem = 'Fanni tanlang.'
    else if (ownerIds.length === 0) problem = 'Kamida bitta sinf tanlang.'
    else if (!Number.isInteger(max) || max < 1 || max > 100) problem = "Maksimal ball 1 dan 100 gacha bo'lsin."
    else if (dueDate && !item && dueDate < todayISO()) problem = "Muddat o'tgan kun bo'lmasin."
    const qs = format === 'test' ? cleanQuestions() : []
    if (!problem && typeof qs === 'string') problem = qs
    if (problem) {
      setError(problem)
      hapticNotify('error')
      return
    }

    const body = {
      subjectId,
      title: title.trim(),
      description: description.trim(),
      format,
      classIds: ownerIds,
      ownerKind: kind,
      startDate: item?.startDate ?? null,
      dueDate: dueDate || null,
      lateAccept,
      latePenaltyPct: item?.latePenaltyPct ?? 0,
      maxScore: max,
      autoGrade: item?.autoGrade ?? true,
      materials,
      questions: format === 'test' ? qs : [],
    }
    setSaving(true)
    setError(null)
    try {
      if (item) await teacherApi.updateAssignment(item.id, body)
      else await teacherApi.createAssignment(body)
      hapticNotify('success')
      onSaved()
    } catch (e) {
      setError(e?.message || "Saqlab bo'lmadi")
      setSaving(false)
    }
  }

  return (
    <Screen>
      <Hero title={item ? 'Vazifani tahrirlash' : 'Yangi vazifa'} subtitle="O'quvchilarga" right={<BackPill label="Bekor" onClick={onCancel} />} />

      <Card title="Vazifa">
        <div className="space-y-3 px-4 pb-4 pt-2">
          <Field label="Nomi">
            <input value={title} maxLength={200} onChange={(e) => setTitle(e.target.value)} placeholder="Masalan: Kasrlar — test" className={INPUT} />
          </Field>
          <Field label="Tavsif (ixtiyoriy)">
            <textarea
              value={description}
              rows={3}
              maxLength={2000}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Nima qilish kerak, qayerdan o'qish…"
              className={INPUT + ' resize-none'}
            />
          </Field>
          <div>
            <span className="mb-1 block text-[13px] font-semibold text-slate-500">Turi</span>
            <div className="grid grid-cols-2 gap-2">
              {FORMATS.map((f) => {
                const on = format === f.value
                const Icon = f.icon
                return (
                  <button
                    key={f.value}
                    type="button"
                    onClick={() => setFormat(f.value)}
                    aria-pressed={on}
                    className={
                      'flex items-center gap-2.5 rounded-xl border px-3 py-2.5 text-left ' +
                      (on ? 'border-brand-ink bg-brand/20' : 'border-slate-200 bg-white')
                    }
                  >
                    <Icon className="h-5 w-5 shrink-0" />
                    <span>
                      <span className="block text-[14px] font-bold">{f.label}</span>
                      <span className="block text-[11px] text-slate-500">{f.note}</span>
                    </span>
                  </button>
                )
              })}
            </div>
          </div>
        </div>
      </Card>

      <Card title="Kimga">
        <div className="space-y-3 px-4 pb-4 pt-2">
          {owners.loading && !owners.data ? (
            <p className="text-[14px] text-slate-400">Sinflar yuklanmoqda…</p>
          ) : owners.error ? (
            <p className="text-[14px] text-red-600">{owners.error}</p>
          ) : (
            <>
              <div>
                <span className="mb-1.5 block text-[13px] font-semibold text-slate-500">Fan</span>
                <div className="flex flex-wrap gap-1.5">
                  {subjects.length === 0 && <p className="text-[14px] text-slate-400">Sizga fan biriktirilmagan.</p>}
                  {subjects.map((s) => (
                    <Chip key={s.id} on={subjectId === s.id} onClick={() => pickSubject(s.id)}>
                      {s.name}
                    </Chip>
                  ))}
                </div>
              </div>
              <div>
                <span className="mb-1.5 block text-[13px] font-semibold text-slate-500">Sinflar (bir nechtasini tanlash mumkin)</span>
                <div className="flex flex-wrap gap-1.5">
                  {visibleOwners.map((o) => (
                    <Chip key={o.classId} on={ownerIds.includes(o.classId)} onClick={() => toggleOwner(o)}>
                      {o.className}
                    </Chip>
                  ))}
                </div>
                {all.some((o) => o.ownerKind === 'group') && (
                  <p className="mt-1.5 text-[12px] text-slate-400">Sinf va o'quv guruhi bitta vazifada birga tanlanmaydi.</p>
                )}
              </div>
            </>
          )}
        </div>
      </Card>

      <Card title="Muddat va ball">
        <div className="space-y-3 px-4 pb-4 pt-2">
          <div className="grid grid-cols-2 gap-2">
            <Field label="Muddat">
              <input type="date" value={dueDate} min={item ? undefined : todayISO()} onChange={(e) => setDueDate(e.target.value)} className={INPUT + ' min-h-[44px]'} />
            </Field>
            <Field label="Maksimal ball">
              <input type="number" inputMode="numeric" min={1} max={100} value={maxScore} onChange={(e) => setMaxScore(e.target.value)} className={INPUT} />
            </Field>
          </div>
          <button
            type="button"
            onClick={() => setLateAccept((v) => !v)}
            aria-pressed={lateAccept}
            className="flex w-full items-center gap-3 rounded-xl bg-slate-50 px-3 py-2.5 text-left"
          >
            <span className="min-w-0 flex-1">
              <span className="block text-[14px] font-semibold">Muddatdan keyin ham qabul qilinsin</span>
              <span className="block text-[12px] text-slate-500">O'chiq bo'lsa, muddat o'tgach o'quvchi topshira olmaydi</span>
            </span>
            <span className={'flex h-7 w-12 shrink-0 items-center rounded-full p-0.5 transition ' + (lateAccept ? 'bg-brand-ink' : 'bg-slate-300')}>
              <span className={'h-6 w-6 rounded-full bg-white transition ' + (lateAccept ? 'translate-x-5' : '')} />
            </span>
          </button>
        </div>
      </Card>

      <Card title="Materiallar">
        <div className="space-y-2 px-4 pb-4 pt-2">
          {materials.length === 0 && <p className="text-[13px] text-slate-400">Ixtiyoriy: PDF, rasm yoki hujjat (20 MB gacha).</p>}
          {materials.map((m, i) => (
            <div key={`${m.url}-${i}`} className="flex items-center gap-2 rounded-xl bg-slate-50 px-3 py-2">
              <Paperclip className="h-4 w-4 shrink-0 text-slate-400" />
              <span className="min-w-0 flex-1 truncate text-[14px] font-semibold">{m.name}</span>
              <button type="button" aria-label="Olib tashlash" onClick={() => setMaterials((prev) => prev.filter((_, j) => j !== i))} className="text-red-600">
                <Trash2 className="h-4 w-4" />
              </button>
            </div>
          ))}
          <button
            type="button"
            disabled={uploading}
            onClick={() => fileRef.current?.click()}
            className="flex w-full items-center justify-center gap-2 rounded-xl border border-dashed border-slate-300 py-2.5 text-[14px] font-semibold text-slate-600 disabled:opacity-50"
          >
            <Plus className="h-4 w-4" />
            {uploading ? 'Yuklanmoqda…' : "Fayl qo'shish"}
          </button>
          <input ref={fileRef} type="file" hidden onChange={onPickFile} />
        </div>
      </Card>

      {format === 'test' && (
        <Card
          title={filledQuestions.length > 0 ? `Savollar · ${filledQuestions.length}` : 'Savollar'}
          action={
            <button
              type="button"
              onClick={() => {
                haptic('light')
                setImportNote(null)
                setImportOpen(true)
              }}
              className="flex h-8 shrink-0 items-center gap-1 rounded-full bg-brand/25 px-3 text-[13px] font-semibold text-brand-ink"
            >
              <Upload className="h-4 w-4" />
              Ommaviy yuklash
            </button>
          }
        >
          <div className="space-y-3 px-4 pb-4 pt-2">
            {importNote ? (
              <p className="rounded-xl bg-emerald-50 px-3 py-2 text-[13px] font-medium text-emerald-700">{importNote}</p>
            ) : (
              filledQuestions.length === 0 && (
                <p className="text-[12px] text-slate-400">
                  Savollar ko'p bo'lsa — «Ommaviy yuklash»: matnni joylang (AI javobini ham), Word yoki Excel faylni tanlang.
                </p>
              )
            )}
            {questions.map((x, i) => (
              <div key={i} className="rounded-2xl border border-slate-200 p-3">
                <div className="mb-2 flex items-center gap-2">
                  <span className="flex h-6 w-6 items-center justify-center rounded-lg bg-brand-ink text-[12px] font-bold text-brand">{i + 1}</span>
                  <span className="flex-1 text-[12px] text-slate-400">To'g'ri javobni doira bilan belgilang</span>
                  {questions.length > 1 && (
                    <button type="button" aria-label="Savolni o'chirish" onClick={() => setQuestions((prev) => prev.filter((_, j) => j !== i))} className="text-red-600">
                      <Trash2 className="h-4 w-4" />
                    </button>
                  )}
                </div>
                <input value={x.text} onChange={(e) => setQuestion(i, { text: e.target.value })} placeholder="Savol matni" className={INPUT} />
                <div className="mt-2 space-y-1.5">
                  {x.options.map((o, k) => {
                    const correct = x.correctIndex === k
                    return (
                      <div key={k} className="flex items-center gap-2">
                        <button
                          type="button"
                          aria-label={`${k + 1}-variant to'g'ri`}
                          aria-pressed={correct}
                          onClick={() => setQuestion(i, { correctIndex: k })}
                          className={
                            'flex h-7 w-7 shrink-0 items-center justify-center rounded-full border-2 ' +
                            (correct ? 'border-emerald-600 bg-emerald-600 text-white' : 'border-slate-300')
                          }
                        >
                          {correct && <Check className="h-4 w-4" />}
                        </button>
                        <input value={o} onChange={(e) => setOption(i, k, e.target.value)} placeholder={`${k + 1}-variant`} className={INPUT + ' py-2 text-[14px]'} />
                      </div>
                    )
                  })}
                </div>
              </div>
            ))}
            <button
              type="button"
              onClick={() => setQuestions((prev) => [...prev, emptyQuestion()])}
              className="flex w-full items-center justify-center gap-2 rounded-xl bg-slate-100 py-2.5 text-[14px] font-semibold text-slate-700"
            >
              <Plus className="h-4 w-4" /> Savol qo'shish
            </button>
          </div>
        </Card>
      )}

      {importOpen && (
        <QuestionImportSheet
          existing={filledQuestions.length}
          subject={subjects.find((s) => s.id === subjectId)?.name ?? ''}
          grade={gradeOf(all.find((o) => o.classId === ownerIds[0])?.className)}
          onApply={applyImport}
          onClose={() => setImportOpen(false)}
        />
      )}

      {error && <p className="mx-6 mt-3 text-[13px] font-medium text-red-600">{error}</p>}

      {/* Yopishqoq panel tab panelining ustida; yozayotganda (klaviatura ochiq) yashirinadi. */}
      <div className="h-28" />
      {!keyboardOpen && (
        <div className="fixed inset-x-0 bottom-[calc(76px+var(--tg-safe-bottom))] z-10 border-t border-slate-200 bg-white px-4 py-3">
          <button
            type="button"
            onClick={save}
            disabled={saving || uploading}
            className="w-full rounded-2xl bg-brand py-3.5 text-[16px] font-bold text-brand-ink disabled:opacity-50"
          >
            {saving ? 'Saqlanmoqda…' : item ? 'Saqlash' : 'Vazifani berish'}
          </button>
        </div>
      )}
    </Screen>
  )
}
