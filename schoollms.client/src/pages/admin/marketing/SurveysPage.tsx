/**
 * ARIZALAR — ommaviy qabul formalarining registri
 * (`docs/modules/sales-marketing.md` §2.7, §6.2; task SM-8).
 *
 * Ekranning asosiy mahsuloti — HAVOLA. Xodim arizani yaratadi, havolani
 * nusxalaydi va uni Telegram postiga yoki Instagram bio'siga qo'yadi; ota-ona
 * shu havolani ochadi va forma to'ldiradi, undan lid yaratiladi. Shuning uchun
 * havola qatorda ko'rinib turadi va bitta bosishda nusxalanadi.
 *
 * `publicUrl` ni SERVER yasaydi (§5.2) — admin `test.` subdomenida o'tirgani
 * uchun klient yasagan manzil noto'g'ri domenga olib borardi.
 *
 * RUXSAT (§5.2, `AdminPermAttribute`): har qanday xodim O'QIY oladi, yozish
 * uchun `marketing` kaliti kerak. Ruxsati yo'q xodimda tugmalar UMUMAN
 * chizilmaydi — "bosdim, 403 oldim" foydalanuvchini chalg'itadi.
 *
 * Marshrut va menyu — SM-12 ning ishi; unga qadar bu sahifa ataylab
 * ochilmaydi.
 */
import { useMemo, useState } from 'react'
import {
  ClipboardList,
  Copy,
  Pencil,
  Plus,
  Power,
  RefreshCw,
  RotateCcw,
  Trash2,
} from 'lucide-react'
import { useAsync } from '@/hooks/useAsync'
import { useAuth } from '@/context/auth-context'
import {
  deleteSurvey,
  getSurveys,
  setSurveyActive,
  surveyError,
  type Survey,
} from '@/api/services/surveys'
import { getStages } from '@/api/services/stages'
import type { Stage } from '@/types'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { Toast } from '@/components/ui/Toast'
import { cn, copyText, formatDate } from '@/lib/utils'
import { SurveyFormModal } from './SurveyFormModal'
import { MarketingTabs } from './MarketingTabs'

/** Bitta xabar oynachasi — muvaffaqiyat ham, xato ham shu yerdan chiqadi. */
interface Notice {
  message: string
  tone: 'success' | 'error'
}

export function SurveysPage() {
  const { user } = useAuth()
  // Xodimda `permissions` bor, admin/superadmin'da — yo'q (ular cheklanmaydi).
  const canWrite = !user?.permissions || user.permissions.includes('marketing')

  const [includeInactive, setIncludeInactive] = useState(false)
  const surveys = useAsync(() => getSurveys(includeInactive), [includeInactive])
  const stages = useAsync(() => getStages(), [])

  const [editing, setEditing] = useState<Survey | null>(null)
  const [formOpen, setFormOpen] = useState(false)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [notice, setNotice] = useState<Notice | null>(null)

  const rows = useMemo(() => surveys.data ?? [], [surveys.data])
  const activeCount = rows.filter((r) => r.isActive).length
  const stageList: Stage[] = stages.data ?? []

  /**
   * Yangi arizada to'liq havolani ko'rsatish uchun domen kerak, lekin uni
   * klient yasay olmaydi. Mavjud arizaning `publicUrl` idan ajratib olamiz —
   * bitta ham bo'lmasa, oyna faqat yo'lni ko'rsatadi va shuni ochiq aytadi.
   */
  const publicOrigin = useMemo(() => {
    const sample = rows.find((r) => r.publicUrl)
    if (!sample) return null
    const base = sample.publicUrl.replace(/\/ariza\/[^/]*$/, '')
    return base && base !== sample.publicUrl ? base : null
  }, [rows])

  const openCreate = () => {
    setEditing(null)
    setFormOpen(true)
  }

  const openEdit = (row: Survey) => {
    setEditing(row)
    setFormOpen(true)
  }

  const copyLink = async (row: Survey) => {
    const ok = await copyText(row.publicUrl)
    setNotice(
      ok
        ? { message: 'Nusxa olindi', tone: 'success' }
        : { message: "Nusxa olinmadi — havolani qo'lda belgilang", tone: 'error' },
    )
  }

  const toggleActive = async (row: Survey) => {
    setBusyId(row.id)
    try {
      await setSurveyActive(row.id, !row.isActive)
      setNotice({
        message: row.isActive
          ? `"${row.name}" yopildi — havola endi ishlamaydi`
          : `"${row.name}" qayta ochildi`,
        tone: 'success',
      })
      surveys.refetch()
    } catch (err) {
      setNotice({ message: surveyError(err).message, tone: 'error' })
    } finally {
      setBusyId(null)
    }
  }

  /** D7: topshirilgan arizasi bor yozuv o'chmaydi — server 409 `survey_in_use`. */
  const remove = async (row: Survey) => {
    if (!confirm(`"${row.name}" arizasini o'chirasizmi? Havola butunlay ishlamay qoladi.`)) return
    setBusyId(row.id)
    try {
      await deleteSurvey(row.id)
      setNotice({ message: `"${row.name}" o'chirildi`, tone: 'success' })
      surveys.refetch()
    } catch (err) {
      setNotice({ message: surveyError(err).message, tone: 'error' })
    } finally {
      setBusyId(null)
    }
  }

  const onSaved = (saved: Survey, created: boolean) => {
    setFormOpen(false)
    setEditing(null)
    setNotice({
      message: created ? `"${saved.name}" yaratildi` : `"${saved.name}" saqlandi`,
      tone: 'success',
    })
    surveys.refetch()
  }

  return (
    <div className="space-y-6">
      <MarketingTabs />
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Arizalar</h1>
          <p className="text-sm text-slate-400">
            Ommaviy qabul formasi: havolani Telegram yoki Instagramga qo'ysangiz, to'ldirgan
            ota-ona lidlar doskasiga tushadi
            {rows.length > 0 && ` · ${rows.length} ta ariza, ${activeCount} tasi faol`}
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <label className="flex cursor-pointer items-center gap-2 text-sm text-slate-600">
            <input
              type="checkbox"
              checked={includeInactive}
              onChange={(e) => setIncludeInactive(e.target.checked)}
              className="h-4 w-4 rounded border-slate-300 accent-brand-600"
            />
            Yopilganlarni ko'rsatish
          </label>
          {canWrite && (
            <Button onClick={openCreate}>
              <Plus className="h-4 w-4" /> Yangi ariza
            </Button>
          )}
        </div>
      </div>

      {!canWrite && (
        <p className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-500">
          Sizda "Sotuv va marketing" ruxsati yo'q — ro'yxat faqat ko'rish uchun ochiq.
        </p>
      )}

      {surveys.loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : surveys.error ? (
        <Card className="flex flex-col items-center justify-center gap-3 py-16 text-center">
          <p className="text-sm font-medium text-slate-600">Ro'yxatni ochib bo'lmadi</p>
          <p className="max-w-md text-sm text-slate-400">{surveys.error}</p>
          <Button variant="secondary" onClick={surveys.refetch}>
            <RefreshCw className="h-4 w-4" /> Qayta urinish
          </Button>
        </Card>
      ) : rows.length === 0 ? (
        <Card className="flex flex-col items-center justify-center gap-3 py-16 text-center">
          <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-slate-100">
            <ClipboardList className="h-7 w-7 text-slate-400" />
          </div>
          <p className="text-sm font-medium text-slate-600">
            {includeInactive ? "Hali birorta ariza yaratilmagan" : "Faol ariza yo'q"}
          </p>
          <p className="max-w-md text-sm text-slate-400">
            Ariza — bu ota-ona to'ldiradigan ommaviy forma. Uni yarating, havolasini nusxalab
            Telegram kanalingizga qo'ying: kelgan har bir javob lidlar doskasida paydo bo'ladi.
          </p>
          {canWrite && (
            <Button onClick={openCreate}>
              <Plus className="h-4 w-4" /> Birinchi arizani yaratish
            </Button>
          )}
        </Card>
      ) : (
        <Card className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="w-10 px-4 py-3">#</th>
                  <th className="px-4 py-3">Nomi</th>
                  <th className="px-4 py-3">Holati</th>
                  <th className="px-4 py-3 text-right">Arizalar</th>
                  <th className="px-4 py-3">Oxirgi ariza</th>
                  <th className="px-4 py-3">Ommaviy havola</th>
                  {canWrite && <th className="px-4 py-3 text-right">Amallar</th>}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((row, i) => (
                  <tr key={row.id} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3 text-slate-400">{i + 1}</td>
                    <td className="px-4 py-3">
                      <span
                        className="block max-w-[16rem] truncate font-medium text-slate-800"
                        title={row.name}
                      >
                        {row.name}
                      </span>
                      {row.subtitle && (
                        <span
                          className="block max-w-[16rem] truncate text-xs text-slate-400"
                          title={row.subtitle}
                        >
                          {row.subtitle}
                        </span>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      <span
                        className={cn(
                          'rounded-full px-2 py-0.5 text-xs font-medium',
                          row.isActive
                            ? 'bg-emerald-50 text-emerald-700'
                            : 'bg-slate-100 text-slate-500',
                        )}
                      >
                        {row.isActive ? 'Faol' : 'Yopiq'}
                      </span>
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-right text-slate-600">
                      {row.submissionCount}
                      {row.leadCount !== row.submissionCount && (
                        <span className="ml-1 text-xs text-slate-400">
                          ({row.leadCount} lid)
                        </span>
                      )}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 text-slate-500">
                      {row.lastSubmissionAt ? formatDate(row.lastSubmissionAt) : '—'}
                    </td>
                    <td className="px-4 py-3">
                      <span className="inline-flex max-w-[18rem] items-center gap-1.5 rounded-lg bg-slate-50 px-2.5 py-1.5">
                        <a
                          href={row.publicUrl}
                          target="_blank"
                          rel="noreferrer"
                          title={row.publicUrl}
                          className="truncate text-xs text-brand-600 hover:text-brand-700"
                        >
                          {row.publicUrl}
                        </a>
                        <button
                          type="button"
                          title="Havoladan nusxa olish"
                          onClick={() => void copyLink(row)}
                          className="shrink-0 rounded p-0.5 text-slate-400 transition-colors hover:text-slate-700"
                        >
                          <Copy className="h-3.5 w-3.5" />
                        </button>
                      </span>
                    </td>
                    {canWrite && (
                      <td className="px-4 py-3">
                        <div className="flex items-center justify-end gap-0.5">
                          <button
                            type="button"
                            title="Tahrirlash"
                            onClick={() => openEdit(row)}
                            className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                          >
                            <Pencil className="h-4 w-4" />
                          </button>
                          <button
                            type="button"
                            disabled={busyId === row.id}
                            title={row.isActive ? 'Yopish' : 'Qayta ochish'}
                            onClick={() => void toggleActive(row)}
                            className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 disabled:cursor-not-allowed disabled:text-slate-200"
                          >
                            {row.isActive ? (
                              <Power className="h-4 w-4" />
                            ) : (
                              <RotateCcw className="h-4 w-4" />
                            )}
                          </button>
                          <button
                            type="button"
                            disabled={busyId === row.id || row.submissionCount > 0}
                            title={
                              row.submissionCount > 0
                                ? "Bu arizada topshirilgan so'rovlar bor — o'chirib bo'lmaydi. Uni yopib qo'ying."
                                : "O'chirish"
                            }
                            onClick={() => void remove(row)}
                            className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600 disabled:cursor-not-allowed disabled:text-slate-200 disabled:hover:bg-transparent"
                          >
                            <Trash2 className="h-4 w-4" />
                          </button>
                        </div>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>
      )}

      {formOpen && canWrite && (
        <SurveyFormModal
          editing={editing}
          stages={stageList}
          publicOrigin={publicOrigin}
          onClose={() => {
            setFormOpen(false)
            setEditing(null)
          }}
          onSaved={onSaved}
        />
      )}

      <Toast
        message={notice?.message ?? null}
        tone={notice?.tone ?? 'success'}
        onClose={() => setNotice(null)}
      />
    </div>
  )
}
