# O'qituvchi (Teacher) ilovasi — API

O'qituvchi ilovasi maktab serveri bilan `TeacherPortalController` (`/api/teacher/*`) orqali ishlaydi.
Barcha endpointlar **`teacher`** rolini talab qiladi. Javoblar **JSON**.

**Base URL:** `https://intellectschool.uz`
Har bir so'rovda: `Authorization: Bearer <token>` sarlavhasi.

---

## 1. Ulanish (auth)

Login **faqat login + parol** bilan (maktab kodi shart emas — login global unikal, server maktabni
o'zi aniqlaydi). Token ichida maktab "yopilgan".

```http
POST /api/auth/login
Content-Type: application/json

{ "email": "<login>", "password": "<parol>" }
```
**Javob:** `{ "token": "eyJ...", "user": { id, fullName, role:"teacher", email, avatarUrl, permissions, modules } }`
- `email` — bu **login** (username, e.g. `karimovadilnoza`), pochta emas. Login/parolni maktab beradi
  (o'qituvchi qo'shilganda avtomatik). Keyingi so'rovlar: `Authorization: Bearer <token>`.
- `permissions` — o'qituvchiga ochilgan bo'limlar (pastga qarang).
- `401` login/parol xato yoki akkaunt **arxivlangan**; `409` login bir nechta maktabda
  (`X-Tenant: <slug>` bilan qayta login).

### Joriy foydalanuvchi / akkaunt
```http
GET /api/auth/me                 →  { id, fullName, role, email, avatarUrl, permissions, modules }
PUT /api/auth/account            →  { currentPassword, email?, newPassword? }   (parol/login almashtirish)
```

---

## 2. Ruxsatlar (permissions)

Admin bo'limlarni o'qituvchiga ochadi. Kalitlar: **`journal`** · **`assignments`** · **`schedule`** ·
**`messages`** · **`salary`**. Ruxsat yo'q bo'lsa tegishli endpoint **`403 Forbid`** qaytaradi.
Bundan tashqari **jurnal/baholash** endpointlari o'qituvchi **shu sinfda shu fanni o'qitishini**
(jadval template'lari bo'yicha) yoki **sinf rahbari** ekanini tekshiradi — aks holda `403`.

`me` (profil) javobidagi `permissions` ro'yxatiga qarab UI'da bo'limlarni ko'rsating/yashiring.

---

## 3. Endpoint'lar (qisqa jadval)

### Profil / umumiy
| Metod | Yo'l | Tavsif |
|---|---|---|
| GET | `/api/teacher/me` | Profil (FISH, login, fanlar, sinf rahbarligi, ruxsatlar, rasm) |
| GET | `/api/teacher/meta` | Maktab meta (chorak/dars vaqtlari/sabablar + joriy chorak/hafta) |
| GET | `/api/teacher/school` | Maktab nomi (brending) |
| GET | `/api/teacher/holidays` | Bayram/dam olish kunlari |
| GET | `/api/teacher/classes` | Dars beradigan sinflar (har birida fanlar + rahbarmi) |
| GET | `/api/teacher/schedule?quarter=&week=` | Dars jadvali *(perm: schedule)* |
| GET | `/api/teacher/salary?from=&to=` | O'z oyligi tarixi *(perm: salary)* |
| GET | `/api/teacher/progress?quarter=` | O'tilgan darslar progresi (o'tilgan/reja) |

### Sinf rahbarligi (homeroom)
| Metod | Yo'l | Tavsif |
|---|---|---|
| GET | `/api/teacher/homeroom` | Rahbar sinfi o'quvchilari (+ ota-ona kelganmi belgisi) |
| GET | `/api/teacher/pickups` | Bugungi pickup so'rovlari (ota-ona "olishga keldim") |
| POST | `/api/teacher/pickups/{id}/accept` | "Qabul qildim" → ota-onaga ruxsat push |
| POST | `/api/teacher/homeroom/handover` | "Topshirdim" (farzandni topshirish) → ota-onaga push |

### Jurnal *(perm: journal + shu sinf+fanni o'qitish)*
| Metod | Yo'l | Tavsif |
|---|---|---|
| GET | `/api/teacher/journal/students?classId=` | Sinf o'quvchilari |
| GET | `/api/teacher/journal/columns?classId=&subjectId=&quarter=` | Ustunlar (sana×dars) |
| GET | `/api/teacher/journal?classId=&subjectId=&quarter=` | Yozuvlar (baho/davomat/uy vazifa/xulq) |
| PUT | `/api/teacher/journal` | Bitta katakni belgilash |
| DELETE | `/api/teacher/journal?classId=&subjectId=&quarter=&studentId=&date=&period=` | Katakni tozalash |
| GET · PUT | `/api/teacher/journal/notes?classId=&subjectId=&quarter=` | Mavzu / uy vazifa (dars o'tildi belgisi) |
| GET · PUT | `/api/teacher/journal/quarter-grades?classId=&subjectId=&quarter=` | Chorak baholari |

### Baholash (UI: "Feedback") *(o'z fanidan)*
| Metod | Yo'l | Tavsif |
|---|---|---|
| GET | `/api/teacher/evaluation/types` | Baholash turlari (admin belgilaydi) |
| GET | `/api/teacher/evaluation/board?classId=&subjectId=&month=` | Baholash jadvali (o'quvchi×tur, oylik) |
| POST | `/api/teacher/evaluation/grade` | Bitta o'quvchiga bitta tur bo'yicha 1-5 |

### Topshiriqlar *(perm: assignments)*
| Metod | Yo'l | Tavsif |
|---|---|---|
| GET | `/api/teacher/assignments` | O'z topshiriqlari |
| POST | `/api/teacher/assignments` | Yangi topshiriq |
| PUT · DELETE | `/api/teacher/assignments/{id}` | Tahrirlash / o'chirish |
| GET | `/api/teacher/assignments/{id}/results` | Natijalar (kim bajardi/ball) |
| PUT | `/api/teacher/assignments/{id}/submissions/{studentId}` | Bajarish holati + ball |
| GET | `/api/teacher/assignment-types` | Topshiriq turlari (dropdown) |
| POST | `/api/teacher/uploads` | Material fayl yuklash (multipart, ~20MB) |

### Chat *(perm: messages)*
| Metod | Yo'l | Tavsif |
|---|---|---|
| GET | `/api/teacher/chat/classes` | Chat ochiq sinflar |
| GET | `/api/teacher/chat/last-messages` | Har sinf oxirgi xabari (ro'yxat preview) |
| GET | `/api/teacher/chat/{className}?since=` | Sinf xabarlari |
| POST | `/api/teacher/chat/{className}` | Xabar yuborish |

### LMS (Ta'lim) — faqat ko'rish
| Metod | Yo'l | Tavsif |
|---|---|---|
| GET | `/api/teacher/lms/subjects?classId=` | O'z sinflari LMS fanlari |
| GET | `/api/teacher/lms/subjects/{subjectId}/topics` | Mavzular (to'liq kontent) |
| GET | `/api/teacher/lms/subjects/{subjectId}/progress` | O'quvchilar progress matritsasi |

### Boshqa
| Metod | Yo'l | Tavsif |
|---|---|---|
| POST | `/api/teacher/feedback` | Taklif/shikoyat (multipart, rasm bilan) → admin |
| POST | `/api/teacher/notifications/register` | Push qurilmani ro'yxatga olish |
| DELETE | `/api/teacher/notifications/register?token=...` | Qurilmani o'chirish (logout) |

---

## 4. Profil (`GET /api/teacher/me`)
```json
{
  "id": "t-1", "fullName": "Karimova Dilnoza", "email": "karimovadilnoza",
  "homeroomClass": "9-A",
  "subjects": [ { "id": "subj-1", "name": "Matematika" } ],
  "permissions": ["journal","assignments","schedule","messages","salary"],
  "photoUrl": "/uploads/abc.jpg"
}
```
- `homeroomClass` bo'sh bo'lsa — sinf rahbari emas (homeroom bo'limini yashiring).

---

## 5. Sinflar va jadval

`GET /api/teacher/classes` → o'qituvchi dars beradigan + rahbarlik qiladigan sinflar:
```json
[ { "classId":"c-1", "className":"9-A", "grade":9, "isHomeroom":true,
    "subjects":[ { "id":"subj-1", "name":"Matematika" } ] } ]
```

`GET /api/teacher/schedule?quarter=&week=` *(perm: schedule)* → o'qituvchining haftalik darslari:
```json
[ { "day":0, "period":1, "startTime":"08:30", "endTime":"09:15",
    "classId":"c-1", "className":"9-A", "subjectId":"subj-1", "subjectName":"Matematika", "subGroup":0 } ]
```
- `day`: 0=Dushanba…5=Shanba. `quarter`/`week` berilmasa — joriy. `subGroup`: 0=butun sinf, 1/2=guruh.

---

## 6. Maosh (`GET /api/teacher/salary?from=&to=`) *(perm: salary)*

O'qituvchining **o'z** oyligi tarixi/jamlamasi (`SalaryLedger`). `from`/`to` — ISO sana oralig'i
(ixtiyoriy). Faqat o'ziniki — boshqa o'qituvchini ko'rmaydi.

---

## 7. O'tilgan darslar progresi (`GET /api/teacher/progress?quarter=`)

O'tilgan / rejalashtirilgan darslar nisbati — umumiy + har (sinf, fan, guruh) kesimi.
Reja jadvaldan, o'tilgan jurnaldagi **"dars o'tildi"** belgisidan. `quarter` berilmasa — joriy.

---

## 8. Sinf rahbarligi (homeroom)

`GET /api/teacher/homeroom` → `{ className, students: [ { id, fullName, hasPendingPickup, pickupStatus, pickupAt } ] }`
(faqat rahbar sinfi; `hasPendingPickup=true` → ota-ona kelgan).

`GET /api/teacher/pickups` → **bugungi** pickup so'rovlari:
`[ { id, studentId, studentName, className, status, createdAt, acceptedAt, acceptedByName } ]`.

`POST /api/teacher/pickups/{id}/accept` → so'rovni tasdiqlaydi (status `accepted`), **ota-onaga push**
("Ruxsat berildi"). Javob — yangilangan pickup.

`POST /api/teacher/homeroom/handover` `{ "studentId": "..." }` → farzandni ota-onaga topshirish
(sana qoldiriladi) + ota-onaga push. Javob — pickup yozuvi.

---

## 9. Jurnal *(perm: journal + shu sinf+fanni o'qitish)*

Barcha jurnal endpointlari **`classId`, `subjectId`, `quarter`** bilan ishlaydi. Ruxsat/egalik
bo'lmasa `403`.

- `GET /api/teacher/journal/students?classId=` → sinf o'quvchilari (`StudentDto`).
- `GET /api/teacher/journal/columns?classId=&subjectId=&quarter=` → ustunlar `[{ date, period, subGroup }]`.
- `GET /api/teacher/journal?classId=&subjectId=&quarter=` → yozuvlar:
  ```json
  [ { "studentId":"...", "date":"2026-06-05", "period":1, "grade":5, "reasonId":null,
      "homework":1, "behavior":0, "mastery":null } ]
  ```
  - `homework`: 0=belgisiz · 1=qildi · 2=qilmadi. `behavior`: 0=belgisiz · 1=yaxshi · 2=yomon.
- **`PUT /api/teacher/journal`** — bitta katakni belgilash:
  ```json
  { "classId":"c-1", "subjectId":"subj-1", "quarter":4, "studentId":"s-1",
    "date":"2026-06-05", "period":1, "grade":5, "reasonId":null,
    "homework":1, "behavior":0, "mastery":null }
  ```
  - **Kelajak sanaga** baho/davomat qo'yib bo'lmaydi (`400`). Baho qo'yilsa o'quvchiga push boradi.
- `DELETE /api/teacher/journal?classId=&subjectId=&quarter=&studentId=&date=&period=` → katakni tozalash.
- `GET · PUT /api/teacher/journal/notes` → mavzu / uy vazifa:
  `PUT { classId, subjectId, quarter, date, period, topic, homework?, conducted, subGroup? }`
  (`conducted=true` = "dars o'tildi" — progressga ta'sir qiladi).
- `GET · PUT /api/teacher/journal/quarter-grades` → chorak baholari:
  `GET` → `[{ studentId, grade, recommended }]` (`recommended` = kunlik baholar o'rtachasidan tavsiya).
  `PUT { classId, subjectId, quarter, studentId, grade? }` — **faqat admin ochgan chorak**ka
  (`gradesOpen`); aks holda `403`. `grade=null` → o'chiradi.

---

## 10. Baholash / "Feedback" *(o'z fanidan)*

O'qituvchi **o'z fanidan** o'quvchilarga, admin belgilagan **baholash turlari** (UI'da "Feedback nomi":
yozma, og'zaki/suhbat...) bo'yicha oylik 1-5 qo'yadi.

- `GET /api/teacher/evaluation/types` → `[{ id, name, description }]`.
- `GET /api/teacher/evaluation/board?classId=&subjectId=&month=` → jadval (o'quvchi × tur, tanlangan oy):
  ```json
  { "months":["2026-06","2026-05"], "month":"2026-06", "week":0,
    "types":[ { "id":"type-1", "name":"Yozma", "description":"" } ],
    "rows":[ { "studentId":"s-1", "fullName":"Aliyev Ali", "className":"9-A",
               "grades":{ "type-1":5 }, "avg":5.0 } ],
    "subjectId":"subj-1", "subjects":[] }
  ```
  - O'qituvchi shu sinfda shu fanni o'qitmasa `403`. `month` berilmasa — eng so'nggi oy.
- **`POST /api/teacher/evaluation/grade`** — bitta baho:
  ```json
  { "studentId":"s-1", "typeId":"type-1", "month":"2026-06", "week":0, "score":5,
    "subjectId":"subj-1", "classId":"c-1" }
  ```
  - `subjectId` va `classId` **majburiy** (egalik). `score` bo'sh / 1-5 dan tashqari → o'sha bahoni tozalaydi.

---

## 11. Topshiriqlar *(perm: assignments)*

- `GET /api/teacher/assignments` → o'qituvchining o'z topshiriqlari.
- **`POST /api/teacher/assignments`** — yangi topshiriq:
  ```json
  { "subjectId":"subj-1", "title":"Nazorat ishi", "description":"...", "format":"test",
    "classIds":["c-1","c-2"], "startDate":"2026-06-05", "dueDate":"2026-06-10",
    "lateAccept":true, "latePenaltyPct":20, "maxScore":100, "autoGrade":true,
    "materials":[ ... ], "questions":[ ... ] }
  ```
  `format`: masalan `test` / `file` / `text`. `questions` — test bo'lsa.
- `PUT /api/teacher/assignments/{id}` — tahrirlash; `DELETE /api/teacher/assignments/{id}` — o'chirish.
- `GET /api/teacher/assignments/{id}/results` → kim bajardi/bajarmadi + ball (faqat o'z topshirig'i).
- `PUT /api/teacher/assignments/{id}/submissions/{studentId}` `{ "completed":true, "score":85 }` —
  o'quvchi holatini/ballini qo'lda belgilash.
- `GET /api/teacher/assignment-types` → dropdown uchun turlar.
- `POST /api/teacher/uploads` (multipart `file`, ~20MB) → `{ name, url:"/uploads/...", size, contentType }`.

---

## 12. Chat *(perm: messages)*
- `GET /api/teacher/chat/classes` → o'qituvchi yoza oladigan sinflar.
- `GET /api/teacher/chat/last-messages` → `{ "9-A": "oxirgi xabar matni", ... }` (ro'yxat preview).
- `GET /api/teacher/chat/{className}?since=<iso?>` → xabarlar.
- `POST /api/teacher/chat/{className}` `{ "text":"..." }` → xabar yuborish.

---

## 13. LMS (Ta'lim) — faqat ko'rish

O'qituvchi LMS kontentini **yaratmaydi** (uni admin qiladi); faqat o'zi dars beradigan/rahbarlik
qiladigan sinflar materialini va o'quvchilar progresini ko'radi.
- `GET /api/teacher/lms/subjects?classId=` → fanlar (ixtiyoriy bitta sinfga filtr).
- `GET /api/teacher/lms/subjects/{subjectId}/topics` → mavzular (o'qituvchiga **hammasi ochiq**),
  har birida tugatgan o'quvchi soni.
- `GET /api/teacher/lms/subjects/{subjectId}/progress` → matritsa: kim qaysi mavzuni tugatgan.

---

## 14. Taklif / shikoyat (`POST /api/teacher/feedback`)

O'qituvchi → admin. **Multipart/form-data:**
```
type=suggestion | complaint
text=<matn>
image=<fayl?>            (ixtiyoriy rasm, ~20MB)
```
- `text` bo'sh → `400`. Javob `204`. Yuboruvchi = o'qituvchi (admin "Taklif va shikoyatlar"da ko'radi).

---

## 15. Push bildirishnoma (FCM)
```http
POST /api/teacher/notifications/register
{ "token":"<fcm-token>", "platform":"android", "deviceName":"Samsung A52", "appId":"<firebase-app-id>" }
```
- Javob `{ "ok": true }`. Logout: `DELETE /api/teacher/notifications/register?token=<fcm-token>`.
- Push: pickup so'rovi (rahbarga), e'lon va h.k.

---

## curl misol
```bash
B=https://intellectschool.uz
TOKEN=$(curl -s -X POST $B/api/auth/login -H "Content-Type: application/json" \
  -d '{"email":"LOGIN","password":"PAROL"}' | jq -r .token)

curl -s $B/api/teacher/me   -H "Authorization: Bearer $TOKEN"
curl -s "$B/api/teacher/schedule" -H "Authorization: Bearer $TOKEN"
curl -s "$B/api/teacher/journal?classId=c-1&subjectId=subj-1&quarter=4" -H "Authorization: Bearer $TOKEN"
```

---

## Xatolar
| Kod | Sabab |
|---|---|
| `401` | Token yo'q/noto'g'ri yoki login/parol xato; akkaunt arxivlangan. |
| `403` | Ruxsat (perm) yo'q, yoki shu sinf+fanni o'qitmaydi, yoki chorak bahosi yopiq. |
| `400` | Noto'g'ri ma'lumot (masalan kelajak sanaga baho, bo'sh matn). |
| `404` | O'qituvchi/sinf topilmadi. |
| `409` | Login bir nechta maktabda — `X-Tenant: <slug>` bilan qayta login. |
