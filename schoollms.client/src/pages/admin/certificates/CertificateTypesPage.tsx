import { useEffect, useState } from 'react'
import { Plus, Pencil, Trash2, Award } from 'lucide-react'
import {
  getCertificateTypes,
  createCertificateType,
  updateCertificateType,
  deleteCertificateType,
  certificateError,
  type CertificateType,
} from '@/api/services/certificates'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Input } from '@/components/ui/Input'
import { cn } from '@/lib/utils'

/**
 * Sertifikat turlari — katalog (§2.3).
 *
 * Jadval BO'SH holda yetkaziladi: migratsiya birorta tur seed qilmaydi, chunki
 * qaysi imtihonlar kerakligini faqat maktab biladi. Shuning uchun bu ekranning
 * bo'sh holati "ma'lumot yo'q" emas, birinchi turni qo'shishga CHAQIRIQ.
 *
 * "Ball qo'yiladi" belgisi — butun bo'limning kaliti: faqat shunday turlar
 * "Natijalar" tab'ida ko'rinadi va faqat ularda ball qabul qilinadi.
 */
export function CertificateTypesPage() {
  const [types, setTypes] = useState<CertificateType[]>([])
  const [loading, setLoading] = useState(true)
  const [open, setOpen] = useState(false)
  const [editing, setEditing] = useState<CertificateType | null>(null)
  const [name, setName] = useState('')
  const [isScored, setIsScored] = useState(false)
  const [isActive, setIsActive] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    getCertificateTypes()
      .then(setTypes)
      .finally(() => setLoading(false))
  }, [])

  const openCreate = () => {
    setEditing(null)
    setName('')
    setIsScored(false)
    setIsActive(true)
    setError('')
    setOpen(true)
  }

  const openEdit = (t: CertificateType) => {
    setEditing(t)
    setName(t.name)
    setIsScored(t.isScored)
    setIsActive(t.isActive)
    setError('')
    setOpen(true)
  }

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!name.trim()) return
    setSaving(true)
    setError('')
    try {
      const payload = { name: name.trim(), isScored, isActive }
      if (editing) {
        const saved = await updateCertificateType(editing.id, payload)
        setTypes((p) => p.map((x) => (x.id === saved.id ? saved : x)))
      } else {
        const saved = await createCertificateType(payload)
        setTypes((p) => [...p, saved].sort((a, b) => a.name.localeCompare(b.name)))
      }
      setOpen(false)
    } catch (err) {
      setError(certificateError(err))
    } finally {
      setSaving(false)
    }
  }

  const remove = async (t: CertificateType) => {
    if (!confirm(`"${t.name}" turini o'chirasizmi?`)) return
    try {
      await deleteCertificateType(t.id)
      setTypes((p) => p.filter((x) => x.id !== t.id))
    } catch (err) {
      // Ishlatilgan tur o'chmaydi — server nima qilish kerakligini aytadi.
      alert(certificateError(err))
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Sertifikat turlari</h1>
          <p className="text-sm text-slate-400">
            IELTS, SAT, olimpiada diplomi — o'quvchilarga beriladigan hujjat turlari
          </p>
        </div>
        <Button onClick={openCreate}>
          <Plus className="h-4 w-4" /> Yangi tur
        </Button>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : types.length === 0 ? (
        <Card className="flex flex-col items-center justify-center gap-3 py-16 text-center">
          <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-slate-100">
            <Award className="h-7 w-7 text-slate-400" />
          </div>
          <p className="text-sm font-medium text-slate-600">Hali birorta tur qo'shilmagan</p>
          <p className="max-w-md text-sm text-slate-400">
            Sertifikat qo'shishdan oldin uning turi kerak. Masalan: "IELTS" (ball qo'yiladi),
            "Matematika olimpiadasi" yoki "Ichki imtihon".
          </p>
          <Button onClick={openCreate}>
            <Plus className="h-4 w-4" /> Birinchi turni qo'shish
          </Button>
        </Card>
      ) : (
        <Card className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="w-10 px-4 py-3">#</th>
                  <th className="px-4 py-3">Nomi</th>
                  <th className="px-4 py-3">Ball</th>
                  <th className="px-4 py-3">Holati</th>
                  <th className="px-4 py-3 text-right">Hujjatlar</th>
                  <th className="px-4 py-3 text-right">Amallar</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {types.map((t, i) => (
                  <tr key={t.id} className="hover:bg-slate-50/60">
                    <td className="px-4 py-3 text-slate-400">{i + 1}</td>
                    <td className="px-4 py-3 font-medium text-slate-800">{t.name}</td>
                    <td className="px-4 py-3">
                      {t.isScored ? (
                        <span className="rounded-full bg-brand-50 px-2 py-0.5 text-xs font-medium text-brand-700">
                          Ball qo'yiladi
                        </span>
                      ) : (
                        <span className="text-slate-400">—</span>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      <span
                        className={cn(
                          'rounded-full px-2 py-0.5 text-xs font-medium',
                          t.isActive
                            ? 'bg-emerald-50 text-emerald-700'
                            : 'bg-slate-100 text-slate-500',
                        )}
                      >
                        {t.isActive ? 'Faol' : 'Faol emas'}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-right text-slate-500">{t.certificateCount}</td>
                    <td className="px-4 py-3">
                      <div className="flex items-center justify-end gap-0.5">
                        <button
                          type="button"
                          title="Tahrirlash"
                          onClick={() => openEdit(t)}
                          className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                        >
                          <Pencil className="h-4 w-4" />
                        </button>
                        <button
                          type="button"
                          disabled={t.certificateCount > 0}
                          title={
                            t.certificateCount > 0
                              ? "Bu turda hujjatlar bor — o'chirib bo'lmaydi. Uni «Faol emas» qilib qo'ying."
                              : "O'chirish"
                          }
                          onClick={() => remove(t)}
                          className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600 disabled:cursor-not-allowed disabled:text-slate-200 disabled:hover:bg-transparent"
                        >
                          <Trash2 className="h-4 w-4" />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>
      )}

      <Modal
        open={open}
        onClose={() => setOpen(false)}
        title={editing ? 'Turni tahrirlash' : 'Yangi sertifikat turi'}
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setOpen(false)}>
              Bekor qilish
            </Button>
            <Button type="submit" form="certificate-type-form" disabled={!name.trim() || saving}>
              Saqlash
            </Button>
          </>
        }
      >
        <form id="certificate-type-form" onSubmit={submit} className="space-y-4">
          <Input
            label="Tur nomi"
            required
            placeholder="masalan: IELTS, Matematika olimpiadasi"
            value={name}
            onChange={(e) => setName(e.target.value)}
          />

          <label className="flex cursor-pointer items-start gap-2.5">
            <input
              type="checkbox"
              checked={isScored}
              // Ishlatilgan turda bu belgi muzlatiladi (server ham rad etadi):
              // aks holda ballik hujjatlar bir kechada qoidaga zid bo'lib qolardi.
              disabled={!!editing && editing.certificateCount > 0}
              onChange={(e) => setIsScored(e.target.checked)}
              className="mt-0.5 h-4 w-4 rounded border-slate-300 accent-brand-600 disabled:opacity-40"
            />
            <span className="text-sm">
              <span className="font-medium text-slate-700">Ball qo'yiladi (standart test)</span>
              <span className="mt-0.5 block text-slate-400">
                IELTS 7.5, SAT 1340 kabi. Faqat shunday turlar "Natijalar" tab'ida ko'rinadi.
                {!!editing && editing.certificateCount > 0 && (
                  <> Bu turda hujjatlar bor — belgini endi o'zgartirib bo'lmaydi.</>
                )}
              </span>
            </span>
          </label>

          <label className="flex cursor-pointer items-start gap-2.5">
            <input
              type="checkbox"
              checked={isActive}
              onChange={(e) => setIsActive(e.target.checked)}
              className="mt-0.5 h-4 w-4 rounded border-slate-300 accent-brand-600"
            />
            <span className="text-sm">
              <span className="font-medium text-slate-700">Faol</span>
              <span className="mt-0.5 block text-slate-400">
                Belgi olib tashlansa, tur yangi sertifikatda tanlanmaydi — eski hujjatlar esa
                joyida qoladi. Kerak bo'lmay qolgan turni SHU yo'l bilan chiqarib qo'ying.
              </span>
            </span>
          </label>

          {error && (
            <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>
          )}
        </form>
      </Modal>
    </div>
  )
}
