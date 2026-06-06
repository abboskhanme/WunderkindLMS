import { useState } from 'react'
import { BellRing, CheckCircle2, Users2 } from 'lucide-react'
import { ScreenHeader } from '../components/ui'
import Avatar from '../components/Avatar'
import AppButton from '../components/AppButton'
import { AsyncView } from '../components/State'
import EmptyState, { EmptyIllustration } from '../components/EmptyState'
import { useFetch } from '../lib/session'
import { api } from '../lib/api'

// Homeroom — parent-arrived students highlighted + sorted to top, handover button.
export default function HomeroomScreen({ onBack }) {
  const home = useFetch(() => api.homeroom(), [])
  const picks = useFetch(() => api.pickups(), [])
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
  const pending = sorted.filter((s) => s.hasPendingPickup && s.status !== 'accepted').length

  return (
    <div className="h-full flex flex-col bg-bg">
      <ScreenHeader
        title="Sinf rahbarligi"
        subtitle={data ? `${data.className || ''} sinfi · ${sorted.length} o'quvchi` : undefined}
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
                      <p className="text-[15px] font-bold text-text truncate">{s.fullName}</p>
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
