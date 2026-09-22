/**
 * Ariza tahriri oynasidagi JONLI KO'RINISH — ota-ona telefonida ochadigan
 * kartaning aynan o'zi (`docs/modules/sales-marketing.md` §6.2, task SM-8).
 *
 * MAYDON KOMPONENTLARI QAYTA ISHLATILGAN, ko'chirilmagan: `TextField`,
 * `PhoneField`, `SelectField`, `RadioPills` — hammasi SM-7 ning
 * `pages/public/SurveyFields.tsx` faylidan keladi. Shu sababli ko'rinish
 * haqiqiy sahifadan chetga chiqa olmaydi: o'sha faylga tegilsa, bu yer ham
 * o'zi bilan o'zgaradi. SM-7 fayllari TAHRIRLANMAGAN.
 *
 * NIMA UCHUN `fieldset disabled`: ko'rinish — rasm, forma emas. `disabled`
 * brauzerning o'zi beradigan qulf: ichidagi hech bir maydon fokus olmaydi,
 * tab tartibiga tushmaydi va yubormaydi. `aria-hidden` esa uni ekran
 * o'qigichdan yashiradi — xodim bitta formani ikki marta eshitmasin.
 *
 * Tugma `<div>` sifatida chizilgan: `<Button disabled>` xira ko'rinadi va
 * ota-ona ko'radigan kartani yolg'on ko'rsatardi.
 */
import type { PublicSurveyFields } from '@/api/services/publicSurvey'
import { PhoneField, RadioPills, SelectField, TextField } from '@/pages/public/SurveyFields'
import { Card } from '@/components/ui/Card'
import { FileText } from 'lucide-react'

/** 0 — haqiqiy sinf (nol sinf), "noma'lum" emas (§2.1). */
const GRADES: readonly number[] = Array.from({ length: 12 }, (_, i) => i)

function gradeLabel(grade: number): string {
  return grade === 0 ? 'Nol sinf' : `${grade}-sinf`
}

const GENDER_OPTIONS = [
  { value: 'male', label: "O'g'il bola" },
  { value: 'female', label: 'Qiz bola' },
] as const

/** Ko'rinish faqat shu to'rt matnni va beshta kalitni biladi. */
export interface SurveyPreviewProps {
  name: string
  subtitle: string
  imageUrl: string | null
  offerUrl: string | null
  thankYouText: string
  fields: PublicSurveyFields
}

const DEFAULT_THANK_YOU = "Arizangiz qabul qilindi. Tez orada siz bilan bog'lanamiz."

export function SurveyPreview({
  name,
  subtitle,
  imageUrl,
  offerUrl,
  thankYouText,
  fields,
}: SurveyPreviewProps) {
  const noop = () => {}

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <span className="text-sm font-medium text-slate-600">Ko'rinishi</span>
        <span className="text-xs text-slate-400">ota-ona telefonida shunday ochiladi</span>
      </div>

      <div className="rounded-2xl bg-slate-100 p-4">
        <div className="mx-auto w-full max-w-sm space-y-4">
          {/* Ommaviy sahifaning sarlavhasi — SurveyPage.tsx dagi bilan bir xil. */}
          <div className="flex items-center justify-center gap-3">
            <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-[#FFD006]">
              <img src="/logo.png" alt="" className="h-7 w-7 object-contain" />
            </div>
            <span className="text-base font-semibold text-slate-800">
              Wunderkind International School
            </span>
          </div>

          <Card>
            {imageUrl && (
              <img
                src={imageUrl}
                alt=""
                className="mb-4 h-40 w-full rounded-xl bg-slate-100 object-cover"
              />
            )}

            <h2 className="text-xl font-semibold text-slate-800">
              {name.trim() || 'Ariza nomi'}
            </h2>
            {subtitle.trim() && <p className="mt-1 text-base text-slate-500">{subtitle}</p>}

            <fieldset disabled aria-hidden="true" className="pointer-events-none mt-5 space-y-4">
              <TextField
                label="Ota-onaning ismi"
                required
                value=""
                onValueChange={noop}
                placeholder="Aziz"
              />
              <TextField
                label="Ota-onaning familiyasi"
                value=""
                onValueChange={noop}
                placeholder="Karimov"
              />
              <PhoneField
                label="Telefon raqami"
                required
                digits=""
                onDigitsChange={noop}
                hint="Shu raqamga qo'ng'iroq qilamiz"
              />

              {fields.studentFirstName && (
                <TextField
                  label="O'quvchining ismi"
                  required
                  value=""
                  onValueChange={noop}
                  placeholder="Ali"
                />
              )}
              {fields.studentLastName && (
                <TextField
                  label="O'quvchining familiyasi"
                  value=""
                  onValueChange={noop}
                  placeholder="Karimov"
                />
              )}
              {fields.studentGender && (
                <RadioPills
                  label="Jinsi"
                  name="preview-student-gender"
                  value=""
                  options={GENDER_OPTIONS}
                  onValueChange={noop}
                  required
                />
              )}
              {fields.studentGrade && (
                <SelectField label="Nechanchi sinfga" required value="" onValueChange={noop}>
                  <option value="">Tanlang</option>
                  {GRADES.map((grade) => (
                    <option key={grade} value={String(grade)}>
                      {gradeLabel(grade)}
                    </option>
                  ))}
                </SelectField>
              )}
              {fields.studentPhone && (
                <PhoneField
                  label="O'quvchining telefoni"
                  digits=""
                  onDigitsChange={noop}
                  hint="Majburiy emas"
                />
              )}

              {/* Haqiqiy sahifadagi `Button` ning aynan o'zi, faqat bosilmaydigani. */}
              <div className="flex h-12 w-full items-center justify-center rounded-lg bg-brand-600 text-sm font-medium text-white">
                Yuborish
              </div>
            </fieldset>

            {offerUrl && (
              <div className="mt-4 border-t border-slate-100 pt-4">
                <span className="inline-flex items-center justify-center gap-2 rounded-xl border border-slate-200 px-3.5 py-2.5 text-sm font-medium text-brand-700">
                  <FileText className="h-4 w-4" />
                  Taklif hujjati
                </span>
              </div>
            )}
          </Card>
        </div>
      </div>

      <p className="text-xs text-slate-400">
        Yuborilgandan keyin: «{thankYouText.trim() || DEFAULT_THANK_YOU}»
      </p>
    </div>
  )
}
