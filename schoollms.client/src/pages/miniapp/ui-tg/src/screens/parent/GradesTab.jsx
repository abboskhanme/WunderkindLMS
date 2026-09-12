/**
 * BAHOLAR — chorak kesimida o'zlashtirish va davomat.
 *
 * CHORAK — EKRANNING O'QI. Bitta "hammasi" ro'yxati ota-onaga hech narsa
 * aytmaydi: 2-chorakdagi 3 baho va 1-chorakdagi 3 baho bir xil emas.
 * Shuning uchun tepada `Stepper` bilan chorak tanlanadi va PASTDAGI HAMMA
 * NARSA — o'rtachalar ham, davomat ham, sabablar ro'yxati ham — o'sha
 * chorakniki.
 *
 * "So'nggi baholar" FAQAT joriy chorakda ko'rinadi: u joriy haftaning
 * jurnalidan olinadi, ya'ni o'tgan chorakni ochganda o'sha kartochka
 * boshqa davrning ma'lumotini ko'rsatib turgan bo'lardi.
 */
import { useState } from 'react'
import { CalendarCheck2, GraduationCap } from 'lucide-react'
import { Badge, Card, DateChip, EmptyState, Row, StatGrid, Stepper } from '../../components/ui'
import { useAsync } from '../../lib/useAsync'
import { dayMonth } from '../../lib/format'
import { getAttendance, getGrades, getMeta, getWeek } from '../../lib/parentApi'
import { AsyncBlock, gradeTone } from './shared'
import { parseISO } from './weeks'

/** Chorak jamlamalari `{ "1": 5 }` ko'rinishida keladi — kalit satr. */
const perQuarter = (map, quarter) => Number(map?.[String(quarter)] ?? 0)

export function GradesTab({ child }) {
  // `null` — "hali tanlanmagan": ma'lumot kelgach joriy chorak qo'yiladi.
  const [picked, setPicked] = useState(null)

  const state = useAsync(async () => {
    const [report, attendance, meta] = await Promise.all([
      getGrades(child.id),
      getAttendance(child.id),
      getMeta(),
    ])
    // So'nggi baholar joriy haftaning jurnalidan — meta kelmaguncha
    // qaysi hafta ekanini bilmaymiz, shuning uchun ikkinchi qadamda.
    const week = await getWeek(child.id, meta.currentQuarter, meta.currentWeek)
    return { report, attendance, meta, week }
  }, [child.id])

  return (
    <AsyncBlock state={state} loadingLabel="Baholar yuklanmoqda…">
      {({ report, attendance, meta, week }) => {
        const quarters = (meta.quarters ?? []).map((q) => q.quarter).sort((a, b) => a - b)
        const quarter = picked ?? meta.currentQuarter
        const idx = quarters.indexOf(quarter)

        const subjects = (report.subjects ?? []).map((s) => ({
          ...s,
          avg: report.grades?.[s.id]?.[String(quarter)] ?? null,
        }))
        const graded = subjects.filter((s) => s.avg !== null)
        const overall = graded.length
          ? graded.reduce((acc, s) => acc + s.avg, 0) / graded.length
          : null

        const summary = attendance.summary ?? {}
        const rows = (attendance.rows ?? []).filter((r) => r.quarter === quarter)
        const recent = (week ?? []).filter((r) => r.grade !== null && r.grade !== undefined)

        return (
          <>
            <Card>
              <div className="px-2 py-2">
                <Stepper
                  label={`${quarter}-chorak`}
                  onPrev={() => idx > 0 && setPicked(quarters[idx - 1])}
                  onNext={() => idx < quarters.length - 1 && setPicked(quarters[idx + 1])}
                  disabledPrev={idx <= 0}
                  disabledNext={idx >= quarters.length - 1}
                />
              </div>
            </Card>

            <StatGrid
              items={[
                {
                  label: "Fanlar o'rtachasi",
                  value: overall === null ? '—' : overall.toFixed(2),
                  note: `${graded.length} ta fandan`,
                },
                { label: 'Qoldirilgan dars', value: String(perQuarter(summary.missedLessons, quarter)) },
                { label: 'Shundan kasal', value: String(perQuarter(summary.illnessLessons, quarter)) },
                { label: 'Kech qolgan', value: String(perQuarter(summary.lateCount, quarter)) },
              ]}
            />

            {quarter === meta.currentQuarter && (
              <Card title="So'nggi baholar" action={<span className="text-[13px] text-slate-400">{meta.currentWeek}-hafta</span>}>
                {recent.length === 0 ? (
                  <EmptyState
                    icon={<GraduationCap className="h-8 w-8" />}
                    title="Bu haftada baho qo'yilmagan"
                    note="O'qituvchi jurnalga baho kiritishi bilan shu yerda ko'rinadi."
                  />
                ) : (
                  [...recent]
                    .sort((a, b) => (a.date === b.date ? b.period - a.period : a.date < b.date ? 1 : -1))
                    .map((r) => (
                      <Row
                        key={`${r.date}-${r.period}-${r.subjectId}`}
                        lead={<DateChip {...chipOf(r.date)} tone="brand" />}
                        title={r.subjectName}
                        subtitle={r.topic || `${r.period}-dars`}
                        right={<Badge tone={gradeTone(r.grade)}>{r.grade}</Badge>}
                      />
                    ))
                )}
              </Card>
            )}

            <Card title="Fanlar bo'yicha o'rtacha">
              {graded.length === 0 ? (
                <EmptyState
                  icon={<GraduationCap className="h-8 w-8" />}
                  title={`${quarter}-chorakda baho yo'q`}
                  note="Bu chorak uchun hali birorta fandan baho qo'yilmagan."
                />
              ) : (
                graded.map((s) => (
                  <Row
                    key={s.id}
                    title={s.name}
                    right={<Badge tone={gradeTone(s.avg)}>{s.avg.toFixed(2)}</Badge>}
                  />
                ))
              )}
            </Card>

            <Card title="Davomat sabablari">
              {rows.length === 0 ? (
                <EmptyState
                  icon={<CalendarCheck2 className="h-8 w-8" />}
                  title="Darslar qoldirilmagan"
                  note={`${quarter}-chorakda birorta darsdan qolish yoki kechikish belgilanmagan.`}
                />
              ) : (
                rows.map((r) => (
                  <Row
                    key={`${r.date}-${r.period}-${r.subjectId}`}
                    lead={<DateChip {...chipOf(r.date)} tone={r.isLate ? 'neutral' : 'danger'} />}
                    title={r.subjectName}
                    subtitle={`${dayMonth(parseISO(r.date))} · ${r.period}-dars`}
                    right={<Badge tone={r.isLate ? 'brand' : 'danger'}>{r.reasonName}</Badge>}
                  />
                ))
              )}
            </Card>
          </>
        )
      }}
    </AsyncBlock>
  )
}

/** "2026-09-12" → `DateChip` uchun `{ day: 12, month: 'Sen' }`. */
function chipOf(iso) {
  const d = parseISO(iso)
  if (!d) return { day: '—', month: '' }
  return { day: d.getDate(), month: dayMonth(d).split(' ')[1]?.slice(0, 3) ?? '' }
}
