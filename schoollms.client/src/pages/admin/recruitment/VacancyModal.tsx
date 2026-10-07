import { useState } from 'react'
import { createVacancy, updateVacancy, type Vacancy } from '@/api/services/recruitment'
import { hrErrorMessage } from '@/api/services/hrTasks'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Textarea } from '@/components/ui/Input'

interface Props {
  vacancy: Vacancy | null
  onClose: () => void
  onSaved: () => void
}

export function VacancyModal({ vacancy, onClose, onSaved }: Props) {
  const [title, setTitle] = useState(vacancy?.title ?? '')
  const [department, setDepartment] = useState(vacancy?.department ?? '')
  const [description, setDescription] = useState(vacancy?.description ?? '')
  const [requirements, setRequirements] = useState(vacancy?.requirements ?? '')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  const save = async () => {
    setSaving(true)
    setError('')
    const input = {
      title: title.trim(),
      department: department.trim() || null,
      description: description.trim() || null,
      requirements: requirements.trim() || null,
    }
    try {
      if (vacancy) await updateVacancy(vacancy.id, input)
      else await createVacancy(input)
      onSaved()
    } catch (e) {
      setError(hrErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      size="md"
      title={vacancy ? 'Vakansiyani tahrirlash' : 'Yangi vakansiya'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button onClick={save} disabled={saving || !title.trim()}>
            {saving ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Input label="Lavozim" required value={title} maxLength={200} onChange={(e) => setTitle(e.target.value)} placeholder="Masalan: Ingliz tili o'qituvchisi" />
        <Input label="Bo'lim" value={department} maxLength={100} onChange={(e) => setDepartment(e.target.value)} placeholder="O'quv bo'limi" />
        <Textarea label="Tavsif" rows={4} value={description} maxLength={4000} onChange={(e) => setDescription(e.target.value)} placeholder="Vazifalar, ish vaqti, maosh shartlari" />
        <Textarea label="Talablar" rows={4} value={requirements} maxLength={4000} onChange={(e) => setRequirements(e.target.value)} placeholder="Ma'lumot, tajriba, sertifikatlar" />
        {!vacancy && <p className="text-xs text-slate-400">Saqlangach, ochiq ariza havolasi yaratiladi — uni Telegram yoki Instagram'da tarqating.</p>}
        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}
