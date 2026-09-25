import { useState } from 'react'
import { Award, ChevronDown, Search, SlidersHorizontal, X } from 'lucide-react'
import type { StudentListFilter, StudentPlacement } from '@/api/services/studentSearch'
import type { StudentStatusTag } from '@/api/services/studentStatuses'
import type { ArchiveReason } from '@/api/services/archiveReasons'
import type { CertificateType, IssuingTeacher } from '@/api/services/certificates'
import { genderLabels } from '@/config/constants'
import { cn } from '@/lib/utils'
import { StatusChip } from './StatusChip'
import { DatePicker } from '@/components/ui/DatePicker'

/**
 * O'quvchilar ro'yxatining filtr paneli — §2.3.1 dagi filtrlar ro'yxati.
 *
 * IKKI QAVAT. Yuqori qator BUGUNGI ekrandagi tanlovlarning aynan o'zi
 * (qidiruv, sinf, jins, balans, sertifikat) — kundalik ish shu qatorda
 * qoladi. Qolgan filtrlar "Filtrlar" tugmasi ostida: EduSchool ham ularni
 * yig'iladigan panelda tutadi, va ochilmagan panel hech narsani toraytirmaydi.
 *
 * FILTR YO'Q = RO'YXAT BUGUNGIDEK. Bo'sh qiymatlar so'rovga UMUMAN
 * qo'shilmaydi (`studentFilterParams`), ya'ni tegilmagan panel bilan so'rov
 * parametrsiz ketadi.
 *
 * `groupId` va `categoryId` filtrlari serverda BOR, lekin bu panelda yo'q:
 * ularning ro'yxatini beradigan ekranlar (Guruhlar, Moliya → Toifalar)
 * boshqa slice'larda quriladi. Ular tayyor bo'lganda shu yerga ikkita select
 * qo'shiladi, server tarafda o'zgarish kerak emas.
 */

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

const LANGUAGES: Array<{ value: string; label: string }> = [
  { value: 'uz', label: "O'zbek" },
  { value: 'ru', label: 'Rus' },
  { value: 'en', label: 'Ingliz' },
  { value: 'kaa', label: 'Qoraqalpoq' },
]

interface Props {
  filter: StudentListFilter
  onChange: (patch: Partial<StudentListFilter>) => void
  onReset: () => void
  /** Arxiv tab'ida arxivga oid filtrlar ko'rinadi, faol tab'ida — yo'q. */
  archived: boolean
  classNames: string[]
  grades: number[]
  statuses: StudentStatusTag[]
  archiveReasons: ArchiveReason[]
  certTypes: CertificateType[]
  certIssuers: IssuingTeacher[]
  /** Nechta filtr qo'yilgan — tugmadagi son. */
  activeCount: number
}

export function StudentListFilters({
  filter,
  onChange,
  onReset,
  archived,
  classNames,
  grades,
  statuses,
  archiveReasons,
  certTypes,
  certIssuers,
  activeCount,
}: Props) {
  const [open, setOpen] = useState(false)
  const [certMenuOpen, setCertMenuOpen] = useState(false)
  const [gradeMenuOpen, setGradeMenuOpen] = useState(false)

  const selectedGrades = filter.grades ?? []
  const certTypeIds = filter.certificateTypeIds ?? []

  const toggleGrade = (grade: number) =>
    onChange({
      grades: selectedGrades.includes(grade)
        ? selectedGrades.filter((g) => g !== grade)
        : [...selectedGrades, grade],
    })

  const toggleCertType = (id: string) =>
    onChange({
      certificateTypeIds: certTypeIds.includes(id)
        ? certTypeIds.filter((x) => x !== id)
        : [...certTypeIds, id],
    })

  /** Bo'sh satrni `undefined` ga aylantiradi — shunda filtr so'rovdan tushib qoladi. */
  const text = (value: string) => (value.trim() === '' ? undefined : value.trim())
  const num = (value: string) => {
    const parsed = Number(value.replace(/\s/g, ''))
    return value.trim() === '' || Number.isNaN(parsed) ? undefined : parsed
  }
  const yesNo = (value: string) => (value === '' ? undefined : value === 'yes')
  const fromYesNo = (value: boolean | undefined) =>
    value === undefined ? '' : value ? 'yes' : 'no'

  return (
    <div className="border-b border-slate-100">
      {/* Asosiy qator — bugungi ekrandagi tanlovlar */}
      <div className="flex flex-wrap items-center gap-3 p-4">
        <div className="relative min-w-[200px] flex-1">
          <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
          <input
            value={filter.search ?? ''}
            onChange={(e) => onChange({ search: e.target.value })}
            placeholder="F.I.SH, ota-ona yoki telefon bo'yicha qidirish..."
            className={cn(control, 'w-full pl-9')}
          />
        </div>

        <select
          value={filter.className ?? ''}
          onChange={(e) => onChange({ className: text(e.target.value) })}
          className={control}
        >
          <option value="">Barcha sinflar</option>
          {classNames.map((c) => (
            <option key={c} value={c}>
              {c}
            </option>
          ))}
        </select>

        <select
          value={filter.gender ?? ''}
          onChange={(e) =>
            onChange({ gender: (text(e.target.value) as 'male' | 'female' | undefined) })
          }
          className={control}
        >
          <option value="">Barcha jinslar</option>
          <option value="male">{genderLabels.male}</option>
          <option value="female">{genderLabels.female}</option>
        </select>

        <select
          value={filter.balanceState ?? ''}
          onChange={(e) =>
            onChange({ balanceState: (text(e.target.value) as 'debt' | 'paid' | 'credit' | undefined) })
          }
          className={control}
        >
          <option value="">Barcha balans</option>
          <option value="debt">Qarzdorlar</option>
          <option value="paid">Qarzsizlar</option>
          <option value="credit">Haqdorlar (avansi bor)</option>
        </select>

        {!archived && (
          <select
            value={filter.placement ?? ''}
            onChange={(e) => onChange({ placement: text(e.target.value) as StudentPlacement | undefined })}
            className={control}
            title="Sinfdagi holati"
          >
            <option value="">Sinfli va sinfsiz</option>
            <option value="inClass">Sinfli (sinfda o'qiyotganlar)</option>
            <option value="unassigned">Sinfsiz (sinfga qo'shilmaganlar)</option>
            <option value="waiting">Kutayotganlar</option>
            <option value="leftFromClass">Sinfdan chiqarilganlar</option>
          </select>
        )}

        <select
          value={filter.firstPayment ?? ''}
          onChange={(e) => onChange({ firstPayment: text(e.target.value) as 'ever' | 'thisMonth' | undefined })}
          className={control}
          title="To'lov qilganmi"
        >
          <option value="">Barcha to'lovlar</option>
          <option value="ever">To'lov qilganlar</option>
          <option value="thisMonth">Birinchi to'lovi shu oyda</option>
        </select>

        {statuses.length > 0 && (
          <select
            value={filter.statusId ?? ''}
            onChange={(e) => onChange({ statusId: text(e.target.value), hasStatus: undefined })}
            className={control}
            title="O'quvchi holati"
          >
            <option value="">Barcha holatlar</option>
            {statuses.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
        )}

        {certTypes.length > 0 && (
          <div className="relative">
            <button
              type="button"
              onClick={() => setCertMenuOpen((v) => !v)}
              className={cn(control, 'inline-flex items-center gap-1.5')}
            >
              <Award className="h-4 w-4 text-slate-400" />
              {certTypeIds.length === 0
                ? 'Sertifikat: hammasi'
                : certTypeIds.length === 1
                  ? (certTypes.find((t) => t.id === certTypeIds[0])?.name ?? 'Sertifikat')
                  : `Sertifikat: ${certTypeIds.length} ta`}
              <ChevronDown className="h-4 w-4 text-slate-400" />
            </button>
            {certMenuOpen && (
              <>
                <div className="fixed inset-0 z-10" onClick={() => setCertMenuOpen(false)} />
                <div className="absolute left-0 z-20 mt-1 max-h-64 w-64 overflow-y-auto rounded-xl border border-slate-200 bg-white p-2 shadow-lg">
                  {certTypeIds.length > 0 && (
                    <button
                      type="button"
                      onClick={() => onChange({ certificateTypeIds: [] })}
                      className="mb-1 w-full rounded-lg px-2 py-1.5 text-left text-sm text-slate-500 hover:bg-slate-50"
                    >
                      Tanlovni tozalash
                    </button>
                  )}
                  {certTypes.map((t) => (
                    <label
                      key={t.id}
                      className="flex cursor-pointer items-center gap-2 rounded-lg px-2 py-1.5 text-sm text-slate-700 hover:bg-slate-50"
                    >
                      <input
                        type="checkbox"
                        checked={certTypeIds.includes(t.id)}
                        onChange={() => toggleCertType(t.id)}
                        className="h-4 w-4 rounded border-slate-300 accent-brand-600"
                      />
                      {t.name}
                    </label>
                  ))}
                </div>
              </>
            )}
          </div>
        )}

        {certIssuers.length > 0 && (
          <select
            value={filter.certificateTeacherId ?? ''}
            onChange={(e) => onChange({ certificateTeacherId: text(e.target.value) })}
            className={control}
            title="Sertifikatni bergan o'qituvchi"
          >
            <option value="">Sertifikat bergan: hammasi</option>
            {certIssuers.map((t) => (
              <option key={t.id} value={t.id}>
                {t.fullName}
              </option>
            ))}
          </select>
        )}

        <button
          type="button"
          onClick={() => setOpen((v) => !v)}
          className={cn(
            control,
            'inline-flex items-center gap-1.5',
            open && 'border-brand-400 text-brand-700',
          )}
        >
          <SlidersHorizontal className="h-4 w-4 text-slate-400" />
          Filtrlar
          {activeCount > 0 && (
            <span className="rounded-full bg-brand-600 px-1.5 text-xs font-semibold text-white">
              {activeCount}
            </span>
          )}
        </button>

        {activeCount > 0 && (
          <button
            type="button"
            onClick={onReset}
            className="inline-flex items-center gap-1 text-sm text-slate-500 hover:text-slate-700"
          >
            <X className="h-4 w-4" /> Tozalash
          </button>
        )}
      </div>

      {/* Qo'shimcha filtrlar */}
      {open && (
        <div className="grid gap-4 border-t border-slate-100 bg-slate-50/60 p-4 sm:grid-cols-2 lg:grid-cols-4">
          <Field label="Sinf darajasi">
            <div className="relative">
              <button
                type="button"
                onClick={() => setGradeMenuOpen((v) => !v)}
                className={cn(control, 'inline-flex w-full items-center justify-between gap-1.5')}
              >
                {selectedGrades.length === 0
                  ? 'Hammasi'
                  : selectedGrades.slice().sort((a, b) => a - b).join(', ')}
                <ChevronDown className="h-4 w-4 text-slate-400" />
              </button>
              {gradeMenuOpen && (
                <>
                  <div className="fixed inset-0 z-10" onClick={() => setGradeMenuOpen(false)} />
                  <div className="absolute left-0 z-20 mt-1 max-h-64 w-48 overflow-y-auto rounded-xl border border-slate-200 bg-white p-2 shadow-lg">
                    {selectedGrades.length > 0 && (
                      <button
                        type="button"
                        onClick={() => onChange({ grades: [] })}
                        className="mb-1 w-full rounded-lg px-2 py-1.5 text-left text-sm text-slate-500 hover:bg-slate-50"
                      >
                        Tanlovni tozalash
                      </button>
                    )}
                    {grades.map((g) => (
                      <label
                        key={g}
                        className="flex cursor-pointer items-center gap-2 rounded-lg px-2 py-1.5 text-sm text-slate-700 hover:bg-slate-50"
                      >
                        <input
                          type="checkbox"
                          checked={selectedGrades.includes(g)}
                          onChange={() => toggleGrade(g)}
                          className="h-4 w-4 rounded border-slate-300 accent-brand-600"
                        />
                        {g}-sinf
                      </label>
                    ))}
                  </div>
                </>
              )}
            </div>
          </Field>

          <Field label="O'qish tili">
            <select
              value={filter.language ?? ''}
              onChange={(e) => onChange({ language: text(e.target.value) })}
              className={cn(control, 'w-full')}
            >
              <option value="">Hammasi</option>
              {LANGUAGES.map((l) => (
                <option key={l.value} value={l.value}>
                  {l.label}
                </option>
              ))}
            </select>
          </Field>

          <Field label="Holat qo'yilganmi">
            <select
              value={fromYesNo(filter.hasStatus)}
              onChange={(e) => onChange({ hasStatus: yesNo(e.target.value), statusId: undefined })}
              className={cn(control, 'w-full')}
            >
              <option value="">Farqi yo'q</option>
              <option value="yes">Holati bor</option>
              <option value="no">Holati yo'q</option>
            </select>
          </Field>

          <Field label="Shartnoma">
            <select
              value={fromYesNo(filter.hasContract)}
              onChange={(e) => onChange({ hasContract: yesNo(e.target.value) })}
              className={cn(control, 'w-full')}
            >
              <option value="">Farqi yo'q</option>
              <option value="yes">Shartnomasi bor</option>
              <option value="no">Shartnomasi yo'q</option>
            </select>
          </Field>

          <Field label="Obuna">
            <select
              value={fromYesNo(filter.hasSubscription)}
              onChange={(e) => onChange({ hasSubscription: yesNo(e.target.value) })}
              className={cn(control, 'w-full')}
            >
              <option value="">Farqi yo'q</option>
              <option value="yes">Faol obunasi bor</option>
              <option value="no">Faol obunasi yo'q</option>
            </select>
          </Field>

          <Field label="Chegirma">
            <select
              value={fromYesNo(filter.hasDiscount)}
              onChange={(e) => onChange({ hasDiscount: yesNo(e.target.value) })}
              className={cn(control, 'w-full')}
            >
              <option value="">Farqi yo'q</option>
              <option value="yes">Chegirmasi bor</option>
              <option value="no">Chegirmasi yo'q</option>
            </select>
          </Field>

          <Field label="Eng kam qarz (so'm)">
            <input
              inputMode="numeric"
              value={filter.minDebt ?? ''}
              onChange={(e) => onChange({ minDebt: num(e.target.value) })}
              placeholder="masalan 500000"
              className={cn(control, 'w-full')}
            />
          </Field>

          <Field label="Qoldiq oralig'i (ishorali)">
            <div className="flex items-center gap-2">
              <input
                inputMode="numeric"
                value={filter.balanceFrom ?? ''}
                onChange={(e) => onChange({ balanceFrom: num(e.target.value) })}
                placeholder="dan"
                className={cn(control, 'w-full')}
              />
              <input
                inputMode="numeric"
                value={filter.balanceTo ?? ''}
                onChange={(e) => onChange({ balanceTo: num(e.target.value) })}
                placeholder="gacha"
                className={cn(control, 'w-full')}
              />
            </div>
          </Field>

          <Field label="Yosh">
            <div className="flex items-center gap-2">
              <input
                inputMode="numeric"
                value={filter.ageFrom ?? ''}
                onChange={(e) => onChange({ ageFrom: num(e.target.value) })}
                placeholder="dan"
                className={cn(control, 'w-full')}
              />
              <input
                inputMode="numeric"
                value={filter.ageTo ?? ''}
                onChange={(e) => onChange({ ageTo: num(e.target.value) })}
                placeholder="gacha"
                className={cn(control, 'w-full')}
              />
            </div>
          </Field>

          <Field label="Qabul sanasi">
            <div className="flex items-center gap-2">
              <DatePicker
                value={filter.enrolledFrom ?? ''}
                onChange={(value: string) => onChange({ enrolledFrom: text(value) })}
                className="w-full"
              />
              <DatePicker
                value={filter.enrolledTo ?? ''}
                onChange={(value: string) => onChange({ enrolledTo: text(value) })}
                className="w-full"
              />
            </div>
          </Field>

          {archived && (
            <Field label="Arxiv sanasi">
              <div className="flex items-center gap-2">
                <DatePicker
                  value={filter.archivedFrom ?? ''}
                  onChange={(value: string) => onChange({ archivedFrom: text(value) })}
                  className="w-full"
                />
                <DatePicker
                  value={filter.archivedTo ?? ''}
                  onChange={(value: string) => onChange({ archivedTo: text(value) })}
                  className="w-full"
                />
              </div>
            </Field>
          )}

          {archived && archiveReasons.length > 0 && (
            <Field label="Arxivlash sababi">
              <select
                value={filter.archiveReasonId ?? ''}
                onChange={(e) => onChange({ archiveReasonId: text(e.target.value) })}
                className={cn(control, 'w-full')}
              >
                <option value="">Hammasi</option>
                {archiveReasons.map((r) => (
                  <option key={r.id} value={r.id}>
                    {r.name}
                  </option>
                ))}
              </select>
            </Field>
          )}

          {statuses.length > 0 && (
            <div className="sm:col-span-2 lg:col-span-4">
              <span className="mb-1 block text-xs font-medium uppercase tracking-wide text-slate-400">
                Holat bo'yicha tez tanlov
              </span>
              <div className="flex flex-wrap gap-2">
                {statuses.map((s) => (
                  <button
                    key={s.id}
                    type="button"
                    onClick={() =>
                      onChange({
                        statusId: filter.statusId === s.id ? undefined : s.id,
                        hasStatus: undefined,
                      })
                    }
                    className={cn(
                      'rounded-lg border p-0.5 transition-colors',
                      filter.statusId === s.id ? 'border-brand-400' : 'border-transparent',
                    )}
                  >
                    <StatusChip name={s.name} color={s.color} />
                  </button>
                ))}
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-medium uppercase tracking-wide text-slate-400">
        {label}
      </span>
      {children}
    </label>
  )
}
