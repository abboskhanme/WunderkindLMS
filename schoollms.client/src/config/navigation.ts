import type { LucideIcon } from 'lucide-react'
import {
  LayoutDashboard,
  UserPlus,
  GraduationCap,
  NotebookText,
  CalendarRange,
  ClipboardList,
  CalendarCheck,
  Wallet,
  MessageSquare,
  ClipboardCheck,
  Settings,
  Smartphone,
  BarChart3,
  Building2,
  BookOpen,
  ShieldAlert,
} from 'lucide-react'
import type { Role } from '@/types'

export interface NavChild {
  label: string
  to: string
  /**
   * Yonga ochiladigan panel ichidagi ustun sarlavhasi (EduSchool naqshi:
   * "O'QUV JARAYONI", "O'QUVCHILAR", "HUJJATLAR"). Berilmasa — bolalar
   * bitta ustunda, sarlavhasiz chiqadi.
   */
  group?: string
  /** NavLink exact match (faqat shu manzilda faol) */
  end?: boolean
  /** Faqat shu rollarga ko'rinadi (yo'q = barcha rollarga) */
  roles?: Role[]
  /** Ruxsat kaliti — xodim (staff) shu bo'limga ega bo'lsagina ko'rinadi */
  perm?: string
}

export interface NavItem {
  label: string
  to: string
  icon: LucideIcon
  children?: NavChild[]
  /** Bo'lim ruxsat kaliti (o'qituvchi/xodim filtri uchun; yo'q = har doim ko'rinadi) */
  perm?: string
  /** Faqat shu rollarga ko'rinadi (yo'q = barcha rollarga) */
  roles?: Role[]
}

/** Har bir rol uchun yon menyu (sidebar) elementlari */
export const navByRole: Record<Role, NavItem[]> = {
  // ==========================================================================
  //  TARTIB EDUSCHOOL BILAN BIR XIL (mijoz talabi, 2026-09-13).
  //  Maqsad ro'yxati va farqlar: docs/MENU-PARITY.md
  //
  //  Hali qurilmagan modullar (Topshiriqlar/xodim vazifalari, Imtihonlar,
  //  Qabul, Gamifikatsiya, Blok Test, WareHouse) bu yerda YO'Q — bo'sh menyu
  //  yozuvi ishlamaydigan sahifaga olib borardi. Har biri o'z o'rniga
  //  qo'shiladi; o'rni MENU-PARITY.md da belgilangan.
  // ==========================================================================
  admin: [
    { label: 'Bosh sahifa', to: '/admin', icon: LayoutDashboard },
    {
      label: 'Lidlar',
      to: '/admin/leads',
      icon: UserPlus,
      perm: 'leads',
      children: [
        // Taxtaning O'ZI o'zgarmaydi (CLAUDE.md: dizayni muzlatilgan) — voronka
        // yonidagi ALOHIDA sahifa, menyuda esa qo'shni yozuv.
        { label: 'Doska', to: '/admin/leads', end: true },
        { label: 'Voronka', to: '/admin/leads/funnel' },
      ],
    },
    {
      label: 'Moliya',
      to: '/admin/finance',
      icon: Wallet,
      perm: 'finance',
      children: [
        { label: 'Umumiy', to: '/admin/finance', end: true, group: 'AMALIYOT' },
        // Kassa ish joyi — admin/direktor uchun ham ochiq (F1.11). Kassirning
        // o'z menyusi alohida va qisqa: `navByRole.cashier`.
        { label: 'Kassa', to: '/cashier', roles: ['admin', 'superadmin'], group: 'AMALIYOT' },
        // Billing katalogi — `perm: 'finance'` yolg'iz o'zi buni finance ruxsatli
        // xodimga ham ko'rsatardi, server esa unga 403 beradi. Shuning uchun rol
        // ham ko'rsatiladi: menyu va endpoint bir xil qoidaga bo'ysunsin.
        { label: "To'lov toifalari", to: '/admin/billing/categories', roles: ['admin', 'superadmin'], group: 'AMALIYOT' },
        { label: 'Obunalar', to: '/admin/billing/subscriptions', roles: ['admin', 'superadmin'], group: 'AMALIYOT' },
        { label: 'Chegirmalar', to: '/admin/billing/discounts', roles: ['admin', 'superadmin'], group: 'AMALIYOT' },
        { label: 'Chiqimlar', to: '/admin/billing/expenses', roles: ['admin', 'superadmin'], group: 'AMALIYOT' },
        { label: 'Hisob-fakturalar', to: '/admin/billing/invoices', roles: ['admin', 'superadmin'], group: 'AMALIYOT' },
        { label: 'Qarzdor holatlari', to: '/admin/finance/debtor-statuses', roles: ['admin', 'superadmin'], group: 'AMALIYOT' },
        // Moliya sozlamalari (F14.01): to'lov muddati, kechikish kuni va chiqim
        // tasdiqlash chegarasi. Chegarani FAQAT direktor o'zgartira oladi —
        // buni server hal qiladi (`ManageBillingSettings`), menyu esa qo'shni
        // katalog yozuvlari bilan bir xil rol darvozasida turadi.
        // Pul qaytarish (F1.05): so'rash va tasdiqlash AYRI huquq — so'rovni
        // admin qo'yadi, tasdiqni faqat superadmin beradi, va o'z so'rovini
        // o'zi tasdiqlay olmaydi (bazadagi `ck_student_refunds_approver_differs`).
        { label: 'Qaytarimlar', to: '/admin/finance/refunds', roles: ['admin', 'superadmin'], group: 'AMALIYOT' },
        // Bonus va Jarima EduSchool'da ham Moliya ostida turadi (HR emas).
        // Xodimning pulini o'zgartiradigan amal, shuning uchun kassir ham,
        // oddiy xodim ham kira olmaydi — server `ManagePayrollAdjustments`
        // bilan qo'riqlaydi.
        { label: 'Bonus', to: '/admin/finance/bonus', roles: ['admin', 'superadmin'], group: 'AMALIYOT' },
        { label: 'Jarima', to: '/admin/finance/penalty', roles: ['admin', 'superadmin'], group: 'AMALIYOT' },
        { label: 'Moliya sozlamalari', to: '/admin/billing/settings', roles: ['admin', 'superadmin'], group: 'AMALIYOT' },
        // SPEC §4.3: moliya hisobotlari faqat admin va direktorga ochiq —
        // 'finance' ruxsatli xodim (staff) ham bu yerni ko'rmaydi, chunki
        // endpoint unga 403 qaytaradi. Menyuni ham, marshrutni ham yopamiz.
        { label: 'Moliya hisobotlari', to: '/admin/finance/reports', roles: ['admin', 'superadmin'], group: 'HISOBOTLAR' },
        { label: 'Tranzaksiyalar', to: '/admin/finance/transactions', roles: ['admin', 'superadmin'], group: 'HISOBOTLAR' },
        // Kunlik ekran — pul aylanmasidan oldin turadi, chunki har kuni ochiladi.
        { label: 'Kassa kuni', to: '/admin/finance/cash-day', roles: ['admin', 'superadmin'], group: 'HISOBOTLAR' },
        { label: 'Pul aylanmasi', to: '/admin/finance/money-flow', roles: ['admin', 'superadmin'], group: 'HISOBOTLAR' },
        // EduSchool: "Abonement tranzaksiyalari (qarzdorlik oyma-oy)" — MENU-PARITY.md §Moliya.
        { label: 'Oyma-oy qarzdorlik', to: '/admin/finance/arrears', roles: ['admin', 'superadmin'], group: 'HISOBOTLAR' },
      ],
    },
    { label: 'Jurnal', to: '/admin/journal', icon: NotebookText, perm: 'journal' },
    {
      label: "O'quv bo'limi",
      to: '/admin/students',
      icon: BookOpen,
      // Bo'limning O'ZIDA `perm` YO'Q — ataylab. Ilgari u `students` edi va
      // faqat `classes` ruxsatli xodim (Sinflar va Guruhlar aynan uniki)
      // bo'limni UMUMAN ko'rmasdi. Endi ko'rinishni bolalar hal qiladi:
      // Sidebar bolalarni filtrlaydi va bolasi qolmagan bo'limni yashiradi,
      // ya'ni hech bir ekraniga ruxsati yo'q xodimga bo'lim baribir chiqmaydi.
      // Ota-satr — tugma, u hech qayerga o'tkazmaydi (faqat panelni ochadi),
      // shuning uchun `to` ning ruxsati bu yerda ahamiyatsiz.
      children: [
        // TARTIB EduSchool'ning O'quv bo'limi menyusidan AYNAN olingan
        // (mijoz ko'rsatgan ekran, 2026-09-17): Sinflar · Guruhlar · Fanlar ·
        // Xonalar || O'quvchilar · Arxiv o'quvchilar · O'quvchilar manzili ·
        // Ota onalar || Sertifikatlar · Shartnomalar.
        // Mijoz 2026-09-17 da: "eduschoolda bor menular bo'lsa yetadi" —
        // BAHOLASH guruhi (ikkita feedback yozuvi) va "O'quvchi holatlari"
        // menyudan OLIB TASHLANDI. Sahifalari va marshrutlari joyida qoldi,
        // ya'ni funksiya o'chmadi, faqat menyuda ko'rinmaydi.
        // `Sertifikat turlari` qoldi: u EduSchool'da BOR, faqat Sozlamalar
        // ostida turadi — bizda esa o'sha marshrut `settings` ruxsatiga,
        // API esa `students` ga bog'langani uchun u yerga qo'yib bo'lmaydi.
        //
        // HAR BIR YOZUVDA `perm` BOR — VA U SAHIFANI HAQIQATDA QO'RIQLAYDIGAN
        // KALIT. Bo'lim bitta (`O'quv bo'limi`), lekin ichidagi ekranlar TO'RT
        // xil ruxsat ostida turadi: sinf va guruh `classes`, fan va xona
        // `schedule`, manzil va ota-ona `app`, qolgani `students`.
        // Avval bolalarda `perm` yo'q edi va Sidebar ularni HAMMAGA ko'rsatardi:
        // `students` ruxsatli xodim to'liq menyuni ko'rib, Fanlar yoki Xonalarni
        // bosganda "ruxsat yo'q" sahifasiga tushardi. Menyu ocholmaydigan
        // yozuvni ko'rsatmasligi kerak — Sidebar bolalarni ham filtrlaydi va
        // bolasi qolmagan bo'limni butunlay yashiradi.
        { label: 'Sinflar', to: '/admin/classes', end: true, perm: 'classes', group: "O'QUV JARAYONI" },
        { label: 'Guruhlar', to: '/admin/groups', perm: 'classes', group: "O'QUV JARAYONI" },
        { label: 'Fanlar', to: '/admin/subjects', perm: 'students', group: "O'QUV JARAYONI" },
        { label: 'Xonalar', to: '/admin/rooms', perm: 'students', group: "O'QUV JARAYONI" },
        { label: "O'quvchilar", to: '/admin/students', end: true, perm: 'students', group: "O'QUVCHILAR" },
        { label: "Arxiv o'quvchilar", to: '/admin/students/arxiv', perm: 'students', group: "O'QUVCHILAR" },
        // Manzil va Ota-onalar `app` ("Ilova") dan `students` ga KO'CHDI —
        // menyu, marshrut va controller bir vaqtda. `app` mobil ilova davridan
        // qolgan kalit edi; ikkala ekran endi O'quv bo'limida turibdi, shuning
        // uchun bo'limning kalitiga bo'ysunadi.
        { label: "O'quvchilar manzili", to: '/admin/locations', perm: 'students', group: "O'QUVCHILAR" },
        { label: 'Ota-onalar', to: '/admin/parents', perm: 'students', group: "O'QUVCHILAR" },
        { label: 'Sertifikatlar', to: '/admin/certificates', end: true, perm: 'students', group: 'HUJJATLAR' },
        { label: 'Shartnomalar', to: '/admin/contracts', perm: 'contracts', group: 'HUJJATLAR' },
        // Sertifikat turlari SOZLAMALAR ostida emas: u yerdagi marshrut `settings`
        // ruxsatiga bog'langan, API esa `students` ga — menyu va server zid bo'lardi.
        { label: 'Sertifikat turlari', to: '/admin/certificates/types', perm: 'students', group: 'HUJJATLAR' },
      ],
    },
    {
      label: 'Dars jadvali',
      to: '/admin/schedule',
      icon: CalendarRange,
      perm: 'schedule',
      children: [
        { label: 'Sinf jadvali', to: '/admin/schedule', end: true, group: 'JADVAL' },
        { label: "O'qituvchi jadvali", to: '/admin/schedule/teachers', group: 'JADVAL' },
        { label: 'Dars jadvali yaratish', to: '/admin/schedule/manage', group: 'JADVAL' },
        { label: 'Bayram kunlari', to: '/admin/schedule/holidays', group: 'SOZLAMA' },
        { label: 'Choraklar', to: '/admin/settings/quarters', group: 'SOZLAMA' },
        { label: 'Dars vaqtlari', to: '/admin/settings/lesson-times', group: 'SOZLAMA' },
        { label: 'Davomat sabablari', to: '/admin/settings/reasons', group: 'SOZLAMA' },
      ],
    },
    { label: 'Xabarlar', to: '/admin/messages', icon: MessageSquare, perm: 'messages' },
    {
      label: 'Davomat',
      to: '/admin/attendance',
      icon: CalendarCheck,
      perm: 'attendance',
      children: [
        { label: 'Kunlik davomat', to: '/admin/attendance', end: true, group: 'DAVOMAT' },
        { label: 'Davomat analitikasi', to: '/admin/attendance/analytics', group: 'DAVOMAT' },
      ],
    },
    {
      label: 'Keldi-ketdi',
      to: '/admin/students/turniket',
      icon: ClipboardCheck,
      perm: 'students',
      children: [
        { label: 'Jonli turniket', to: '/admin/students/turniket', end: true, group: 'TURNIKET' },
        { label: 'Turniket analitikasi', to: '/admin/students/turniket/analitika', group: 'HISOBOTLAR' },
        { label: 'Kirib-chiqish statistikasi', to: '/admin/students/turniket/kirish-chiqish', group: 'HISOBOTLAR' },
        // Ruxsat kaliti `students` — endpoint ham shunday. `attendance` ostiga
        // qo'yilsa menyu bilan server bir xodim uchun ZID javob berardi.
        { label: 'Kunlik davomat hisoboti', to: '/admin/students/turniket/kunlik-davomat', group: 'HISOBOTLAR' },
      ],
    },
    {
      label: 'HR',
      to: '/admin/teachers',
      icon: GraduationCap,
      perm: 'teachers',
      children: [
        { label: "O'qituvchilar", to: '/admin/teachers', end: true, group: 'XODIMLAR' },
        { label: "O'qituvchilar davomati", to: '/admin/teachers/attendance', group: 'XODIMLAR' },
        { label: 'Oylik hisoblash', to: '/admin/teachers/salary', group: 'ISH HAQI' },
      ],
    },
    {
      label: 'Analitika',
      to: '/admin/grades-report/school',
      icon: BarChart3,
      perm: 'gradesReport',
      children: [
        { label: "Maktab bo'yicha baholar", to: '/admin/grades-report/school', group: "O'QUV" },
        { label: "Sinf bo'yicha baholar", to: '/admin/grades-report/class', group: "O'QUV" },
        { label: "O'quvchi bo'yicha baholar", to: '/admin/grades-report/student', group: "O'QUV" },
        { label: "Fanlar bo'yicha baholar", to: '/admin/grades-report/subjects', group: "O'QUV" },
        { label: 'Sinflar reytingi', to: '/admin/classes/rating', group: "O'QUV" },
        { label: "O'qituvchilar hisoboti", to: '/admin/teacher-reports', perm: 'teacherReports', group: 'XODIMLAR' },
      ],
    },
    {
      label: 'Ilova',
      to: '/admin/assignments',
      icon: Smartphone,
      perm: 'app',
      children: [
        { label: 'Topshiriqlar', to: '/admin/assignments', group: "TA'LIM" },
        { label: 'Topshiriqlar bali', to: '/admin/assignment-scores', group: "TA'LIM" },
        { label: "Ta'lim (LMS)", to: '/admin/lms', group: "TA'LIM" },
        { label: 'Oshxona', to: '/admin/canteen', group: 'BOSHQA' },
        { label: "O'qituvchilar", to: '/admin/app/teachers', group: 'BOSHQA' },
      ],
    },
    {
      label: 'Boshqaruv',
      to: '/admin/boshqaruv/staff',
      icon: Building2,
      children: [
        { label: 'Filiallar', to: '/admin/boshqaruv/branches', roles: ['superadmin'], group: 'TASHKILOT' },
        { label: 'Xodimlar va rollar', to: '/admin/boshqaruv/staff', perm: 'staff', group: 'TASHKILOT' },
        { label: 'Avtobus-gps', to: '/admin/boshqaruv/gps', perm: 'gps', group: 'KUZATUV' },
        { label: 'Kameralar', to: '/admin/boshqaruv/cameras', perm: 'cameras', group: 'KUZATUV' },
        { label: 'Taklif va shikoyatlar', to: '/admin/boshqaruv/feedback', perm: 'feedback', group: 'FIKR' },
      ],
    },
    {
      label: 'Xulq-atvor',
      to: '/admin/discipline',
      icon: ShieldAlert,
      perm: 'discipline',
      children: [
        { label: 'Ballar nazorati', to: '/admin/discipline', end: true },
        { label: 'Harakatlar', to: '/admin/discipline/incidents' },
        { label: 'Davomat intizomi', to: '/admin/discipline/attendance-report' },
        { label: 'Ball sabablar', to: '/admin/discipline/reasons' },
      ],
    },
    {
      label: 'Sozlamalar',
      to: '/admin/settings/school',
      icon: Settings,
      perm: 'settings',
      children: [
        { label: "Maktab ma'lumotlari", to: '/admin/settings/school', group: 'UMUMIY' },
        { label: "Yangi o'quv yiliga o'tish", to: '/admin/academic-year', perm: 'academicYear', group: 'UMUMIY' },
        { label: 'Arxivlash sabablari', to: '/admin/settings/archive-reasons', group: 'UMUMIY' },
        { label: 'Telegram bot', to: '/admin/settings/telegram', group: 'INTEGRATSIYALAR' },
        { label: 'Push (Firebase)', to: '/admin/settings/firebase', group: 'INTEGRATSIYALAR' },
        { label: 'Turniket integratsiya', to: '/admin/settings/turnstile', group: 'INTEGRATSIYALAR' },
        { label: 'GPS integratsiya', to: '/admin/settings/gps', group: 'INTEGRATSIYALAR' },
        { label: 'Kamera integratsiya', to: '/admin/settings/cameras', group: 'INTEGRATSIYALAR' },
      ],
    },
  ],
  teacher: [
    { label: 'Bosh sahifa', to: '/teacher', icon: LayoutDashboard },
    { label: 'Jurnal', to: '/teacher/journal', icon: NotebookText, perm: 'journal' },
    { label: 'Feedback', to: '/teacher/evaluation', icon: ClipboardList },
    { label: 'Topshiriqlar', to: '/teacher/assignments', icon: ClipboardCheck, perm: 'assignments' },
    { label: "Ta'lim (LMS)", to: '/teacher/lms', icon: BookOpen },
    { label: 'Dars jadvali', to: '/teacher/schedule', icon: CalendarRange, perm: 'schedule' },
    { label: 'Xabarlar', to: '/teacher/messages', icon: MessageSquare, perm: 'messages' },
    { label: 'Maosh', to: '/teacher/salary', icon: Wallet, perm: 'salary' },
  ],
  student: [{ label: 'Bosh sahifa', to: '/student', icon: LayoutDashboard }],
  parent: [{ label: 'Bosh sahifa', to: '/parent', icon: LayoutDashboard }],
  // Superadmin admin bilan bir xil nav'ni ishlatadi (qo'shimcha menyusiz, faqat ruxsat farqli)
  superadmin: [],
  // Xodim ham admin nav'ini ishlatadi — Sidebar uni permissions bo'yicha filtrlaydi
  staff: [],
  // Kassir (P1-04) — ATAYLAB admin nav'idan MUSTAQIL va qisqa. SPEC §4.3 ga ko'ra
  // u moliyaning qolgan qismini ko'rmaydi: storno, chegirma, chiqim va kassirlar
  // kesimidagi hisobot unga yopiq. To'liq kassir ish joyi P1-16 da, marshrutlar
  // esa P1-20 da ulanadi — shu ikkisigacha bu ro'yxat bitta elementdan iborat.
  cashier: [{ label: 'Kassa', to: '/cashier', icon: Wallet }],
}

// Superadmin va xodim admin nav'ini qayta ishlatadi (Sidebar rol/ruxsat bo'yicha filtrlaydi).
navByRole.superadmin = navByRole.admin
navByRole.staff = navByRole.admin

/** Rol bo'yicha asosiy sahifa manzili */
export const homeByRole: Record<Role, string> = {
  superadmin: '/admin',
  admin: '/admin',
  teacher: '/teacher',
  // Mobil ilova bekor qilindi (SPEC §8.1) — web portal asosiy kanal.
  student: '/student',
  parent: '/parent',
  staff: '/admin',
  // Kassir uchun boshlang'ich sahifa. Marshrutning o'zi P1-20 da qo'shiladi.
  cashier: '/cashier',
}

export const roleLabels: Record<Role, string> = {
  superadmin: 'Tizim egasi',
  admin: 'Administrator',
  teacher: "O'qituvchi",
  student: "O'quvchi",
  parent: 'Ota-ona',
  staff: 'Xodim',
  cashier: 'Kassir',
}
