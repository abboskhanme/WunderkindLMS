/**
 * Alohida qo'ng'iroq jadvallari ro'yxati — "Dars vaqtlari" bo'limida asosiy jadval ostida
 * (2026-10-11). Biriktirilmagan sinf va guruhlar asosiy jadval bo'yicha qoladi.
 */
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Clock, Pencil, Plus, Trash2 } from 'lucide-react'
import type { BellSchedule, BellSchedulesOverview } from '@/types'
import { deleteBellSchedule, getBellSchedules } from '@/api/services/settings'
import { billingErrorMessage } from '@/api/services/billingError'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { Toast } from '@/components/ui/Toast'
import { BellScheduleModal } from './BellScheduleModal'

const SHOWN_OWNERS = 10

export function BellSchedulesSettings() {
  const [data, setData] = useState<BellSchedulesOverview | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState<BellSchedule | 'new' | null>(null)
  const [toast, setToast] = useState<string | null>(null)

  const load = useCallback(
    () =>
      getBellSchedules()
        .then((d) => {
          setData(d)
          setError(null)
        })
        .catch((e: unknown) => setError(billingErrorMessage(e, "Jadvallarni yuklab bo'lmadi"))),
    [],
  )

  useEffect(() => {
    void load()
  }, [load])

  const ownerName = useMemo(() => new Map((data?.owners ?? []).map((o) => [o.id, o.name])), [data])

  const remove = async (s: BellSchedule) => {
    if (!confirm(`"${s.name}" jadvalini o'chirasizmi? Uning sinf va guruhlari asosiy jadvalga qaytadi.`)) return
    try {
      await deleteBellSchedule(s.id)
      setToast("Jadval o'chirildi")
      await load()
    } catch (e) {
      setError(billingErrorMessage(e, "Jadvalni o'chirib bo'lmadi"))
    }
  }

  return (
    <Card>
      <div className="mb-1 flex flex-wrap items-center justify-between gap-2">
        <h2 className="font-semibold text-slate-800">Alohida jadvallar</h2>
        <Button variant="secondary" onClick={() => setEditing('new')} disabled={!data}>
          <Plus className="h-4 w-4" /> Yangi jadval
        </Button>
      </div>
      <p className="mb-4 text-sm text-slate-500">
        Sinflarda dars vaqtlari har xil bo&apos;lsa — masalan, boshlang&apos;ich sinflar uchun alohida jadval tuzib, sinflarni
        biriktiring. Biriktirilmagan sinf va guruhlar asosiy jadval bo&apos;yicha.
      </p>

      {!data && !error ? (
        <Loader label="Yuklanmoqda..." />
      ) : error && !data ? (
        <p className="py-4 text-sm text-red-600">{error}</p>
      ) : data && data.schedules.length === 0 ? (
        <div className="rounded-xl border border-dashed border-slate-200 px-4 py-6 text-center text-sm text-slate-400">
          Hozircha alohida jadval yo&apos;q — hamma sinf asosiy jadval bo&apos;yicha.
        </div>
      ) : (
        <ul className="divide-y divide-slate-100 rounded-xl border border-slate-100">
          {data?.schedules.map((s) => {
            const first = s.periods[0]
            const last = s.periods[s.periods.length - 1]
            const names = s.ownerIds.map((id) => ownerName.get(id)).filter((n): n is string => !!n)
            return (
              <li key={s.id} className="flex items-start gap-3 px-4 py-3">
                <div className="mt-0.5 rounded-lg bg-brand-50 p-2 text-brand-700">
                  <Clock className="h-4 w-4" />
                </div>
                <div className="min-w-0 flex-1">
                  <p className="font-medium text-slate-800">{s.name}</p>
                  <p className="text-xs text-slate-500">
                    {s.periods.length} ta dars
                    {first && last ? ` · ${first.startTime}–${last.endTime}` : ''}
                  </p>
                  <div className="mt-1.5 flex flex-wrap gap-1">
                    {names.length === 0 ? (
                      <span className="text-xs text-amber-700">Hech qaysi sinfga biriktirilmagan</span>
                    ) : (
                      <>
                        {names.slice(0, SHOWN_OWNERS).map((n) => (
                          <span key={n} className="rounded-md bg-slate-100 px-1.5 py-0.5 text-[11px] font-medium text-slate-600">
                            {n}
                          </span>
                        ))}
                        {names.length > SHOWN_OWNERS && (
                          <span className="px-1 py-0.5 text-[11px] text-slate-400">+{names.length - SHOWN_OWNERS}</span>
                        )}
                      </>
                    )}
                  </div>
                </div>
                <div className="flex shrink-0 gap-1">
                  <button
                    type="button"
                    onClick={() => setEditing(s)}
                    title="Tahrirlash"
                    className="rounded-lg p-2 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                  >
                    <Pencil className="h-4 w-4" />
                  </button>
                  <button
                    type="button"
                    onClick={() => void remove(s)}
                    title="O'chirish"
                    className="rounded-lg p-2 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                  >
                    <Trash2 className="h-4 w-4" />
                  </button>
                </div>
              </li>
            )
          })}
        </ul>
      )}
      {error && data && <p className="mt-3 text-sm text-red-600">{error}</p>}

      {editing && data && (
        <BellScheduleModal
          schedule={editing === 'new' ? null : editing}
          schedules={data.schedules}
          owners={data.owners}
          defaultTimes={data.default}
          onClose={() => setEditing(null)}
          onSaved={(saved) => {
            setEditing(null)
            setToast(`"${saved.name}" saqlandi`)
            void load()
          }}
        />
      )}
      <Toast message={toast} onClose={() => setToast(null)} />
    </Card>
  )
}
