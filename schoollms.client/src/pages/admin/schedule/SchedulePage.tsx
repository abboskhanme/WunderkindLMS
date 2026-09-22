import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { ChevronRight, Users2 } from 'lucide-react'
import { getGroups, type StudyGroupListItem } from '@/api/services/groups'
import {
  getGroupLessonsSwitch,
  setGroupLessonsSwitch,
  type GroupLessonsSwitch,
} from '@/api/services/groupLessons'
import { MasterScheduleGrid } from './MasterScheduleGrid'
import { Loader } from '@/components/ui/Loader'

/**
 * Jadval egasini tanlash — SINF yoki O'QUV GURUHI
 * (`docs/modules/students-parity.md` §2.1.4, G-11).
 *
 * Guruhlar ro'yxati faqat guruh darslari O'CHIRGICHI yoqilganda ko'rinadi:
 * o'chiq paytda guruh jadvalini haftaga biriktirib bo'lmaydi va uni shu
 * yerda ko'rsatish foydalanuvchini ish qilib bo'lmaydigan ekranga olib
 * borardi. O'chirgichning o'zi ham shu sahifada — uni yoqadigan odam aynan
 * shu yerda ishlaydi.
 */
export function SchedulePage() {
  const navigate = useNavigate()
  const [groups, setGroups] = useState<StudyGroupListItem[]>([])
  const [flag, setFlag] = useState<GroupLessonsSwitch | null>(null)
  const [loading, setLoading] = useState(true)
  const [switching, setSwitching] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getGroupLessonsSwitch()
      .catch(() => null)
      .then(async (sw) => {
        setFlag(sw)
        if (sw?.enabled) setGroups(await getGroups().catch(() => []))
      })
      .finally(() => setLoading(false))
  }, [])

  const toggle = async () => {
    if (!flag) return
    const next = !flag.enabled
    if (
      next &&
      !confirm(
        "Guruh darslarini yoqasizmi?\n\nShundan keyin guruh jadvallari haftaga biriktiriladi va " +
          "jurnal, davomat, hisobot, MAOSH hamda turniket raqamlari guruh darslarini ham " +
          "hisobga oladi. Avval cut-over tekshiruvini bajaring.",
      )
    )
      return
    setSwitching(true)
    setError(null)
    try {
      const saved = await setGroupLessonsSwitch(next)
      setFlag(saved)
      setGroups(saved.enabled ? await getGroups().catch(() => []) : [])
    } catch (e) {
      setError(
        (e as { response?: { data?: { message?: string } } })?.response?.data?.message ??
          "O'chirgichni o'zgartirib bo'lmadi — bu amal faqat tizim egasiga ochiq.",
      )
    } finally {
      setSwitching(false)
    }
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">Dars jadvali yaratish</h1>
        <p className="text-sm text-slate-400">
          Hamma sinf bir jadvalda — katakni bosib fan va o'qituvchini qo'ying. Sinf nomi bosilsa —
          o'sha sinfning shablonlari va haftalari
        </p>
      </div>

      {flag && (
        <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-slate-200/80 bg-white p-4">
          <div>
            <p className="font-semibold text-slate-800">Guruh darslari</p>
            <p className="text-xs text-slate-400">
              {flag.enabled
                ? `Yoqilgan · ${flag.activeGroups} ta faol guruh, ${flag.groupWeekAssignments} ta biriktirilgan hafta`
                : `O'chiq · ${flag.activeGroups} ta guruh bor, lekin birorta dars, hisobot yoki maosh ularni ko'rmaydi`}
            </p>
          </div>
          <button
            type="button"
            onClick={toggle}
            disabled={switching}
            className={
              flag.enabled
                ? 'rounded-lg border border-slate-200 px-3 py-2 text-sm font-medium text-slate-600 transition-colors hover:bg-slate-50 disabled:opacity-50'
                : 'rounded-lg bg-brand-600 px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-brand-700 disabled:opacity-50'
            }
          >
            {flag.enabled ? "O'chirish" : 'Yoqish'}
          </button>
        </div>
      )}

      {error && (
        <p className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          {error}
        </p>
      )}

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-6">
          {/* Hamma sinf bitta jadvalda — katak bosilsa fan/o'qituvchi oynasi (mijoz, 2026-09-23). */}
          <MasterScheduleGrid />

          {flag?.enabled && groups.length > 0 && (
            <div className="space-y-3">
              <h2 className="text-sm font-semibold uppercase tracking-wide text-slate-400">
                O'quv guruhlari
              </h2>
              <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
                {groups.map((g) => (
                  <button
                    key={g.id}
                    onClick={() => navigate(`/admin/schedule/manage/${g.id}`)}
                    className="flex items-center justify-between gap-3 rounded-2xl border border-slate-200/80 bg-white p-4 text-left shadow-sm transition-colors hover:border-brand-300 hover:bg-brand-50/40"
                  >
                    <div className="flex items-center gap-3">
                      <div className="flex h-11 w-11 items-center justify-center rounded-xl bg-violet-50 text-violet-600">
                        <Users2 className="h-5 w-5" />
                      </div>
                      <div>
                        <p className="font-semibold text-slate-800">{g.name}</p>
                        <p className="text-xs text-slate-400">
                          {g.subjectName} · {g.memberCount} ta o'quvchi
                        </p>
                      </div>
                    </div>
                    <ChevronRight className="h-5 w-5 text-slate-300" />
                  </button>
                ))}
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  )
}
