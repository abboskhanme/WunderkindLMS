import { useEffect, useRef, useState } from 'react'
import { Plus, Trash2, Upload, Check, FileText, PenLine, ListChecks, Video } from 'lucide-react'
import type { Assignment, AssignmentFormat, Subject } from '@/types'
import type { MaterialInput, SaveAssignmentInput } from '@/api/services/assignments'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { cn } from '@/lib/utils'

/** Wizard sinf tanlovi — admin (barcha sinf) va o'qituvchi (dars beradigan sinflar) ikkalasi uchun. */
export interface WizardClass {
  id: string
  name: string
}

interface QForm {
  text: string
  options: string[]
  correctIndex: number
}

interface Props {
  open: boolean
  onClose: () => void
  onSaved: () => void
  classes: WizardClass[]
  subjects: Subject[]
  initial: Assignment | null
  /** Topshiriqni saqlash — yangi (id=null) yoki tahrir (id berilgan). Admin/o'qituvchi o'z servisini beradi. */
  onSubmit: (input: SaveAssignmentInput, id: string | null) => Promise<void>
  /** Material faylini yuklash — admin/o'qituvchi o'z upload endpointini beradi. */
  onUpload: (file: File) => Promise<MaterialInput>
}

const formats: { key: AssignmentFormat; label: string; desc: string; icon: typeof FileText }[] = [
  { key: 'file', label: 'Fayl yuklash', desc: 'PDF, doc, rasm', icon: FileText },
  { key: 'written', label: 'Yozma topshiriq', desc: 'Matn, insho', icon: PenLine },
  { key: 'test', label: 'Test', desc: 'Variant tanlash', icon: ListChecks },
  { key: 'video', label: 'Video javob', desc: 'Yozib yuborish', icon: Video },
]

const steps = ['Asosiy', 'Mazmun va materiallar', 'Sinflar va muddat', 'Baholash']

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

function formatSize(bytes: number): string {
  if (bytes >= 1_000_000) return `${(bytes / 1_000_000).toFixed(1)} MB`
  if (bytes >= 1000) return `${Math.round(bytes / 1000)} KB`
  return `${bytes} B`
}

export function AssignmentWizard({
  open,
  onClose,
  onSaved,
  classes,
  subjects,
  initial,
  onSubmit,
  onUpload,
}: Props) {
  const [step, setStep] = useState(0)
  const [title, setTitle] = useState('')
  const [subjectId, setSubjectId] = useState('')
  const [format, setFormat] = useState<AssignmentFormat>('written')
  const [description, setDescription] = useState('')
  const [materials, setMaterials] = useState<MaterialInput[]>([])
  const [classIds, setClassIds] = useState<string[]>([])
  const [startDate, setStartDate] = useState('')
  const [dueDate, setDueDate] = useState('')
  const [lateAccept, setLateAccept] = useState(false)
  const [latePenaltyPct, setLatePenaltyPct] = useState(0)
  const [maxScore, setMaxScore] = useState(100)
  const [autoGrade, setAutoGrade] = useState(true)
  const [questions, setQuestions] = useState<QForm[]>([])
  const [uploading, setUploading] = useState(false)
  const [saving, setSaving] = useState(false)
  const fileRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    if (!open) return
    /* eslint-disable react-hooks/set-state-in-effect -- modal ochilganda formani initial bilan to'ldirish (maqsadli) */
    setStep(0)
    setTitle(initial?.title ?? '')
    setSubjectId(initial?.subjectId ?? subjects[0]?.id ?? '')
    setFormat(initial?.format ?? 'written')
    setDescription(initial?.description ?? '')
    setMaterials(initial ? initial.materials.map((m) => ({ name: m.name, url: m.url, size: m.size, contentType: m.contentType })) : [])
    setClassIds(initial ? [...initial.classIds] : [])
    setStartDate(initial?.startDate?.slice(0, 16) ?? '')
    setDueDate(initial?.dueDate?.slice(0, 16) ?? '')
    setLateAccept(initial?.lateAccept ?? false)
    setLatePenaltyPct(initial?.latePenaltyPct ?? 0)
    setMaxScore(initial?.maxScore ?? 100)
    setAutoGrade(initial?.autoGrade ?? true)
    setQuestions(
      initial && initial.format === 'test'
        ? initial.questions.map((q) => ({ text: q.text, options: [...q.options], correctIndex: q.correctIndex }))
        : [],
    )
    /* eslint-enable react-hooks/set-state-in-effect */
  }, [open, initial, subjects])

  const toggleClass = (id: string) =>
    setClassIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]))

  const onPickFiles = async (files: FileList | null) => {
    if (!files || files.length === 0) return
    setUploading(true)
    try {
      for (const file of Array.from(files)) {
        const m = await onUpload(file)
        setMaterials((prev) => [...prev, m])
      }
    } finally {
      setUploading(false)
      if (fileRef.current) fileRef.current.value = ''
    }
  }

  // Savollar
  const addQuestion = () =>
    setQuestions((prev) => [...prev, { text: '', options: ['', ''], correctIndex: 0 }])
  const removeQuestion = (i: number) => setQuestions((prev) => prev.filter((_, idx) => idx !== i))
  const setQText = (i: number, text: string) =>
    setQuestions((prev) => prev.map((q, idx) => (idx === i ? { ...q, text } : q)))
  const addOption = (i: number) =>
    setQuestions((prev) => prev.map((q, idx) => (idx === i ? { ...q, options: [...q.options, ''] } : q)))
  const setOption = (i: number, j: number, val: string) =>
    setQuestions((prev) =>
      prev.map((q, idx) => (idx === i ? { ...q, options: q.options.map((o, k) => (k === j ? val : o)) } : q)),
    )
  const removeOption = (i: number, j: number) =>
    setQuestions((prev) =>
      prev.map((q, idx) =>
        idx === i
          ? {
              ...q,
              options: q.options.filter((_, k) => k !== j),
              correctIndex: q.correctIndex >= j && q.correctIndex > 0 ? q.correctIndex - 1 : q.correctIndex,
            }
          : q,
      ),
    )
  const setCorrect = (i: number, j: number) =>
    setQuestions((prev) => prev.map((q, idx) => (idx === i ? { ...q, correctIndex: j } : q)))

  const step0Valid = title.trim() && subjectId && format
  const canSave = step0Valid && classIds.length > 0

  const handleSave = async () => {
    if (!canSave || saving) return
    setSaving(true)
    const input: SaveAssignmentInput = {
      subjectId,
      title: title.trim(),
      description: description.trim(),
      format,
      classIds,
      startDate: startDate || null,
      dueDate: dueDate || null,
      lateAccept,
      latePenaltyPct,
      maxScore,
      autoGrade,
      materials,
      questions:
        format === 'test'
          ? questions
              .filter((q) => q.text.trim())
              .map((q) => ({ text: q.text.trim(), options: q.options, correctIndex: q.correctIndex }))
          : [],
    }
    try {
      await onSubmit(input, initial?.id ?? null)
      onSaved()
      onClose()
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="lg"
      title={initial ? 'Topshiriqni tahrirlash' : 'Yangi topshiriq'}
      footer={
        <>
          {step > 0 && (
            <Button variant="secondary" className="mr-auto" onClick={() => setStep((s) => s - 1)}>
              Orqaga
            </Button>
          )}
          {step < steps.length - 1 ? (
            <Button onClick={() => setStep((s) => s + 1)} disabled={step === 0 && !step0Valid}>
              Keyingi
            </Button>
          ) : (
            <Button onClick={handleSave} disabled={!canSave || saving}>
              {saving ? 'Saqlanmoqda...' : 'Saqlash'}
            </Button>
          )}
        </>
      }
    >
      {/* Qadamlar indikatori */}
      <div className="mb-5 flex flex-wrap gap-2">
        {steps.map((s, i) => (
          <button
            key={s}
            type="button"
            onClick={() => (i === 0 || step0Valid ? setStep(i) : undefined)}
            className={cn(
              'flex items-center gap-1.5 rounded-full px-3 py-1 text-xs font-medium transition-colors',
              i === step ? 'bg-brand-600 text-white' : 'bg-slate-100 text-slate-500 hover:bg-slate-200',
            )}
          >
            <span
              className={cn(
                'flex h-4 w-4 items-center justify-center rounded-full text-[10px]',
                i === step ? 'bg-white/25' : 'bg-white',
              )}
            >
              {i + 1}
            </span>
            {s}
          </button>
        ))}
      </div>

      {/* Qadam 1: Asosiy */}
      {step === 0 && (
        <div className="space-y-4">
          <Input
            label="Topshiriq nomi"
            required
            placeholder="Masalan: Kvadrat tenglamalar — masalalar to'plami"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
          />
          <Select label="Fan" value={subjectId} onChange={(e) => setSubjectId(e.target.value)}>
            {subjects.length === 0 && <option value="">Fan yo'q</option>}
            {subjects.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </Select>
          <div>
            <span className="mb-2 block text-sm font-medium text-slate-600">Topshiriq turi</span>
            <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
              {formats.map((f) => {
                const active = format === f.key
                return (
                  <button
                    key={f.key}
                    type="button"
                    onClick={() => setFormat(f.key)}
                    className={cn(
                      'flex flex-col items-center gap-1 rounded-xl border p-3 text-center transition-colors',
                      active
                        ? 'border-brand-500 bg-brand-50 text-brand-700'
                        : 'border-slate-200 text-slate-600 hover:bg-slate-50',
                    )}
                  >
                    <f.icon className="h-5 w-5" />
                    <span className="text-sm font-medium">{f.label}</span>
                    <span className="text-[11px] text-slate-400">{f.desc}</span>
                  </button>
                )
              })}
            </div>
          </div>
        </div>
      )}

      {/* Qadam 2: Mazmun va materiallar */}
      {step === 1 && (
        <div className="space-y-4">
          <Textarea
            label="Topshiriq tavsifi"
            rows={5}
            placeholder="Topshiriq mazmuni, ko'rsatmalar..."
            value={description}
            onChange={(e) => setDescription(e.target.value)}
          />

          <div>
            <div className="mb-2 flex items-center justify-between">
              <span className="text-sm font-medium text-slate-600">Materiallar biriktirish</span>
              <Button
                type="button"
                variant="secondary"
                onClick={() => fileRef.current?.click()}
                disabled={uploading}
              >
                <Upload className="h-4 w-4" /> {uploading ? 'Yuklanmoqda...' : "Qo'shish"}
              </Button>
              <input
                ref={fileRef}
                type="file"
                multiple
                className="hidden"
                onChange={(e) => onPickFiles(e.target.files)}
              />
            </div>
            <div className="space-y-2">
              {materials.length === 0 && (
                <p className="text-xs text-slate-400">Hali material yo'q (PDF, rasm, doc — maks 20 MB)</p>
              )}
              {materials.map((m, i) => (
                <div
                  key={i}
                  className="flex items-center justify-between gap-2 rounded-lg border border-slate-100 px-3 py-2 text-sm"
                >
                  <a href={m.url} target="_blank" rel="noreferrer" className="truncate text-brand-700 hover:underline">
                    {m.name}
                  </a>
                  <div className="flex shrink-0 items-center gap-2">
                    <span className="text-xs text-slate-400">{formatSize(m.size)}</span>
                    <button
                      type="button"
                      onClick={() => setMaterials((prev) => prev.filter((_, idx) => idx !== i))}
                      className="rounded p-1 text-slate-400 hover:bg-red-50 hover:text-red-600"
                    >
                      <Trash2 className="h-3.5 w-3.5" />
                    </button>
                  </div>
                </div>
              ))}
            </div>
          </div>

          {/* Test savollari */}
          {format === 'test' && (
            <div className="border-t border-slate-100 pt-4">
              <div className="mb-2 flex items-center justify-between">
                <span className="text-sm font-medium text-slate-600">Test savollari</span>
                <Button type="button" variant="secondary" onClick={addQuestion}>
                  <Plus className="h-4 w-4" /> Savol
                </Button>
              </div>
              <div className="space-y-3">
                {questions.length === 0 && (
                  <p className="text-xs text-slate-400">Savol qo'shing (to'g'ri javobni belgilang)</p>
                )}
                {questions.map((q, i) => (
                  <div key={i} className="rounded-xl border border-slate-100 p-3">
                    <div className="mb-2 flex items-start gap-2">
                      <span className="mt-2 text-xs font-semibold text-slate-400">{i + 1}.</span>
                      <input
                        value={q.text}
                        onChange={(e) => setQText(i, e.target.value)}
                        placeholder="Savol matni"
                        className={`${control} flex-1`}
                      />
                      <button
                        type="button"
                        onClick={() => removeQuestion(i)}
                        className="mt-1 rounded p-1.5 text-slate-400 hover:bg-red-50 hover:text-red-600"
                      >
                        <Trash2 className="h-4 w-4" />
                      </button>
                    </div>
                    <div className="space-y-1.5 pl-5">
                      {q.options.map((opt, j) => (
                        <div key={j} className="flex items-center gap-2">
                          <button
                            type="button"
                            title="To'g'ri javob"
                            onClick={() => setCorrect(i, j)}
                            className={cn(
                              'flex h-5 w-5 shrink-0 items-center justify-center rounded-full border',
                              q.correctIndex === j
                                ? 'border-emerald-500 bg-emerald-500 text-white'
                                : 'border-slate-300 text-transparent',
                            )}
                          >
                            <Check className="h-3 w-3" />
                          </button>
                          <input
                            value={opt}
                            onChange={(e) => setOption(i, j, e.target.value)}
                            placeholder={`Variant ${j + 1}`}
                            className={`${control} flex-1`}
                          />
                          {q.options.length > 2 && (
                            <button
                              type="button"
                              onClick={() => removeOption(i, j)}
                              className="rounded p-1 text-slate-300 hover:text-red-600"
                            >
                              <Trash2 className="h-3.5 w-3.5" />
                            </button>
                          )}
                        </div>
                      ))}
                      <button
                        type="button"
                        onClick={() => addOption(i)}
                        className="text-xs font-medium text-brand-600 hover:text-brand-700"
                      >
                        + Variant
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      )}

      {/* Qadam 3: Sinflar va muddat */}
      {step === 2 && (
        <div className="space-y-4">
          <div>
            <span className="mb-2 block text-sm font-medium text-slate-600">Sinflarni tanlash</span>
            <div className="flex flex-wrap gap-2">
              {classes.length === 0 && <p className="text-sm text-slate-400">Sinf yo'q</p>}
              {classes.map((c) => {
                const active = classIds.includes(c.id)
                return (
                  <button
                    key={c.id}
                    type="button"
                    onClick={() => toggleClass(c.id)}
                    className={cn(
                      'rounded-full border px-3 py-1.5 text-sm transition-colors',
                      active
                        ? 'border-brand-500 bg-brand-50 text-brand-700'
                        : 'border-slate-200 text-slate-600 hover:bg-slate-50',
                    )}
                  >
                    {c.name}
                  </button>
                )
              })}
            </div>
          </div>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <label className="block">
              <span className="mb-1 block text-sm font-medium text-slate-600">Boshlash</span>
              <input
                type="datetime-local"
                value={startDate}
                onChange={(e) => setStartDate(e.target.value)}
                className={`${control} w-full`}
              />
            </label>
            <label className="block">
              <span className="mb-1 block text-sm font-medium text-slate-600">Tugash (muddat)</span>
              <input
                type="datetime-local"
                value={dueDate}
                onChange={(e) => setDueDate(e.target.value)}
                className={`${control} w-full`}
              />
            </label>
          </div>
          <label className="flex items-center gap-2 text-sm text-slate-600">
            <input type="checkbox" checked={lateAccept} onChange={(e) => setLateAccept(e.target.checked)} />
            Muddatdan keyin ham qabul qilish
          </label>
          {lateAccept && (
            <label className="block max-w-xs">
              <span className="mb-1 block text-sm font-medium text-slate-600">Kechikish jarimasi (% / kun)</span>
              <input
                type="number"
                min={0}
                max={100}
                value={latePenaltyPct}
                onChange={(e) => setLatePenaltyPct(Number(e.target.value))}
                className={`${control} w-full`}
              />
            </label>
          )}
        </div>
      )}

      {/* Qadam 4: Baholash */}
      {step === 3 && (
        <div className="space-y-4">
          <label className="block max-w-xs">
            <span className="mb-1 block text-sm font-medium text-slate-600">Maksimal ball</span>
            <input
              type="number"
              min={1}
              value={maxScore}
              onChange={(e) => setMaxScore(Number(e.target.value))}
              className={`${control} w-full`}
            />
          </label>
          {format === 'test' && (
            <label className="flex items-center gap-2 text-sm text-slate-600">
              <input type="checkbox" checked={autoGrade} onChange={(e) => setAutoGrade(e.target.checked)} />
              Avto-baholash (test javoblari avtomatik tekshiriladi)
            </label>
          )}
          {!canSave && (
            <p className="text-sm text-amber-600">
              Saqlash uchun: nom, fan va kamida bitta sinf tanlangan bo'lishi kerak.
            </p>
          )}
        </div>
      )}
    </Modal>
  )
}
