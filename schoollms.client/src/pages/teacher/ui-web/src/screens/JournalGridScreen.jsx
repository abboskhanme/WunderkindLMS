import { useMemo, useState, useRef } from 'react'
import { Plus, Check, ClipboardList, BookOpen, AlertCircle, Download, Upload } from 'lucide-react'
import { ScreenHeader } from '../components/ui'
import SegmentedControl from '../components/SegmentedControl'
import Avatar from '../components/Avatar'
import AppSheet from '../components/AppSheet'
import AppButton from '../components/AppButton'
import { Loading, ErrorState } from '../components/State'
import EmptyState, { EmptyIllustration } from '../components/EmptyState'
import { gradeColor } from '../lib/colors'
import { useFetch } from '../lib/session'
import { api } from '../lib/api'

const ROW_H = 56
const COL_W = 50
const NAME_W = 150

const cellKey = (studentId, date, period) => `${studentId}|${date}|${period}`

// Journal grid — frozen name column + horizontally scrolling grade cells.
// Tabs: Baholar (grid), Mavzu (topics), Chorak (quarter grades).
export default function JournalGridScreen({ params, onBack }) {
  const { classId, className, subjectId, subjectName } = params || {}

  const metaQ = useFetch(() => api.meta(), [])
  const quarter = metaQ.data?.currentQuarter

  if (!classId || !subjectId) {
    return (
      <div className="h-full flex flex-col bg-bg">
        <ScreenHeader title="Jurnal" onBack={onBack} titleSize={16} />
        <EmptyState
          icon={<EmptyIllustration><BookOpen size={30} /></EmptyIllustration>}
          title="Sinf tanlanmagan"
          subtitle="Avval sinf va fanni tanlang."
        />
      </div>
    )
  }

  if (metaQ.loading && !metaQ.data) {
    return (
      <div className="h-full flex flex-col bg-bg">
        <ScreenHeader title={`${className} · ${subjectName}`} onBack={onBack} titleSize={16} />
        <Loading />
      </div>
    )
  }
  if (metaQ.error) {
    return (
      <div className="h-full flex flex-col bg-bg">
        <ScreenHeader title={`${className} · ${subjectName}`} onBack={onBack} titleSize={16} />
        <ErrorState error={metaQ.error} onRetry={metaQ.reload} />
      </div>
    )
  }

  return (
    <JournalGridBody
      classId={classId}
      className={className}
      subjectId={subjectId}
      subjectName={subjectName}
      quarter={quarter}
      meta={metaQ.data}
      onBack={onBack}
    />
  )
}

function JournalGridBody({ classId, className, subjectId, subjectName, quarter, meta, onBack }) {
  const [tab, setTab] = useState('grades')
  const [sheetCell, setSheetCell] = useState(null) // { student, col }

  const studentsQ = useFetch(() => api.journalStudents(classId), [classId])
  const columnsQ = useFetch(() => api.journalColumns(classId, subjectId, quarter), [classId, subjectId, quarter])
  const entriesQ = useFetch(() => api.journalEntries(classId, subjectId, quarter), [classId, subjectId, quarter])

  const students = studentsQ.data || []
  const columns = columnsQ.data || []
  const entries = entriesQ.data || []

  const entryMap = useMemo(() => {
    const m = {}
    for (const e of entries) m[cellKey(e.studentId, e.date, e.period)] = e
    return m
  }, [entries])

  const loading = (studentsQ.loading && !studentsQ.data) || (columnsQ.loading && !columnsQ.data) || (entriesQ.loading && !entriesQ.data)
  const error = studentsQ.error || columnsQ.error || entriesQ.error
  const retry = () => { studentsQ.reload(); columnsQ.reload(); entriesQ.reload() }

  return (
    <div className="h-full flex flex-col bg-bg">
      <ScreenHeader
        title={`${className} · ${subjectName}`}
        subtitle={`${quarter}-chorak · ${students.length} o'quvchi`}
        onBack={onBack}
        titleSize={16}
      />
      <div className="px-4 pt-1 pb-2">
        <SegmentedControl
          value={tab}
          onChange={setTab}
          options={[
            { value: 'grades', label: 'Baholar' },
            { value: 'topics', label: 'Mavzu' },
            { value: 'quarter', label: 'Chorak' },
          ]}
        />
      </div>

      <div className="flex-1 overflow-hidden">
        {loading ? (
          <Loading />
        ) : error ? (
          <ErrorState error={error} onRetry={retry} />
        ) : (
          <>
            {tab === 'grades' && (
              <GradeGrid
                students={students}
                columns={columns}
                entryMap={entryMap}
                onCell={(student, col) => setSheetCell({ student, col })}
              />
            )}
            {tab === 'topics' && (
              <TopicsTab classId={classId} subjectId={subjectId} quarter={quarter} columns={columns} />
            )}
            {tab === 'quarter' && (
              <QuarterTab
                classId={classId}
                subjectId={subjectId}
                quarter={quarter}
                students={students}
              />
            )}
          </>
        )}
      </div>

      <GradeEntrySheet
        cell={sheetCell}
        existing={sheetCell ? entryMap[cellKey(sheetCell.student.id, sheetCell.col.date, sheetCell.col.period)] : null}
        reasons={meta?.absenceReasons || []}
        onClose={() => setSheetCell(null)}
        onSaved={() => { entriesQ.reload(); setSheetCell(null) }}
        ctx={{ classId, subjectId, quarter }}
      />
    </div>
  )
}

function GradeGrid({ students, columns, entryMap, onCell }) {
  if (students.length === 0) {
    return (
      <EmptyState
        icon={<EmptyIllustration><BookOpen size={30} /></EmptyIllustration>}
        title="O'quvchilar yo'q"
        subtitle="Bu sinfda o'quvchi topilmadi."
      />
    )
  }
  return (
    <div className="h-full overflow-auto no-scrollbar">
      <div className="flex" style={{ minHeight: '100%' }}>
        {/* Frozen name column */}
        <div className="sticky left-0 z-10" style={{ width: NAME_W }}>
          <div
            className="flex items-center px-3 bg-surface border-b border-r border-border text-[11px] font-bold text-muted tracking-wide"
            style={{ height: ROW_H }}
          >
            O'QUVCHI
          </div>
          {students.map((s, i) => (
            <div
              key={s.id}
              className="flex items-center px-3 border-b border-r"
              style={{ height: ROW_H, background: i % 2 ? 'var(--surface2)' : 'var(--surface)', borderColor: 'var(--border)' }}
            >
              <span className="w-5 text-[11px] font-bold text-faint font-mono">{i + 1}.</span>
              <div className="flex-1 min-w-0">
                <p className="text-[12px] font-semibold text-text leading-tight truncate">{s.fullName}</p>
                {s.subGroup > 0 && <p className="text-[9px] font-semibold text-faint">{s.subGroup}-guruh</p>}
              </div>
            </div>
          ))}
        </div>

        {/* Scrolling cells */}
        <div>
          <div className="flex" style={{ height: ROW_H }}>
            {columns.map((col) => {
              const [, m, d] = col.date.split('-')
              return (
                <div
                  key={cellKey('', col.date, col.period)}
                  className="flex flex-col items-center justify-center bg-surface border-b border-r border-border/40"
                  style={{ width: COL_W }}
                >
                  <span className="text-[12px] font-bold text-text font-mono">{d}.{m}</span>
                  <span className="text-[9px] text-faint">{col.subGroup > 0 ? `${col.subGroup}G` : ''}</span>
                </div>
              )
            })}
          </div>

          {students.map((s, i) => (
            <div key={s.id} className="flex" style={{ height: ROW_H, background: i % 2 ? 'var(--surface2)' : 'var(--surface)' }}>
              {columns.map((col) => {
                const entry = entryMap[cellKey(s.id, col.date, col.period)]
                const extra = entry && (entry.homework || entry.behavior || entry.mastery != null)
                return (
                  <button
                    key={cellKey(s.id, col.date, col.period)}
                    onClick={() => onCell(s, col)}
                    className="relative border-b border-r border-border/40 flex items-center justify-center"
                    style={{ width: COL_W, height: ROW_H }}
                  >
                    {entry?.grade != null && (
                      <span
                        className="w-[30px] h-[30px] rounded-lg flex items-center justify-center text-white text-[14px] font-bold font-mono"
                        style={{ background: gradeColor(entry.grade) }}
                      >
                        {entry.grade}
                      </span>
                    )}
                    {entry?.grade == null && entry?.reasonId && (
                      <span className="text-[13px] font-extrabold text-warning">N</span>
                    )}
                    {extra && <span className="absolute top-1 right-1 w-1.5 h-1.5 rounded-full bg-primary" />}
                  </button>
                )
              })}
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}

function TopicsTab({ classId, subjectId, quarter, columns }) {
  const notesQ = useFetch(() => api.journalNotes(classId, subjectId, quarter), [classId, subjectId, quarter])

  if (notesQ.loading && !notesQ.data) return <Loading />
  if (notesQ.error) return <ErrorState error={notesQ.error} onRetry={notesQ.reload} />

  const notes = notesQ.data || []
  const noteMap = {}
  for (const n of notes) noteMap[cellKey('', n.date, n.period)] = n

  // Mavzu list — har bir ustun (dars) uchun mavjud mavzu yoki bo'sh holatda qo'shish.
  const rows = columns.length
    ? columns.map((col) => ({ col, note: noteMap[cellKey('', col.date, col.period)] }))
    : notes.map((n) => ({ col: { date: n.date, period: n.period, subGroup: n.subGroup }, note: n }))

  return (
    <div className="h-full flex flex-col">
      <TopicsImportBar
        classId={classId}
        subjectId={subjectId}
        quarter={quarter}
        onImported={() => notesQ.reload()}
      />
      {rows.length === 0 ? (
        <EmptyState
          icon={<EmptyIllustration><ClipboardList size={30} /></EmptyIllustration>}
          title="Darslar yo'q"
          subtitle="Bu chorak uchun dars ustunlari topilmadi."
        />
      ) : (
        <TopicsList rows={rows} ctx={{ classId, subjectId, quarter }} onSaved={() => notesQ.reload()} />
      )}
    </div>
  )
}

// Mavzularni Excel'dan: shablon yuklab olish + to'ldirilganini yuklash (darsni o'tilgan QILMAYDI).
function TopicsImportBar({ classId, subjectId, quarter, onImported }) {
  const fileRef = useRef(null)
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState(null)

  const download = async () => {
    setMsg(null)
    try {
      const blob = await api.journalTopicsTemplate(classId, subjectId, quarter)
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = 'mavzular_shablon.xlsx'
      document.body.appendChild(a)
      a.click()
      a.remove()
      URL.revokeObjectURL(url)
    } catch {
      setMsg("Shablonni yuklab bo'lmadi")
    }
  }

  const onFile = async (e) => {
    const f = e.target.files?.[0]
    e.target.value = ''
    if (!f) return
    setBusy(true)
    setMsg(null)
    try {
      const r = await api.importTopics(f, classId, subjectId, quarter)
      setMsg(`${r.imported} ta to'ldirildi${r.errors ? `, ${r.errors} ta xato` : ''}`)
      onImported()
    } catch (err) {
      setMsg(err?.message || 'Import xatosi')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="px-3 pt-2 pb-1 shrink-0">
      <div className="flex gap-2">
        <button
          onClick={download}
          className="flex-1 h-10 rounded-xl bg-surface2 border border-border text-[13px] font-semibold text-text flex items-center justify-center gap-1.5"
        >
          <Download size={15} /> Shablon
        </button>
        <button
          onClick={() => fileRef.current?.click()}
          disabled={busy}
          className="flex-1 h-10 rounded-xl bg-primary text-white text-[13px] font-semibold flex items-center justify-center gap-1.5 disabled:opacity-50"
        >
          <Upload size={15} /> {busy ? 'Yuklanmoqda…' : "Excel'dan"}
        </button>
        <input ref={fileRef} type="file" accept=".xlsx" className="hidden" onChange={onFile} />
      </div>
      {msg && <p className="mt-1.5 text-[12px] font-medium text-primary">{msg}</p>}
      <p className="mt-1 text-[10.5px] text-faint">Mavzu/uy vazifani to'ldiradi — darsni "o'tilgan" qilmaydi.</p>
    </div>
  )
}

function TopicsList({ rows, ctx, onSaved }) {
  const [editing, setEditing] = useState(null) // { col, note }
  return (
    <>
      <div className="h-full overflow-y-auto no-scrollbar px-4 pt-3 pb-6 space-y-2.5">
        {rows.map(({ col, note }) => (
          <div key={cellKey('', col.date, col.period)} className="p-3.5 rounded-4xl bg-surface border border-border">
            <div className="flex items-center gap-2">
              <span className="px-2 py-1 rounded-md bg-primary-soft text-[12px] font-bold text-primary font-mono">{col.date}</span>
              <span className="text-[11px] font-semibold text-faint">{col.period}-dars</span>
              {col.subGroup > 0 && <span className="text-[11px] font-semibold text-faint">{col.subGroup}-guruh</span>}
              {note?.conducted && (
                <span className="ml-auto px-2 py-0.5 rounded-md bg-primary-soft flex items-center gap-1 text-[11px] font-bold text-primary">
                  <Check size={12} /> O'tildi
                </span>
              )}
            </div>
            {note?.topic ? (
              <button className="w-full text-left" onClick={() => setEditing({ col, note })}>
                <p className="mt-2.5 text-[14px] font-bold text-text">{note.topic}</p>
                {note.homework && (
                  <div className="mt-1.5 px-3 py-2 rounded-[10px] bg-surface2 flex items-center gap-1.5">
                    <ClipboardList size={14} className="text-muted" />
                    <span className="text-[12px] text-muted">{note.homework}</span>
                  </div>
                )}
              </button>
            ) : (
              <button
                className="mt-2.5 w-full p-3 rounded-xl border border-border flex items-center justify-center gap-1.5 text-muted"
                onClick={() => setEditing({ col, note })}
              >
                <Plus size={14} />
                <span className="text-[13px] font-semibold">Mavzu va uyga vazifa qo'shish</span>
              </button>
            )}
          </div>
        ))}
      </div>

      <TopicSheet
        editing={editing}
        ctx={ctx}
        onClose={() => setEditing(null)}
        onSaved={() => { onSaved(); setEditing(null) }}
      />
    </>
  )
}

function TopicSheet({ editing, ctx, onClose, onSaved }) {
  const [topic, setTopic] = useState('')
  const [homework, setHomework] = useState('')
  const [conducted, setConducted] = useState(false)
  const [saving, setSaving] = useState(false)
  const [err, setErr] = useState(null)
  const [seeded, setSeeded] = useState(null)

  // Sheet ochilganda mavjud qiymatlarni yuklash.
  if (editing && seeded !== editing) {
    setSeeded(editing)
    setTopic(editing.note?.topic || '')
    setHomework(editing.note?.homework || '')
    setConducted(!!editing.note?.conducted)
    setErr(null)
  }
  if (!editing && seeded) setSeeded(null)

  const save = async () => {
    if (!editing) return
    setSaving(true)
    setErr(null)
    try {
      await api.setJournalNote({
        classId: ctx.classId,
        subjectId: ctx.subjectId,
        quarter: ctx.quarter,
        date: editing.col.date,
        period: editing.col.period,
        topic,
        homework,
        conducted,
        subGroup: editing.col.subGroup || 0,
      })
      onSaved()
    } catch (e) {
      setErr(e?.message || 'Saqlashda xatolik')
    } finally {
      setSaving(false)
    }
  }

  return (
    <AppSheet open={!!editing} onClose={onClose}>
      {editing && (
        <div className="px-5 pb-5">
          <p className="text-[18px] font-bold text-text">Mavzu · {editing.col.date}</p>
          <p className="text-[11px] text-muted font-mono">{editing.col.period}-dars</p>

          <p className="mt-5 text-[12px] font-bold text-muted tracking-wide">MAVZU</p>
          <textarea
            value={topic}
            onChange={(e) => setTopic(e.target.value)}
            rows={2}
            placeholder="Dars mavzusi"
            className="mt-2 w-full px-3.5 py-3 rounded-xl bg-surface2 border border-border text-[14px] text-text resize-none outline-none"
          />

          <p className="mt-4 text-[12px] font-bold text-muted tracking-wide">UYGA VAZIFA</p>
          <textarea
            value={homework}
            onChange={(e) => setHomework(e.target.value)}
            rows={2}
            placeholder="Uyga vazifa"
            className="mt-2 w-full px-3.5 py-3 rounded-xl bg-surface2 border border-border text-[14px] text-text resize-none outline-none"
          />

          <button
            onClick={() => setConducted((v) => !v)}
            className="mt-4 w-full px-3.5 py-3 rounded-xl bg-surface2 border border-border flex items-center gap-2.5"
          >
            <span
              className="w-6 h-6 rounded-md flex items-center justify-center"
              style={conducted ? { background: 'var(--primary)', color: '#fff' } : { border: '1px solid var(--border)' }}
            >
              {conducted && <Check size={14} />}
            </span>
            <span className="text-[14px] font-semibold text-text">Dars o'tildi</span>
          </button>

          {err && (
            <div className="mt-4 px-3 py-2.5 rounded-xl bg-danger/10 flex items-center gap-2 text-[13px] font-semibold text-danger">
              <AlertCircle size={16} /> {err}
            </div>
          )}

          <div className="mt-6 flex gap-2.5">
            <AppButton label="Bekor" style="ghost" expand onClick={onClose} />
            <div className="flex-[2]">
              <AppButton label="Saqlash" expand loading={saving} onClick={save} />
            </div>
          </div>
        </div>
      )}
    </AppSheet>
  )
}

function QuarterTab({ classId, subjectId, quarter, students }) {
  const gradesQ = useFetch(() => api.quarterGrades(classId, subjectId, quarter), [classId, subjectId, quarter])
  const [editing, setEditing] = useState(null) // student
  const [saving, setSaving] = useState(false)
  const [err, setErr] = useState(null)

  if (gradesQ.loading && !gradesQ.data) return <Loading />
  if (gradesQ.error) return <ErrorState error={gradesQ.error} onRetry={gradesQ.reload} />

  const rows = gradesQ.data || []
  const byStudent = {}
  for (const r of rows) byStudent[r.studentId] = r

  const setGrade = async (studentId, grade) => {
    setSaving(true)
    setErr(null)
    try {
      await api.setQuarterGrade({ classId, subjectId, quarter, studentId, grade })
      await gradesQ.reload()
      setEditing(null)
    } catch (e) {
      setErr(e?.status === 403 ? 'Chorak bahosi kiritish yopiq (admin ochishi kerak)' : (e?.message || 'Saqlashda xatolik'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <>
      <div className="h-full overflow-y-auto no-scrollbar px-4 pt-2 pb-6">
        {err && (
          <div className="mb-2 px-3 py-2.5 rounded-xl bg-danger/10 flex items-center gap-2 text-[13px] font-semibold text-danger">
            <AlertCircle size={16} /> {err}
          </div>
        )}
        {students.map((s) => {
          const row = byStudent[s.id]
          const qGrade = row?.grade ?? null
          const recommended = row?.recommended ?? null
          return (
            <button
              key={s.id}
              onClick={() => { setErr(null); setEditing(s) }}
              className="mb-2 w-full p-3 rounded-4xl bg-surface border border-border flex items-center gap-3 text-left"
            >
              <Avatar name={s.fullName} size={36} />
              <div className="flex-1">
                <p className="text-[13px] font-bold text-text">{s.fullName}</p>
                <p className="text-[11px] text-muted">
                  Tavsiya{' '}
                  <span className="font-bold font-mono" style={{ color: recommended ? gradeColor(Math.round(recommended)) : 'var(--muted)' }}>
                    {recommended != null ? Number(recommended).toFixed(1) : '—'}
                  </span>
                </p>
              </div>
              {qGrade != null ? (
                <span className="w-9 h-9 rounded-[10px] flex items-center justify-center text-white font-extrabold font-mono" style={{ background: gradeColor(qGrade) }}>
                  {qGrade}
                </span>
              ) : (
                <span className="w-9 h-9 rounded-[10px] flex items-center justify-center border border-border text-muted font-extrabold font-mono">
                  —
                </span>
              )}
            </button>
          )
        })}
      </div>

      <AppSheet open={!!editing} onClose={() => setEditing(null)}>
        {editing && (
          <div className="px-5 pb-5">
            <p className="text-[18px] font-bold text-text">Chorak bahosi</p>
            <div className="mt-4 p-3 rounded-xl bg-surface2 flex items-center gap-2.5">
              <Avatar name={editing.fullName} size={36} />
              <p className="text-[14px] font-bold text-text">{editing.fullName}</p>
            </div>
            <p className="mt-5 text-[12px] font-bold text-muted tracking-wide">BAHO</p>
            <div className="mt-2.5 flex gap-2">
              {[1, 2, 3, 4, 5].map((g) => {
                const on = byStudent[editing.id]?.grade === g
                return (
                  <button
                    key={g}
                    disabled={saving}
                    onClick={() => setGrade(editing.id, g)}
                    className="flex-1 h-14 rounded-xl flex items-center justify-center text-[22px] font-extrabold font-mono transition-all"
                    style={on ? { background: gradeColor(g), color: '#fff', boxShadow: `0 6px 18px ${gradeColor(g)}59` } : { background: 'var(--surface2)', color: 'var(--text)' }}
                  >
                    {g}
                  </button>
                )
              })}
            </div>
            {err && (
              <div className="mt-4 px-3 py-2.5 rounded-xl bg-danger/10 flex items-center gap-2 text-[13px] font-semibold text-danger">
                <AlertCircle size={16} /> {err}
              </div>
            )}
            <div className="mt-6 flex gap-2.5">
              <AppButton label="O'chirish" style="danger" expand loading={saving} onClick={() => setGrade(editing.id, null)} />
              <div className="flex-[2]">
                <AppButton label="Yopish" style="ghost" expand onClick={() => setEditing(null)} />
              </div>
            </div>
          </div>
        )}
      </AppSheet>
    </>
  )
}

function GradeEntrySheet({ cell, existing, reasons, onClose, onSaved, ctx }) {
  const [grade, setGrade] = useState(null)
  const [reasonId, setReasonId] = useState(null)
  const [homework, setHomework] = useState(0)
  const [behavior, setBehavior] = useState(0)
  const [saving, setSaving] = useState(false)
  const [clearing, setClearing] = useState(false)
  const [err, setErr] = useState(null)
  const [seeded, setSeeded] = useState(null)

  // Katak ochilganda mavjud yozuvni yuklash.
  if (cell && seeded !== cell) {
    setSeeded(cell)
    setGrade(existing?.grade ?? null)
    setReasonId(existing?.reasonId ?? null)
    setHomework(existing?.homework ?? 0)
    setBehavior(existing?.behavior ?? 0)
    setErr(null)
  }
  if (!cell && seeded) setSeeded(null)

  const save = async () => {
    if (!cell) return
    setSaving(true)
    setErr(null)
    try {
      await api.setJournalEntry({
        classId: ctx.classId,
        subjectId: ctx.subjectId,
        quarter: ctx.quarter,
        studentId: cell.student.id,
        date: cell.col.date,
        period: cell.col.period,
        grade,
        reasonId,
        homework,
        behavior,
        mastery: existing?.mastery ?? null,
      })
      onSaved()
    } catch (e) {
      setErr(
        e?.status === 400 ? 'Kelajak sanaga baho/davomat qo\'yib bo\'lmaydi'
          : e?.status === 403 ? 'Bu jurnalga ruxsatingiz yo\'q'
            : (e?.message || 'Saqlashda xatolik')
      )
    } finally {
      setSaving(false)
    }
  }

  const clear = async () => {
    if (!cell) return
    setClearing(true)
    setErr(null)
    try {
      await api.clearJournalEntry({
        classId: ctx.classId,
        subjectId: ctx.subjectId,
        quarter: ctx.quarter,
        studentId: cell.student.id,
        date: cell.col.date,
        period: cell.col.period,
      })
      onSaved()
    } catch (e) {
      setErr(e?.message || 'Tozalashda xatolik')
    } finally {
      setClearing(false)
    }
  }

  const toggle3 = (cur, val, set) => set(cur === val ? 0 : val)

  return (
    <AppSheet open={!!cell} onClose={onClose}>
      {cell && (
        <div className="px-5 pb-5">
          <p className="text-[18px] font-bold text-text">Baho qo'yish · {cell.col.date}</p>
          <div className="mt-4 p-3 rounded-xl bg-surface2 flex items-center gap-2.5">
            <Avatar name={cell.student.fullName} size={36} />
            <div>
              <p className="text-[14px] font-bold text-text">{cell.student.fullName}</p>
              <p className="text-[11px] text-muted font-mono">{cell.col.date} · {cell.col.period}-dars</p>
            </div>
          </div>

          <p className="mt-5 text-[12px] font-bold text-muted tracking-wide">BAHO</p>
          <div className="mt-2.5 flex gap-2">
            {[1, 2, 3, 4, 5].map((g) => {
              const on = grade === g
              return (
                <button
                  key={g}
                  onClick={() => setGrade(on ? null : g)}
                  className="flex-1 h-14 rounded-xl flex items-center justify-center text-[22px] font-extrabold font-mono transition-all"
                  style={on ? { background: gradeColor(g), color: '#fff', boxShadow: `0 6px 18px ${gradeColor(g)}59` } : { background: 'var(--surface2)', color: 'var(--text)' }}
                >
                  {g}
                </button>
              )
            })}
          </div>

          <p className="mt-5 text-[12px] font-bold text-muted tracking-wide">DAVOMAT</p>
          <div className="mt-2.5 flex flex-wrap gap-2">
            {reasons.map((r) => {
              const on = reasonId === r.id
              return (
                <button
                  key={r.id}
                  onClick={() => setReasonId(on ? null : r.id)}
                  className="px-3.5 py-2.5 rounded-xl border flex items-center gap-1.5"
                  style={on ? { background: 'var(--primary-soft)', borderColor: 'var(--primary)' } : { background: 'var(--surface)', borderColor: 'var(--border)' }}
                >
                  <span className="w-[22px] h-[22px] rounded-md bg-warning text-white text-[11px] font-extrabold font-mono flex items-center justify-center">{r.short}</span>
                  <span className="text-[13px] font-bold text-text">{r.name}</span>
                </button>
              )
            })}
          </div>

          <p className="mt-5 text-[12px] font-bold text-muted tracking-wide">UY VAZIFA</p>
          <div className="mt-2.5 flex gap-2">
            {[{ v: 1, label: 'Qildi' }, { v: 2, label: 'Qilmadi' }].map((o) => {
              const on = homework === o.v
              return (
                <button
                  key={o.v}
                  onClick={() => toggle3(homework, o.v, setHomework)}
                  className="flex-1 h-11 rounded-xl text-[13px] font-bold border"
                  style={on ? { background: o.v === 1 ? gradeColor(5) : gradeColor(1), color: '#fff', borderColor: 'transparent' } : { background: 'var(--surface2)', color: 'var(--text)', borderColor: 'var(--border)' }}
                >
                  {o.label}
                </button>
              )
            })}
          </div>

          <p className="mt-5 text-[12px] font-bold text-muted tracking-wide">XULQ</p>
          <div className="mt-2.5 flex gap-2">
            {[{ v: 1, label: 'Yaxshi' }, { v: 2, label: 'Yomon' }].map((o) => {
              const on = behavior === o.v
              return (
                <button
                  key={o.v}
                  onClick={() => toggle3(behavior, o.v, setBehavior)}
                  className="flex-1 h-11 rounded-xl text-[13px] font-bold border"
                  style={on ? { background: o.v === 1 ? gradeColor(5) : gradeColor(1), color: '#fff', borderColor: 'transparent' } : { background: 'var(--surface2)', color: 'var(--text)', borderColor: 'var(--border)' }}
                >
                  {o.label}
                </button>
              )
            })}
          </div>

          {err && (
            <div className="mt-4 px-3 py-2.5 rounded-xl bg-danger/10 flex items-center gap-2 text-[13px] font-semibold text-danger">
              <AlertCircle size={16} /> {err}
            </div>
          )}

          <div className="mt-6 flex gap-2.5">
            <AppButton label="Tozalash" style="ghost" expand loading={clearing} onClick={clear} />
            <div className="flex-[2]">
              <AppButton label="Saqlash" expand loading={saving} onClick={save} />
            </div>
          </div>
        </div>
      )}
    </AppSheet>
  )
}
