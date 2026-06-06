import { useState, useEffect, useMemo, useRef } from 'react'
import { X, CheckCircle, Edit3, Paperclip, Video, Check, ArrowRight, Plus, Trash2, Loader2 } from 'lucide-react'
import { FieldLabel } from '../components/ui'
import AppButton from '../components/AppButton'
import { Loading, ErrorState } from '../components/State'
import { useFetch } from '../lib/session'
import { api } from '../lib/api'

const STEPS = ['Asosiy', 'Sinflar', 'Sozlamalar', 'Savollar']
const FORMATS = [
  ['test', CheckCircle, 'Test', 'Avto-baholash', '#0D9488'],
  ['written', Edit3, 'Yozma', 'Matn javob', '#0EA5E9'],
  ['file', Paperclip, 'Fayl', 'Fayl yuklash', '#7C3AED'],
  ['video', Video, 'Video', 'Video link', '#DB2777'],
]

const emptyQuestion = () => ({ text: '', options: ['', '', '', ''], correctIndex: 0 })

// Assignment create/edit — 4-step wizard. Live API.
export default function AssignmentCreateScreen({ params, onBack }) {
  const editId = params?.id || null
  const classesQ = useFetch(() => api.classes(), [])
  // Edit: load assignment list once and find the one to edit.
  const editQ = useFetch(() => (editId ? api.assignments() : Promise.resolve(null)), [editId])

  const [step, setStep] = useState(0)
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [format, setFormat] = useState('test')
  const [classIds, setClassIds] = useState([])
  const [subjectId, setSubjectId] = useState('')
  const [maxScore, setMaxScore] = useState(10)
  const [lateAccept, setLateAccept] = useState(false)
  const [latePenaltyPct, setLatePenaltyPct] = useState(0)
  const [autoGrade, setAutoGrade] = useState(true)
  const [startDate, setStartDate] = useState('')
  const [dueDate, setDueDate] = useState('')
  const [materials, setMaterials] = useState([])
  const [questions, setQuestions] = useState([emptyQuestion()])

  const [uploading, setUploading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState(null)
  const prefilled = useRef(false)

  const classes = classesQ.data || []
  const subjects = useMemo(
    () => [...new Map(classes.flatMap((c) => c.subjects || []).map((s) => [s.id, s])).values()],
    [classes],
  )

  // Default subject to first available once classes load (create mode).
  useEffect(() => {
    if (!editId && !subjectId && subjects.length) setSubjectId(subjects[0].id)
  }, [subjects, subjectId, editId])

  // Prefill from existing assignment (edit mode).
  useEffect(() => {
    if (editId && !prefilled.current && editQ.data) {
      const a = (editQ.data || []).find((x) => x.id === editId)
      if (a) {
        prefilled.current = true
        setTitle(a.title || '')
        setDescription(a.description || '')
        setFormat(a.format || 'test')
        setClassIds(a.classIds || [])
        setSubjectId(a.subjectId || '')
        setMaxScore(a.maxScore ?? 10)
        setLateAccept(!!a.lateAccept)
        setLatePenaltyPct(a.latePenaltyPct ?? 0)
        setAutoGrade(a.autoGrade ?? true)
        setStartDate((a.startDate || '').split('T')[0])
        setDueDate((a.dueDate || '').split('T')[0])
        setMaterials(
          (a.materials || []).map((m) => ({ name: m.name, url: m.url, size: m.size, contentType: m.contentType })),
        )
        if (a.questions && a.questions.length) {
          setQuestions(a.questions.map((qq) => ({ text: qq.text, options: qq.options, correctIndex: qq.correctIndex })))
        }
      }
    }
  }, [editId, editQ.data])

  const toggleClass = (id) => setClassIds((p) => (p.includes(id) ? p.filter((x) => x !== id) : [...p, id]))

  const onPickFile = async (e) => {
    const file = e.target.files?.[0]
    e.target.value = ''
    if (!file) return
    setUploading(true)
    setError(null)
    try {
      const up = await api.upload(file)
      setMaterials((p) => [...p, { name: up.name, url: up.url, size: up.size, contentType: up.contentType }])
    } catch (err) {
      setError(err)
    } finally {
      setUploading(false)
    }
  }

  const submit = async () => {
    if (!title.trim()) {
      setError(new Error('Sarlavha kiriting'))
      setStep(0)
      return
    }
    if (classIds.length === 0) {
      setError(new Error('Kamida bitta sinf tanlang'))
      setStep(1)
      return
    }
    if (!subjectId) {
      setError(new Error('Fan tanlang'))
      setStep(1)
      return
    }
    const body = {
      subjectId,
      title: title.trim(),
      description: description.trim(),
      format,
      classIds,
      startDate: startDate || null,
      dueDate: dueDate || null,
      lateAccept,
      latePenaltyPct: lateAccept ? Number(latePenaltyPct) || 0 : 0,
      maxScore: Number(maxScore) || 0,
      autoGrade,
      materials,
      questions:
        format === 'test'
          ? questions
              .filter((qq) => qq.text.trim())
              .map((qq) => ({ text: qq.text.trim(), options: qq.options, correctIndex: qq.correctIndex }))
          : [],
    }
    setSaving(true)
    setError(null)
    try {
      if (editId) await api.updateAssignment(editId, body)
      else await api.createAssignment(body)
      onBack?.()
    } catch (err) {
      setError(err)
    } finally {
      setSaving(false)
    }
  }

  if (classesQ.loading && !classesQ.data) return <Loading label="Sinflar yuklanmoqda…" />
  if (classesQ.error) return <ErrorState error={classesQ.error} onRetry={classesQ.reload} />

  return (
    <div className="h-full flex flex-col bg-bg">
      <div className="flex items-center gap-2 px-3 pt-2 pb-1">
        <button onClick={onBack} className="w-10 h-10 rounded-xl bg-surface2 flex items-center justify-center text-text">
          <X size={20} />
        </button>
        <div className="flex-1">
          <p className="text-[17px] font-extrabold text-text">{editId ? 'Topshiriqni tahrirlash' : 'Yangi topshiriq'}</p>
          <p className="text-[11px] text-muted">{step + 1}-qadam · {STEPS[step]}</p>
        </div>
        <button onClick={submit} className="text-[13px] font-semibold text-primary px-2">Saqlash</button>
      </div>

      {/* Stepper */}
      <div className="flex gap-1.5 px-4 py-3">
        {[0, 1, 2, 3].map((i) => (
          <div key={i} className="flex-1 h-1 rounded transition-colors duration-300" style={{ background: i <= step ? 'var(--primary)' : 'var(--surface3)' }} />
        ))}
      </div>

      {error && (
        <div className="mx-4 mb-2 px-3 py-2 rounded-xl bg-danger/10 text-danger text-[12px] font-semibold">
          {error.message || 'Xatolik yuz berdi'}
        </div>
      )}

      <div className="flex-1 overflow-y-auto no-scrollbar px-4 pt-2 pb-6">
        {step === 0 && (
          <div className="space-y-4">
            <Labeled label="Sarlavha">
              <Input value={title} onChange={setTitle} placeholder="Misol: Kvadrat tenglamalar — test" />
            </Labeled>
            <Labeled label="Tavsif (ixtiyoriy)">
              <Textarea value={description} onChange={setDescription} placeholder="Topshiriq haqida qisqacha..." />
            </Labeled>
            <Labeled label="Format">
              <div className="grid grid-cols-2 gap-2">
                {FORMATS.map(([val, Icon, t, sub, color]) => {
                  const on = format === val
                  return (
                    <button
                      key={val}
                      onClick={() => setFormat(val)}
                      className="p-3.5 rounded-xl border flex items-center gap-2.5 text-left transition-all"
                      style={{ background: on ? `${color}14` : 'var(--surface)', borderColor: on ? color : 'var(--border)', borderWidth: on ? 1.5 : 1 }}
                    >
                      <span className="w-9 h-9 rounded-[10px] flex items-center justify-center" style={{ background: on ? color : 'var(--surface2)', color: on ? '#fff' : color }}>
                        <Icon size={18} />
                      </span>
                      <span>
                        <span className="block text-[14px] font-bold text-text">{t}</span>
                        <span className="block text-[11px] text-muted">{sub}</span>
                      </span>
                    </button>
                  )
                })}
              </div>
            </Labeled>
          </div>
        )}

        {step === 1 && (
          <div className="space-y-4">
            <Labeled label="Fan">
              <div className="space-y-2">
                {subjects.length === 0 && <p className="text-[13px] text-muted">Fan topilmadi.</p>}
                {subjects.map((s) => {
                  const on = subjectId === s.id
                  return (
                    <button
                      key={s.id}
                      onClick={() => setSubjectId(s.id)}
                      className="w-full p-3.5 rounded-xl border flex items-center gap-2.5"
                      style={{ background: on ? 'var(--primary-soft)' : 'var(--surface)', borderColor: on ? 'var(--primary)' : 'var(--border)', borderWidth: on ? 1.5 : 1 }}
                    >
                      <span className="flex-1 text-left text-[14px] font-bold" style={{ color: on ? 'var(--primary)' : 'var(--text)' }}>{s.name}</span>
                      {on && <Check size={18} className="text-primary" />}
                    </button>
                  )
                })}
              </div>
            </Labeled>
            <Labeled label="Sinflar (bir nechta tanlash mumkin)">
              <div className="flex flex-wrap gap-2">
                {classes.map((c) => {
                  const on = classIds.includes(c.classId)
                  return (
                    <button
                      key={c.classId}
                      onClick={() => toggleClass(c.classId)}
                      className="px-3.5 py-2.5 rounded-xl border flex items-center gap-1.5 text-[14px] font-bold"
                      style={{ background: on ? '#0D948814' : 'var(--surface)', borderColor: on ? '#0D9488' : 'var(--border)', borderWidth: on ? 1.5 : 1, color: on ? '#0D9488' : 'var(--text)' }}
                    >
                      {c.className}
                      {on && <Check size={14} />}
                    </button>
                  )
                })}
              </div>
            </Labeled>
          </div>
        )}

        {step === 2 && (
          <div className="space-y-3.5">
            <div className="grid grid-cols-2 gap-2.5">
              <Labeled label="Boshlanish">
                <DateInput value={startDate} onChange={setStartDate} />
              </Labeled>
              <Labeled label="Muddat">
                <DateInput value={dueDate} onChange={setDueDate} />
              </Labeled>
            </div>
            <div className="p-4 rounded-4xl bg-surface border border-border">
              <FieldLabel>Maksimal ball</FieldLabel>
              <div className="mt-3 flex items-center">
                <Stepper onClick={() => setMaxScore((v) => Math.max(1, v - 5))}>−</Stepper>
                <span className="flex-1 text-center text-[32px] font-extrabold text-primary font-mono">{maxScore}</span>
                <Stepper onClick={() => setMaxScore((v) => Math.min(100, v + 5))}>+</Stepper>
              </div>
            </div>
            <ToggleRow title="Kechikish qabul qilish" sub="Muddatdan keyin topshirish mumkinmi" value={lateAccept} onChange={setLateAccept} />
            {lateAccept && (
              <div className="p-4 rounded-4xl bg-surface border border-border">
                <FieldLabel>Kechikish jarimasi (%)</FieldLabel>
                <div className="mt-2">
                  <NumberInput value={latePenaltyPct} onChange={setLatePenaltyPct} placeholder="0" />
                </div>
              </div>
            )}
            {format === 'test' && (
              <ToggleRow title="Avto-baholash" sub="Test natijasini avtomatik hisoblash" value={autoGrade} onChange={setAutoGrade} />
            )}

            {/* Materials */}
            <div className="p-4 rounded-4xl bg-surface border border-border">
              <FieldLabel>Materiallar (fayllar)</FieldLabel>
              <div className="mt-2.5 space-y-2">
                {materials.map((m, i) => (
                  <div key={`${m.url}-${i}`} className="flex items-center gap-2 p-2.5 rounded-xl bg-surface2">
                    <Paperclip size={16} className="text-muted shrink-0" />
                    <span className="flex-1 text-[13px] font-semibold text-text truncate">{m.name}</span>
                    <button onClick={() => setMaterials((p) => p.filter((_, j) => j !== i))} className="text-danger">
                      <Trash2 size={15} />
                    </button>
                  </div>
                ))}
              </div>
              <label className="mt-2.5 w-full h-11 rounded-xl border border-border flex items-center justify-center gap-2 text-text font-semibold cursor-pointer">
                {uploading ? <Loader2 size={16} className="animate-spin" /> : <Plus size={16} />}
                {uploading ? 'Yuklanmoqda…' : 'Fayl qo\'shish'}
                <input type="file" className="hidden" onChange={onPickFile} disabled={uploading} />
              </label>
            </div>
          </div>
        )}

        {step === 3 &&
          (format === 'test' ? (
            <QuestionEditor questions={questions} setQuestions={setQuestions} />
          ) : (
            <div className="p-5 rounded-4xl bg-surface border border-border">
              <p className="text-[18px] font-extrabold text-text">{title || '(Sarlavhasiz)'}</p>
              <p className="mt-1.5 text-[13px] text-muted">{description || 'Tavsif kiritilmagan'}</p>
              <div className="mt-3.5 flex flex-wrap gap-1.5">
                {[format, `${maxScore} ball`].map((c) => (
                  <span key={c} className="px-2.5 py-1 rounded-lg bg-chip text-[12px] font-semibold text-text">{c}</span>
                ))}
              </div>
              <div className="my-4 h-px bg-border" />
              <p className="text-[12px] text-muted">Sinflar: {classIds.length} ta · Materiallar: {materials.length} ta</p>
            </div>
          ))}
      </div>

      {/* Bottom nav buttons */}
      <div className="px-4 py-3 border-t border-border/50 flex gap-2.5">
        {step > 0 && (
          <div className="flex-1">
            <AppButton label="Orqaga" style="ghost" expand onClick={() => setStep((s) => s - 1)} />
          </div>
        )}
        <div className="flex-[2]">
          <AppButton
            label={step < 3 ? 'Davom etish' : editId ? 'Saqlash' : 'Yaratish'}
            expand
            loading={saving}
            onClick={() => (step < 3 ? setStep((s) => s + 1) : submit())}
            trailingIcon={step < 3 ? <ArrowRight size={16} /> : null}
          />
        </div>
      </div>
    </div>
  )
}

function Labeled({ label, children }) {
  return (
    <div>
      <FieldLabel>{label}</FieldLabel>
      <div className="mt-2">{children}</div>
    </div>
  )
}
function Input({ value, onChange, placeholder }) {
  return <input value={value} onChange={(e) => onChange(e.target.value)} placeholder={placeholder} className="w-full px-4 h-12 rounded-xl bg-surface2 border border-border outline-none text-[15px] text-text placeholder:text-faint focus:border-primary" />
}
function Textarea({ value, onChange, placeholder }) {
  return <textarea value={value} onChange={(e) => onChange(e.target.value)} placeholder={placeholder} rows={4} className="w-full p-4 rounded-xl bg-surface2 border border-border outline-none text-[14px] text-text placeholder:text-faint focus:border-primary resize-none" />
}
function DateInput({ value, onChange }) {
  return <input type="date" value={value || ''} onChange={(e) => onChange(e.target.value)} className="w-full px-3 h-12 rounded-xl bg-surface2 border border-border outline-none text-[14px] text-text focus:border-primary" />
}
function NumberInput({ value, onChange, placeholder }) {
  return <input type="number" min={0} max={100} value={value} onChange={(e) => onChange(e.target.value)} placeholder={placeholder} className="w-full px-4 h-11 rounded-xl bg-surface2 border border-border outline-none text-[15px] text-text placeholder:text-faint focus:border-primary" />
}
function Stepper({ children, onClick }) {
  return (
    <button onClick={onClick} className="w-9 h-9 rounded-xl bg-surface2 flex items-center justify-center text-[18px] font-bold text-text">{children}</button>
  )
}
function ToggleRow({ title, sub, value, onChange }) {
  return (
    <div className="p-4 rounded-4xl bg-surface border border-border flex items-center">
      <div className="flex-1">
        <p className="text-[14px] font-bold text-text">{title}</p>
        <p className="text-[12px] text-muted">{sub}</p>
      </div>
      <button
        onClick={() => onChange(!value)}
        className="w-12 h-7 rounded-full p-0.5 transition-colors"
        style={{ background: value ? 'var(--primary)' : 'var(--surface3)' }}
      >
        <span className="block w-6 h-6 rounded-full bg-white transition-transform" style={{ transform: value ? 'translateX(20px)' : 'none' }} />
      </button>
    </div>
  )
}
function QuestionEditor({ questions, setQuestions }) {
  const update = (i, patch) => setQuestions((p) => p.map((q, j) => (j === i ? { ...q, ...patch } : q)))
  const updateOption = (i, oi, val) =>
    setQuestions((p) => p.map((q, j) => (j === i ? { ...q, options: q.options.map((o, k) => (k === oi ? val : o)) } : q)))
  return (
    <div className="space-y-3">
      {questions.map((q, i) => (
        <div key={i} className="p-4 rounded-4xl bg-surface border border-border">
          <div className="flex items-center gap-2.5">
            <span className="w-7 h-7 rounded-lg bg-primary text-white text-[13px] font-bold font-mono flex items-center justify-center">{i + 1}</span>
            <span className="flex-1 text-[12px] font-semibold text-muted">Savol</span>
            {questions.length > 1 && (
              <button onClick={() => setQuestions((p) => p.filter((_, j) => j !== i))} className="text-danger">
                <Trash2 size={15} />
              </button>
            )}
          </div>
          <input
            value={q.text}
            onChange={(e) => update(i, { text: e.target.value })}
            placeholder="Savol matni..."
            className="mt-3 w-full px-4 h-11 rounded-xl bg-surface2 border border-border outline-none text-[14px] text-text placeholder:text-faint"
          />
          <div className="mt-3 space-y-2">
            {q.options.map((o, j) => {
              const correct = q.correctIndex === j
              return (
                <div key={j} className="flex items-center gap-2">
                  <button
                    type="button"
                    onClick={() => update(i, { correctIndex: j })}
                    className="w-6 h-6 rounded-full border-2 flex items-center justify-center shrink-0"
                    style={{ borderColor: correct ? '#10B981' : 'var(--border)', background: correct ? '#10B981' : 'transparent' }}
                  >
                    {correct && <Check size={12} className="text-white" />}
                  </button>
                  <input
                    value={o}
                    onChange={(e) => updateOption(i, j, e.target.value)}
                    placeholder={`Variant ${j + 1}`}
                    className="flex-1 px-3 h-10 rounded-xl bg-surface2 border border-border outline-none text-[14px] text-text placeholder:text-faint"
                  />
                </div>
              )
            })}
          </div>
        </div>
      ))}
      <button
        onClick={() => setQuestions((p) => [...p, emptyQuestion()])}
        className="w-full h-12 rounded-xl border border-border flex items-center justify-center gap-2 text-text font-semibold"
      >
        <Plus size={18} /> Savol qo'shish
      </button>
    </div>
  )
}
