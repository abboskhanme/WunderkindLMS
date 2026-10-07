import { useCallback, useEffect, useState } from 'react'
import { Briefcase, Copy, ExternalLink, FileText, Plus, Search, Settings2, UserPlus } from 'lucide-react'
import {
  getApplicants,
  getRecruitmentCounts,
  getVacancies,
  setVacancyOpen,
  PIPELINE,
  STAGE_LABEL,
  type ApplicantRow,
  type RecruitmentCounts,
  type Vacancy,
} from '@/api/services/recruitment'
import { hrErrorMessage } from '@/api/services/hrTasks'
import { cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Toast } from '@/components/ui/Toast'
import { fmtDate } from '@/components/lesson-analysis/format'
import { ApplicantModal } from './ApplicantModal'
import { ApplicantFormModal } from './ApplicantFormModal'
import { VacancyModal } from './VacancyModal'
import { OnboardingStepsModal } from './OnboardingStepsModal'

type Tab = 'new' | 'pipeline' | 'onboarding' | 'archive' | 'vacancies'

const daysSince = (iso: string) => Math.max(0, Math.floor((Date.now() - new Date(iso).getTime()) / 86_400_000))

/**
 * Ishga qabul (client, 2026-10-07): the three sections the client named — applications left,
 * people going through the five hiring stages, and new hires in their 30-day adaptation — plus
 * the archive and the vacancies with their public apply links.
 */
export function RecruitmentPage() {
  const [tab, setTab] = useState<Tab>('new')
  const [search, setSearch] = useState('')
  const [rows, setRows] = useState<ApplicantRow[] | null>(null)
  const [vacancies, setVacancies] = useState<Vacancy[]>([])
  const [counts, setCounts] = useState<RecruitmentCounts | null>(null)
  const [openId, setOpenId] = useState<string | null>(null)
  const [adding, setAdding] = useState(false)
  const [vacancyEdit, setVacancyEdit] = useState<Vacancy | 'new' | null>(null)
  const [stepsOpen, setStepsOpen] = useState(false)
  const [toast, setToast] = useState<string | null>(null)
  const [error, setError] = useState('')

  const reload = useCallback(() => {
    getRecruitmentCounts().then(setCounts).catch(() => setCounts(null))
    getVacancies().then(setVacancies).catch(() => setVacancies([]))
    if (tab === 'vacancies') return
    getApplicants(tab, search.trim())
      .then(setRows)
      .catch((e) => {
        setRows([])
        setError(hrErrorMessage(e, "Yuklab bo'lmadi"))
      })
  }, [tab, search])

  useEffect(() => {
    const t = setTimeout(reload, search ? 300 : 0)
    return () => clearTimeout(t)
  }, [reload, search])

  const pipelineCount = counts ? Object.values(counts.pipeline).reduce((s, n) => s + n, 0) : 0
  const tabs: { key: Tab; label: string; count?: number }[] = [
    { key: 'new', label: 'Arizalar', count: counts?.new },
    { key: 'pipeline', label: 'Ishga olish jarayoni', count: pipelineCount },
    { key: 'onboarding', label: 'Adaptatsiya', count: counts?.onboarding },
    { key: 'archive', label: 'Arxiv' },
    { key: 'vacancies', label: 'Vakansiyalar', count: vacancies.filter((v) => v.status === 'open').length },
  ]

  const changed = (message: string) => {
    setToast(message)
    reload()
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Ishga qabul</h1>
          <p className="text-sm text-slate-400">
            Ariza → CV → Test → Demo → HR suhbat → Direktor suhbati → 30 kunlik adaptatsiya
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          {tab === 'onboarding' && (
            <Button variant="secondary" onClick={() => setStepsOpen(true)}>
              <Settings2 className="h-4 w-4" /> Adaptatsiya bosqichlari
            </Button>
          )}
          {tab === 'vacancies' ? (
            <Button onClick={() => setVacancyEdit('new')}>
              <Plus className="h-4 w-4" /> Yangi vakansiya
            </Button>
          ) : (
            <Button onClick={() => setAdding(true)}>
              <UserPlus className="h-4 w-4" /> Nomzod qo'shish
            </Button>
          )}
        </div>
      </div>

      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap gap-1 rounded-xl bg-slate-100 p-1">
          {tabs.map((t) => (
            <button
              key={t.key}
              type="button"
              onClick={() => {
                setRows(null)
                setTab(t.key)
              }}
              className={cn(
                'rounded-lg px-3 py-1.5 text-sm font-medium',
                tab === t.key ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
              )}
            >
              {t.label}
              {t.count ? <span className="ml-1.5 rounded-md bg-brand-50 px-1.5 text-xs text-brand-700">{t.count}</span> : null}
            </button>
          ))}
        </div>
        {tab !== 'vacancies' && (
          <div className="relative">
            <Search className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Ism yoki telefon..."
              className="w-56 rounded-lg border border-slate-200 bg-white py-2 pl-8 pr-3 text-sm text-slate-700 outline-none focus:border-brand-400"
            />
          </div>
        )}
      </div>

      {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}

      {tab === 'vacancies' ? (
        <VacancyList
          vacancies={vacancies}
          onEdit={setVacancyEdit}
          onToggle={async (v) => {
            try {
              await setVacancyOpen(v.id, v.status !== 'open')
              changed(v.status === 'open' ? 'Vakansiya yopildi' : 'Vakansiya ochildi')
            } catch (e) {
              setError(hrErrorMessage(e, 'Bajarib bo\'lmadi'))
            }
          }}
          onCopied={() => setToast('Havola nusxalandi')}
        />
      ) : rows === null ? (
        <Loader label="Yuklanmoqda..." />
      ) : tab === 'pipeline' ? (
        <Board rows={rows} onOpen={setOpenId} />
      ) : tab === 'onboarding' ? (
        <OnboardingList rows={rows} onOpen={setOpenId} />
      ) : rows.length === 0 ? (
        <Card>
          <p className="py-10 text-center text-slate-400">
            {tab === 'new' ? "Yangi ariza yo'q — vakansiya havolasini tarqating" : "Arxiv bo'sh"}
          </p>
        </Card>
      ) : (
        <ApplicantTable rows={rows} archive={tab === 'archive'} onOpen={setOpenId} />
      )}

      {openId && <ApplicantModal id={openId} onClose={() => setOpenId(null)} onChanged={changed} />}
      {adding && (
        <ApplicantFormModal
          vacancies={vacancies}
          onClose={() => setAdding(false)}
          onSaved={(id) => {
            setAdding(false)
            setTab('new')
            changed("Nomzod qo'shildi")
            setOpenId(id)
          }}
        />
      )}
      {vacancyEdit && (
        <VacancyModal
          vacancy={vacancyEdit === 'new' ? null : vacancyEdit}
          onClose={() => setVacancyEdit(null)}
          onSaved={() => {
            setVacancyEdit(null)
            changed('Vakansiya saqlandi')
          }}
        />
      )}
      {stepsOpen && (
        <OnboardingStepsModal
          onClose={() => setStepsOpen(false)}
          onSaved={() => {
            setStepsOpen(false)
            setToast('Adaptatsiya bosqichlari saqlandi')
          }}
        />
      )}
      <Toast message={toast} onClose={() => setToast(null)} />
    </div>
  )
}

function ApplicantTable({ rows, archive, onOpen }: { rows: ApplicantRow[]; archive: boolean; onOpen: (id: string) => void }) {
  return (
    <Card className="overflow-x-auto p-0">
      <table className="w-full min-w-[760px] text-sm">
        <thead>
          <tr className="border-b border-slate-100 text-left text-xs font-medium uppercase tracking-wide text-slate-400">
            <th className="px-4 py-3">Nomzod</th>
            <th className="px-4 py-3">Vakansiya</th>
            <th className="px-4 py-3">{archive ? 'Natija' : 'Manba'}</th>
            <th className="px-4 py-3">Sana</th>
            <th className="px-4 py-3 text-center">CV</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((r) => (
            <tr key={r.id} onClick={() => onOpen(r.id)} className="cursor-pointer border-b border-slate-50 last:border-0 hover:bg-slate-50/60">
              <td className="px-4 py-3">
                <div className="font-medium text-slate-800">{r.fullName}</div>
                <div className="text-xs text-slate-400">{r.phone}</div>
              </td>
              <td className="px-4 py-3 text-slate-600">{r.vacancyTitle ?? r.position ?? '—'}</td>
              <td className="px-4 py-3">
                {archive ? (
                  <span
                    className={cn(
                      'rounded-md px-2 py-0.5 text-xs font-medium',
                      r.stage === 'adapted' ? 'bg-emerald-50 text-emerald-700' : 'bg-red-50 text-red-600',
                    )}
                    title={r.rejectReason ?? undefined}
                  >
                    {r.stageLabel}
                    {r.rejectedAtStage && ` · ${STAGE_LABEL[r.rejectedAtStage]}`}
                  </span>
                ) : (
                  <span className="text-xs text-slate-500">{r.source === 'public' ? 'Sayt orqali' : "Qo'lda"}</span>
                )}
              </td>
              <td className="px-4 py-3 text-slate-500">{fmtDate(archive ? r.stageChangedAt : r.createdAt)}</td>
              <td className="px-4 py-3 text-center">{r.hasCv ? <FileText className="mx-auto h-4 w-4 text-brand-600" /> : '—'}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </Card>
  )
}

/** The five hiring stages side by side (its own simple board — not the protected Leads board). */
function Board({ rows, onOpen }: { rows: ApplicantRow[]; onOpen: (id: string) => void }) {
  return (
    <div className="flex gap-3 overflow-x-auto pb-2">
      {PIPELINE.map((stage, i) => {
        const items = rows.filter((r) => r.stage === stage)
        return (
          <div key={stage} className="flex w-64 shrink-0 flex-col rounded-2xl bg-slate-100/70 p-2">
            <div className="flex items-center justify-between px-2 py-1.5">
              <span className="text-sm font-semibold text-slate-700">
                <span className="mr-1 text-slate-400">{i + 1}.</span>
                {STAGE_LABEL[stage]}
              </span>
              <span className="rounded-md bg-white px-1.5 text-xs font-medium text-slate-500">{items.length}</span>
            </div>
            <div className="space-y-2">
              {items.map((r) => (
                <button
                  key={r.id}
                  type="button"
                  onClick={() => onOpen(r.id)}
                  className="w-full rounded-xl bg-white p-3 text-left shadow-sm transition hover:shadow"
                >
                  <div className="font-medium text-slate-800">{r.fullName}</div>
                  <div className="truncate text-xs text-slate-500">{r.vacancyTitle ?? r.position ?? '—'}</div>
                  <div className="mt-2 flex items-center justify-between text-xs text-slate-400">
                    <span>{daysSince(r.stageChangedAt)} kun shu bosqichda</span>
                    {r.lastScore !== null && <span className="font-semibold text-brand-700">{r.lastScore} ball</span>}
                  </div>
                </button>
              ))}
              {items.length === 0 && <p className="px-2 py-4 text-center text-xs text-slate-400">Bo'sh</p>}
            </div>
          </div>
        )
      })}
    </div>
  )
}

function OnboardingList({ rows, onOpen }: { rows: ApplicantRow[]; onOpen: (id: string) => void }) {
  if (rows.length === 0)
    return (
      <Card>
        <p className="py-10 text-center text-slate-400">Hozir adaptatsiyada hech kim yo'q</p>
      </Card>
    )
  return (
    <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
      {rows.map((r) => {
        const day = r.hiredOn ? Math.min(30, Math.max(1, daysSince(r.hiredOn) + 1)) : null
        const pct = r.stepsTotal ? Math.round((100 * r.stepsDone) / r.stepsTotal) : 0
        return (
          <button key={r.id} type="button" onClick={() => onOpen(r.id)} className="text-left">
            <Card className="space-y-3 transition hover:shadow-md">
              <div className="flex items-start justify-between gap-2">
                <div className="min-w-0">
                  <div className="font-semibold text-slate-800">{r.fullName}</div>
                  <div className="truncate text-xs text-slate-500">{r.vacancyTitle ?? r.position ?? '—'}</div>
                </div>
                {day !== null && (
                  <span className="shrink-0 rounded-md bg-brand-50 px-2 py-0.5 text-xs font-semibold text-brand-700">{day}/30 kun</span>
                )}
              </div>
              <div>
                <div className="h-2 overflow-hidden rounded-full bg-slate-100">
                  <div className="h-full rounded-full bg-emerald-500" style={{ width: `${pct}%` }} />
                </div>
                <div className="mt-1.5 flex justify-between text-xs text-slate-500">
                  <span>
                    {r.stepsDone}/{r.stepsTotal} bosqich
                  </span>
                  {r.stepsOverdue > 0 && <span className="font-medium text-red-600">{r.stepsOverdue} ta kechikkan</span>}
                </div>
              </div>
            </Card>
          </button>
        )
      })}
    </div>
  )
}

function VacancyList({
  vacancies,
  onEdit,
  onToggle,
  onCopied,
}: {
  vacancies: Vacancy[]
  onEdit: (v: Vacancy) => void
  onToggle: (v: Vacancy) => void
  onCopied: () => void
}) {
  const origin = typeof window === 'undefined' ? '' : window.location.origin
  return (
    <div className="space-y-3">
      <Card className="flex flex-wrap items-center justify-between gap-2 bg-brand-50/40">
        <p className="text-sm text-slate-600">
          Barcha ochiq vakansiyalar sahifasi: <span className="font-medium text-slate-800">{origin}/vakansiya</span>
        </p>
        <Button
          variant="secondary"
          onClick={() => {
            void navigator.clipboard?.writeText(`${origin}/vakansiya`)
            onCopied()
          }}
        >
          <Copy className="h-4 w-4" /> Nusxalash
        </Button>
      </Card>
      {vacancies.length === 0 ? (
        <Card>
          <p className="py-10 text-center text-slate-400">Vakansiya yo'q</p>
        </Card>
      ) : (
        vacancies.map((v) => {
          const link = `${origin}/vakansiya/${v.slug}`
          return (
            <Card key={v.id} className="flex flex-col gap-3">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="flex min-w-0 items-start gap-3">
                  <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-brand-50 text-brand-600">
                    <Briefcase className="h-5 w-5" />
                  </div>
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <h2 className="font-semibold text-slate-800">{v.title}</h2>
                      <span
                        className={cn(
                          'rounded-md px-2 py-0.5 text-xs font-medium',
                          v.status === 'open' ? 'bg-emerald-50 text-emerald-700' : 'bg-slate-100 text-slate-500',
                        )}
                      >
                        {v.status === 'open' ? 'Ochiq' : 'Yopilgan'}
                      </span>
                    </div>
                    <p className="text-xs text-slate-400">
                      {[v.department, `${v.applicants} ta nomzod`, v.active ? `${v.active} ta jarayonda` : null, fmtDate(v.createdAt)]
                        .filter(Boolean)
                        .join(' · ')}
                    </p>
                  </div>
                </div>
              </div>
              {v.status === 'open' && (
                <div className="flex flex-wrap items-center gap-2 rounded-lg bg-slate-50 px-3 py-2 text-sm">
                  <span className="min-w-0 flex-1 truncate text-slate-600">{link}</span>
                  <button
                    type="button"
                    onClick={() => {
                      void navigator.clipboard?.writeText(link)
                      onCopied()
                    }}
                    className="inline-flex items-center gap-1 text-xs font-medium text-brand-700"
                  >
                    <Copy className="h-3.5 w-3.5" /> Nusxalash
                  </button>
                  <a href={link} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1 text-xs font-medium text-slate-500">
                    <ExternalLink className="h-3.5 w-3.5" /> Ochish
                  </a>
                </div>
              )}
              <div className="flex gap-2">
                <Button variant="secondary" onClick={() => onEdit(v)}>
                  Tahrirlash
                </Button>
                <Button variant="ghost" onClick={() => onToggle(v)}>
                  {v.status === 'open' ? 'Yopish' : 'Qayta ochish'}
                </Button>
              </div>
            </Card>
          )
        })
      )}
    </div>
  )
}
