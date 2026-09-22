import { useCallback, useEffect, useRef, useState } from 'react'
import { Upload, X, FileText, Loader2, Star, Link2Off, Send } from 'lucide-react'
import type { GuardianRelation, Student, StudentGuardian, StudentGuardianInput } from '@/types'
import type { StudentPayload } from '@/api/services/students'
import { uploadAdminFile, getStudentCredentials } from '@/api/services/students'
import {
  detachGuardian,
  getStudentCard,
  guardianRelations,
  makeGuardianPrimary,
  relationLabel,
  updateStudentGuardian,
} from '@/api/services/studentGuardians'
import { getClasses } from '@/api/services/classes'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { genderOptions } from '@/config/constants'
import { randomPassword, cn } from '@/lib/utils'
import { DatePicker } from '@/components/ui/DatePicker'

interface Props {
  open: boolean
  onClose: () => void
  onSubmit: (values: StudentPayload) => void
  /** Tahrirlash uchun mavjud o'quvchi, qo'shish uchun null */
  initial?: Student | null
  /**
   * Yangi o'quvchi formasini oldindan to'ldirish (masalan lid → o'quvchi).
   * Faqat `initial` yo'q bo'lganda ishlatiladi.
   */
  prefill?: Partial<StudentPayload> | null
  /** Sarlavhani almashtirish — sukut bo'yicha "Yangi o'quvchi" / "O'quvchini tahrirlash". */
  title?: string
}

/**
 * O'qish tili (students-parity.md §2.3, S-8). Ro'yxat bazadagi
 * `ck_students_language` bilan AYNAN bir xil — undan boshqasi 400 qaytadi.
 */
const languageOptions: { value: string; label: string }[] = [
  { value: '', label: "Ko'rsatilmagan" },
  { value: 'uz', label: "O'zbek" },
  { value: 'ru', label: 'Rus' },
  { value: 'en', label: 'Ingliz' },
  { value: 'kaa', label: 'Qoraqalpoq' },
]

/**
 * S-9 — sinfi hali yo'q o'quvchining mo'ljaldagi sinf darajasi. `ck_students_target_grade`
 * bilan bir xil diapazon (0-11); 0 — maktabgacha tayyorlov.
 */
const targetGradeOptions: { value: number; label: string }[] = [
  { value: 0, label: 'Tayyorlov (0)' },
  ...Array.from({ length: 11 }, (_, i) => ({ value: i + 1, label: `${i + 1}-sinf` })),
]

/** Formadagi ikkinchi vasiy — `students` qatorida ustuni yo'q, faqat vasiy jadvalida. */
interface GuardianDraft {
  /** Mavjud vasiyni tahrirlayapmizmi (null = yangi). */
  guardianId: string | null
  fullName: string
  phone: string
  relation: GuardianRelation
  relationNote: string
  passportUrl: string | null
}

const emptyGuardian: GuardianDraft = {
  guardianId: null,
  fullName: '',
  phone: '',
  relation: 'father',
  relationNote: '',
  passportUrl: null,
}

const empty: StudentPayload = {
  fullName: '',
  lastName: '',
  firstName: '',
  middleName: '',
  birthDate: '',
  birthCertificateUrl: null,
  address: '',
  gender: 'male',
  parentFullName: '',
  parentLastName: '',
  parentFirstName: '',
  parentMiddleName: '',
  parentPhone: '',
  parentPassportUrl: null,
  className: '',
  targetGrade: null,
  enrollmentDate: new Date().toISOString().slice(0, 10),
  subGroup: 0,
  phone: '',
  language: '',
  documentUrl: null,
}

/** "Familiya Ism Sharifi" stringidan parts. Eski yozuvlarni tahrirda taqsimlaymiz. */
/**
 * Oldindan to'ldirishda faqat to'liq ism berilgan bo'lsa (masalan lid), forma
 * ko'rsatadigan Familiya / Ism / Sharifi qismlari undan yig'iladi — tahrirdagi
 * eski o'quvchilar bilan bir xil qoida.
 */
function withNameParts(f: StudentPayload): StudentPayload {
  const s = f.lastName || f.firstName || f.middleName ? null : splitFullName(f.fullName ?? '')
  const p = f.parentLastName || f.parentFirstName || f.parentMiddleName
    ? null
    : splitFullName(f.parentFullName ?? '')
  return {
    ...f,
    ...(s && { lastName: s.last, firstName: s.first, middleName: s.middle }),
    ...(p && { parentLastName: p.last, parentFirstName: p.first, parentMiddleName: p.middle }),
  }
}

function splitFullName(full: string): { last: string; first: string; middle: string } {
  const parts = (full ?? '').trim().split(/\s+/).filter(Boolean)
  return {
    last: parts[0] ?? '',
    first: parts[1] ?? '',
    middle: parts.slice(2).join(' '),
  }
}

function joinName(last?: string, first?: string, middle?: string): string {
  return [last, first, middle]
    .map((p) => (p ?? '').trim())
    .filter((p) => p !== '')
    .join(' ')
}

/** Raqamlari 7 tadan kam bo'lsa telefon deb qabul qilinmaydi (server ham shunday). */
function hasPhone(value: string | null | undefined): boolean {
  return (value ?? '').replace(/\D/g, '').length >= 7
}

export function StudentFormModal({ open, onClose, onSubmit, initial, prefill, title }: Props) {
  const [form, setForm] = useState<StudentPayload>(empty)
  const [classNames, setClassNames] = useState<string[]>([])
  /** S-9 — sinfi hali yo'q: sinf tanlovi o'rniga mo'ljaldagi sinf darajasi ko'rsatiladi. */
  const [noClassYet, setNoClassYet] = useState(false)
  /** Fayl yuklash holatlari (har maydon uchun alohida). */
  const [uploading, setUploading] = useState<{
    birth?: boolean
    passport?: boolean
    document?: boolean
    guardian?: boolean
  }>({})
  /** Tahrirlanayotgan o'quvchining login (username)i — backend'dan olinadi, faqat ko'rsatish uchun. */
  const [login, setLogin] = useState('')

  /* ---- §2.3 (S-8) vasiylar ---- */
  /** Asosiy vasiyning turi (eski "Ota-ona" maydonlari aynan shu odam). */
  const [primaryRelation, setPrimaryRelation] = useState<GuardianRelation>('parent')
  const [primaryNote, setPrimaryNote] = useState('')
  const [second, setSecond] = useState<GuardianDraft>(emptyGuardian)
  /** Saqlangan vasiylar — faqat tahrirda; ro'yxat serverdan keladi. */
  const [saved, setSaved] = useState<StudentGuardian[]>([])
  const [busy, setBusy] = useState(false)

  const today = new Date().toISOString().slice(0, 10)

  useEffect(() => {
    if (open) getClasses().then((cs) => setClassNames(cs.map((c) => c.name)))
  }, [open])

  // Tahrirda o'quvchining login(username)ini yuklab ko'rsatamiz.
  useEffect(() => {
    if (!open || !initial) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- avvalgi loginni tozalash (maqsadli)
    setLogin('')
    let active = true
    getStudentCredentials(initial.id)
      .then((c) => { if (active) setLogin(c.login) })
      .catch(() => { /* tarmoq/mok xatosi — login ko'rsatilmaydi */ })
    return () => { active = false }
  }, [open, initial])

  /**
   * Vasiylar va ro'yxat ustunlarida BO'LMAGAN maydonlar (telefon, til, hujjat)
   * alohida so'rov bilan keladi. Ularsiz forma ochilsa, saqlashda ular
   * jimgina tozalanib ketardi.
   */
  const loadCard = useCallback((studentId: string, syncParent = false) => {
    // `setBusy(true)` ATAYLAB bu yerda emas — uni chaqiruvchi (tugma bosilishi)
    // qo'yadi. Shunda effekt ichida to'g'ridan-to'g'ri holat yozilmaydi.
    return getStudentCard(studentId)
      .then((card) => {
        setSaved(card.guardians)
        const extra = card.guardians.find((g) => !g.isPrimary)
        const primary = card.guardians.find((g) => g.isPrimary)
        setForm((f) => {
          const next = {
            ...f,
            phone: card.phone ?? '',
            language: card.language ?? '',
            documentUrl: card.documentUrl ?? null,
          }
          // Asosiy vasiy almashgan bo'lsa server `parent_*` ustunlarini ham
          // yangilagan — formadagi eski qiymat saqlanishda uni qaytarib
          // yozib yuborardi.
          if (!syncParent || !primary) return next
          const parts = splitFullName(primary.fullName)
          return {
            ...next,
            parentFullName: primary.fullName,
            parentLastName: parts.last,
            parentFirstName: parts.first,
            parentMiddleName: parts.middle,
            parentPhone: primary.phone,
            parentPassportUrl: primary.passportUrl,
          }
        })
        setPrimaryRelation(primary?.relation ?? 'parent')
        setPrimaryNote(primary?.relationNote ?? '')
        setSecond(
          extra
            ? {
                guardianId: extra.guardianId,
                fullName: extra.fullName,
                phone: extra.phone,
                relation: extra.relation,
                relationNote: extra.relationNote ?? '',
                passportUrl: extra.passportUrl,
              }
            : emptyGuardian,
        )
      })
      .catch(() => { /* mok yoki tarmoq xatosi — vasiylar bo'limi bo'sh qoladi */ })
      .finally(() => setBusy(false))
  }, [])

  useEffect(() => {
    if (!open) return
    if (initial) {
      // Tahrirda: agar parts saqlanmagan bo'lsa, FullName'dan parse qilamiz (eski o'quvchilar).
      const sParts = initial.lastName || initial.firstName || initial.middleName
        ? { last: initial.lastName ?? '', first: initial.firstName ?? '', middle: initial.middleName ?? '' }
        : splitFullName(initial.fullName)
      const pParts = initial.parentLastName || initial.parentFirstName || initial.parentMiddleName
        ? { last: initial.parentLastName ?? '', first: initial.parentFirstName ?? '', middle: initial.parentMiddleName ?? '' }
        : splitFullName(initial.parentFullName)
      // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda formani initial bilan sinxronlash (maqsadli)
      setForm({
        fullName: initial.fullName,
        lastName: sParts.last,
        firstName: sParts.first,
        middleName: sParts.middle,
        birthDate: initial.birthDate,
        birthCertificateUrl: initial.birthCertificateUrl ?? null,
        address: initial.address,
        gender: initial.gender,
        parentFullName: initial.parentFullName,
        parentLastName: pParts.last,
        parentFirstName: pParts.first,
        parentMiddleName: pParts.middle,
        parentPhone: initial.parentPhone,
        parentPassportUrl: initial.parentPassportUrl ?? null,
        className: initial.className,
        targetGrade: initial.targetGrade ?? null,
        enrollmentDate: initial.enrollmentDate,
        subGroup: initial.subGroup,
        phone: initial.phone ?? '',
        language: initial.language ?? '',
        documentUrl: initial.documentUrl ?? null,
      })
      // S-9 — sinfi bo'sh o'quvchi mo'ljal rejimida ochiladi.
      setNoClassYet(!initial.className)
      void loadCard(initial.id)
    } else {
      /* eslint-disable react-hooks/set-state-in-effect -- yangi forma boshlash (maqsadli) */
      setForm(prefill ? withNameParts({ ...empty, ...prefill }) : empty)
      setSaved([])
      setPrimaryRelation('parent')
      setPrimaryNote('')
      setSecond(emptyGuardian)
      setNoClassYet(false)
      /* eslint-enable react-hooks/set-state-in-effect */
    }
  }, [open, initial, loadCard, prefill])

  // Yangi o'quvchida sinf tanlanmagan bo'lsa, birinchi sinfni standart qilamiz
  // (S-9 — "sinfi hali yo'q" belgilangan bo'lsa TEGILMAYDI, u sinfsiz qoladi).
  useEffect(() => {
    if (open && !initial && !noClassYet && classNames.length) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- sinflar yuklangach standart sinfni o'rnatish (maqsadli)
      setForm((f) => (f.className ? f : { ...f, className: classNames[0] }))
    }
  }, [open, initial, classNames, noClassYet])

  /**
   * S-9 — "sinfi hali yo'q" belgisi almashganda ikkala maydon ham mos
   * holatga qaytariladi: sinf tanlansa mo'ljal bo'shaydi (server ham xuddi
   * shunday tozalaydi — `CleanTargetGrade`), sinfsiz rejimga o'tilsa sinf
   * bo'shaydi va mo'ljalga sukut qiymat (0) qo'yiladi.
   */
  const toggleNoClassYet = (value: boolean) => {
    setNoClassYet(value)
    setForm((f) =>
      value
        ? { ...f, className: '', targetGrade: f.targetGrade ?? 0 }
        : { ...f, className: f.className || classNames[0] || '', targetGrade: null },
    )
  }

  const update = <K extends keyof StudentPayload>(key: K, value: StudentPayload[K]) =>
    setForm((f) => ({ ...f, [key]: value }))

  /** Fayl yuklash — admin uploads endpoint'iga uzatib, qaytgan URL'ni formaga yozadi. */
  const handleUpload = async (
    key: 'birthCertificateUrl' | 'parentPassportUrl' | 'documentUrl',
    file: File,
  ) => {
    const flag = key === 'birthCertificateUrl' ? 'birth' : key === 'documentUrl' ? 'document' : 'passport'
    setUploading((u) => ({ ...u, [flag]: true }))
    try {
      const res = await uploadAdminFile(file)
      update(key, res.url)
    } catch {
      // mock yoki tarmoq xatosi — sukut bilan o'tkazamiz; istasangiz toast ko'rsatish mumkin
    } finally {
      setUploading((u) => ({ ...u, [flag]: false }))
    }
  }

  const handleGuardianUpload = async (file: File) => {
    setUploading((u) => ({ ...u, guardian: true }))
    try {
      const res = await uploadAdminFile(file)
      setSecond((g) => ({ ...g, passportUrl: res.url }))
    } catch {
      // mok yoki tarmoq xatosi
    } finally {
      setUploading((u) => ({ ...u, guardian: false }))
    }
  }

  /** Asosiy vasiyni almashtirish — eski `parent_phone` ustuni ham shunga tenglashadi. */
  const handleMakePrimary = async (guardianId: string) => {
    if (!initial) return
    setBusy(true)
    try {
      await makeGuardianPrimary(initial.id, guardianId)
      await loadCard(initial.id, true)
    } catch {
      alert("Asosiy vasiyni almashtirib bo'lmadi")
      setBusy(false)
    }
  }

  /** Vasiyni uzish. Vasiy qatori o'chmaydi — uning boshqa farzandi bo'lishi mumkin. */
  const handleDetach = async (g: StudentGuardian) => {
    if (!initial) return
    if (!confirm(`${g.fullName} shu o'quvchidan uzilsinmi?`)) return
    setBusy(true)
    try {
      await detachGuardian(initial.id, g.guardianId)
      await loadCard(initial.id, true)
    } catch {
      alert("Vasiyni uzib bo'lmadi (yagona vasiy bo'lishi mumkin)")
      setBusy(false)
    }
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    const last = (form.lastName ?? '').trim()
    const first = (form.firstName ?? '').trim()
    const middle = (form.middleName ?? '').trim()
    if (!last && !first && !middle) return
    const pwd = (form.newPassword ?? '').trim()
    if (pwd.length > 0 && pwd.length < 8) {
      alert("Parol kamida 8 belgidan iborat bo'lsin")
      return
    }
    const fullName = joinName(last, first, middle)
    const parentFullName = joinName(form.parentLastName, form.parentFirstName, form.parentMiddleName)

    // §2.3 (S-8) — vasiylar. Birinchi yozuv ATAYLAB eski "Ota-ona"
    // maydonlarining o'zi: shunda `parent_full_name` / `parent_phone` va
    // vasiy qatori hech qachon ayrilmaydi.
    const guardians: StudentGuardianInput[] = []
    if (hasPhone(form.parentPhone)) {
      guardians.push({
        fullName: parentFullName,
        phone: form.parentPhone,
        relation: primaryRelation,
        relationNote: primaryRelation === 'other' ? primaryNote.trim() : null,
        isPrimary: true,
        passportUrl: form.parentPassportUrl ?? null,
      })
    }

    const secondInput: StudentGuardianInput = {
      fullName: second.fullName.trim(),
      phone: second.phone.trim(),
      relation: second.relation,
      relationNote: second.relation === 'other' ? second.relationNote.trim() : null,
      isPrimary: false,
      passportUrl: second.passportUrl,
    }
    const secondFilled = hasPhone(second.phone) && secondInput.fullName.length > 0

    if (secondFilled) {
      if (initial && second.guardianId) {
        // MAVJUD vasiy: telefon ham o'zgargan bo'lishi mumkin, shuning uchun
        // uni payload orqali emas, o'z endpointi orqali yangilaymiz —
        // aks holda yangi raqam YANGI vasiy yasab, eskisi osilib qolardi.
        try {
          await updateStudentGuardian(initial.id, second.guardianId, secondInput)
        } catch {
          alert("Ikkinchi vasiyni saqlab bo'lmadi — telefon raqamini tekshiring")
          return
        }
      } else {
        guardians.push(secondInput)
      }
    }

    onSubmit({
      ...form,
      fullName,
      parentFullName,
      guardians: guardians.length > 0 ? guardians : undefined,
    })
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="lg"
      title={title ?? (initial ? "O'quvchini tahrirlash" : "Yangi o'quvchi")}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button type="submit" form="student-form">
            Saqlash
          </Button>
        </>
      }
    >
      <form id="student-form" onSubmit={handleSubmit} className="space-y-5">
        {/* ---------- O'quvchi ---------- */}
        <Section title="O'quvchi">
          <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
            <Input
              label="Familiya"
              required
              value={form.lastName ?? ''}
              onChange={(e) => update('lastName', e.target.value)}
            />
            <Input
              label="Ism"
              required
              value={form.firstName ?? ''}
              onChange={(e) => update('firstName', e.target.value)}
            />
            <Input
              label="Otasining ismi"
              value={form.middleName ?? ''}
              onChange={(e) => update('middleName', e.target.value)}
            />
          </div>
          <div className="mt-3 grid grid-cols-1 gap-3 md:grid-cols-2">
            <DatePicker
              label="Tug'ilgan kun"
              max={today}
              value={form.birthDate}
              onChange={(value: string) => update('birthDate', value)}
            />
            <Select
              label="Jinsi"
              value={form.gender}
              onChange={(e) => update('gender', e.target.value as StudentPayload['gender'])}
            >
              {genderOptions.map((g) => (
                <option key={g.value} value={g.value}>
                  {g.label}
                </option>
              ))}
            </Select>
          </div>
          <div className="mt-3 grid grid-cols-1 gap-3 md:grid-cols-2">
            <Input
              label="O'quvchi telefoni"
              placeholder="+998 90 123 45 67"
              value={form.phone ?? ''}
              onChange={(e) => update('phone', e.target.value)}
            />
            <Select
              label="O'qish tili"
              value={form.language ?? ''}
              onChange={(e) => update('language', e.target.value)}
            >
              {languageOptions.map((l) => (
                <option key={l.value} value={l.value}>
                  {l.label}
                </option>
              ))}
            </Select>
          </div>
          <div className="mt-3 grid grid-cols-1 gap-3 md:grid-cols-2">
            <FileField
              label="O'quvchi rasmi"
              url={form.birthCertificateUrl ?? null}
              uploading={uploading.birth}
              onUpload={(f) => handleUpload('birthCertificateUrl', f)}
              onClear={() => update('birthCertificateUrl', null)}
            />
            <FileField
              label="Hujjat nusxasi (metrika / pasport)"
              accept="image/*,application/pdf"
              url={form.documentUrl ?? null}
              uploading={uploading.document}
              onUpload={(f) => handleUpload('documentUrl', f)}
              onClear={() => update('documentUrl', null)}
            />
          </div>
        </Section>

        {/* ---------- Ota-ona (asosiy vasiy) ---------- */}
        <Section title="Ota-ona (asosiy vasiy)">
          <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
            <Input
              label="Familiya"
              value={form.parentLastName ?? ''}
              onChange={(e) => update('parentLastName', e.target.value)}
            />
            <Input
              label="Ism"
              value={form.parentFirstName ?? ''}
              onChange={(e) => update('parentFirstName', e.target.value)}
            />
            <Input
              label="Otasining ismi"
              value={form.parentMiddleName ?? ''}
              onChange={(e) => update('parentMiddleName', e.target.value)}
            />
          </div>
          <div className="mt-3 grid grid-cols-1 gap-3 md:grid-cols-3">
            <Input
              label="Telefon raqami"
              placeholder="+998 90 123 45 67"
              value={form.parentPhone}
              onChange={(e) => update('parentPhone', e.target.value)}
            />
            <Select
              label="Kimi bo'ladi"
              value={primaryRelation}
              onChange={(e) => setPrimaryRelation(e.target.value as GuardianRelation)}
            >
              {guardianRelations.map((r) => (
                <option key={r.value} value={r.value}>
                  {r.label}
                </option>
              ))}
            </Select>
            {primaryRelation === 'other' && (
              <Input
                label="Kim ekani"
                placeholder="amakisi, opasi..."
                value={primaryNote}
                onChange={(e) => setPrimaryNote(e.target.value)}
              />
            )}
          </div>
          <div className="mt-3">
            <FileField
              label="Ota-ona rasmi"
              url={form.parentPassportUrl ?? null}
              uploading={uploading.passport}
              onUpload={(f) => handleUpload('parentPassportUrl', f)}
              onClear={() => update('parentPassportUrl', null)}
            />
          </div>
          <p className="mt-2 text-xs text-slate-400">
            Telegram xabarlari va chek shu odamga boradi. Raqam vasiylar ro'yxatiga ham
            avtomatik tushadi.
          </p>
        </Section>

        {/* ---------- Ikkinchi vasiy ---------- */}
        <Section title="Ikkinchi vasiy">
          <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
            <Input
              label="F.I.SH"
              placeholder="Karimova Dilnoza"
              value={second.fullName}
              onChange={(e) => setSecond((g) => ({ ...g, fullName: e.target.value }))}
            />
            <Input
              label="Telefon raqami"
              placeholder="+998 90 123 45 67"
              value={second.phone}
              onChange={(e) => setSecond((g) => ({ ...g, phone: e.target.value }))}
            />
          </div>
          <div className="mt-3 grid grid-cols-1 gap-3 md:grid-cols-2">
            <Select
              label="Kimi bo'ladi"
              value={second.relation}
              onChange={(e) =>
                setSecond((g) => ({ ...g, relation: e.target.value as GuardianRelation }))
              }
            >
              {guardianRelations.map((r) => (
                <option key={r.value} value={r.value}>
                  {r.label}
                </option>
              ))}
            </Select>
            {second.relation === 'other' && (
              <Input
                label="Kim ekani"
                placeholder="amakisi, opasi..."
                value={second.relationNote}
                onChange={(e) => setSecond((g) => ({ ...g, relationNote: e.target.value }))}
              />
            )}
          </div>
          <div className="mt-3">
            <FileField
              label="Hujjat nusxasi"
              accept="image/*,application/pdf"
              url={second.passportUrl}
              uploading={uploading.guardian}
              onUpload={handleGuardianUpload}
              onClear={() => setSecond((g) => ({ ...g, passportUrl: null }))}
            />
          </div>
          <p className="mt-2 text-xs text-slate-400">
            Ixtiyoriy. F.I.SH va telefon to'ldirilsa saqlanadi; bo'sh qolsa hech narsa
            o'zgarmaydi.
          </p>
        </Section>

        {/* ---------- Saqlangan vasiylar (faqat tahrirda) ---------- */}
        {initial && saved.length > 0 && (
          <Section title="Vasiylar ro'yxati">
            <ul className="space-y-2">
              {saved.map((g) => (
                <li
                  key={g.guardianId}
                  className="flex flex-wrap items-center gap-2 rounded-lg border border-slate-200 bg-white px-3 py-2"
                >
                  <div className="min-w-[160px] flex-1">
                    <div className="flex items-center gap-1.5 text-sm font-medium text-slate-800">
                      {g.isPrimary && <Star className="h-3.5 w-3.5 fill-amber-400 text-amber-400" />}
                      {g.fullName}
                    </div>
                    <div className="text-xs text-slate-500">
                      {g.phone} · {relationLabel(g.relation, g.relationNote)}
                      {g.childrenCount > 1 && ` · ${g.childrenCount} farzand`}
                    </div>
                  </div>
                  {g.telegramLinked && (
                    <span className="inline-flex items-center gap-1 rounded-md bg-sky-50 px-2 py-0.5 text-xs font-medium text-sky-700">
                      <Send className="h-3 w-3" /> Telegram
                    </span>
                  )}
                  {!g.isPrimary && (
                    <>
                      <Button
                        type="button"
                        variant="secondary"
                        disabled={busy}
                        onClick={() => handleMakePrimary(g.guardianId)}
                      >
                        <Star className="h-4 w-4" /> Asosiy qilish
                      </Button>
                      <Button
                        type="button"
                        variant="danger"
                        disabled={busy}
                        onClick={() => handleDetach(g)}
                      >
                        <Link2Off className="h-4 w-4" /> Uzish
                      </Button>
                    </>
                  )}
                </li>
              ))}
            </ul>
            <p className="mt-2 text-xs text-slate-400">
              Asosiy vasiy yulduzcha bilan belgilangan — chek, shartnoma va xabarlar unga
              boradi. Uzilgan vasiy tizimdan o'chmaydi: uning boshqa farzandi bo'lishi mumkin.
            </p>
          </Section>
        )}

        {/* ---------- Boshqa ma'lumotlar ---------- */}
        <Section title="Boshqa ma'lumotlar">
          <Input
            label="Manzil"
            value={form.address}
            onChange={(e) => update('address', e.target.value)}
          />
          <div className="mt-3 grid grid-cols-1 gap-3 md:grid-cols-2">
            {noClassYet ? (
              <Select
                label="Mo'ljaldagi sinf darajasi"
                value={String(form.targetGrade ?? 0)}
                onChange={(e) => update('targetGrade', Number(e.target.value))}
              >
                {targetGradeOptions.map((g) => (
                  <option key={g.value} value={g.value}>
                    {g.label}
                  </option>
                ))}
              </Select>
            ) : (
              <Select
                label="Sinfga biriktirish"
                value={form.className}
                onChange={(e) => update('className', e.target.value)}
              >
                {classNames.map((c) => (
                  <option key={c} value={c}>
                    {c}
                  </option>
                ))}
              </Select>
            )}
            <DatePicker
              label="Maktabga kelgan sana"
              value={form.enrollmentDate}
              onChange={(value: string) => update('enrollmentDate', value)}
            />
          </div>
          <label className="mt-3 flex items-center gap-2 text-sm text-slate-600">
            <input
              type="checkbox"
              className="h-4 w-4 accent-brand-600"
              checked={noClassYet}
              onChange={(e) => toggleNoClassYet(e.target.checked)}
            />
            Sinfi hali yo'q — faqat mo'ljaldagi sinf darajasi bilan qo'shish (S-9)
          </label>
          {noClassYet && (
            <p className="mt-1 text-xs text-slate-400">
              Bu o'quvchi sinf ro'yxatida ko'rinmaydi; keyinroq sinf jadvalidan mos sinfga
              biriktirilganda mo'ljal avtomatik bo'shaydi.
            </p>
          )}
        </Section>

        {/*
          Chegirma maydonlari BU YERDAN OLIB TASHLANDI (P1-21).
          Mijoz javobi (SPEC §8.1 Q5): har qanday chegirma direktor tasdig'ini
          talab qiladi. O'quvchi kartochkasidagi maydon esa tasdiqsiz chegirma
          berishning ochiq yo'li edi. Yangi joyi — "Moliya → Chegirmalar":
          chegirma `pending` bo'lib tug'iladi va tasdiqlangunicha hisob-kitobga
          ta'sir qilmaydi. Oylik summa ham bu yerda emas — u obunada
          ("Moliya → Obunalar", toifa bo'yicha).

          Ota-onaning EMAIL va PAROLI ham ataylab yo'q (§2.3.3 "declined
          outright"): ota-ona tizimga Telegram orqali kiradi.
        */}

        {/* ---------- Login va parol (faqat tahrirda) ---------- */}
        {initial && (
          <Section title="Login va parol">
            <Input
              label="Login (username)"
              value={login}
              readOnly
              placeholder="Yuklanmoqda..."
              className="bg-slate-100 text-slate-600"
            />
            <div className="mt-3 flex items-end gap-2">
              <div className="flex-1">
                <Input
                  label="Yangi parol"
                  type="text"
                  autoComplete="new-password"
                  placeholder="Bo'sh qoldirilsa — parol o'zgarmaydi"
                  value={form.newPassword ?? ''}
                  onChange={(e) => update('newPassword', e.target.value)}
                />
              </div>
              <Button
                type="button"
                variant="secondary"
                onClick={() => update('newPassword', randomPassword(8))}
              >
                Generatsiya
              </Button>
            </div>
            <p className="mt-1 text-xs text-slate-400">
              Login o'zgarmaydi. Yangi parol kamida 8 belgi — kiriting yoki generatsiya qiling;
              saqlangach o'quvchiga topshiring.
            </p>
          </Section>
        )}
      </form>
    </Modal>
  )
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="rounded-xl border border-slate-200 bg-slate-50/40 p-3">
      <h4 className="mb-3 text-xs font-semibold uppercase tracking-wide text-slate-500">{title}</h4>
      {children}
    </div>
  )
}

function FileField({
  label,
  url,
  uploading,
  accept,
  onUpload,
  onClear,
}: {
  label: string
  url: string | null
  uploading: boolean | undefined
  accept?: string
  onUpload: (file: File) => void
  onClear: () => void
}) {
  const ref = useRef<HTMLInputElement | null>(null)
  const isImage = url && /\.(png|jpe?g|webp|gif|bmp)$/i.test(url)
  return (
    <div>
      <span className="mb-1 block text-sm font-medium text-slate-600">{label}</span>
      <div className="flex items-start gap-3">
        <div
          className={cn(
            'flex h-20 w-20 shrink-0 items-center justify-center rounded-lg border border-dashed bg-white',
            url ? 'border-brand-200' : 'border-slate-200 text-slate-300',
          )}
        >
          {uploading ? (
            <Loader2 className="h-5 w-5 animate-spin text-brand-500" />
          ) : isImage ? (
            <img src={url} alt="" className="h-full w-full rounded-lg object-cover" />
          ) : url ? (
            <FileText className="h-7 w-7 text-brand-500" />
          ) : (
            <Upload className="h-5 w-5" />
          )}
        </div>
        <div className="flex flex-1 flex-col gap-2">
          <input
            ref={ref}
            type="file"
            accept={accept ?? 'image/*'}
            className="hidden"
            onChange={(e) => {
              const f = e.target.files?.[0]
              if (f) onUpload(f)
              if (ref.current) ref.current.value = ''
            }}
          />
          <div className="flex gap-2">
            <Button
              type="button"
              variant="secondary"
              onClick={() => ref.current?.click()}
              disabled={uploading}
            >
              <Upload className="h-4 w-4" />
              {url ? 'Yangilash' : 'Yuklash'}
            </Button>
            {url && (
              <>
                <Button
                  type="button"
                  variant="secondary"
                  onClick={() => window.open(url, '_blank', 'noopener,noreferrer')}
                >
                  Ko'rish
                </Button>
                <Button type="button" variant="danger" onClick={onClear}>
                  <X className="h-4 w-4" /> O'chirish
                </Button>
              </>
            )}
          </div>
          {url && <p className="text-xs text-slate-400 break-all">{url}</p>}
        </div>
      </div>
    </div>
  )
}
