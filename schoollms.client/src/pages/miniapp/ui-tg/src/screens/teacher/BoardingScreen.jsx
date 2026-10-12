/**
 * KOMENDANT — kechki dars va yotoqxona yo'qlamasi (mijoz, 2026-10-12: "o'qituvchi profilida role
 * berish mumkin bo'lsin").
 *
 * Ba'zi o'qituvchilar yotoqxona komendanti ham. Ularga alohida akkaunt ochilmaydi: superadmin
 * o'qituvchi akkauntiga rol beradi (Boshqaruv → Rollar, masalan «Komendant»), shu ekran esa
 * "Bugun" dagi Komendant kartasidan ochiladi. Pastki menyu hammada bir xil qoladi (2026-10-07).
 *
 * Qoidalar admin paneldagi "Kechki va yotoqxona davomati" bilan bir xil: sukut — belgilanmagan,
 * ikki tugma (Joyida / Yo'q), "Yo'q" ostida sabab (sukut — Sababsiz: ota-onaga Telegram xabar),
 * hamma belgilanmaguncha saqlanmaydi. Kun — bugun.
 */
import { useState } from 'react'
import { Check, ChevronLeft, Send, UserX } from 'lucide-react'
import { Card, EmptyState, Hero, HeroProgress, Screen, Segmented } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { teacherApi } from '../../lib/teacherApi'
import { todayISO } from '../../lib/weeks'
import { fullDate } from '../../lib/format'
import { haptic, hapticNotify } from '../../lib/telegram'
import { AsyncBlock } from './shared'

const SESSIONS = [
  {
    key: 'evening',
    perm: 'attendanceEvening',
    title: 'Kechki dars',
    present: 'Keldi',
    absent: 'Kelmadi',
    all: 'Hammasi keldi',
  },
  {
    key: 'dorm',
    perm: 'attendanceDorm',
    title: 'Yotoqxona',
    present: 'Joyida',
    absent: "Yo'q",
    all: 'Hammasi joyida',
  },
]

/** Sessions the teacher's role opens. `canMark` — the full key; a ":view" key only reads. */
export function boardingSessions(profile) {
  const perms = profile?.rolePermissions || []
  return SESSIONS.filter((s) => perms.includes(s.perm) || perms.includes(`${s.perm}:view`)).map((s) => ({
    ...s,
    canMark: perms.includes(s.perm),
  }))
}

export function BoardingScreen({ profile, meta, session, onSession, onBack }) {
  const sessions = boardingSessions(profile)
  const info = sessions.find((s) => s.key === session) ?? sessions[0]
  const date = todayISO()
  const q = useAsync(() => (info ? teacherApi.boardingDay(date, info.key) : Promise.resolve(null)), [info?.key])

  /** Saqlanmagan belgilar: o'quvchi → { mark: 'present' | 'absent', reasonId }. */
  const [draft, setDraft] = useState({})
  const [flagged, setFlagged] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState(null)
  const [done, setDone] = useState(null)

  const back = (
    <button
      type="button"
      onClick={onBack}
      aria-label="Orqaga"
      className="flex h-9 items-center gap-1 rounded-full bg-brand-ink px-3 text-[13px] font-semibold text-brand"
    >
      <ChevronLeft className="h-4 w-4" />
      Bugun
    </button>
  )

  if (!info) {
    return (
      <Screen>
        <Hero title="Komendant" subtitle="Bo'lim" right={back} />
        <Card>
          <EmptyState
            title="Bo'lim yopiq"
            note="Kechki dars va yotoqxona davomati sizga berilmagan. Maktab administratoriga murojaat qiling."
          />
        </Card>
      </Screen>
    )
  }

  // Sabablar: "Yo'q" ostida — Sababsiz (sukut) va qolgan yo'qlik sabablari; kechikish sababi saqlanganicha qoladi.
  const reasons = meta.absenceReasons || []
  const absentAll = reasons.filter((r) => !r.isLate)
  const unexcused = absentAll.find((r) => /sababsiz/i.test(r.name)) ?? absentAll[0] ?? null
  const choices = unexcused ? [unexcused, ...absentAll.filter((r) => r.id !== unexcused.id)] : absentAll
  const lateIds = new Set(reasons.filter((r) => r.isLate).map((r) => r.id))

  /** Saqlangan qator → ekrandagi belgi (admin paneldagidek). */
  const savedPick = (s) => {
    if (!s.status) return null
    if (s.status === 'present')
      return { mark: 'present', reasonId: s.reasonId && lateIds.has(s.reasonId) ? s.reasonId : null }
    const fallback =
      s.status === 'excused' ? (choices.find((r) => r.id !== unexcused?.id)?.id ?? null) : (unexcused?.id ?? null)
    return { mark: 'absent', reasonId: s.reasonId ?? fallback }
  }
  const pickOf = (s) => draft[s.studentId] ?? savedPick(s)

  const day = q.data
  const sections = (day?.sections || [])
    .map((sec) => ({ ...sec, students: sec.students.filter((s) => s.eligible) }))
    .filter((sec) => sec.students.length > 0)
  const students = sections.flatMap((sec) => sec.students)
  const picks = students.map(pickOf)
  const presentCount = picks.filter((p) => p?.mark === 'present').length
  const absentCount = picks.filter((p) => p?.mark === 'absent').length
  const unmarked = students.length - presentCount - absentCount

  const pick = (studentId, value) => {
    setError(null)
    setDone(null)
    setDraft((prev) => ({ ...prev, [studentId]: value }))
  }

  const markRest = () => {
    haptic('light')
    setDone(null)
    setDraft((prev) => {
      const next = { ...prev }
      for (const s of students) if (!(next[s.studentId] ?? savedPick(s))) next[s.studentId] = { mark: 'present', reasonId: null }
      return next
    })
  }

  const save = async () => {
    if (unmarked > 0) {
      setFlagged(true)
      setError(`${unmarked} ta o'quvchi belgilanmagan — avval hammasini belgilang.`)
      hapticNotify('error')
      return
    }
    const marks = students.map((s) => {
      const p = pickOf(s)
      const status =
        p.mark === 'present' ? 'present' : !p.reasonId || p.reasonId === unexcused?.id ? 'absent' : 'excused'
      return { studentId: s.studentId, status, reasonId: p.reasonId }
    })
    setSaving(true)
    setError(null)
    try {
      const res = await teacherApi.saveBoarding(date, info.key, marks)
      haptic('medium')
      setDone(res?.notified > 0 ? `Saqlandi · ${res.notified} ta ota-onaga xabar ketdi` : 'Saqlandi')
      setDraft({})
      setFlagged(false)
      q.reload()
    } catch (e) {
      setError(e?.message || 'Saqlashda xatolik')
    } finally {
      setSaving(false)
    }
  }

  let index = 0

  return (
    <Screen>
      <Hero title={info.title} subtitle={`${profile.accessRoleName || 'Komendant'} · ${fullDate(date)}`} right={back}>
        {sessions.length > 1 && (
          <div className="mb-4">
            <Segmented
              value={info.key}
              options={sessions.map((s) => ({ value: s.key, label: s.title }))}
              onChange={onSession}
            />
          </div>
        )}
        <HeroProgress
          value={students.length - unmarked}
          max={students.length}
          lead={`${students.length - unmarked}/${students.length}`}
          note="belgilandi"
        />
      </Hero>

      <AsyncBlock query={q} loadingLabel="Ro'yxat yuklanmoqda…">
        {() =>
          students.length === 0 ? (
            <Card>
              <EmptyState
                icon={<UserX className="h-9 w-9" />}
                title="Ro'yxat bo'sh"
                note={`Bugun ${info.title.toLowerCase()} ro'yxatida o'quvchi yo'q. Ro'yxatni maktab administratori yuritadi.`}
              />
            </Card>
          ) : (
            <>
              <Card
                title="O'quvchilar"
                action={
                  info.canMark && unmarked > 0 ? (
                    <button
                      type="button"
                      onClick={markRest}
                      className="rounded-full bg-slate-100 px-3 py-1.5 text-[12px] font-semibold text-slate-600"
                    >
                      {info.all}
                    </button>
                  ) : null
                }
              >
                <p className="px-4 pb-2 pt-1 text-[13px] text-slate-500">
                  <span className="font-bold text-emerald-700">
                    {presentCount} {info.present.toLowerCase()}
                  </span>
                  {' · '}
                  <span className={absentCount ? 'font-bold text-red-600' : ''}>
                    {absentCount} {info.absent.toLowerCase()}
                  </span>
                  {' · '}
                  <span className={unmarked && flagged ? 'font-bold text-amber-700' : ''}>{unmarked} belgilanmagan</span>
                  {' · '}
                  {students.length} ta jami
                  {!info.canMark && ' · faqat ko‘rish'}
                </p>
                {sections.map((sec) => (
                  <div key={sec.key}>
                    <p className="border-t border-slate-100 bg-slate-50 px-4 pb-1.5 pt-2.5 text-[12px] font-bold uppercase tracking-wide text-slate-500">
                      {sec.title} · {sec.students.length}
                    </p>
                    {sec.students.map((s) => {
                      index += 1
                      return (
                        <StudentRow
                          key={s.studentId}
                          index={index}
                          student={s}
                          pick={pickOf(s)}
                          info={info}
                          choices={choices}
                          unexcusedId={unexcused?.id ?? null}
                          flagged={flagged}
                          onPick={(value) => pick(s.studentId, value)}
                        />
                      )
                    })}
                  </div>
                ))}
              </Card>

              {error && <p className="mx-6 mt-3 text-[13px] font-medium text-red-600">{error}</p>}

              {info.canMark && (
                <>
                  {/* Yopishqoq panel tab panelining ustida — ro'yxat uzun bo'lsa ham "Saqlash" ko'rinadi. */}
                  <div className="h-28" />
                  <div className="fixed inset-x-0 bottom-[calc(76px+var(--tg-safe-bottom))] z-10 border-t border-slate-200 bg-white px-4 py-3">
                    <button
                      type="button"
                      onClick={save}
                      disabled={saving}
                      className="flex w-full items-center justify-center gap-2 rounded-2xl bg-brand py-3.5 text-[16px] font-bold text-brand-ink disabled:opacity-50"
                    >
                      {saving ? (
                        'Saqlanmoqda…'
                      ) : done ? (
                        <>
                          <Check className="h-5 w-5" /> {done}
                        </>
                      ) : (
                        'Saqlash'
                      )}
                    </button>
                  </div>
                </>
              )}
            </>
          )
        }
      </AsyncBlock>
    </Screen>
  )
}

/**
 * Bitta o'quvchi: ikki tugma, "Yo'q" bo'lsa ostida sabablar. Sababsiz va hali "yo'q" deb
 * saqlanmagan bo'lsa — saqlanganda ota-onaga Telegram xabar ketishi aytiladi (bir holat — bir xabar).
 */
function StudentRow({ index, student, pick, info, choices, unexcusedId, flagged, onPick }) {
  const present = pick?.mark === 'present'
  const absent = pick?.mark === 'absent'
  const notify = absent && (!pick.reasonId || pick.reasonId === unexcusedId) && student.status !== 'absent'
  const button = (on, tone) =>
    'h-9 min-w-[64px] rounded-full px-3 text-[13px] font-bold disabled:opacity-60 ' +
    (on ? (tone === 'present' ? 'bg-emerald-700 text-white' : 'bg-red-600 text-white') : 'bg-slate-100 text-slate-500')

  return (
    <div className={'border-t border-slate-100 px-4 py-2.5 ' + (flagged && !pick ? 'bg-amber-50' : '')}>
      <div className="flex items-center gap-3">
        <span className="w-5 shrink-0 text-[12px] font-bold text-slate-400">{index}</span>
        <div className="min-w-0 flex-1">
          <p className="truncate text-[15px] font-semibold">{student.fullName}</p>
          <p className="text-[12px] text-slate-500">
            {student.className}
            {pick ? '' : ' · belgilanmagan'}
          </p>
        </div>
        <div className="flex shrink-0 gap-1.5">
          <button
            type="button"
            disabled={!info.canMark}
            aria-pressed={present}
            onClick={() => onPick({ mark: 'present', reasonId: present ? pick.reasonId : null })}
            className={button(present, 'present')}
          >
            {info.present}
          </button>
          <button
            type="button"
            disabled={!info.canMark}
            aria-pressed={absent}
            onClick={() => onPick({ mark: 'absent', reasonId: absent ? pick.reasonId : unexcusedId })}
            className={button(absent, 'absent')}
          >
            {info.absent}
          </button>
        </div>
      </div>

      {absent && choices.length > 0 && (
        <div className="mt-2 flex flex-wrap gap-1.5 pl-8" role="group" aria-label={`${student.fullName} — sabab`}>
          {choices.map((r) => {
            const on = r.id === pick.reasonId
            return (
              <button
                key={r.id}
                type="button"
                disabled={!info.canMark}
                aria-pressed={on}
                onClick={() => onPick({ mark: 'absent', reasonId: r.id })}
                className={
                  'rounded-lg px-2.5 py-1 text-[12px] font-bold ' +
                  (on ? 'bg-brand-ink text-brand' : 'bg-slate-100 text-slate-500')
                }
              >
                {r.name}
              </button>
            )
          })}
        </div>
      )}

      {notify && (
        <p className="mt-1.5 flex items-center gap-1 pl-8 text-[12px] text-red-700">
          <Send className="h-3 w-3" /> Saqlanganda ota-onaga Telegram xabar ketadi
        </p>
      )}
    </div>
  )
}
