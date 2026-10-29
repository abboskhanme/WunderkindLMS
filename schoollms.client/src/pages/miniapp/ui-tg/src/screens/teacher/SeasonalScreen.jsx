/**
 * MAVSUMIY BAHOLASH — o'qituvchi o'z sinf·fanlariga oylik, choraklik va yillik ball (0–100) qo'yadi
 * (mijoz, 2026-10-28; docs/modules/admission-and-testing.md §3.4 ekran 14, §6.6).
 *
 * Backend va o'qituvchi veb-ilovasidagi ekranlar (`teacher/ui-web/.../SeasonalMarksScreen.jsx`,
 * `SeasonalEntryScreen.jsx`) avvaldan bor edi, lekin hech qayerdan ochilmasdi. Bu — o'sha oqimning
 * Mini App ko'rinishi: "Bugun" dagi kartadan ochiladi, pastki menyu hammada bir xil qoladi (2026-10-07).
 *
 * Tartib spetsifikatsiyadagidek: tur → davr → sinf·fan → o'quvchilar. Sinf·fan juftlarini server
 * o'zi cheklaydi (faqat dars jadvalida shu o'qituvchiga biriktirilganlari) — ekran hech narsani
 * filtrlamaydi. Saqlashda faqat o'zgargan qatorlar bitta so'rovda ketadi; ball ham izoh ham
 * bo'shatilsa — shu davrdagi baho o'chiriladi. 0–100 dan tashqari ball va 1–2 harfli izoh shu yerda
 * rad etiladi, kesilmaydi.
 */
import { useEffect, useMemo, useRef, useState } from 'react'
import { BarChart3, Check, ChevronLeft, ChevronRight, Lock, Plus, Users } from 'lucide-react'
import { Badge, Card, EmptyState, Hero, HeroProgress, Row, Screen, Segmented, Stepper } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { teacherApi } from '../../lib/teacherApi'
import { haptic, hapticNotify, useKeyboardOpen } from '../../lib/telegram'
import {
  PERIOD_KIND_OPTIONS,
  QUARTER_OPTIONS,
  SCORE_LABEL,
  SEASONAL_PERM,
  commentProblem,
  defaultPeriod,
  formatScore,
  normalizeComment,
  parseScore,
  periodCaption,
  periodKey,
  periodKindLabel,
  periodQuery,
  saveSummary,
  seasonalErrorMessage,
  seasonalRequest,
  shiftMonth,
  shiftYear,
  yearBounds,
} from '../../lib/seasonal'
import { AsyncBlock } from './shared'

/** "Bugun" dagi karta — har o'qituvchida bir xil; ruxsati yo'q bo'lsa ichida "Bo'lim yopiq". */
export function SeasonalCard({ onOpen }) {
  return (
    <Card>
      <Row
        onClick={() => {
          haptic('light')
          onOpen()
        }}
        lead={
          <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-brand/25 text-brand-ink">
            <BarChart3 className="h-5 w-5" />
          </span>
        }
        title="Mavsumiy baholash"
        subtitle={`${SCORE_LABEL} · oylik, choraklik, yillik`}
        right={<ChevronRight className="h-5 w-5 shrink-0 text-slate-300" />}
      />
    </Card>
  )
}

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

export function SeasonalScreen({ profile, onBack }) {
  // Davr ekranda turadi: sinfdan orqaga qaytilganda "2-chorak" saqlanib qoladi.
  const [period, setPeriod] = useState(() => defaultPeriod())
  const [pair, setPair] = useState(null)

  if (!(profile?.permissions || []).includes(SEASONAL_PERM)) {
    return (
      <Screen>
        <Hero title="Mavsumiy baholash" subtitle="Bo'lim" right={<BackPill label="Bugun" onClick={onBack} />} />
        <Card>
          <EmptyState
            icon={<Lock className="h-9 w-9" />}
            title="Bo'lim yopiq"
            note="Bu bo'lim siz uchun hali ochilmagan. Maktab administratoriga murojaat qiling."
          />
        </Card>
      </Screen>
    )
  }

  if (pair) {
    return <EntryScreen key={`${pair.classId}|${pair.subjectId}|${periodKey(period)}`} pair={pair} period={period} onBack={() => setPair(null)} />
  }
  return <PickerScreen period={period} onPeriod={setPeriod} onOpen={setPair} onBack={onBack} />
}

/* ------------------------------------------------------------- davr + juftlar */

function PickerScreen({ period, onPeriod, onOpen, onBack }) {
  const q = useAsync(() => seasonalRequest(teacherApi.seasonalScope()), [])
  const groups = useMemo(() => groupPairs(q.data), [q.data])

  const { min, max } = yearBounds()
  const monthly = period.kind === 'monthly'
  const monthIndex = period.year * 12 + period.month - 1
  const step = (delta) => {
    haptic('light')
    onPeriod(monthly ? shiftMonth(period, delta) : shiftYear(period, delta))
  }

  return (
    <Screen>
      <Hero title="Mavsumiy baholash" subtitle={`${SCORE_LABEL} · oylik, choraklik, yillik`} right={<BackPill label="Bugun" onClick={onBack} />}>
        <Segmented value={period.kind} options={PERIOD_KIND_OPTIONS} onChange={(kind) => onPeriod({ ...period, kind })} />
        <div className="mt-3">
          <Stepper
            label={monthly ? periodCaption(period) : `${period.year}-yil`}
            onPrev={() => step(-1)}
            onNext={() => step(1)}
            disabledPrev={monthly ? monthIndex <= min * 12 : period.year <= min}
            disabledNext={monthly ? monthIndex >= max * 12 + 11 : period.year >= max}
          />
        </div>
        {period.kind === 'quarterly' && (
          <div className="mt-2">
            <Segmented value={period.quarter} options={QUARTER_OPTIONS} onChange={(quarter) => onPeriod({ ...period, quarter })} />
          </div>
        )}
      </Hero>

      <AsyncBlock query={q} loadingLabel="Sinflar yuklanmoqda…">
        {() =>
          groups.length === 0 ? (
            <Card>
              <EmptyState
                icon={<BarChart3 className="h-9 w-9" />}
                title="Baholanadigan sinf va fan yo'q"
                note="Sinf va fanlar dars jadvalidan olinadi — jadvalda sizga dars biriktirilmagan."
              />
            </Card>
          ) : (
            groups.map((g) => (
              <Card key={g.key} title={g.title} action={<Badge>{periodCaption(period)}</Badge>}>
                {g.pairs.map((p) => (
                  <Row
                    key={`${p.classId}|${p.subjectId}`}
                    onClick={() => {
                      haptic('light')
                      onOpen(p)
                    }}
                    lead={
                      <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-brand-ink text-[13px] font-extrabold text-brand">
                        {(p.className || '').slice(0, 4)}
                      </span>
                    }
                    title={`${p.className} sinfi`}
                    subtitle={p.subjectName}
                    right={<ChevronRight className="h-5 w-5 shrink-0 text-slate-300" />}
                  />
                ))}
              </Card>
            ))
          )
        }
      </AsyncBlock>
    </Screen>
  )
}

const byName = (a, b) => String(a).localeCompare(String(b), 'uz', { numeric: true, sensitivity: 'base' })

/** ScopeDto → sinf darajasi bo'yicha guruhlar ("5-sinflar"); bir juft jadvalda bir necha marta bo'lsa ham bir marta. */
function groupPairs(scope) {
  if (!scope) return []
  const classes = new Map((scope.classes || []).map((c) => [c.id, c]))
  const seen = new Set()
  const rows = []
  for (const p of scope.pairs || []) {
    const cls = classes.get(p.classId)
    const key = `${p.classId}|${p.subjectId}`
    if (!cls || seen.has(key)) continue
    seen.add(key)
    rows.push({ classId: p.classId, className: cls.name, grade: Number(cls.grade) || 0, subjectId: p.subjectId, subjectName: p.subjectName })
  }
  rows.sort((a, b) => a.grade - b.grade || byName(a.className, b.className) || byName(a.subjectName, b.subjectName))
  const groups = []
  for (const r of rows) {
    const key = String(r.grade)
    let g = groups.find((x) => x.key === key)
    if (!g) {
      g = { key, title: r.grade > 0 ? `${r.grade}-sinflar` : 'Boshqa', pairs: [] }
      groups.push(g)
    }
    g.pairs.push(r)
  }
  return groups
}

/* ------------------------------------------------------------- baho kiritish */

function EntryScreen({ pair, period, onBack }) {
  const base = { classId: pair.classId, subjectId: pair.subjectId, ...periodQuery(period) }
  const q = useAsync(() => seasonalRequest(teacherApi.seasonalStudents(base)), [])
  const rows = q.data
  const [notice, setNotice] = useState(null)
  const [dirty, setDirty] = useState(false)
  const [leaving, setLeaving] = useState(false)

  const save = async (changes) => {
    const result = await teacherApi.seasonalBulk({ ...base, rows: changes })
    setNotice(saveSummary(result))
    hapticNotify('success')
    q.reload()
  }

  const graded = Array.isArray(rows) ? rows.filter((r) => r.markId != null).length : 0
  const total = Array.isArray(rows) ? rows.length : 0

  return (
    <Screen>
      <Hero
        title={`${pair.className} · ${pair.subjectName}`}
        subtitle={`${periodKindLabel(period.kind)} · ${periodCaption(period)}`}
        right={<BackPill label="Sinflar" onClick={() => (dirty ? setLeaving(true) : onBack())} />}
      >
        <HeroProgress value={graded} max={total} lead={`${graded}/${total}`} note="baholangan" />
      </Hero>

      {leaving && (
        <Card className="ring-2 ring-red-200">
          <p className="px-4 pt-4 text-[15px] font-semibold">Saqlanmagan o'zgarishlar bor</p>
          <p className="px-4 pt-1 text-[13px] text-slate-500">Orqaga qaytsangiz, kiritilgan ballar yo'qoladi.</p>
          <div className="flex gap-2 p-4">
            <button type="button" onClick={() => setLeaving(false)} className="flex-1 rounded-2xl bg-slate-100 py-3 text-[15px] font-semibold">
              Qolish
            </button>
            <button type="button" onClick={onBack} className="flex-1 rounded-2xl bg-red-600 py-3 text-[15px] font-semibold text-white">
              Tashlab ketish
            </button>
          </div>
        </Card>
      )}

      <AsyncBlock query={q} loadingLabel="O'quvchilar yuklanmoqda…">
        {(data) =>
          data.length === 0 ? (
            <Card>
              <EmptyState icon={<Users className="h-9 w-9" />} title="O'quvchilar yo'q" note="Bu sinfda o'quvchi topilmadi." />
            </Card>
          ) : (
            <EntryGrid rows={data} busy={q.loading} notice={notice} onSave={save} onDirty={setDirty} />
          )
        }
      </AsyncBlock>
    </Screen>
  )
}

// Server qatorlarining mazmuni: saqlangandan keyin ro'yxat qayta kelganda qoralamalar yangidan boshlanadi.
function EntryGrid(props) {
  const signature = useMemo(
    () => props.rows.map((r) => `${r.studentId}|${r.markId ?? ''}|${r.score ?? ''}|${r.comment ?? ''}`).join('\n'),
    [props.rows],
  )
  return <GridBody key={signature} {...props} />
}

function initialDrafts(rows) {
  const out = {}
  for (const r of rows) out[r.studentId] = { score: formatScore(r.score), comment: r.comment ?? '' }
  return out
}

/** Bitta qatorning tekshiruvi va yuboriladigan o'zgarishi (null — yuboriladigan narsa yo'q). */
function analyse(row, draft) {
  const parsed = parseScore(draft.score)
  const commentError = commentProblem(draft.comment)
  const scoreError = parsed.ok ? null : parsed.reason
  if (scoreError || commentError) return { row, draft, scoreError, commentError, changed: true, willDelete: false, change: null }
  const comment = normalizeComment(draft.comment)
  const stored = row.score === null || row.score === undefined ? null : Number(row.score)
  const changed = parsed.value !== stored || comment !== normalizeComment(row.comment)
  const willDelete = changed && row.markId != null && parsed.value === null && comment === null
  return {
    row,
    draft,
    scoreError: null,
    commentError: null,
    changed,
    willDelete,
    change: changed ? { studentId: row.studentId, score: parsed.value, comment } : null,
  }
}

function GridBody({ rows, busy, notice, onSave, onDirty }) {
  const [drafts, setDrafts] = useState(() => initialDrafts(rows))
  // Izoh maydoni faqat saqlangan izoh bo'lsa ochiq — ro'yxat har o'quvchiga bitta qator bo'lib qoladi.
  const [openComments, setOpenComments] = useState(
    () => new Set(rows.filter((r) => normalizeComment(r.comment) !== null).map((r) => r.studentId)),
  )
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState(null)
  const scoreRefs = useRef([])
  const keyboardOpen = useKeyboardOpen()

  const states = useMemo(() => rows.map((r) => analyse(r, drafts[r.studentId] || { score: '', comment: '' })), [rows, drafts])
  const changes = states.flatMap((s) => (s.change ? [s.change] : []))
  const invalidCount = states.filter((s) => s.scoreError || s.commentError).length
  const dirty = states.some((s) => s.changed)
  const locked = saving || Boolean(busy)
  const canSave = changes.length > 0 && invalidCount === 0 && !locked

  useEffect(() => {
    onDirty(dirty)
  }, [dirty, onDirty])

  const edit = (studentId, patch) =>
    setDrafts((prev) => ({ ...prev, [studentId]: { ...(prev[studentId] || { score: '', comment: '' }), ...patch } }))

  // "Keyingi" (Enter) — keyingi o'quvchining baliga: ro'yxat bo'ylab tez kiritish.
  const onScoreKey = (e, index) => {
    if (e.key !== 'Enter') return
    e.preventDefault()
    const target = scoreRefs.current[index + 1]
    if (target) target.focus()
    else e.currentTarget.blur()
  }

  const save = async () => {
    if (!canSave) return
    haptic('medium')
    setSaving(true)
    setError(null)
    try {
      await onSave(changes)
    } catch (err) {
      setError(seasonalErrorMessage(err))
      hapticNotify('error')
    } finally {
      setSaving(false)
    }
  }

  return (
    <>
      <Card title="O'quvchilar" action={<span className="text-[12px] font-semibold text-slate-400">{SCORE_LABEL}</span>}>
        {states.map((s, index) => {
          const showComment = openComments.has(s.row.studentId)
          const invalid = Boolean(s.scoreError || s.commentError)
          return (
            <div
              key={s.row.studentId}
              className={'border-t border-slate-100 px-4 py-2.5 ' + (invalid ? 'bg-red-50' : s.changed ? 'bg-brand/10' : '')}
            >
              <div className="flex items-center gap-3">
                <span className="w-5 shrink-0 text-[12px] font-bold text-slate-400">{index + 1}</span>
                <div className="min-w-0 flex-1">
                  <p className="truncate text-[15px] font-semibold">{s.row.fullName}</p>
                  <div className="mt-0.5 flex items-center gap-2">
                    <RowStatus state={s} />
                    {!showComment && (
                      <button
                        type="button"
                        disabled={locked}
                        onClick={() => setOpenComments((prev) => new Set(prev).add(s.row.studentId))}
                        className="flex items-center gap-0.5 text-[12px] font-semibold text-slate-500 disabled:opacity-50"
                      >
                        <Plus className="h-3 w-3" /> Izoh
                      </button>
                    )}
                  </div>
                </div>
                <input
                  ref={(el) => {
                    scoreRefs.current[index] = el
                  }}
                  value={s.draft.score}
                  onChange={(e) => edit(s.row.studentId, { score: e.target.value })}
                  onKeyDown={(e) => onScoreKey(e, index)}
                  onFocus={(e) => e.currentTarget.select()}
                  disabled={locked}
                  inputMode="decimal"
                  enterKeyHint="next"
                  autoComplete="off"
                  placeholder="—"
                  aria-label={`${s.row.fullName}: ${SCORE_LABEL}`}
                  aria-invalid={Boolean(s.scoreError)}
                  className={
                    'h-11 w-[76px] shrink-0 rounded-xl border text-center text-[17px] font-extrabold outline-none placeholder:text-slate-300 disabled:opacity-50 ' +
                    (s.scoreError ? 'border-red-400 bg-red-50' : 'border-slate-200 bg-slate-50 focus:border-brand-ink')
                  }
                />
              </div>
              {showComment && (
                <input
                  value={s.draft.comment}
                  onChange={(e) => edit(s.row.studentId, { comment: e.target.value })}
                  disabled={locked}
                  autoComplete="off"
                  placeholder="Izoh (ixtiyoriy)"
                  aria-label={`${s.row.fullName}: izoh`}
                  className={
                    'mt-2 h-10 w-full rounded-xl border px-3 text-[14px] outline-none placeholder:text-slate-400 disabled:opacity-50 ' +
                    (s.commentError ? 'border-red-400 bg-red-50' : 'border-slate-200 bg-slate-50 focus:border-brand-ink')
                  }
                />
              )}
              {invalid && <p className="mt-1.5 text-[12px] font-semibold text-red-600">{s.scoreError || s.commentError}</p>}
            </div>
          )
        })}
        <p className="border-t border-slate-100 px-4 py-3 text-[12px] text-slate-400">
          Ball ham, izoh ham bo'shatilsa — saqlashda o'quvchining shu davrdagi bahosi o'chiriladi.
        </p>
      </Card>

      {/* Yopishqoq panel tab panelining ustida — ro'yxat uzun bo'lsa ham "Saqlash" ko'rinadi. Ball yozilayotganda
          (klaviatura ochiq) tab paneli yashirinadi — panel pastga tushadi, havoda osilib qolmaydi. */}
      <div className="h-36" />
      <div
        className={
          'fixed inset-x-0 z-10 border-t border-slate-200 bg-white px-4 py-3 ' +
          (keyboardOpen ? 'bottom-0' : 'bottom-[calc(76px+var(--tg-safe-bottom))]')
        }
      >
        <p className="mb-2 text-[13px]">
          {error ? (
            <span className="font-semibold text-red-600">{error}</span>
          ) : invalidCount > 0 ? (
            <span className="font-semibold text-red-600">{invalidCount} ta qatorda xato — tuzatmaguncha saqlanmaydi</span>
          ) : changes.length > 0 ? (
            <span className="font-semibold text-slate-700">{changes.length} ta o'zgarish saqlanmagan</span>
          ) : notice ? (
            <span className="flex items-center gap-1.5 font-semibold text-emerald-700">
              <Check className="h-4 w-4" /> {notice}
            </span>
          ) : (
            <span className="text-slate-400">Saqlanmagan o'zgarish yo'q</span>
          )}
        </p>
        <div className="flex gap-2">
          <button
            type="button"
            disabled={!dirty || locked}
            onClick={() => {
              setDrafts(initialDrafts(rows))
              setError(null)
            }}
            className="rounded-2xl bg-slate-100 px-4 py-3.5 text-[15px] font-semibold text-slate-600 disabled:opacity-40"
          >
            Bekor
          </button>
          <button
            type="button"
            onClick={save}
            disabled={!canSave}
            className="flex-1 rounded-2xl bg-brand py-3.5 text-[16px] font-bold text-brand-ink disabled:opacity-50"
          >
            {saving ? 'Saqlanmoqda…' : 'Saqlash'}
          </button>
        </div>
      </div>
    </>
  )
}

function RowStatus({ state }) {
  if (state.changed) {
    if (state.scoreError || state.commentError) return <span className="text-[12px] font-semibold text-red-600">Xato</span>
    if (state.willDelete) return <span className="text-[12px] font-semibold text-amber-700">O'chiriladi</span>
    return <span className="text-[12px] font-semibold text-brand-ink">{state.row.markId == null ? 'Yangi' : "O'zgardi"}</span>
  }
  if (state.row.markId != null) {
    return (
      <span className="flex items-center gap-1 text-[12px] font-semibold text-emerald-700">
        <Check className="h-3 w-3" /> Saqlangan
      </span>
    )
  }
  return <span className="text-[12px] text-slate-400">Baholanmagan</span>
}
