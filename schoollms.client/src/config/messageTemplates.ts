/**
 * E'lon (Telegram) va Push (Firebase) bo'limlari uchun umumiy tayyor xabar andozalari va
 * o'rinbosarlar. O'rinbosarlar yuborishda har bir o'quvchiga moslab to'ldiriladi (backend).
 */
export const messageTemplates: { label: string; text: string }[] = [
  {
    label: "Ota-onalar yig'ilishi",
    text: "Hurmatli ota-ona! {sinf} sinf ota-onalar yig'ilishi ___ kuni soat ___ da bo'lib o'tadi. Farzandingiz {fish}ning ishtiroki muhim. Iltimos, keling.",
  },
  {
    label: 'Darsga kelmayapti',
    text: "Hurmatli ota-ona! Farzandingiz {fish} ({sinf}) so'nggi kunlarda darslarga qatnashmayapti. Iltimos, sababini maktabga ma'lum qiling.",
  },
  {
    label: "O'zlashtirish pasaymoqda",
    text: "Hurmatli ota-ona! Farzandingiz {fish} ({sinf})ning o'zlashtirishi pasaymoqda. Iltimos, qulay vaqtda maktabga tashrif buyuring.",
  },
  {
    label: 'Qarzdorlik eslatmasi',
    text: "Hurmatli ota-ona! Farzandingiz {fish} ({sinf}) bo'yicha to'lov qarzi: {qarzdorlik}. Iltimos, to'lovni amalga oshiring.",
  },
]

/** Matnga qo'yiladigan o'rinbosarlar — har o'quvchiga moslab to'ldiriladi. */
export const messageTokens: { token: string; label: string }[] = [
  { token: '{fish}', label: 'F.I.SH' },
  { token: '{sinf}', label: 'Sinf' },
  { token: '{qarzdorlik}', label: 'Qarzdorlik' },
  { token: '{balans}', label: 'Balans' },
  { token: '{ota-ona}', label: 'Ota-ona' },
  { token: '{telefon}', label: 'Telefon' },
]
