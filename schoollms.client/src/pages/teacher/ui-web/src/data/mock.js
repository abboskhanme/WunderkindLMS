// Namuna ma'lumotlar — ekranlar to'ldirilgan ko'rinishda chiqishi uchun.
// Haqiqiy ilovada bular REST API (TeacherApi) dan keladi.

export const user = {
  id: 'u1',
  fullName: 'Dilnoza Karimova',
  email: 'dilnoza@school.uz',
  avatarUrl: null,
}

export const meta = { currentQuarter: 2, currentWeek: 7 }

export const todayLessons = [
  { period: 1, subjectName: 'Matematika', className: '9-A', subGroup: 0, startTime: '08:30', endTime: '09:15' },
  { period: 2, subjectName: 'Algebra', className: '10-B', subGroup: 1, startTime: '09:25', endTime: '10:10' },
  { period: 3, subjectName: 'Geometriya', className: '9-A', subGroup: 0, startTime: '10:20', endTime: '11:05' },
  { period: 4, subjectName: 'Matematika', className: '11-A', subGroup: 0, startTime: '11:15', endTime: '12:00' },
]

// "Hozir dars" kartasi uchun aktiv dars (progress bilan)
export const currentLesson = {
  period: 3,
  subjectName: 'Geometriya',
  className: '9-A',
  subGroup: 0,
  startTime: '10:20',
  endTime: '11:05',
  progress: 0.6,
  remaining: 18,
}

export const weekLessons = {
  0: todayLessons,
  1: [
    { period: 1, subjectName: 'Algebra', className: '10-B', subGroup: 0, startTime: '08:30', endTime: '09:15' },
    { period: 2, subjectName: 'Matematika', className: '9-A', subGroup: 0, startTime: '09:25', endTime: '10:10' },
  ],
  2: [
    { period: 1, subjectName: 'Geometriya', className: '11-A', subGroup: 0, startTime: '08:30', endTime: '09:15' },
  ],
  3: [
    { period: 2, subjectName: 'Matematika', className: '9-A', subGroup: 0, startTime: '09:25', endTime: '10:10' },
    { period: 3, subjectName: 'Algebra', className: '10-B', subGroup: 0, startTime: '10:20', endTime: '11:05' },
  ],
  4: [
    { period: 1, subjectName: 'Geometriya', className: '9-A', subGroup: 0, startTime: '08:30', endTime: '09:15' },
  ],
  5: [],
}

export const homeroomClass = {
  className: '9-A',
  isHomeroom: true,
  subjects: [{ name: 'Matematika' }, { name: 'Geometriya' }],
}

export const classes = [
  {
    classId: 'c1',
    className: '9-A',
    grade: 9,
    isHomeroom: true,
    subjects: [{ id: 's1', name: 'Matematika' }, { id: 's2', name: 'Geometriya' }],
  },
  {
    classId: 'c2',
    className: '9-B',
    grade: 9,
    isHomeroom: false,
    subjects: [{ id: 's1', name: 'Matematika' }],
  },
  {
    classId: 'c3',
    className: '10-B',
    grade: 10,
    isHomeroom: false,
    subjects: [{ id: 's3', name: 'Algebra' }],
  },
  {
    classId: 'c4',
    className: '11-A',
    grade: 11,
    isHomeroom: false,
    subjects: [{ id: 's2', name: 'Geometriya' }, { id: 's3', name: 'Algebra' }],
  },
]

export const students = [
  'Akmal Tursunov', 'Madina Yusupova', 'Jasur Raximov', 'Zarina Aliyeva',
  'Bekzod Olimov', 'Nilufar Saidova', 'Sardor Qodirov', 'Gulnoza Ismoilova',
  'Otabek Nazarov', 'Sevara Tosheva', 'Dilshod Ergashev', 'Malika Yo\'ldosheva',
].map((fullName, i) => ({ id: `st${i}`, fullName, subGroup: i % 4 === 0 ? 1 : 0 }))

// Jurnal ustunlari (sana + dars raqami)
export const journalColumns = [
  { key: 'k1', date: '2026-01-12', period: 1, subGroup: 0 },
  { key: 'k2', date: '2026-01-14', period: 1, subGroup: 0 },
  { key: 'k3', date: '2026-01-16', period: 2, subGroup: 1 },
  { key: 'k4', date: '2026-01-19', period: 1, subGroup: 0 },
  { key: 'k5', date: '2026-01-21', period: 1, subGroup: 0 },
  { key: 'k6', date: '2026-01-23', period: 2, subGroup: 0 },
]

// Baholar matritsasi — student index -> { colKey -> {grade?, reason?, extra?} }
export const journalEntries = {
  st0: { k1: { grade: 5 }, k2: { grade: 4, extra: true }, k4: { reason: 'S' } },
  st1: { k1: { grade: 5 }, k3: { grade: 5 }, k5: { grade: 4 } },
  st2: { k1: { grade: 3 }, k2: { reason: 'B' }, k4: { grade: 4 } },
  st3: { k2: { grade: 5 }, k5: { grade: 5, extra: true } },
  st4: { k1: { grade: 2 }, k3: { grade: 3 } },
  st5: { k1: { grade: 4 }, k4: { grade: 5 }, k6: { grade: 5 } },
}

export const absenceReasons = [
  { id: 'r1', name: 'Sababli', short: 'S' },
  { id: 'r2', name: 'Sababsiz', short: 'B' },
  { id: 'r3', name: 'Kasal', short: 'K' },
]

export const topics = [
  { key: 'k1', date: '2026-01-12', period: 1, topic: 'Kvadrat tenglamalar', homework: '142-145 misollar', conducted: true },
  { key: 'k2', date: '2026-01-14', period: 1, topic: 'Diskriminant', homework: '150-152 misollar', conducted: true },
  { key: 'k3', date: '2026-01-16', period: 2, topic: '', homework: '', conducted: false },
]

export const assignments = [
  {
    id: 'a1', title: 'Kvadrat tenglamalar — nazorat testi', format: 'test', subjectName: 'Algebra',
    classNames: ['9-A', '9-B'], classIds: ['c1', 'c2'], dueDate: '2026-06-10', autoGrade: true,
  },
  {
    id: 'a2', title: 'Geometrik shakllar yuzasi (yozma)', format: 'written', subjectName: 'Geometriya',
    classNames: ['11-A'], classIds: ['c4'], dueDate: '2026-06-08', autoGrade: false,
  },
  {
    id: 'a3', title: 'Loyiha hisoboti — fayl yuklash', format: 'file', subjectName: 'Matematika',
    classNames: ['10-B'], classIds: ['c3'], dueDate: '2026-06-02', autoGrade: false,
  },
  {
    id: 'a4', title: 'Video tushuntirish — funksiyalar', format: 'video', subjectName: 'Algebra',
    classNames: ['9-A'], classIds: ['c1'], dueDate: null, autoGrade: false,
  },
]

export const assignmentResults = {
  title: 'Kvadrat tenglamalar — nazorat testi',
  format: 'test',
  maxScore: 10,
  total: 12,
  completedCount: 8,
  rows: students.map((s, i) => ({
    studentId: s.id,
    studentName: s.fullName,
    className: '9-A',
    completed: i < 8,
    score: i < 8 ? [9, 7, 10, 6, 8, 5, 9, 4][i] : null,
    submittedAt: i < 8 ? '2026-06-01T10:00:00' : null,
  })),
}

export const salary = {
  salary: 5_400_000,
  totalExpected: 48_600_000,
  totalPaid: 43_200_000,
  remaining: 5_400_000,
  paidPercent: '89%',
  payments: [
    { month: '2026-05', date: '2026-05-05', amount: 5_400_000, note: 'May oyligi' },
    { month: '2026-04', date: '2026-04-05', amount: 5_400_000, note: 'Aprel oyligi' },
    { month: '2026-03', date: '2026-03-05', amount: 5_400_000, note: 'Mart oyligi' },
  ],
  months: [
    { month: '2026-01', paid: 5_400_000, expected: 5_400_000, remaining: 0, status: 'paid' },
    { month: '2026-02', paid: 5_400_000, expected: 5_400_000, remaining: 0, status: 'paid' },
    { month: '2026-03', paid: 5_400_000, expected: 5_400_000, remaining: 0, status: 'paid' },
    { month: '2026-04', paid: 5_400_000, expected: 5_400_000, remaining: 0, status: 'paid' },
    { month: '2026-05', paid: 2_700_000, expected: 5_400_000, remaining: 2_700_000, status: 'partial' },
    { month: '2026-06', paid: 0, expected: 5_400_000, remaining: 5_400_000, status: 'unpaid' },
  ],
}

export const channels = [
  { id: '9-A', name: '9-A', isStaff: false, last: { sender: 'Madina Y.', text: 'Rahmat, tushunarli!' }, time: '14:32', unread: 2 },
  { id: '__xodimlar__', name: 'Xodimlar', isStaff: true, last: { sender: 'Admin', text: 'Ertaga yig\'ilish 15:00' }, time: '12:10', unread: 0 },
  { id: '10-B', name: '10-B', isStaff: false, last: { sender: 'Otabek N.', text: 'Uy vazifa qayerda?' }, time: 'Kecha', unread: 0 },
  { id: '11-A', name: '11-A', isStaff: false, last: { sender: 'Siz', text: 'Darslik 45-bet' }, time: 'Du', unread: 0 },
]

export const messages = [
  { id: 'm1', senderUserId: 'p1', senderName: 'Madina Yusupova (ona)', senderRole: 'parent', text: 'Assalomu alaykum, farzandimning bahosi qancha?', time: '14:20' },
  { id: 'm2', senderUserId: 'u1', senderName: 'Siz', senderRole: 'teacher', text: 'Vaalaykum assalom. Bu chorak 4 va 5 baholar oldi.', time: '14:25' },
  { id: 'm3', senderUserId: 'p1', senderName: 'Madina Yusupova (ona)', senderRole: 'parent', text: 'Rahmat, tushunarli!', time: '14:32' },
  { id: 'm4', senderUserId: 's5', senderName: 'Akmal Tursunov', senderRole: 'student', text: 'Domla, uyga vazifa qaysi misollar?', time: '14:40' },
]

export const homeroomStudents = students.map((s, i) => ({
  studentId: s.id,
  fullName: s.fullName,
  hasPendingPickup: i < 2,
  status: i === 5 ? 'accepted' : 'waiting',
}))

export const notifications = [
  { title: 'Yangi xabar — 9-A', body: 'Madina Yusupova (ona): Rahmat, tushunarli!', minsAgo: 3 },
  { title: 'Ota-ona keldi', body: 'Akmal Tursunovning ota-onasi sizni kutmoqda', minsAgo: 25 },
  { title: 'Topshiriq muddati', body: 'Loyiha hisoboti bugun tugaydi', minsAgo: 180 },
]

export const progress = {
  quarter: 2,
  totalPercent: 72,
  totalPlanned: 96,
  totalConducted: 69,
  totalRemaining: 27,
  items: [
    { subjectName: 'Matematika', className: '9-A', subGroup: 0, percent: 85, planned: 32, conducted: 27, remaining: 5, isBehind: false, expectedByToday: 26 },
    { subjectName: 'Algebra', className: '10-B', subGroup: 1, percent: 60, planned: 30, conducted: 18, remaining: 12, isBehind: true, expectedByToday: 24 },
    { subjectName: 'Geometriya', className: '11-A', subGroup: 0, percent: 70, planned: 34, conducted: 24, remaining: 10, isBehind: false, expectedByToday: 23 },
  ],
}

export const lmsSubjects = [
  { id: 'l1', className: '9-A', title: 'Algebra asoslari', description: 'Kvadrat tenglamalar va funksiyalar bo\'yicha to\'liq kurs.', topicsCount: 12, unlockMode: 'sequential', batchSize: 0 },
  { id: 'l2', className: '10-B', title: 'Geometriya — planimetriya', description: 'Tekislikdagi figuralar, yuzalar va isbotlar.', topicsCount: 8, unlockMode: 'all', batchSize: 0 },
  { id: 'l3', className: '11-A', title: 'Matematik analiz', description: '', topicsCount: 15, unlockMode: 'batch', batchSize: 3 },
]

export const lmsTopics = [
  { id: 't1', order: 1, title: 'Kvadrat tenglama tushunchasi', description: 'Asosiy ta\'riflar va misollar', videoUrl: 'https://youtu.be/abc', materials: [{ name: 'tushuncha.pdf', size: 1_200_000 }], completedCount: 18, textContent: 'Kvadrat tenglama ax² + bx + c = 0 ko\'rinishidagi tenglamadir...' },
  { id: 't2', order: 2, title: 'Diskriminant formulasi', description: 'D = b² − 4ac', videoUrl: null, materials: [{ name: 'misollar.docx', size: 540_000 }], completedCount: 12, textContent: '' },
  { id: 't3', order: 3, title: 'Vieta teoremasi', description: '', videoUrl: 'https://youtu.be/xyz', materials: [], completedCount: 0, textContent: '' },
]

export const lmsProgress = {
  topics: lmsTopics.map((t) => ({ id: t.id, order: t.order, title: t.title })),
  students: students.map((s, i) => ({
    fullName: s.fullName,
    completedCount: [3, 3, 2, 1, 0, 3, 2, 1, 0, 2, 1, 3][i],
    totalCount: 3,
    completedTopicIds: ['t1', 't2', 't3'].slice(0, [3, 3, 2, 1, 0, 3, 2, 1, 0, 2, 1, 3][i]),
  })),
}
