import type { LucideIcon } from 'lucide-react'
import {
  FileBadge,
  CalendarCheck,
  FlaskConical,
  LayoutDashboard,
  UserPlus,
  GraduationCap,
  NotebookText,
  CalendarRange,
  ClipboardList,
  Wallet,
  MessageSquare,
  ClipboardCheck,
  Settings,
  BarChart3,
  Building2,
  BookOpen,
  ShieldAlert,
  Newspaper,
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
    // TARTIB EduSchool'ning yon menyusidan olingan (mijoz ko'rsatgan ekran,
    // 2026-09-18): Dashboard · Lidlar · Moliya · Jurnal · O'quv bo'limi ·
    // Dars jadvali · Chat · HR · Analitika · Boshqaruv · Xulq-atvor ·
    // Sozlamalar. Ularda bor-u bizda hali qurilmagan bo'limlar (Topshiriqlar,
    // Gamifikatsiya, Qabul, Imtihonlar, Blok Test) menyuda YO'Q — bo'sh
    // sahifaga olib boradigan yozuv yo'q yozuvdan battar (MENU-PARITY.md).
    //
    // Bizda bor-u ularda yo'q uchtasi (Davomat, Keldi-ketdi, Ilova) OXIRIGA,
    // Sozlamalardan oldin qo'yilgan — shunda EduSchool ketma-ketligi
    // boshidan Xulq-atvorgacha uzilmay o'qiladi.
    // Mijoz, 2026-09-24: "bosh sahifa ham role uchun ... bazilarga u sahifa uchun ham dostup bo'lmaydi".
    { label: 'Bosh sahifa', to: '/admin', icon: LayoutDashboard, perm: 'dashboard' },
    // Mijoz, 2026-09-22: "leadlar bo'limi uchun voronka kerakmas" — menyuda faqat
    // doska, ichki ro'yxatsiz. `/admin/leads/funnel` sahifasi o'chirilmadi (manzil
    // ishlaydi), faqat menyudan olindi; voronka bosh sahifada vidjet sifatida qoladi.
    { label: 'Lidlar', to: '/admin/leads', icon: UserPlus, perm: 'leads' },
    {
      label: 'Moliya',
      to: '/admin/finance',
      icon: Wallet,
      perm: 'finance',
      children: [
        // BU RO'YXAT EduSchool'ning Moliya menyusining AYNAN O'ZI — mijoz
        // 2026-09-18 da: "eduschool bilan bir xil bo'lsin". O'zimiz qo'shgan
        // yozuvlar (Umumiy, To'lov toifalari, Obunalar, Chegirmalar,
        // Chiqimlar, Qarzdor holatlari, Qaytarimlar, Moliya sozlamalari,
        // Kassa kuni) menyudan OLIB TASHLANDI. Ekranlarning o'zi va
        // marshrutlari joyida — EduSchool ham ularni katalog sifatida
        // "Moliya sozlamalari" ichidan ochadi (finance-parity.md §2.14),
        // menyuda alohida yozuv qilmaydi.
        //
        // YANGI YOZUV QO'SHMANG. Bu ro'yxat EduSchool ekranidan nusxa.
        { label: 'Kassa', to: '/cashier', roles: ['admin', 'superadmin'], group: 'AMALLAR' },
        { label: 'Qarzdorlar bilan ishlash', to: '/admin/finance/debtors', roles: ['admin', 'superadmin'], group: 'AMALLAR' },
        { label: 'Tranzaksiyalar', to: '/admin/finance/transactions', roles: ['admin', 'superadmin'], group: 'AMALLAR' },
        { label: 'Abonement tranzaksiyalari', to: '/admin/billing/invoices', roles: ['admin', 'superadmin'], group: 'AMALLAR' },
        { label: 'Abonement tranzaksiyalari (Qarzdorlik oyma oy)', to: '/admin/finance/arrears', roles: ['admin', 'superadmin'], group: 'AMALLAR' },

        { label: 'Ish haqi', to: '/admin/teachers/salary', roles: ['admin', 'superadmin'], group: 'ISH HAQI' },
        { label: 'Bonus', to: '/admin/finance/bonus', roles: ['admin', 'superadmin'], group: 'ISH HAQI' },
        { label: 'Jarima', to: '/admin/finance/penalty', roles: ['admin', 'superadmin'], group: 'ISH HAQI' },

        { label: 'Moliya hisobotlari', to: '/admin/finance/reports', roles: ['admin', 'superadmin'], group: 'HISOBOTLAR' },
        { label: 'Moliya hisobotlari (P&L)', to: '/admin/finance/pnl', roles: ['admin', 'superadmin'], group: 'HISOBOTLAR' },
        // P&L 2.0 (beta) — ilgari rad etilgan edi (existing-module-gaps.md
        // §3.6), mijoz 2026-09-18 da qaytardi. Ekranda hali qurilmagan
        // qismlari OCHIQ yozilgan, soxta tab qo'yilmagan.
        { label: 'Moliya hisobotlari (P&L) 2.0', to: '/admin/finance/pnl-2', roles: ['admin', 'superadmin'], group: 'HISOBOTLAR' },
        { label: 'Pul oqimi', to: '/admin/finance/cashflow', roles: ['admin', 'superadmin'], group: 'HISOBOTLAR' },
        { label: 'Moliya analitikasi', to: '/admin/finance/money-flow', roles: ['admin', 'superadmin'], group: 'HISOBOTLAR' },
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
        // FAQAT JADVAL. Choraklar, Dars vaqtlari, Davomat sabablari va
        // Bayram kunlari SOZLAMALAR bo'limiga ("Umumiy sozlamalar") ko'chdi —
        // mijoz qoidasi: ishchi bo'limlar ichida sozlama turmaydi.
        // Eski manzillar (`/admin/settings/quarters` va h.k.) ishlashda qoladi.
        { label: 'Dars jadvali', to: '/admin/schedule', end: true, group: 'JADVAL' },
        { label: "O'qituvchi jadvali", to: '/admin/schedule/teachers', group: 'JADVAL' },
        { label: 'Dars jadvali yaratish', to: '/admin/schedule/manage', group: 'JADVAL' },
      ],
    },
    { label: 'Xabarlar', to: '/admin/messages', icon: MessageSquare, perm: 'messages' },
    {
      // Davomat EduSchool'da yo'q, lekin mijoz uni yuqorida qoldirishni
      // so'radi (2026-09-18) — kundalik ishlatiladigan bo'lim, `Future`
      // ichiga tiqib qo'yish uni uzoqlashtirardi. Shu bois EduSchool
      // ketma-ketligi aynan shu bitta yozuvda uziladi, ataylab.
      label: 'Davomat',
      to: '/admin/attendance',
      icon: CalendarCheck,
      // Ruxsat bolalarda (2026-09-23): kechki/yotoqxona xodimi kunduzgi davomatni ko'rmasdan
      // ham o'z bo'limini ko'rishi kerak. Bola qolmasa — bo'lim o'zi yashiriladi.
      children: [
        // Belgilash BIRINCHI: mas'ul xodim bu bo'limga har kuni AYNAN shu ish
        // uchun kiradi (mijoz, 2026-09-18), hisobot esa keyin o'qiladi.
        { label: 'Davomat belgilash', to: '/admin/attendance/mark', perm: 'attendance', group: 'DAVOMAT' },
        { label: 'Kunlik davomat', to: '/admin/attendance', end: true, perm: 'attendance', group: 'DAVOMAT' },
        { label: 'Davomat analitikasi', to: '/admin/attendance/analytics', perm: 'attendance', group: 'DAVOMAT' },
        { label: 'Kechki dars', to: '/admin/attendance/boarding?session=evening', perm: 'attendanceEvening', group: 'KECHKI' },
        { label: 'Yotoqxona', to: '/admin/attendance/boarding?session=dorm', perm: 'attendanceDorm', group: 'KECHKI' },
      ],
    },
    {
      // EduSchool'dagi "Imtihonlar" (mijoz, 2026-09-23). Sahifalar ilgari yozilgan, lekin
      // menyuga ulanmagan edi ("parked"). "Baholash" (kiritish) ro'yxatning o'z tugmasidan ochiladi.
      label: 'Imtihonlar',
      to: '/admin/seasonal-marks',
      icon: FileBadge,
      perm: 'seasonalMarks',
      children: [
        { label: 'Mavsumiy baholash', to: '/admin/seasonal-marks', end: true, group: 'BAHOLASH' },
        { label: "Mavsumiy baholash (fanlar bo'yicha)", to: '/admin/seasonal-marks/by-subjects', group: 'BAHOLASH' },
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
        // 'Oylik hisoblash' MOLIYA ostiga ko'chdi — EduSchool'da Ish haqi
        // aynan o'sha yerda turadi. Ikki joyda ko'rsatish menyuni chalkashtirardi.
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
        { label: 'Mavsumiy baholash hisoboti', to: '/admin/seasonal-marks/report', perm: 'seasonalMarks', group: 'XODIMLAR' },
      ],
    },
    {
      label: 'Boshqaruv',
      to: '/admin/boshqaruv/staff',
      icon: Building2,
      children: [
        { label: 'Filiallar', to: '/admin/boshqaruv/branches', roles: ['superadmin'], group: 'TASHKILOT' },
        // Mijoz, 2026-09-25: xodimlar va rollar alohida — ruxsat rolga beriladi, xodimga rol biriktiriladi.
        { label: 'Xodimlar', to: '/admin/boshqaruv/staff', perm: 'staff', group: 'TASHKILOT' },
        { label: 'Rollar', to: '/admin/boshqaruv/roles', perm: 'staff', group: 'TASHKILOT' },
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
      // EduSchool'da #16 — Xulq-atvordan keyin, Sozlamalardan oldin (MENU-PARITY.md).
      // Hozircha qog'ozda o'tkaziladi, natija qo'lda kiritiladi (EduSchool ham shunday).
      label: 'Blok Test',
      to: '/admin/exams/list',
      icon: ClipboardCheck,
      perm: 'exams',
      children: [
        { label: 'Imtihonlar', to: '/admin/exams/list', group: 'BLOK TEST' },
        { label: 'Imtihon turi', to: '/admin/exams/types', group: 'BLOK TEST' },
        { label: 'Natijalar', to: '/admin/exams/results', group: 'BLOK TEST' },
      ],
    },
    {
      label: 'Sozlamalar',
      to: '/admin/settings/school',
      icon: Settings,
      // BO'LIM DARAJASIDA `perm` YO'Q — ATAYLAB. `Sidebar.tsx` ota-yozuv
      // `canSee` dan o'tmasa BOLALARINI ham yashiradi, shuning uchun faqat
      // `marketing` ruxsati bor xodim `settings` gate ortida qolib ketardi.
      // Ruxsat har bir bolaga ko'chirildi; 2026-09-17 da O'quv bo'limida
      // aynan shu nuqson shunday tuzatilgan (MENU-PARITY.md).
      children: [
        // EduSchool'ning Sozlamalar menyusi AYNAN to'rtta yozuvdan iborat
        // (mijoz ko'rsatgan ekran, 2026-09-18). Har biri — ichida bo'limlari
        // bor bitta sahifa, menyuda esa o'nta yassi yozuv emas.
        // Eski manzillar (`/admin/settings/telegram` va h.k.) ishlashda
        // qoladi — hub qo'shimcha yo'l, almashtiruvchi emas.
        { label: 'Moliya sozlamalari', to: '/admin/billing/settings', roles: ['admin', 'superadmin'], group: 'SOZLAMALAR' },
        { label: 'Integratsiyalar', to: '/admin/settings/integrations', perm: 'settings', group: 'SOZLAMALAR' },
        { label: 'Umumiy sozlamalar', to: '/admin/settings/general', perm: 'settings', group: 'SOZLAMALAR' },
        // Sotuv va marketing — EduSchool'da ham Sozlamalar ichida, `Umumiy
        // sozlamalar` dan keyin. Ariza formasi lid yaratadi, taxtaning O'ZI
        // esa tegilmaydi (CLAUDE.md). Spetsifikatsiya:
        // docs/modules/sales-marketing.md §6.4.
        { label: 'Sotuv va marketing', to: '/admin/marketing/arizalar', perm: 'marketing', group: 'SOZLAMALAR' },
        //
        // `Yangi o'quv yiliga o'tish` ham bu yerda emas: u sozlama emas,
        // yiliga bir marta bajariladigan va ORQAGA QAYTMAYDIGAN amal
        // (o'quvchilarni ko'chiradi, baho va jadvalni tozalaydi).
        { label: "Yangi o'quv yiliga o'tish", to: '/admin/academic-year', perm: 'academicYear', group: 'AMALLAR' },
      ],
    },
    {
      // ---------------------------------------------------------------------
      //  FUTURE — bizda bor, EduSchool'da yo'q bo'limlar vaqtincha shu yerda.
      //
      //  Mijoz taklifi (2026-09-18): asosiy menyu EduSchool bilan bir xil
      //  bo'lib tursin, o'zimiz qo'shgan qismlar esa eng pastda bitta joyda
      //  yig'ilsin — keyinchalik yo asosiy menyuga qaytariladi, yo o'chiriladi.
      //
      //  Ekranlar va marshrutlar TEGILMAGAN — bu faqat menyudagi joylashuv.
      //  Qaytarish kerak bo'lsa: bu blokni o'chirib, uchta bo'limni
      //  (Davomat / Keldi-ketdi / Ilova) o'z holida qaytarish kifoya —
      //  git tarixida ular shu ko'rinishda turibdi.
      // ---------------------------------------------------------------------
      label: 'Future',
      to: '/admin/attendance',
      icon: FlaskConical,
      children: [
        { label: 'Jonli turniket', to: '/admin/students/turniket', end: true, perm: 'students', group: 'KELDI-KETDI' },
        { label: 'Turniket analitikasi', to: '/admin/students/turniket/analitika', perm: 'students', group: 'KELDI-KETDI' },
        { label: 'Kirib-chiqish statistikasi', to: '/admin/students/turniket/kirish-chiqish', perm: 'students', group: 'KELDI-KETDI' },
        { label: 'Kunlik davomat hisoboti', to: '/admin/students/turniket/kunlik-davomat', perm: 'students', group: 'KELDI-KETDI' },
        { label: 'Topshiriqlar', to: '/admin/assignments', perm: 'app', group: 'ILOVA' },
        { label: 'Topshiriqlar bali', to: '/admin/assignment-scores', perm: 'app', group: 'ILOVA' },
        { label: "Ta'lim (LMS)", to: '/admin/lms', perm: 'app', group: 'ILOVA' },
        { label: 'Oshxona', to: '/admin/canteen', perm: 'app', group: 'ILOVA' },
        { label: "O'qituvchilar", to: '/admin/app/teachers', perm: 'app', group: 'ILOVA' },
        // Qabul hozircha ishlatilmaydi (mijoz, 2026-09-23) — asosiy menyudan shu yerga ko'chdi.
        // Sahifalar va manzillar o'zgarmagan.
        { label: 'Nomzodlar', to: '/admin/admission/candidates', end: true, perm: 'admission', group: 'QABUL' },
        { label: 'Test bazasi', to: '/admin/admission/banks', perm: 'admission', group: 'QABUL' },
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
  student: [
    { label: 'Bosh sahifa', to: '/student', icon: LayoutDashboard },
    { label: 'Yangiliklar', to: '/student/yangiliklar', icon: Newspaper },
  ],
  parent: [
    { label: 'Bosh sahifa', to: '/parent', icon: LayoutDashboard },
    { label: 'Yangiliklar', to: '/parent/yangiliklar', icon: Newspaper },
  ],
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
