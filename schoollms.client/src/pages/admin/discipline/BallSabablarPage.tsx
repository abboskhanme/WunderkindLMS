import { useEffect, useMemo, useState } from 'react'
import { Plus, Pencil, Trash2, CalendarCheck, Send, Users, Search, Repeat } from 'lucide-react'
import type { DisciplineReason, DisciplineType, DisciplinePeriod } from '@/types'
import {
  getDisciplineReasons,
  createDisciplineReason,
  updateDisciplineReason,
  deleteDisciplineReason,
  setAttendanceReasonPoints,
  disciplineTypeLabel,
  disciplineTypeOf,
  disciplinePeriodLabel,
  getDisciplineOwnerCandidates,
  type DisciplineOwnerCandidate,
} from '@/api/services/discipline'
import { cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Input, Textarea } from '@/components/ui/Input'

export function BallSabablarPage() {
  const [reasons, setReasons] = useState<DisciplineReason[]>([])
  const [loading, setLoading] = useState(true)
  const [open, setOpen] = useState(false)
  const [editing, setEditing] = useState<DisciplineReason | null>(null)
  const [name, setName] = useState('')
  // Behaviour reasons: a positive number + the type (EduSchool "Ijobiy / Salbiy"); the server signs it.
  // Attendance reasons keep the signed number (0 = no effect is valid there).
  const [points, setPoints] = useState(0)
  const [type, setType] = useState<DisciplineType>('negative')
  const [typeFilter, setTypeFilter] = useState<'all' | DisciplineType>('all')
  // §6.3, 4–5-qadam: ota-onaga xabar (sukut — O'CHIQ), izoh va faollik.
  const [notifyParent, setNotifyParent] = useState(false)
  const [description, setDescription] = useState('')
  const [isActive, setIsActive] = useState(true)
  // Mas'ullar (o'qituvchi, sinf rahbari, xodim): ball FAQAT ular orqali qo'yiladi; bo'sh = faqat admin.
  const [candidates, setCandidates] = useState<DisciplineOwnerCandidate[]>([])
  const [ownerIds, setOwnerIds] = useState<string[]>([])
  const [ownerSearch, setOwnerSearch] = useState('')
  const [period, setPeriod] = useState<DisciplinePeriod>('none')

  useEffect(() => {
    getDisciplineReasons()
      .then(setReasons)
      .finally(() => setLoading(false))
    getDisciplineOwnerCandidates()
      .then(setCandidates)
      .catch(() => setCandidates([]))
  }, [])

  const ownerName = useMemo(() => new Map(candidates.map((c) => [c.userId, c.fullName])), [candidates])
  // Arxivdagi o'qituvchi tanlanmaydi, lekin oldin biriktirilgan bo'lsa ro'yxatda qoladi (olib tashlash uchun).
  const ownerOptions = useMemo(() => {
    const q = ownerSearch.trim().toLowerCase()
    return candidates.filter(
      (c) =>
        (!c.isArchived || ownerIds.includes(c.userId)) &&
        (!q || c.fullName.toLowerCase().includes(q) || c.label.toLowerCase().includes(q)),
    )
  }, [candidates, ownerIds, ownerSearch])
  const toggleOwner = (id: string) =>
    setOwnerIds((p) => (p.includes(id) ? p.filter((x) => x !== id) : [...p, id]))

  const attendance = useMemo(() => reasons.filter((r) => r.kind === 'attendance'), [reasons])
  const other = useMemo(
    () =>
      reasons.filter(
        (r) => r.kind === 'other' && (typeFilter === 'all' || disciplineTypeOf(r.points) === typeFilter),
      ),
    [reasons, typeFilter],
  )

  const openCreate = () => {
    setEditing(null)
    setName('')
    setPoints(0)
    setType(typeFilter === 'positive' ? 'positive' : 'negative')
    setNotifyParent(false)
    setDescription('')
    setIsActive(true)
    setOwnerIds([])
    setOwnerSearch('')
    setPeriod('none')
    setOpen(true)
  }
  const openEdit = (r: DisciplineReason) => {
    setEditing(r)
    setName(r.name)
    setPoints(r.kind === 'attendance' ? r.points : Math.abs(r.points))
    setType(disciplineTypeOf(r.points))
    setNotifyParent(r.notifyParent)
    setDescription(r.description ?? '')
    setIsActive(r.isActive)
    setOwnerIds(r.ownerUserIds ?? [])
    setOwnerSearch('')
    setPeriod(r.period ?? 'none')
    setOpen(true)
  }

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    const isAttendance = editing?.kind === 'attendance'
    if (!isAttendance && (!name.trim() || points === 0)) return
    const input = {
      name: name.trim(),
      points: Math.abs(points),
      type,
      notifyParent,
      description: description.trim(),
      isActive,
      ownerUserIds: ownerIds,
      period,
    }
    let saved: DisciplineReason
    if (editing) {
      saved = isAttendance
        ? await setAttendanceReasonPoints(editing.id, points)
        : await updateDisciplineReason(editing.id, input)
      setReasons((p) => p.map((x) => (x.id === saved.id ? saved : x)))
    } else {
      saved = await createDisciplineReason(input)
      setReasons((p) => [...p, saved])
    }
    setOpen(false)
  }

  const remove = (r: DisciplineReason) => {
    if (!confirm(`"${r.name}" sababini o'chirasizmi?`)) return
    deleteDisciplineReason(r.id).then(() => setReasons((p) => p.filter((x) => x.id !== r.id)))
  }

  const PointsBadge = ({ p }: { p: number }) => (
    <span
      className={cn(
        'rounded-md px-2 py-0.5 text-sm font-semibold',
        p === 0
          ? 'bg-slate-100 text-slate-400'
          : p < 0
            ? 'bg-red-50 text-red-600'
            : 'bg-emerald-50 text-emerald-600',
      )}
    >
      {p > 0 ? `+${p}` : p}
    </span>
  )

  const TypeBadge = ({ t }: { t: DisciplineType }) => (
    <span
      className={cn(
        'rounded-md px-2 py-0.5 text-xs font-medium',
        t === 'negative' ? 'bg-red-50 text-red-600' : 'bg-emerald-50 text-emerald-600',
      )}
    >
      {disciplineTypeLabel[t]}
    </span>
  )

  const OwnersCell = ({ ids }: { ids: string[] }) => {
    if (ids.length === 0)
      return (
        <span
          title="Mas'ul biriktirilmagan — bu sabab bilan faqat admin ball qo'ya oladi"
          className="rounded-md bg-amber-50 px-1.5 py-0.5 text-xs font-medium text-amber-600"
        >
          Biriktirilmagan
        </span>
      )
    const names = ids.map((id) => ownerName.get(id) ?? "Noma'lum")
    return (
      <span
        title={names.join('\n')}
        className="inline-flex max-w-[14rem] items-center gap-1 rounded-md bg-violet-50 px-1.5 py-0.5 text-xs font-medium text-violet-600"
      >
        <Users className="h-3 w-3 shrink-0" />
        <span className="truncate">{names.length === 1 ? names[0] : `${names.length} ta mas'ul`}</span>
      </span>
    )
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">Ball sabablar</h1>
          <p className="text-sm text-slate-400">
            Xulq-atvor sabablari — har biri ijobiy (ball qo'shiladi) yoki salbiy (ball ayriladi)
          </p>
        </div>
        <Button onClick={openCreate}>
          <Plus className="h-4 w-4" /> Yangi sabab
        </Button>
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <>
          {/* Davomat sabablari (jurnal bilan bog'liq) */}
          <Card className="p-0">
            <div className="flex items-center gap-2 border-b border-slate-100 p-4">
              <CalendarCheck className="h-4 w-4 text-brand-600" />
              <p className="font-semibold text-slate-800">Davomat sabablari</p>
              <span className="text-xs text-slate-400">
                — jurnalda shu sabab bilan davomat qo'yilsa, qoldiga ta'sir qiladi (nomi "Davomat sabablari"da)
              </span>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="w-10 px-4 py-3">#</th>
                    <th className="px-4 py-3">Sabab</th>
                    <th className="px-4 py-3">Ball</th>
                    <th className="px-4 py-3 text-right">Amallar</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {attendance.map((r, i) => (
                    <tr key={r.id} className="hover:bg-slate-50/60">
                      <td className="px-4 py-3 text-slate-400">{i + 1}</td>
                      <td className="px-4 py-3 font-medium text-slate-800">
                      <span className="block max-w-[14rem] truncate" title={r.name}>
                        {r.name}
                      </span>
                    </td>
                      <td className="px-4 py-3">
                        <PointsBadge p={r.points} />
                      </td>
                      <td className="px-4 py-3">
                        <div className="flex items-center justify-end">
                          <button
                            type="button"
                            title="Ballni o'zgartirish"
                            onClick={() => openEdit(r)}
                            className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                          >
                            <Pencil className="h-4 w-4" />
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                  {attendance.length === 0 && (
                    <tr>
                      <td colSpan={4} className="px-4 py-8 text-center text-slate-400">
                        Davomat sabablari yo'q (Sozlamalar → Davomat sabablarida qo'shiladi)
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </Card>

          {/* Boshqa intizomiy sabablar */}
          <Card className="p-0">
            <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 p-4">
              <div>
                <p className="font-semibold text-slate-800">Boshqa intizomiy sabablar</p>
                <span className="text-xs text-slate-400">— qo'lda kiritiladigan sabablar (janjal, rag'bat va h.k.)</span>
              </div>
              <div className="flex rounded-lg bg-slate-100 p-0.5">
                {(['all', 'positive', 'negative'] as const).map((t) => (
                  <button
                    key={t}
                    type="button"
                    onClick={() => setTypeFilter(t)}
                    className={cn(
                      'rounded-md px-3 py-1 text-xs font-medium transition-colors',
                      typeFilter === t ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
                    )}
                  >
                    {t === 'all' ? 'Barchasi' : disciplineTypeLabel[t]}
                  </button>
                ))}
              </div>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="whitespace-nowrap bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="w-10 px-4 py-3">#</th>
                    <th className="px-4 py-3">Sabab</th>
                    <th className="px-4 py-3">Turi</th>
                    <th className="px-4 py-3">Ball</th>
                    <th className="px-4 py-3">Mas'ullar</th>
                    <th className="px-4 py-3 text-right">Amallar</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {other.map((r, i) => (
                    <tr key={r.id} className="hover:bg-slate-50/60">
                      <td className="px-4 py-3 text-slate-400">{i + 1}</td>
                      <td className="px-4 py-3">
                        <div className="flex flex-wrap items-center gap-2">
                          <span className={cn('font-medium', r.isActive ? 'text-slate-800' : 'text-slate-400')}>
                            {r.name}
                          </span>
                          {r.notifyParent && (
                            <span
                              title="Ball qo'yilganda ota-onaga Telegram orqali xabar yuboriladi"
                              className="inline-flex items-center gap-1 rounded-md bg-sky-50 px-1.5 py-0.5 text-xs font-medium text-sky-600"
                            >
                              <Send className="h-3 w-3" /> Ota-onaga xabar
                            </span>
                          )}
                          {r.period && r.period !== 'none' && (
                            <span
                              title="Bitta o'quvchiga shu muddat ichida faqat bir marta qo'yiladi"
                              className="inline-flex items-center gap-1 rounded-md bg-amber-50 px-1.5 py-0.5 text-xs font-medium text-amber-700"
                            >
                              <Repeat className="h-3 w-3" /> {disciplinePeriodLabel[r.period]}
                            </span>
                          )}
                          {!r.isActive && (
                            <span className="rounded-md bg-slate-100 px-1.5 py-0.5 text-xs text-slate-400">
                              Faol emas
                            </span>
                          )}
                        </div>
                        {r.description && <p className="mt-0.5 text-xs text-slate-400">{r.description}</p>}
                      </td>
                      <td className="px-4 py-3">
                        <TypeBadge t={disciplineTypeOf(r.points)} />
                      </td>
                      <td className="px-4 py-3">
                        <PointsBadge p={r.points} />
                      </td>
                      <td className="px-4 py-3">
                        <OwnersCell ids={r.ownerUserIds ?? []} />
                      </td>
                      <td className="px-4 py-3">
                        <div className="flex items-center justify-end gap-0.5">
                          <button
                            type="button"
                            title="Tahrirlash"
                            onClick={() => openEdit(r)}
                            className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                          >
                            <Pencil className="h-4 w-4" />
                          </button>
                          <button
                            type="button"
                            title="O'chirish"
                            onClick={() => remove(r)}
                            className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                          >
                            <Trash2 className="h-4 w-4" />
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                  {other.length === 0 && (
                    <tr>
                      <td colSpan={6} className="px-4 py-8 text-center text-slate-400">
                        {typeFilter === 'all'
                          ? 'Hali sabab qo\'shilmagan — "Yangi sabab" tugmasi orqali qo\'shing'
                          : `${disciplineTypeLabel[typeFilter]} sabab yo'q`}
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </Card>
        </>
      )}

      <Modal
        open={open}
        onClose={() => setOpen(false)}
        title={
          editing
            ? editing.kind === 'attendance'
              ? `${editing.name} — ball`
              : 'Sababni tahrirlash'
            : 'Yangi sabab'
        }
        size={editing?.kind === 'attendance' ? 'sm' : 'md'}
        footer={
          <>
            <Button variant="secondary" onClick={() => setOpen(false)}>
              Bekor qilish
            </Button>
            <Button
              type="submit"
              form="reason-form"
              disabled={editing?.kind !== 'attendance' && (!name.trim() || points === 0)}
            >
              Saqlash
            </Button>
          </>
        }
      >
        <form id="reason-form" onSubmit={submit} className="space-y-4">
          <Input
            label="Sabab nomi"
            required
            placeholder="masalan: Darsga kech qoldi"
            value={name}
            disabled={editing?.kind === 'attendance'}
            onChange={(e) => setName(e.target.value)}
          />
          {editing?.kind === 'attendance' ? (
            <Input
              label="Ball (manfiy = jazo, musbat = rag'bat)"
              type="number"
              value={points}
              onChange={(e) => setPoints(Number(e.target.value) || 0)}
            />
          ) : (
            <>
              <div>
                <span className="mb-1 block text-sm font-medium text-slate-700">Turi</span>
                <div className="grid grid-cols-2 gap-2">
                  {(['positive', 'negative'] as const).map((t) => (
                    <button
                      key={t}
                      type="button"
                      onClick={() => setType(t)}
                      className={cn(
                        'rounded-lg border px-3 py-2 text-sm font-semibold transition-colors',
                        type === t
                          ? t === 'positive'
                            ? 'border-emerald-300 bg-emerald-50 text-emerald-700'
                            : 'border-red-300 bg-red-50 text-red-600'
                          : 'border-slate-200 bg-white text-slate-500 hover:bg-slate-50',
                      )}
                    >
                      {disciplineTypeLabel[t]}
                    </button>
                  ))}
                </div>
              </div>
              <Input
                label={type === 'positive' ? "Ball (qo'shiladi)" : 'Ball (ayriladi)'}
                type="number"
                min={1}
                value={points === 0 ? '' : Math.abs(points)}
                onChange={(e) => setPoints(Math.abs(Number(e.target.value) || 0))}
              />
            </>
          )}
          {editing?.kind !== 'attendance' && (
            <>
              <Textarea
                label="Izoh (ixtiyoriy)"
                rows={2}
                placeholder="qachon qo'yiladi, nimani anglatadi"
                value={description}
                onChange={(e) => setDescription(e.target.value)}
              />
              <div>
                <span className="mb-1 block text-sm font-medium text-slate-700">Muddat</span>
                <div className="grid grid-cols-3 gap-2">
                  {(['none', 'week', 'month'] as const).map((p) => (
                    <button
                      key={p}
                      type="button"
                      onClick={() => setPeriod(p)}
                      className={cn(
                        'rounded-lg border px-2 py-2 text-xs font-semibold transition-colors',
                        period === p
                          ? 'border-brand-300 bg-brand-50 text-brand-700'
                          : 'border-slate-200 bg-white text-slate-500 hover:bg-slate-50',
                      )}
                    >
                      {disciplinePeriodLabel[p]}
                    </button>
                  ))}
                </div>
                <p className="mt-1 text-xs text-slate-400">
                  {period === 'none'
                    ? 'Bitta o\'quvchiga xohlagancha qo\'yiladi.'
                    : period === 'week'
                      ? "Bitta o'quvchiga shu hafta (dushanba–yakshanba) ichida faqat bir marta qo'yiladi."
                      : "Bitta o'quvchiga shu oy ichida faqat bir marta qo'yiladi (masalan, sentabr uchun qo'yilsa — sentabr tugaguncha qayta qo'yilmaydi)."}
                </p>
              </div>
              <div>
                <div className="mb-1 flex items-center justify-between gap-2">
                  <span className="text-sm font-medium text-slate-700">Mas'ullar</span>
                  {ownerIds.length > 0 && (
                    <button
                      type="button"
                      onClick={() => setOwnerIds([])}
                      className="text-xs font-medium text-slate-400 hover:text-slate-600"
                    >
                      Tozalash
                    </button>
                  )}
                </div>
                <p className="mb-2 text-xs text-slate-400">
                  {ownerIds.length === 0
                    ? "Hech kim tanlanmagan — bu sabab bilan faqat admin ball qo'ya oladi. Ball qo'yadigan o'qituvchi, sinf rahbari yoki xodimni belgilang."
                    : `Sabab faqat tanlangan ${ownerIds.length} ta mas'ulga ko'rinadi va faqat ular shu sabab bilan ball qo'ya oladi.`}
                </p>
                <div className="rounded-lg border border-slate-200">
                  <div className="flex items-center gap-2 border-b border-slate-100 px-3 py-2">
                    <Search className="h-4 w-4 shrink-0 text-slate-400" />
                    <input
                      value={ownerSearch}
                      onChange={(e) => setOwnerSearch(e.target.value)}
                      placeholder="Ism yoki lavozim bo'yicha qidirish"
                      className="w-full bg-transparent text-sm text-slate-700 outline-none placeholder:text-slate-400"
                    />
                  </div>
                  <div className="max-h-48 overflow-y-auto py-1">
                    {ownerOptions.map((c) => (
                      <label
                        key={c.userId}
                        className="flex cursor-pointer items-center gap-2 px-3 py-1.5 text-sm hover:bg-slate-50"
                      >
                        <input
                          type="checkbox"
                          checked={ownerIds.includes(c.userId)}
                          onChange={() => toggleOwner(c.userId)}
                          className="h-4 w-4 shrink-0 rounded border-slate-300 accent-brand-600"
                        />
                        <span className={cn('min-w-0 flex-1 truncate', c.isArchived ? 'text-slate-400' : 'text-slate-700')}>
                          {c.fullName}
                          {c.isArchived && ' (arxiv)'}
                        </span>
                        <span className="shrink-0 text-xs text-slate-400">{c.label}</span>
                      </label>
                    ))}
                    {ownerOptions.length === 0 && (
                      <p className="px-3 py-3 text-center text-xs text-slate-400">Hech kim topilmadi</p>
                    )}
                  </div>
                </div>
              </div>
              <label className="flex cursor-pointer items-start gap-2">
                <input
                  type="checkbox"
                  checked={notifyParent}
                  onChange={(e) => setNotifyParent(e.target.checked)}
                  className="mt-0.5 h-4 w-4 rounded border-slate-300 accent-brand-600"
                />
                <span>
                  <span className="block text-sm font-medium text-slate-700">Ota-onaga xabar yuborish</span>
                  <span className="block text-xs text-slate-400">
                    Shu sabab bilan ball qo'yilganda ota-onaga Telegram bot orqali xabar boradi (faqat botga
                    ulangan ota-onalarga). Oldingi ballar uchun xabar yuborilmaydi. Har kechikishda xabar
                    oladigan ota-ona tez orada botni o'chirib qo'yadi — faqat muhim sabablar uchun yoqing.
                  </span>
                </span>
              </label>
              <label className="flex cursor-pointer items-start gap-2">
                <input
                  type="checkbox"
                  checked={isActive}
                  onChange={(e) => setIsActive(e.target.checked)}
                  className="mt-0.5 h-4 w-4 rounded border-slate-300 accent-brand-600"
                />
                <span>
                  <span className="block text-sm font-medium text-slate-700">Faol</span>
                  <span className="block text-xs text-slate-400">
                    Faol bo'lmagan sabab bilan yangi ball qo'yilmaydi; eski yozuvlar tarixda qoladi.
                  </span>
                </span>
              </label>
            </>
          )}
          {editing?.kind === 'attendance' ? (
            <p className="text-xs text-slate-400">
              Davomat sababi nomi "Sozlamalar → Davomat sabablari"da o'zgartiriladi. Bu yerda faqat ball
              belgilanadi. 0 = ballga ta'sir qilmaydi.
            </p>
          ) : (
            <p className="text-xs text-slate-400">
              Masalan: Salbiy 5 (kech qoldi), Salbiy 10 (janjal), Ijobiy 3 (faol ishtirok). Ball musbat son
              yoziladi, ishorasi turidan olinadi. 0 bo'lmasin.
            </p>
          )}
        </form>
      </Modal>
    </div>
  )
}
