import { useState } from 'react'
import { Check, UserMinus, UserPlus, Users2 } from 'lucide-react'
import { ScreenHeader } from '../components/ui'
import Avatar from '../components/Avatar'
import AppButton from '../components/AppButton'
import AppSheet from '../components/AppSheet'
import { AsyncView } from '../components/State'
import EmptyState, { EmptyIllustration } from '../components/EmptyState'
import { useFetch } from '../lib/session'
import { api } from '../lib/api'

// Group roster (X-3) — the list is open to any teacher who reaches the group; add/remove
// only render when `params.canEdit` (the attached "yetakchi" teacher — the server enforces
// the same rule independently on every write, so this is a UI convenience, not the gate).
export default function GroupRosterScreen({ params, onBack }) {
  const { id, name, canEdit } = params || {}
  const membersQ = useFetch(() => api.groupMembers(id), [id])

  const [addOpen, setAddOpen] = useState(false)
  const [removeTarget, setRemoveTarget] = useState(null) // { id, fullName } being removed

  const members = membersQ.data || []

  return (
    <div className="h-full flex flex-col bg-bg">
      <ScreenHeader
        title={name || 'Guruh'}
        subtitle={`${members.length} o'quvchi`}
        onBack={onBack}
        trailing={
          canEdit ? (
            <button
              onClick={() => setAddOpen(true)}
              className="w-10 h-10 shrink-0 rounded-xl bg-primary-soft flex items-center justify-center text-primary"
            >
              <UserPlus size={19} />
            </button>
          ) : undefined
        }
      />

      <AsyncView query={membersQ} loadingLabel="Yuklanmoqda…">
        {members.length === 0 ? (
          <EmptyState
            icon={<EmptyIllustration><Users2 size={30} /></EmptyIllustration>}
            title="Ro'yxat bo'sh"
            subtitle={canEdit ? "Yuqoridagi tugma bilan o'quvchi qo'shing" : 'Bu guruhda hali o\'quvchi yo\'q'}
          />
        ) : (
          <div className="flex-1 overflow-y-auto no-scrollbar px-4 pt-1 pb-6 space-y-2.5">
            {members.map((m) => (
              <div key={m.id} className="p-3 rounded-2xl bg-surface border border-border flex items-center gap-3">
                <Avatar name={m.fullName} size={44} />
                <div className="flex-1 min-w-0">
                  <p className="text-[15px] font-bold text-text truncate">{m.fullName}</p>
                  <p className="text-[12.5px] text-muted">{m.className}</p>
                </div>
                {canEdit && (
                  <button
                    onClick={() => setRemoveTarget(m)}
                    className="w-9 h-9 shrink-0 rounded-xl bg-danger/10 flex items-center justify-center text-danger"
                  >
                    <UserMinus size={16} />
                  </button>
                )}
              </div>
            ))}
          </div>
        )}
      </AsyncView>

      {canEdit && (
        <AddMembersSheet
          open={addOpen}
          groupId={id}
          onClose={() => setAddOpen(false)}
          onAdded={() => {
            setAddOpen(false)
            membersQ.reload()
          }}
        />
      )}
      {canEdit && (
        <RemoveMemberSheet
          member={removeTarget}
          groupId={id}
          onClose={() => setRemoveTarget(null)}
          onRemoved={() => {
            setRemoveTarget(null)
            membersQ.reload()
          }}
        />
      )}
    </div>
  )
}

// Candidate picker — students from the group's feeding classes, gender and subject already
// filtered server-side (StudyGroupService.CandidatesAsync); a candidate already in another
// active group for the same subject shows greyed with its current group name, matching the
// admin roster picker's convention.
function AddMembersSheet({ open, groupId, onClose, onAdded }) {
  const q = useFetch(() => (open ? api.groupCandidates(groupId) : Promise.resolve([])), [open, groupId])
  const [selected, setSelected] = useState([])
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState(null)

  const candidates = q.data || []
  const toggle = (studentId) =>
    setSelected((p) => (p.includes(studentId) ? p.filter((x) => x !== studentId) : [...p, studentId]))

  const submit = async () => {
    if (selected.length === 0) return
    setSaving(true)
    setError(null)
    try {
      await api.addGroupMembers(groupId, selected)
      setSelected([])
      onAdded?.()
    } catch (e) {
      setError(e)
    } finally {
      setSaving(false)
    }
  }

  return (
    <AppSheet open={open} onClose={onClose} title="O'quvchi qo'shish">
      <div className="px-5 pb-6">
        {error && (
          <div className="mb-3 px-3 py-2 rounded-xl bg-danger/10 text-danger text-[12px] font-semibold">
            {error.message || 'Xatolik yuz berdi'}
          </div>
        )}
        {q.loading ? (
          <p className="py-6 text-center text-[13px] text-muted">Yuklanmoqda…</p>
        ) : candidates.length === 0 ? (
          <p className="py-6 text-center text-[13px] text-muted">Nomzod topilmadi</p>
        ) : (
          <div className="space-y-1.5 max-h-[45vh] overflow-y-auto no-scrollbar">
            {candidates.map((c) => {
              const busy = c.currentGroupId != null
              const on = selected.includes(c.studentId)
              return (
                <button
                  key={c.studentId}
                  type="button"
                  disabled={busy}
                  onClick={() => toggle(c.studentId)}
                  className="w-full p-3 rounded-2xl border flex items-center gap-3 text-left disabled:opacity-50"
                  style={{
                    background: on ? 'var(--primary-soft)' : 'var(--surface)',
                    borderColor: on ? 'var(--primary)' : 'var(--border)',
                    borderWidth: on ? 1.5 : 1,
                  }}
                >
                  <Avatar name={c.fullName} size={36} />
                  <div className="flex-1 min-w-0">
                    <p className="text-[14px] font-semibold text-text truncate">{c.fullName}</p>
                    <p className="text-[11.5px] text-muted">{busy ? `${c.currentGroupName} da` : c.className}</p>
                  </div>
                  {on && <Check size={18} className="text-primary shrink-0" />}
                </button>
              )
            })}
          </div>
        )}
        <div className="pt-4">
          <AppButton
            label={`Qo'shish${selected.length ? ` (${selected.length})` : ''}`}
            expand
            height={50}
            loading={saving}
            disabled={selected.length === 0}
            onClick={submit}
          />
        </div>
      </div>
    </AppSheet>
  )
}

function RemoveMemberSheet({ member, groupId, onClose, onRemoved }) {
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)

  const submit = async () => {
    setBusy(true)
    setError(null)
    try {
      await api.removeGroupMember(groupId, member.id, reason.trim() || undefined)
      setReason('')
      onRemoved?.()
    } catch (e) {
      setError(e)
    } finally {
      setBusy(false)
    }
  }

  return (
    <AppSheet open={!!member} onClose={onClose} title="Guruhdan chiqarish">
      <div className="px-5 pb-6 space-y-3">
        <p className="text-[13px] text-muted">
          <b className="text-text">{member?.fullName}</b> guruhdan chiqariladi. A'zolik tarixda saqlanadi.
        </p>
        <div>
          <p className="text-[12px] font-bold text-muted uppercase tracking-wide mb-1.5">Sabab (ixtiyoriy)</p>
          <textarea
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            rows={3}
            className="w-full p-3.5 rounded-xl bg-surface2 border border-border outline-none text-[14px] text-text placeholder:text-faint focus:border-primary resize-none"
            placeholder="Masalan: ota-onaning iltimosiga ko'ra"
          />
        </div>
        {error && (
          <div className="px-3 py-2 rounded-xl bg-danger/10 text-danger text-[12px] font-semibold">
            {error.message || 'Xatolik yuz berdi'}
          </div>
        )}
        <AppButton label="Chiqarish" style="danger" expand height={50} loading={busy} onClick={submit} />
      </div>
    </AppSheet>
  )
}
