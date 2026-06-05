# Ota-ona (Parent) ilovasi — API

Ota-ona ilovasi maktab serveri bilan `StudentPortalController` (`/api/student/*`) orqali ishlaydi.
Oila **bitta akkaunt** ishlatadi (maktab bergan login/parol). Ota-ona o'z **farzandi**ning
ma'lumotini ko'radi — o'qish, baho, davomat, to'lov, oshxona va h.k. Barcha javoblar **JSON**.

**Base URL:** `https://intellectschool.uz`
Har bir himoyalangan so'rovda: `Authorization: Bearer <token>` sarlavhasi.

---

## 1. Ulanish (auth)

Login **faqat login + parol** bilan (maktab kodi shart emas — login butun tizimda unikal, server
maktabni o'zi aniqlaydi). Token ichida maktab "yopilgan".

```http
POST /api/auth/login
Content-Type: application/json

{ "email": "<login>", "password": "<parol>" }
```

**Javob:**
```json
{
  "token": "eyJhbGciOi...",
  "user": {
    "id": "u-1",
    "fullName": "Aliyev Ali",
    "role": "student",
    "email": "aliyevali",
    "avatarUrl": null,
    "permissions": null,
    "modules": null
  }
}
```
- `token` — keyingi barcha so'rovlarda `Authorization: Bearer <token>`.
- `email` — bu aslida **login** (username, e.g. `voxidjonovabduxalil`), pochta emas.
- Login/parolni maktab beradi (o'quvchi qo'shilganda avtomatik yaratiladi; admin "O'quvchilar"
  bo'limidan Excel sifatida — *Login, Parol* ustunlari — eksport qiladi).

**Mumkin bo'lgan javoblar:**
- `401` — login yoki parol noto'g'ri.
- `401` — akkaunt arxivlangan/to'xtatilgan.
- `409` — bitta login bir nechta maktabda mavjud (`{ message, schools:[{slug,name}] }`) → maktabni
  tanlab, so'rovni `X-Tenant: <slug>` sarlavhasi bilan qayta yuboring.

### Joriy foydalanuvchi
```http
GET /api/auth/me            →  { id, fullName, role, email, avatarUrl, permissions, modules }
```

### Login / parolni o'zgartirish
```http
PUT /api/auth/account
{ "currentPassword": "<joriy>", "email": "<yangi login?>", "newPassword": "<yangi parol?>" }
```
- `currentPassword` majburiy (tasdiq uchun). `email`/`newPassword` ixtiyoriy (faqat berilgani o'zgaradi).
- Yangi parol — kamida **8** belgi. Login band bo'lsa `400`. Email/Id o'zgarmagani uchun token amal qiladi.

---

## 2. Rollar va farzandni aniqlash

Endpointlar uch xil foydalanuvchini qo'llab-quvvatlaydi:

| Rol | Farzand qanday aniqlanadi |
|---|---|
| `student` | Akkauntga bog'langan o'quvchi (o'z akkaunti). `studentId` shart emas. |
| `parent` | Akkaunt **logini = telefon raqami**, u `Student.ParentPhone` bilan solishtiriladi (telefon bo'yicha bog'langan farzand). |
| `admin` | Istalgan o'quvchi — `?studentId=...` query orqali (admin'ga alohida ekran kerak emas). |

- **O'qish (GET) endpointlari** uch rolga ham ochiq. Admin uchun `?studentId=` **majburiy** (bermasa `400`).
- **Yozish (mutatsiya) amallari** — quyida belgilangan rol cheklovi bilan:
  - `student, parent`: taklif/shikoyat, pickup, joylashuv (PUT), LMS mavzuni "tugatildi".
  - **faqat `student`**: topshiriq topshirish, chatga yozish, fayl yuklash, sozlama saqlash (PUT),
    push qurilmani ro'yxatga olish. (Admin boshqa odam nomidan yoza olmaydi.)

---

## 3. Endpoint'lar (qisqa jadval)

### Profil / bosh sahifa
| Metod | Yo'l | Tavsif |
|---|---|---|
| GET | `/api/student/me` | Farzand profili (rasm bilan) |
| GET | `/api/student/dashboard` | Bosh sahifa (profil + bugungi darslar/baholar + bajarilmagan topshiriq soni + balans) |
| GET | `/api/student/notebook` | **To'liq shaxsiy daftar** (admin detal sahifasi bilan bir xil) |
| GET | `/api/student/meta` | Maktab meta (chorak/dars vaqtlari/sabablar + joriy chorak/hafta) |
| GET | `/api/student/school` | Maktab nomi (brending) |
| GET | `/api/student/settings` · PUT | Til / tema / bildirishnoma (PUT — faqat student rolida) |

### O'qish (baho, davomat, jadval, reyting)
| Metod | Yo'l | Tavsif |
|---|---|---|
| GET | `/api/student/schedule?quarter=&week=` | Dars jadvali (farzand guruhiga mos) |
| GET | `/api/student/grades` | Baholar/davomat hisoboti (fan×chorak) |
| GET | `/api/student/journal?quarter=&week=` | Jurnal (kunlik mavzu/uy vazifa + farzand bahosi/davomati) |
| GET | `/api/student/attendance?quarter=` | Davomat tafsiloti (jamlama + kunlik yozuvlar) |
| GET | `/api/student/homework?quarter=` | Mavzu + uy vazifalari (+ farzand bahosi) |
| GET | `/api/student/rating` | Reyting (o'z sinfi to'liq + maktab top 15) |
| GET | `/api/student/subjects-progress?quarter=` | Fanlar progressi (o'tilgan/reja) |
| GET | `/api/student/subjects-progress/{subjectId}?quarter=` | Bitta fan progressi (darslar ro'yxati) |
| GET | `/api/student/holidays` | Bayram/dam olish kunlari (dars yo'q) |

### Topshiriqlar / LMS (Ta'lim)
| Metod | Yo'l | Tavsif |
|---|---|---|
| GET | `/api/student/assignments` | Topshiriqlar ro'yxati (har birida holat/ball) |
| GET | `/api/student/assignments/{id}` | Bitta topshiriq (test bo'lsa — javobsiz savollar) |
| POST | `/api/student/assignments/{id}/submit` | Topshiriqni topshirish *(faqat student)* |
| GET | `/api/student/assignment-scores` | Topshiriqlar bali (+ yig'ma) |
| GET | `/api/student/lms/subjects` | LMS fanlar (progress bilan) |
| GET | `/api/student/lms/subjects/{subjectId}/topics` | Mavzular (ochilish tartibi + progress) |
| GET | `/api/student/lms/topics/{topicId}` | Mavzu tafsiloti (video/matn/material) — qulflangan bo'lsa `403` |
| POST | `/api/student/lms/topics/{topicId}/complete` | Mavzuni "tugatildi" deb belgilash *(parent ham)* |

### Intizom / moliya / oshxona
| Metod | Yo'l | Tavsif |
|---|---|---|
| GET | `/api/student/discipline` | Intizomiy ball (100 dan, qoldi + tarix) |
| GET | `/api/student/finance` | To'lovlar/balans (oylik tarix) |
| GET | `/api/student/canteen?start=&end=` | Oshxona menyusi (sana oralig'i, maks 120 kun) |
| GET | `/api/student/canteen/{date}` | Bitta kun menyusi |

### Ilova xizmatlari
| Metod | Yo'l | Tavsif |
|---|---|---|
| GET · PUT | `/api/student/location` | Uy joylashuvi (GPS) — PUT *(student, parent)* |
| GET | `/api/student/telegram` | Telegram bot holati + ro'yxat statusi |
| POST · GET | `/api/student/pickup` | "Farzandimni olishga keldim" + holat |
| POST | `/api/student/feedback` | Taklif/shikoyat (multipart, rasm bilan) |
| GET · POST | `/api/student/chat` | Sinf chati (POST — faqat student) |
| POST | `/api/student/uploads` | Fayl yuklash (multipart) *(faqat student)* |
| POST | `/api/student/notifications/register` | Push qurilmani ro'yxatga olish *(faqat student)* |
| DELETE | `/api/student/notifications/register?token=...` | Qurilmani o'chirish (logout) |

---

## 4. Bosh sahifa (`GET /api/student/dashboard`)

Bir chaqiruvda hamma kerakli ma'lumot:
```json
{
  "profile": { "id":"...", "fullName":"Aliyev Ali", "className":"9-A", "birthDate":"2011-05-01",
               "gender":"male", "parentFullName":"Aliyev Vali", "parentPhone":"+998 90 123 45 67",
               "enrollmentDate":"2025-09-01", "birthCertificateUrl":null, "parentPassportUrl":null },
  "meta": { "currentQuarter": 4, "currentWeek": 2, "...": "..." },
  "todayLessons": [ { "day":1, "period":1, "startTime":"08:30", "endTime":"09:15",
                      "subjectId":"...", "subjectName":"Matematika",
                      "teacherId":"...", "teacherName":"Karimova D.", "subGroup":0 } ],
  "todayGrades": [ { "date":"2026-06-05", "period":1, "subjectId":"...", "subjectName":"Matematika",
                     "topic":"Kasrlar", "homework":"5-mashq", "conducted":true,
                     "grade":5, "reasonId":null, "reasonName":null, "isLate":false } ],
  "pendingAssignments": 2,
  "balance": -150000,
  "monthlyFee": 500000
}
```
- `balance` — manfiy = qarz. `pendingAssignments` — bajarilmagan topshiriqlar soni.

---

## 5. To'liq shaxsiy daftar (`GET /api/student/notebook`)

Farzandning **BARCHA** ma'lumoti bitta javobda — **admin ko'radigan o'quvchi detal sahifasi bilan
aynan bir xil** (`StudentProfileBuilder`).

```json
{
  "id": "...", "fullName": "Aliyev Ali", "className": "9-A",
  "homeroomTeacher": "Karimova Dilnoza",
  "parentFullName": "Aliyev Vali", "parentPhone": "+998 90 123 45 67",
  "gender": "male", "birthDate": "2011-05-01", "enrollmentDate": "2025-09-01",
  "balance": -150000, "photoUrl": "/uploads/abc.jpg",

  "address": "Toshkent, Chilonzor", "discountPct": 10, "discountAmount": 0, "discountNote": "",
  "subGroup": 1, "parentPassportUrl": null,

  "subjects": [{ "id": "subj-1", "name": "Matematika" }],
  "grades": { "subj-1": { "1": 5, "2": 4 } },
  "avgGrade": 4.5,

  "attendance": { "missedDays":{}, "illnessDays":{}, "missedLessons":{}, "illnessLessons":{}, "lateCount":{} },
  "conducted": 120, "attended": 116, "attendancePct": 97,
  "reasons": [{ "reasonId":"...", "name":"Sababsiz", "short":"S", "isLate":false, "count":2 }],

  "disciplineScore": 95, "disciplinePlus": 5, "disciplineMinus": 10, "disciplinePoints": [ ... ],

  "assignments": { "count": 8, "gradedCount": 6, "totalScore": 52, "totalMax": 60, "items": [ ... ] },

  "evaluationTypes": [ { "id":"type-1", "name":"Yozma", "description":"" } ],
  "evaluations": [ { "month":"2026-05", "grades":{ "type-1":5 }, "avg":4.5 } ],
  "evaluationsBySubject": [
    { "subjectId":"subj-1", "subjectName":"Matematika", "avg":4.6,
      "evaluations":[ { "month":"2026-05", "grades":{ "type-1":5 }, "avg":4.5 } ] }
  ],

  "homeworkDone": 40, "homeworkMissed": 3, "behaviorGood": 12, "behaviorBad": 1,
  "marksTrend": [ { "quarter":1, "homeworkDone":20, "homeworkMissed":1, "behaviorGood":6, "behaviorBad":0 } ]
}
```

**Oylik feedback** (ilova UI'sida **"Feedback"** deb ataladi; API maydonlari o'zgarmagan):
- `evaluationTypes` — feedback nomlari (ustun sarlavhalari).
- `evaluations` — **umumiy** oylik feedback (har oy: `turId → baho` = barcha fanlar o'rtachasi). Qo'lda
  qo'yilmaydi — **fanlar o'rtachasidan avtomatik**.
- `evaluationsBySubject` — **har fan alohida**; ichidagi `evaluations` formati yuqoridagidek (oy → tur → baho).

`grades`/`avgGrade` — fan×chorak rasmiy baholar; `marksTrend` — jurnaldagi uy vazifa/xulq belgilari
(choraklik, `quarter` 1..4).

---

## 6. Reyting (`GET /api/student/rating`)
```json
{
  "studentId": "...",
  "classRows": [ { "rank":1, "studentId":"...", "fullName":"Aliyev Ali", "className":"9-A",
                   "average":4.8, "attendance":98 } ],
  "schoolRows": [ /* maktab bo'yicha top 15 (xuddi shu format) */ ],
  "meSchoolRank": 7,
  "schoolCount": 240
}
```
- `classRows` — o'z sinfi **to'liq** (o'rtacha baho bo'yicha, `rank` = o'rin).
- `schoolRows` — maktab **top 15**; `meSchoolRank` — farzandning maktabdagi o'rni (top 15 dan tashqarida
  bo'lsa ham); `schoolCount` — jami o'quvchi.

---

## 7. Intizomiy ball (`GET /api/student/discipline`)
```json
{
  "remaining": 93, "plus": 3, "minus": 10,
  "items": [
    { "id":"...", "reasonName":"Darsga kech qoldi", "points":-5, "note":"3-darsda",
      "createdAt":"2026-06-01T09:30:00", "createdBy":"Administrator", "source":"manual" }
  ]
}
```
- `remaining` = 100 + `plus` − `minus`. `items` — tarix (yangidan eskiga). `source`: `manual` (admin) yoki
  `attendance` (jurnal davomati).

---

## 8. Jadval / jurnal / davomat / uy vazifa

- `GET /api/student/schedule?quarter=&week=` → kunlik darslar massivi:
  `[{ day, period, startTime, endTime, subjectId, subjectName, teacherId, teacherName }]`
  (`day`: 0=Dushanba…5=Shanba). Chorak/hafta berilmasa — joriy. Farzand guruhiga (subGroup) moslangan.
- `GET /api/student/journal?quarter=&week=` → haftalik qatorlar: dars + mavzu/uy vazifa +
  `grade`/`reasonId`/`reasonName`/`isLate` (shu farzandniki).
- `GET /api/student/homework?quarter=` → mavzu + uy vazifalari (+ farzand bahosi/davomati).
- `GET /api/student/attendance?quarter=` → `{ attendance: {...}, rows: [ { date, period, quarter,
  subjectId, subjectName, reasonId, reasonName, isLate, isIllness } ] }`.

---

## 9. Moliya (`GET /api/student/finance`)

Farzandning oylik to'lovlar/qarz tarixi va balansi (`StudentLedger`). To'lovni ilova **qabul qilmaydi** —
to'lov maktab kassasida amalga oshiriladi, bu yerda faqat ko'rsatiladi.

---

## 10. Oshxona (`GET /api/student/canteen`)
```http
GET /api/student/canteen?start=2026-06-01&end=2026-06-07
GET /api/student/canteen/2026-06-05
```
- Kunlik menyu (nonushta/tushlik/kechki). `start`/`end` — ISO sanalar (maks 120 kun). Faqat ko'rish.

---

## 11. LMS (Ta'lim)

- `GET /api/student/lms/subjects` → `[{ id, title, description, unlockMode, batchSize, topicsCount, completedCount }]`.
- `GET /api/student/lms/subjects/{subjectId}/topics` → mavzular; har birida `unlocked`/`completed`.
  **Qulflangan** mavzuda kontent (video/matn/material) qaytmaydi.
- **Ochilish tartibi** (`unlockMode`): `all` (hammasi ochiq) · `sequential` (oldingisi tugagach) ·
  `batch` (`batchSize` tadan, partiya tugagach keyingisi).
- `GET /api/student/lms/topics/{topicId}` → mavzu tafsiloti; qulflangan bo'lsa `403`.
- `POST /api/student/lms/topics/{topicId}/complete` → "tugatildi" (ochilish mantig'i uchun). Parent ham
  farzandi nomidan belgilashi mumkin. Javob `204`.

---

## 12. Topshiriqlar
- `GET /api/student/assignments` → ro'yxat (har birida `completed`/ball/muddat).
- `GET /api/student/assignments/{id}` → tafsilot (test bo'lsa — to'g'ri javobsiz savollar).
- `POST /api/student/assignments/{id}/submit` *(faqat student)* → javob/yechim topshirish.
- `GET /api/student/assignment-scores` → `{ count, gradedCount, totalScore, totalMax, items:[...] }`.

---

## 13. Farzandni olib ketish (pickup)

Ota-ona "Farzandimni olishga keldim" bossa → sinf rahbariga push. Rahbar qabul qilsa → ota-onaga
"Ruxsat berildi" (holat `accepted`).
```http
POST /api/student/pickup
{ "studentId": null }
```
- `studentId` ixtiyoriy (bir nechta farzand bo'lsa — qaysi birini; egalik telefon bo'yicha tekshiriladi).
- Pickup **kunlik**: o'sha kun `pending` so'rov bo'lsa — o'shani qaytaradi (takror yaratmaydi).
- Javob: `{ id, studentId, studentName, className, status, createdAt, acceptedAt, acceptedByName }`.
- `GET /api/student/pickup` → **bugungi** so'rov holati (yo'q bo'lsa `null`).
  `status:"accepted"` + `acceptedByName` to'lsa — "Ruxsat berildi".

---

## 14. Taklif / shikoyat (`POST /api/student/feedback`)

Ota-ona maktabga taklif yoki shikoyat yuboradi (admin "Taklif va shikoyatlar"da ko'radi).
**Multipart/form-data:**
```
type=suggestion | complaint
text=<matn>
image=<fayl?>            (ixtiyoriy rasm, maks ~20MB)
```
- `text` bo'sh bo'lsa `400`. Javob `204`. Yuboruvchi — farzandning ota-onasi.

---

## 15. Joylashuv (`/api/student/location`)
```http
PUT /api/student/location
{ "latitude": 41.311081, "longitude": 69.240562, "address": "Toshkent, Chilonzor 12" }
```
- `latitude` (−90..90), `longitude` (−180..180) majburiy; `address` ixtiyoriy. Javob `{ "ok": true }`.
- `GET /api/student/location` → `{ latitude, longitude, address, updatedAt }` (yo'q bo'lsa `null`).
- Admin bu joylashuvni "Ilova → Joylashuv" xaritasida (Leaflet) ko'radi.

---

## 16. Telegram bot orqali e'lon olish (`GET /api/student/telegram`)

Ota-ona e'lon olishi uchun **bir marta** botga telefon raqamini ulashi kerak (raqam farzandning
`ParentPhone`si bilan solishtiriladi).
```json
{ "configured": true, "botUsername": "MaktabBot", "botName": "Maktab LMS Bot",
  "deepLink": "https://t.me/MaktabBot", "registered": false }
```
- Birinchi loginda chaqiring. `configured=true` va `registered=false` bo'lsa — taklif (banner) +
  `deepLink` ga tugma. `registered=true` bo'lgach ko'rsatmang.

---

## 17. Sinf chati (`/api/student/chat`)
- `GET /api/student/chat?since=<iso?>` → sinf xabarlari (since dan keyingilari).
- `POST /api/student/chat` `{ "text": "..." }` *(faqat student rolida)* → xabar yuborish.

---

## 18. Sozlamalar (`/api/student/settings`)
- `GET` → `{ "language":"uz", "theme":"system", "notificationsEnabled":true }`.
- `PUT` *(faqat student)* `{ language?, theme?, notificationsEnabled? }` → berilgan maydon o'zgaradi.

---

## 19. Push bildirishnoma (FCM)

Ilova kirgach qurilma tokenini yuboradi. Push: yangi baho, davomat, e'lon, pickup javobi va h.k.
```http
POST /api/student/notifications/register        (faqat student)
{ "token":"<fcm-token>", "platform":"android", "deviceName":"Samsung A52", "appId":"<firebase-app-id>" }
```
- Javob `{ "ok": true }`. Logout: `DELETE /api/student/notifications/register?token=<fcm-token>`.

---

## 20. Fayl yuklash (`POST /api/student/uploads`)

*(faqat student)* — topshiriq javobi sifatida rasm/PDF/video (maks ~20MB), **multipart** `file` maydoni.
Javob: `{ "fileName", "url":"/uploads/...", "size", "contentType" }`.

---

## curl misol
```bash
B=https://intellectschool.uz
TOKEN=$(curl -s -X POST $B/api/auth/login -H "Content-Type: application/json" \
  -d '{"email":"LOGIN","password":"PAROL"}' | jq -r .token)

curl -s $B/api/student/dashboard -H "Authorization: Bearer $TOKEN"
curl -s $B/api/student/notebook  -H "Authorization: Bearer $TOKEN"   # to'liq daftar + feedback statistikasi
curl -s $B/api/student/rating    -H "Authorization: Bearer $TOKEN"
```

---

## Xatolar
| Kod | Sabab |
|---|---|
| `401` | Token yo'q/noto'g'ri yoki login/parol xato; akkaunt arxivlangan. |
| `403` | Ruxsat yo'q (masalan qulflangan LMS mavzusi, yoki yozish amali noto'g'ri rolda). |
| `400` | Noto'g'ri ma'lumot (masalan koordinata oraliqdan tashqari) yoki admin `?studentId` bermadi. |
| `404` | Akkauntga bog'langan farzand topilmadi. |
| `409` | Login bir nechta maktabda — `X-Tenant: <slug>` bilan qayta login. |
