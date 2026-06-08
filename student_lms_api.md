# O'quvchi / Ota-ona ilovasi — LMS (Ta'lim) API

LMS ierarxiyasi: **Sinf → Fan → Modul → Mavzu**. O'quvchi o'z sinfiga biriktirilgan fanlarni,
fan ichidagi modullarni, modul ichidagi mavzularni (video + matn + materiallar) ko'radi.

**Base URL:** `https://intellectschool.uz`
Har bir so'rovda: `Authorization: Bearer <token>` (login `POST /api/auth/login` orqali olinadi).
Rollar: `student`, `parent` (oilaning bitta akkaunti). Barcha javoblar **JSON**.

> Quyidagi endpointlarda `?studentId=` query — FAQAT admin uchun (boshqa o'quvchini ko'rish).
> Ilovadagi o'quvchi/ota-ona uchun kerak emas (token o'zi farzandni aniqlaydi).

---

## 1. Fanlar ro'yxati

```http
GET /api/student/lms/subjects
```

**Javob:** `StudentLmsSubjectDto[]`
```jsonc
[
  {
    "id": "...",
    "title": "Matematika",
    "description": "9-sinf algebra kursi",
    "unlockMode": "sequential",   // "all" | "sequential" | "batch"
    "batchSize": 3,                // unlockMode="batch" bo'lganda
    "topicsCount": 12,            // fandagi JAMI mavzu (barcha modullar bo'yicha)
    "completedCount": 5           // o'quvchi tugatgan mavzular soni
  }
]
```

---

## 2. Fan modullari (mavzular bilan)  ⭐ asosiy

Fan bosilganda — modullar ro'yxati, har modul ichida mavzular (ochilish va progress bilan).

```http
GET /api/student/lms/subjects/{subjectId}/modules
```

**Javob:** `StudentLmsModuleDto[]` (modullar `order` bo'yicha tartiblangan)
```jsonc
[
  {
    "id": "...",
    "title": "1-modul: Kirish",
    "description": "",
    "order": 1,
    "topicsCount": 4,        // shu moduldagi mavzular
    "completedCount": 4,     // shu modulда tugatilgan mavzular
    "topics": [
      {
        "id": "...",
        "moduleId": "...",
        "title": "Ratsional sonlar",
        "description": "...",
        "videoUrl": "https://...",      // QULFLANGAN bo'lsa null
        "textContent": "Matn...",       // QULFLANGAN bo'lsa null
        "order": 1,
        "materials": [                  // QULFLANGAN bo'lsa bo'sh []
          { "id":"...", "name":"Konspekt.pdf", "url":"/uploads/...", "size":12345, "contentType":"application/pdf" }
        ],
        "isUnlocked": true,
        "isCompleted": true
      }
    ]
  }
]
```

**Ilovada:** fan ekranida modullarni ro'yxat qiling; har modulni ochganda ichidagi mavzularni ko'rsating.
Qulflangan (`isUnlocked:false`) mavzuda kontent (`videoUrl`/`textContent`/`materials`) bo'sh keladi —
ularni "qulf" belgisi bilan ko'rsating va ochishga ruxsat bermang.

---

## 3. Bitta mavzu tafsiloti

Ochiq mavzuning to'liq kontenti (video, matn, materiallar).

```http
GET /api/student/lms/topics/{topicId}
```

**Javob:** `StudentLmsTopicDto` (2-bo'limdagi `topics[]` elementi bilan bir xil shakl — `moduleId` bor).
**Qulflangan** mavzu so'ralsa → **`403`** (ochilmagan).

---

## 4. Mavzuni "tugatildi" deb belgilash

Mavzu o'qib/ko'rib bo'lingach — keyingi mavzu ochilishi (sequential/batch) uchun.

```http
POST /api/student/lms/topics/{topicId}/complete
```
- Javob: **`204`** (muvaffaqiyat). Ota-ona ham farzandi nomidan belgilashi mumkin.

---

## Ochilish tartibi (unlock)

Ochilish butun **fan** bo'yicha global ketma-ketlikda hisoblanadi (modul tartibi → mavzu tartibi):
- **`all`** — barcha mavzular ochiq.
- **`sequential`** — har bir mavzu oldingisi tugatilgach ochiladi (1-modul tugamaguncha 2-modul mavzulari qulf).
- **`batch`** — bir vaqtda `batchSize` ta mavzu ochiq; partiya tugagach keyingisi.

> Eslatma: `unlockMode`/`batchSize` **fan** darajasida. Modul — mavzular guruhi (bo'lim).

---

## Eski (backward-compat) endpoint

Modullarsiz, **tekis** mavzular ro'yxati (eski ilova ishlashda davom etishi uchun). Yangi ilova
`/modules` (2-bo'lim) dan foydalanishi tavsiya etiladi.
```http
GET /api/student/lms/subjects/{subjectId}/topics   →  StudentLmsTopicDto[]  (har topicda moduleId bor)
```

---

## Xatolar

| Kod | Sabab |
|---|---|
| `401` | Token yo'q/noto'g'ri yoki akkaunt arxivlangan. |
| `403` | Qulflangan mavzu so'raldi yoki fan o'quvchining sinfiga tegishli emas. |
| `404` | Fan/mavzu topilmadi yoki akkauntga bog'langan farzand yo'q. |

## curl misol
```bash
B=https://intellectschool.uz
TOKEN=$(curl -s -X POST $B/api/auth/login -H "Content-Type: application/json" \
  -d '{"email":"LOGIN","password":"PAROL"}' | jq -r .token)

curl -s $B/api/student/lms/subjects -H "Authorization: Bearer $TOKEN"
curl -s $B/api/student/lms/subjects/SUBJECT_ID/modules -H "Authorization: Bearer $TOKEN"
curl -s -X POST $B/api/student/lms/topics/TOPIC_ID/complete -H "Authorization: Bearer $TOKEN"
```
