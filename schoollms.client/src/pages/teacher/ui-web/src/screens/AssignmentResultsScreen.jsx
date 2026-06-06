import { useState } from 'react'
import { X, Paperclip, FileText, Download, ExternalLink } from 'lucide-react'
import { ScreenHeader } from '../components/ui'
import SegmentedControl from '../components/SegmentedControl'
import ProgressRing from '../components/ProgressRing'
import Avatar from '../components/Avatar'
import AppButton from '../components/AppButton'
import { AsyncView } from '../components/State'
import { gradeColor } from '../lib/colors'
import { useFetch } from '../lib/session'
import { api } from '../lib/api'

const FORMAT_COLORS = { test: '#0D9488', written: '#0EA5E9', text: '#0EA5E9', file: '#7C3AED', video: '#DB2777' }
const GRADE_BARS = ['#E11D48', '#EA580C', '#D97706', '#0D9488', '#059669']

// Assignment results — summary hero, histogram, lists + inline submission edit. Live API.
export default function AssignmentResultsScreen({ params, onBack }) {
  const id = params?.id
  const q = useFetch(() => api.assignmentResults(id), [id])
  const [tab, setTab] = useState('completed')
  const [editing, setEditing] = useState(null) // row being edited

  return (
    <div className="h-full flex flex-col bg-bg">
      <ScreenHeader title={params?.title || (q.data && q.data.title) || 'Natijalar'} subtitle="Natijalar" onBack={onBack} titleSize={15} />
      <div className="flex-1 overflow-y-auto no-scrollbar px-4 pt-1 pb-6">
        <AsyncView query={q} loadingLabel="Natijalar yuklanmoqda…">
          {q.data && (
            <ResultsBody r={q.data} tab={tab} setTab={setTab} onEdit={setEditing} />
          )}
        </AsyncView>
      </div>

      {editing && q.data && (
        <EditSheet
          row={editing}
          maxScore={q.data.maxScore}
          format={q.data.format}
          onClose={() => setEditing(null)}
          onSaved={async () => {
            setEditing(null)
            await q.reload()
          }}
          assignmentId={id}
        />
      )}
    </div>
  )
}

function ResultsBody({ r, tab, setTab, onEdit }) {
  const color = FORMAT_COLORS[r.format] || '#64748B'
  const rows = r.rows || []
  const completed = rows.filter((x) => x.completed)
  const pending = rows.filter((x) => !x.completed)
  const list = tab === 'completed' ? completed : pending
  const total = r.total || rows.length
  const pct = total ? Math.round((r.completedCount / total) * 100) : 0
  const scores = completed.filter((x) => x.score != null).map((x) => x.score)
  const avg = scores.length ? Math.round(scores.reduce((a, b) => a + b, 0) / scores.length) : 0
  const maxScore = r.maxScore || 0

  return (
    <>
      {/* Summary hero */}
      <div className="p-[18px] rounded-5xl text-white" style={{ background: `linear-gradient(135deg, ${color}, ${color}CC)` }}>
        <div className="flex items-center gap-3.5">
          <ProgressRing value={pct} size={72} strokeWidth={6} color="#fff" trackColor="rgba(255,255,255,0.25)" label={`${pct}%`} />
          <div>
            <p className="text-[28px] font-extrabold font-mono">
              {r.completedCount}
              <span className="text-[18px] text-white/60"> / {total}</span>
            </p>
            <p className="mt-1 text-[12px] text-white/90">bajardi</p>
          </div>
        </div>
        <div className="mt-4 flex gap-2.5">
          <MiniStat label="O'rtacha" value={avg} />
          <MiniStat label="Max" value={maxScore} />
          <MiniStat label="Kech" value={pending.length} />
        </div>
      </div>

      {/* Histogram */}
      {maxScore > 0 && (
        <div className="mt-4 p-4 rounded-4xl bg-surface border border-border">
          <p className="text-[13px] font-bold text-text">Ball taqsimoti</p>
          <div className="mt-3 h-[120px] flex items-end gap-1.5">
            {[0, 1, 2, 3, 4].map((i) => {
              const lo = i * 20
              const hi = (i + 1) * 20
              const count = completed.filter((x) => x.score != null && (x.score / maxScore) * 100 >= lo && (x.score / maxScore) * 100 < hi).length
              const h = count > 0 ? 70 * (count / Math.max(1, completed.length)) : 4
              return (
                <div key={i} className="flex-1 flex flex-col items-center justify-end">
                  <span className="text-[11px] font-bold text-text font-mono">{count}</span>
                  <div className="mt-1 w-full rounded-t-md" style={{ height: h, background: GRADE_BARS[i] }} />
                  <span className="mt-1 text-[9px] font-semibold text-faint">{lo}–{hi}</span>
                </div>
              )
            })}
          </div>
        </div>
      )}

      <div className="mt-2.5">
        <SegmentedControl
          value={tab}
          onChange={setTab}
          options={[
            { value: 'completed', label: `Bajardi (${completed.length})` },
            { value: 'pending', label: `Kech (${pending.length})` },
          ]}
        />
      </div>

      <div className="mt-2.5 space-y-2">
        {list.length === 0 && (
          <p className="py-8 text-center text-[13px] text-muted">Bu ro'yxat bo'sh.</p>
        )}
        {list.map((row) => (
          <button
            key={row.studentId}
            onClick={() => onEdit(row)}
            className="w-full text-left p-3 rounded-4xl bg-surface border border-border flex items-center gap-3"
          >
            <Avatar name={row.studentName} size={40} />
            <div className="flex-1 min-w-0">
              <p className="text-[13px] font-bold text-text truncate">{row.studentName}</p>
              <p className="text-[11px] text-muted">
                {row.completed ? `${row.className || ''} · ${(row.submittedAt || '').split('T')[0] || 'topshirdi'}` : `${row.className || ''} · Hali topshirmagan`}
              </p>
              {row.completed && (row.fileUrl || row.answerText) && (
                <div className="mt-1 flex items-center gap-2">
                  {row.fileUrl && (
                    <span className="inline-flex items-center gap-1 text-[10.5px] font-semibold text-primary">
                      <Paperclip size={11} /> Fayl
                    </span>
                  )}
                  {row.answerText && (
                    <span className="inline-flex items-center gap-1 text-[10.5px] font-semibold text-info">
                      <FileText size={11} /> Matn
                    </span>
                  )}
                </div>
              )}
            </div>
            {row.completed && row.score != null ? (
              <div className="text-right">
                <p className="text-[18px] font-extrabold font-mono" style={{ color: gradeColor(Math.round((row.score / Math.max(1, maxScore)) * 5)) }}>{row.score}</p>
                <p className="text-[10px] text-faint">/ {maxScore}</p>
              </div>
            ) : (
              <span className="px-3 py-1.5 rounded-[10px] bg-primary-soft text-[12px] font-semibold text-primary">Baholash</span>
            )}
          </button>
        ))}
      </div>
    </>
  )
}

function EditSheet({ row, maxScore, format, assignmentId, onClose, onSaved }) {
  const [completed, setCompleted] = useState(!!row.completed)
  const [score, setScore] = useState(row.score != null ? String(row.score) : '')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState(null)

  const save = async () => {
    setSaving(true)
    setError(null)
    try {
      const parsed = score === '' ? null : Number(score)
      await api.setSubmission(assignmentId, row.studentId, { completed, score: parsed })
      await onSaved()
    } catch (e) {
      setError(e)
      setSaving(false)
    }
  }

  return (
    <div className="absolute inset-0 z-30 flex flex-col justify-end bg-black/40" onClick={onClose}>
      <div className="rounded-t-5xl bg-bg p-5 pb-7" onClick={(e) => e.stopPropagation()}>
        <div className="flex items-center gap-3 mb-4">
          <Avatar name={row.studentName} size={44} />
          <div className="flex-1 min-w-0">
            <p className="text-[15px] font-extrabold text-text truncate">{row.studentName}</p>
            <p className="text-[12px] text-muted">{row.className}</p>
          </div>
          <button onClick={onClose} className="w-9 h-9 rounded-xl bg-surface2 flex items-center justify-center text-text">
            <X size={18} />
          </button>
        </div>

        {error && (
          <div className="mb-3 px-3 py-2 rounded-xl bg-danger/10 text-danger text-[12px] font-semibold">
            {error.message || 'Saqlab bo\'lmadi'}
          </div>
        )}

        {/* Topshirilgan ish — o'qituvchi baholashdan oldin ko'radi */}
        <SubmissionView row={row} format={format} />

        <div className="p-4 rounded-4xl bg-surface border border-border flex items-center">
          <div className="flex-1">
            <p className="text-[14px] font-bold text-text">Bajardi</p>
            <p className="text-[12px] text-muted">O'quvchi topshiriqni bajarganini belgilash</p>
          </div>
          <button
            onClick={() => setCompleted((v) => !v)}
            className="w-12 h-7 rounded-full p-0.5 transition-colors"
            style={{ background: completed ? 'var(--primary)' : 'var(--surface3)' }}
          >
            <span className="block w-6 h-6 rounded-full bg-white transition-transform" style={{ transform: completed ? 'translateX(20px)' : 'none' }} />
          </button>
        </div>

        <div className="mt-3 p-4 rounded-4xl bg-surface border border-border">
          <p className="text-[12px] font-bold text-muted uppercase tracking-wide">Ball (max {maxScore})</p>
          <input
            type="number"
            min={0}
            max={maxScore || undefined}
            value={score}
            onChange={(e) => setScore(e.target.value)}
            placeholder="—"
            className="mt-2 w-full px-4 h-12 rounded-xl bg-surface2 border border-border outline-none text-[18px] font-extrabold font-mono text-text placeholder:text-faint focus:border-primary"
          />
        </div>

        <div className="mt-4">
          <AppButton label="Saqlash" expand loading={saving} onClick={save} />
        </div>
      </div>
    </div>
  )
}

// O'quvchi topshirgan ishi: matn javobi va/yoki yuklangan fayl havolasi (o'qituvchi ko'rib baholaydi).
function SubmissionView({ row, format }) {
  const fileName = row.fileUrl ? decodeURIComponent(row.fileUrl.split('/').pop() || 'fayl') : null

  let body
  if (!row.completed) {
    body = <p className="text-[12px] text-muted">O'quvchi hali topshirmagan.</p>
  } else if (format === 'test') {
    body = <p className="text-[12px] text-muted">Test — avtomatik baholandi.</p>
  } else if (row.fileUrl || row.answerText) {
    body = (
      <div className="space-y-2.5">
        {row.fileUrl && (
          <a
            href={row.fileUrl}
            target="_blank"
            rel="noopener noreferrer"
            download
            className="flex items-center gap-2.5 px-3 py-2.5 rounded-xl bg-surface2 border border-border"
          >
            <span className="w-9 h-9 rounded-[10px] bg-primary-soft flex items-center justify-center text-primary shrink-0">
              <Paperclip size={16} />
            </span>
            <span className="flex-1 min-w-0">
              <span className="block text-[13px] font-semibold text-text truncate">{fileName}</span>
              <span className="block text-[11px] text-muted">Yuklangan faylni ochish</span>
            </span>
            <Download size={16} className="text-faint shrink-0" />
          </a>
        )}
        {row.answerText && (
          <div className="px-3 py-2.5 rounded-xl bg-surface2 border border-border">
            <p className="text-[11px] font-bold text-muted uppercase tracking-wide mb-1">Matn javobi</p>
            <p className="text-[13px] text-text whitespace-pre-wrap break-words">{row.answerText}</p>
          </div>
        )}
      </div>
    )
  } else {
    body = <p className="text-[12px] text-muted">Matn yoki fayl biriktirilmagan.</p>
  }

  return (
    <div className="mb-3 p-4 rounded-4xl bg-surface border border-border">
      <div className="flex items-center gap-2 mb-2.5">
        <ExternalLink size={14} className="text-muted" />
        <p className="text-[13px] font-bold text-text">Topshirilgan ish</p>
      </div>
      {body}
    </div>
  )
}

function MiniStat({ label, value }) {
  return (
    <div className="flex-1 p-2.5 rounded-xl bg-white/[0.18]">
      <p className="text-[10px] text-white/85">{label}</p>
      <p className="text-[18px] font-extrabold font-mono">{value}</p>
    </div>
  )
}
