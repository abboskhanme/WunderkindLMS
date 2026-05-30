import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Check } from 'lucide-react'
import { getSchoolInfo, saveSchoolInfo, type SchoolInfo } from '@/api/services/settings'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'

const empty: SchoolInfo = {
  name: '',
  director: '',
  phone: '',
  email: '',
  address: '',
  region: '',
  district: '',
}

/** Maktabga oid umumiy ma'lumotlar (nomi, direktor, manzil va h.k.) kiritiladigan sozlama. */
export function SchoolSettings() {
  const [form, setForm] = useState<SchoolInfo>(empty)
  const [loading, setLoading] = useState(true)
  const [status, setStatus] = useState<'idle' | 'saving' | 'saved'>('idle')

  useEffect(() => {
    getSchoolInfo()
      .then(setForm)
      .finally(() => setLoading(false))
  }, [])

  const update = <K extends keyof SchoolInfo>(key: K, value: SchoolInfo[K]) =>
    setForm((f) => ({ ...f, [key]: value }))

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setStatus('saving')
    await saveSchoolInfo({ ...form, name: form.name.trim() })
    // Yon menyudagi maktab nomini darrov yangilash uchun.
    window.dispatchEvent(new Event('school:updated'))
    setStatus('saved')
    setTimeout(() => setStatus('idle'), 2000)
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <Card>
      <div className="mb-1 font-semibold text-slate-800">Maktab ma'lumotlari</div>
      <p className="mb-4 text-sm text-slate-400">
        Maktab nomi va umumiy ma'lumotlar — hisobotlar va hujjatlarda ishlatiladi.
      </p>
      <form onSubmit={onSubmit} className="max-w-2xl space-y-4">
        <Input
          label="Maktab nomi"
          placeholder="Masalan: 1-sonli umumiy o'rta ta'lim maktabi"
          value={form.name}
          onChange={(e) => update('name', e.target.value)}
          required
        />
        <Input
          label="Direktor (F.I.SH)"
          value={form.director}
          onChange={(e) => update('director', e.target.value)}
        />
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Input label="Telefon" value={form.phone} onChange={(e) => update('phone', e.target.value)} />
          <Input
            label="Email"
            type="email"
            value={form.email}
            onChange={(e) => update('email', e.target.value)}
          />
        </div>
        <Input label="Manzil" value={form.address} onChange={(e) => update('address', e.target.value)} />
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Input label="Viloyat" value={form.region} onChange={(e) => update('region', e.target.value)} />
          <Input label="Tuman" value={form.district} onChange={(e) => update('district', e.target.value)} />
        </div>

        <div className="flex items-center gap-3">
          <Button type="submit" disabled={status === 'saving'}>
            {status === 'saving' ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
          {status === 'saved' && (
            <span className="inline-flex items-center gap-1 text-sm font-medium text-emerald-600">
              <Check className="h-4 w-4" /> Saqlandi
            </span>
          )}
        </div>
      </form>
    </Card>
  )
}
