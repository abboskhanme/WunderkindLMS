import { useState } from 'react'
import { BellRing, CheckCircle2, FileSpreadsheet, Siren, Users2, Wallet } from 'lucide-react'
import { ScreenHeader } from '../components/ui'
import Avatar from '../components/Avatar'
import AppButton from '../components/AppButton'
import { AsyncView } from '../components/State'
import EmptyState, { EmptyIllustration } from '../components/EmptyState'
import { useFetch } from '../lib/session'
import { api } from '../lib/api'
import { MONTH_NAMES } from '../lib/seasonal'

/** "2026-09-01" → "sentabr". */
const monthName = (iso) => (MONTH_NAMES[Number(String(iso).slice(5, 7)) - 1] || '').toLowerCase()

// Homeroom — parent-arrived students highlighted + sorted to top, handover button.
export default function HomeroomScreen({ onBack }) {
  const home = useFetch(() => api.homeroom(), [])
  const picks = useFetch(() => api.pickups(), [])
  // Qarzdorlik (mijoz, 2026-10-12) — alohida so'rov: yiqilsa ham ro'yxat va topshirish ishlayveradi.
  const debts = useFetch(() => api.homeroomDebts().catch(() => null), [])
  const debtById = {}
  for (const p of debts.data?.pupils || []) debtById[p.studentId] = p
  // Qizil ro'yxat (2026-10-15) — faqat ko'rish; holatni Telegram ilovasida (Bugun → Qizil ro'yxat) belgilaydi.
  const redList = useFetch(() => api.homeroomRedList().catch(() => null), [])
  const redById = {}
  for (const item of redList.data?.cases || []) redById[item.case.studentId] = item.case
  const redCount = redList.data?.cases?.length || 0
  const redReason = (c) => {
    const what = redList.data.countExcused ? 'kelmadi' : 'sababsiz kelmadi'
    return c.trigger === 'consecutive'
      ? `${c.streak} kun ketma-ket ${what}`
      : `${redList.data.windowDays} kunda ${c.windowCount} kun ${what}`
  }
  const [excelBusy, setExcelBusy] = useState(false)
  const downloadExcel = async () => {
    setExcelBusy(true)
    try {
      await api.downloadHomeroomDebts()
    } catch (e) {
      alert((e && e.message) || "Excel faylni olib bo'lmadi")
    } finally {
      setExcelBusy(false)
    }
  }
  const [busy, setBusy] = useState(null) // studentId or pickupId being mutated

  const reload = () => { home.reload(); picks.reload() }

  const accept = async (id) => {
    setBusy(id)
    try {
      await api.acceptPickup(id)
      reload()
    } catch (e) {
      alert((e && e.message) || 'Xatolik')
    } finally {
      setBusy(null)
    }
  }

  const handover = async (studentId) => {
    setBusy(studentId)
    try {
      await api.handover(studentId)
      reload()
    } catch (e) {
      alert((e && e.message) || 'Xatolik')
    } finally {
      setBusy(null)
    }
  }

  const data = home.data
  const students = data?.students || []
  // Pending pickups per student (status==='pending') keyed by studentId → pickup id.
  const pendingPickupByStudent = {}
  for (const p of picks.data || []) {
    if (p.status === 'pending') pendingPickupByStudent[p.studentId] = p.id
  }

  const rank = (s) => (s.status === 'accepted' ? 2 : s.hasPendingPickup ? 0 : 1)
  const sorted = [...students].sort((a, b) => rank(a) - rank(b) || a.fullName.localeCompare(b.fullName))
  // Bir nechta sinf rahbari (2026-10-09) — har o'quvchining sinfi ko'rsatiladi.
  const manyClasses = new Set(students.map((s) => s.className).filter(Boolean)).size > 1
  const pending = sorted.filter((s) => s.hasPendingPickup && s.status !== 'accepted').length

  return (
    <div className="h-full flex flex-col bg-bg">
      <ScreenHeader
        title="Sinf rahbarligi"
        subtitle={data ? `${data.className || ''} ${manyClasses ? 'sinflari' : 'sinfi'} · ${sorted.length} o'quvchi` : undefined}
        onBack={onBack}
      />

      <AsyncView query={home} loadingLabel="Yuklanmoqda…">
        {sorted.length === 0 ? (
          <EmptyState
            icon={<EmptyIllustration><Users2 size={30} /></EmptyIllustration>}
            title="O'quvchilar yo'q"
            subtitle="Sizda rahbar sinf biriktirilmagan yoki o'quvchilar topilmadi"
          />
        ) : (
          <>
            <div className="px-4 py-2">
              <div
                className="px-3 py-2.5 rounded-xl flex items-start gap-2.5"
                style={{ background: pending > 0 ? '#F59E0B1F' : 'var(--primary-soft)' }}
              >
                <BellRing size={18} className={`shrink-0 mt-0.5 ${pending > 0 ? 'text-warning' : 'text-primary'}`} />
                <span className="text-[12.5px] font-medium leading-snug" style={{ color: pending > 0 ? '#F59E0B' : 'var(--primary)' }}>
                  {pending > 0
                    ? `${pending} o'quvchining ota-onasi kelgan. Topshirgach ilovasiga bildirishnoma boradi.`
                    : "Ota-ona kelganda bu yerda ko'rinadi."}
                </span>
              </div>
            </div>

            {redCount > 0 && (
              <div className="px-4 pb-2">
                <div className="px-3 py-2.5 rounded-xl flex items-start gap-2.5" style={{ background: '#EF44441A' }}>
                  <Siren size={18} className="shrink-0 mt-0.5 text-danger" />
                  <span className="text-[12.5px] font-medium leading-snug" style={{ color: '#EF4444' }}>
                    {`Qizil ro'yxat: ${redCount} ta o'quvchi — ota-ona bilan bog'lanish kerak. Natijani Telegram ilovasida belgilang (Bugun → Qizil ro'yxat).`}
                  </span>
                </div>
              </div>
            )}

            {debts.data && debts.data.groups.length > 0 && (
              <div className="px-4 pb-2">
                <div
                  className="px-3 py-2.5 rounded-xl flex items-start gap-2.5"
                  style={{ background: debts.data.debtorCount > 0 ? '#EF44441A' : 'var(--primary-soft)' }}
                >
                  <Wallet size={18} className={`shrink-0 mt-0.5 ${debts.data.debtorCount > 0 ? 'text-danger' : 'text-primary'}`} />
                  <span
                    className="text-[12.5px] font-medium leading-snug"
                    style={{ color: debts.data.debtorCount > 0 ? '#EF4444' : 'var(--primary)' }}
                  >
                    {debts.data.debtorCount > 0
                      ? `Qarzdorlar: ${debts.data.debtorCount} ta (${debts.data.groups.join(', ')})`
                      : "Qarzdor o'quvchi yo'q."}
                  </span>
                  {debts.data.debtorCount > 0 && (
                    <button
                      type="button"
                      onClick={downloadExcel}
                      disabled={excelBusy}
                      className="ml-auto shrink-0 inline-flex items-center gap-1 text-[12.5px] font-bold text-danger disabled:opacity-50"
                    >
                      <FileSpreadsheet size={15} /> {excelBusy ? '…' : 'Excel'}
                    </button>
                  )}
                </div>
              </div>
            )}

            <div className="flex-1 overflow-y-auto no-scrollbar px-4 pt-1 pb-6 space-y-2.5">
              {sorted.map((s) => {
                const isPending = s.hasPendingPickup && s.status !== 'accepted'
                const accepted = s.status === 'accepted'
                const pickupId = pendingPickupByStudent[s.studentId]
                const isBusy = busy === s.studentId || busy === pickupId
                return (
                  <div
                    key={s.studentId}
                    className="p-3 rounded-2xl bg-surface flex items-center gap-3"
                    style={{ border: isPending ? '1.5px solid #F59E0B80' : '1px solid var(--border)' }}
                  >
                    <Avatar name={s.fullName} size={44} />
                    <div className="flex-1 min-w-0">
                      <p className="text-[15px] font-bold text-text truncate">
                        {s.fullName}
                        {manyClasses && s.className && (
                          <span className="ml-1.5 text-[12px] font-semibold text-muted">{s.className}</span>
                        )}
                      </p>
                      {isPending ? (
                        <div className="flex items-center gap-1.5 mt-0.5">
                          <span className="w-[7px] h-[7px] rounded-full bg-warning" />
                          <span className="text-[12.5px] font-semibold text-warning">Ota-onasi kelgan</span>
                        </div>
                      ) : accepted ? (
                        <p className="text-[12.5px] text-muted">Topshirilgan</p>
                      ) : (
                        <p className="text-[12.5px] text-muted">Kutilmoqda emas</p>
                      )}
                      {redById[s.studentId] && (
                        <p className="text-[12.5px] font-semibold text-danger mt-0.5">
                          Qizil ro'yxat: {redReason(redById[s.studentId])}
                        </p>
                      )}
                      {debtById[s.studentId]?.isDebtor && (
                        <p className="text-[12.5px] font-semibold text-danger mt-0.5">
                          Qarzdor
                          {debtById[s.studentId].unpaidMonths.length > 0 &&
                            ` · ${debtById[s.studentId].unpaidMonths.map((m) => monthName(m.month)).join(', ')}`}
                        </p>
                      )}
                      {/* Qarz izohi (2026-10-29) — yozish Telegram ilovasida: Bugun → Sinfim → o'quvchi. */}
                      {debtById[s.studentId]?.lastComment && (
                        <p className="text-[12px] text-muted mt-0.5 line-clamp-2">
                          Izoh: {debtById[s.studentId].lastComment}
                        </p>
                      )}
                    </div>
                    {accepted ? (
                      <span className="px-3 py-2.5 rounded-xl bg-success/10 flex items-center gap-1.5 text-[13px] font-bold text-success">
                        <CheckCircle2 size={16} /> Topshirildi
                      </span>
                    ) : isPending && pickupId ? (
                      <AppButton label="Qabul qilish" style="filled" height={40} loading={isBusy} onClick={() => accept(pickupId)} />
                    ) : (
                      <AppButton label="Topshirish" style="soft" height={40} loading={isBusy} onClick={() => handover(s.studentId)} />
                    )}
                  </div>
                )
              })}
            </div>
          </>
        )}
      </AsyncView>
    </div>
  )
}
