/**
 * The settings strip on the bank screen (§3.1 screen 4): three numeric fields
 * and one Saqlash, writing `PUT /banks/{id}` (§6.2).
 *
 * The parent re-mounts it (`key`) whenever the saved values change, so the
 * draft always starts from what the server last said.
 *
 * Without the `admission` key the fields are shown read-only and the button is
 * not rendered (§3.7).
 */
import { useState } from 'react'
import type { FormEvent } from 'react'
import { Loader2, Save, SlidersHorizontal } from 'lucide-react'
import {
  admissionBankError,
  updateBankSettings,
  type QuestionBank,
} from '@/api/services/admissionBanks'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import {
  parseSettingsDraft,
  sameSettings,
  settingsDraftOf,
  type SettingsDraft,
  type SettingsField,
} from './BankHelpers'
import { BankSettingsFields } from './BankSettingsFields'

interface Props {
  bank: QuestionBank
  canWrite: boolean
  onSaved: (saved: QuestionBank) => void
}

export function BankSettingsStrip({ bank, canWrite, onSaved }: Props) {
  const [draft, setDraft] = useState<SettingsDraft>(() => settingsDraftOf(bank))
  const [invalidField, setInvalidField] = useState<SettingsField | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const parsed = parseSettingsDraft(draft)
  const dirty = !parsed.ok || !sameSettings(parsed.value, bank)

  const change = (next: SettingsDraft) => {
    setDraft(next)
    setInvalidField(null)
    setError(null)
  }

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!canWrite || saving || !dirty) return
    const result = parseSettingsDraft(draft)
    if (!result.ok) {
      setInvalidField(result.field)
      setError(result.message)
      return
    }
    setSaving(true)
    setError(null)
    try {
      onSaved(await updateBankSettings(bank.id, result.value))
    } catch (err) {
      setError(admissionBankError(err, 'updateBank').message)
    } finally {
      setSaving(false)
    }
  }

  return (
    <Card>
      <form onSubmit={(e) => void submit(e)} className="space-y-3">
        <div className="flex flex-wrap items-center gap-2">
          <SlidersHorizontal className="h-5 w-5 text-brand-600" />
          <h2 className="font-semibold text-slate-800">Test sozlamalari</h2>
          <p className="w-full text-xs text-slate-400 sm:ml-2 sm:w-auto">
            Ball imtihon e'lon qilinganda unga nusxalanadi — keyingi o'zgarish tugagan imtihon
            natijalariga ta'sir qilmaydi.
          </p>
        </div>

        <div className="flex flex-col gap-3 lg:flex-row lg:items-start">
          <BankSettingsFields
            draft={draft}
            onChange={change}
            disabled={!canWrite || saving}
            invalidField={invalidField}
            className="flex-1"
          />
          {canWrite && (
            <Button type="submit" disabled={saving || !dirty} className="lg:mt-6">
              {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
              Saqlash
            </Button>
          )}
        </div>

        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
      </form>
    </Card>
  )
}
