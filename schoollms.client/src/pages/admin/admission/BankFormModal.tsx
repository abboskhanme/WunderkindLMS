/**
 * "Baza qo'shish" / bank settings — the modal over the Test bazasi register
 * (§3.1 screen 3, §6.2 `POST /banks`, `PUT /banks/{id}`).
 *
 * Grade and subject are chosen once, at creation: the `PUT` body has only the
 * three settings, and a bank's identity is its (grade, subject) pair — one
 * live bank per pair (§5.1). On edit they are shown, not offered.
 *
 * The three settings are optional. A bank without `questionsPerTest` is
 * legitimately "Sozlanmagan"; the server decides the badge, not this form.
 *
 * Mounted conditionally by the parent, so the initial values live in
 * `useState` and every opening starts clean.
 */
import { useState } from 'react'
import type { FormEvent } from 'react'
import { Loader2 } from 'lucide-react'
import type { Subject } from '@/types'
import {
  admissionBankError,
  createBank,
  updateBankSettings,
  type QuestionBank,
} from '@/api/services/admissionBanks'
import { Button } from '@/components/ui/Button'
import { Select } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import {
  EMPTY_SETTINGS_DRAFT,
  GRADES,
  bankTitle,
  gradeLabel,
  parseSettingsDraft,
  sameSettings,
  settingsDraftOf,
  type SettingsDraft,
  type SettingsField,
} from './BankHelpers'
import { BankSettingsFields } from './BankSettingsFields'

interface Props {
  /** `null` — a new bank. */
  editing: QuestionBank | null
  /** Active subjects for the picker; ignored on edit. */
  subjects: Subject[]
  /** Set when the subject list failed to load — creation is impossible then. */
  subjectsError: string | null
  subjectsLoading?: boolean
  onClose: () => void
  onSaved: (saved: QuestionBank, created: boolean) => void
}

export function BankFormModal({
  editing,
  subjects,
  subjectsError,
  subjectsLoading = false,
  onClose,
  onSaved,
}: Props) {
  const [grade, setGrade] = useState('')
  const [subjectId, setSubjectId] = useState('')
  const [draft, setDraft] = useState<SettingsDraft>(
    editing ? settingsDraftOf(editing) : EMPTY_SETTINGS_DRAFT,
  )
  const [invalidField, setInvalidField] = useState<SettingsField | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const parsed = parseSettingsDraft(draft)
  const unchanged = Boolean(editing && parsed.ok && sameSettings(parsed.value, editing))
  const canSave = editing
    ? !saving && !unchanged
    : !saving && grade !== '' && subjectId !== '' && !subjectsError

  const changeDraft = (next: SettingsDraft) => {
    setDraft(next)
    setInvalidField(null)
    setError(null)
  }

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!canSave) return
    const result = parseSettingsDraft(draft)
    if (!result.ok) {
      setInvalidField(result.field)
      setError(result.message)
      return
    }
    setSaving(true)
    setError(null)
    try {
      const saved = editing
        ? await updateBankSettings(editing.id, result.value)
        : await createBank({ grade: Number(grade), subjectId, ...result.value })
      onSaved(saved, !editing)
    } catch (err) {
      setError(admissionBankError(err, editing ? 'updateBank' : 'createBank').message)
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title={editing ? 'Baza sozlamalari' : "Baza qo'shish"}
      size="md"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Bekor qilish
          </Button>
          <Button type="submit" form="bank-form" disabled={!canSave}>
            {saving && <Loader2 className="h-4 w-4 animate-spin" />}
            {editing ? 'Saqlash' : 'Yaratish'}
          </Button>
        </>
      }
    >
      <form id="bank-form" onSubmit={(e) => void submit(e)} className="space-y-4">
        {editing ? (
          <div className="rounded-xl bg-slate-50 px-4 py-3">
            <p className="text-xs font-medium uppercase tracking-wide text-slate-400">Baza</p>
            <p className="mt-0.5 font-medium text-slate-800">{bankTitle(editing)}</p>
            <p className="mt-1 text-xs text-slate-400">
              Sinf va fan baza yaratilganda tanlanadi va keyin o'zgarmaydi.
            </p>
          </div>
        ) : (
          <div className="grid gap-3 sm:grid-cols-2">
            <Select
              label="Sinf"
              required
              value={grade}
              onChange={(e) => {
                setGrade(e.target.value)
                setError(null)
              }}
            >
              <option value="">Sinfni tanlang</option>
              {GRADES.map((g) => (
                <option key={g} value={g}>
                  {gradeLabel(g)}
                </option>
              ))}
            </Select>
            <Select
              label="Fan"
              required
              value={subjectId}
              disabled={Boolean(subjectsError) || subjectsLoading}
              onChange={(e) => {
                setSubjectId(e.target.value)
                setError(null)
              }}
            >
              <option value="">
                {subjectsError
                  ? "Fanlar ro'yxati yuklanmadi"
                  : subjectsLoading
                    ? 'Yuklanmoqda...'
                    : 'Fanni tanlang'}
              </option>
              {subjects.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </Select>
            <p className="text-xs text-slate-400 sm:col-span-2">
              Har bir sinf va fan uchun bitta baza bo'ladi — imtihon savollarni shu bazadan oladi.
            </p>
          </div>
        )}

        {!editing && subjectsError && (
          <p className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-700">
            {subjectsError}
          </p>
        )}
        {!editing && !subjectsError && !subjectsLoading && subjects.length === 0 && (
          <p className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-700">
            Faol fan yo'q — avval «Fanlar» bo'limida fan qo'shing.
          </p>
        )}

        <div className="space-y-2">
          <div>
            <h4 className="text-sm font-semibold text-slate-700">Test sozlamalari</h4>
            <p className="mt-0.5 text-xs text-slate-400">
              Ixtiyoriy — keyin baza sahifasida ham kiritish mumkin. Uchalasi to'ldirilmaguncha
              baza imtihonga tayyor bo'lmaydi.
            </p>
          </div>
          <BankSettingsFields
            draft={draft}
            onChange={changeDraft}
            disabled={saving}
            invalidField={invalidField}
          />
        </div>

        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
      </form>
    </Modal>
  )
}
