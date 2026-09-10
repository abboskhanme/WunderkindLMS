#!/usr/bin/env python3
"""
Wunderkind International School — demo data seeder.

Fills every admin section with realistic sample data through the public API,
so the system can be demonstrated end to end. Re-runnable: existing records are
reused (matched by name), never duplicated.

    python3 tools/seed_demo.py [--base http://localhost:8080] [--user admin] [--password Admin123!]

Counts: 10 per kind where 10 makes sense; natural counts elsewhere
(4 quarters, 6 absence reasons, 3 branches, 5 buses).
"""

import argparse
import json
import random
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import date, timedelta

random.seed(20260909)

# --------------------------------------------------------------------------- HTTP

TOKEN = None
BASE = "http://localhost:8080"
LAST_OK = False


def api(method, path, body=None, quiet=False, form=False):
    global LAST_OK
    LAST_OK = False
    url = BASE + path
    if form:
        # Feedback endpointi IFormFile qabul qilgani uchun faqat multipart bilan ishlaydi.
        boundary = "----seedboundary7d91"
        parts = []
        for k, v in (body or {}).items():
            parts.append(f"--{boundary}\r\n"
                         f'Content-Disposition: form-data; name="{k}"\r\n\r\n{v}\r\n')
        data = ("".join(parts) + f"--{boundary}--\r\n").encode()
        ctype = f"multipart/form-data; boundary={boundary}"
    else:
        data = json.dumps(body).encode() if body is not None else None
        ctype = "application/json"
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("Content-Type", ctype)
    if TOKEN:
        req.add_header("Authorization", "Bearer " + TOKEN)
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            raw = r.read()
            LAST_OK = 200 <= r.status < 300
            return json.loads(raw) if raw else None
    except urllib.error.HTTPError as e:
        detail = e.read().decode(errors="replace")[:300]
        if not quiet:
            print(f"  !! {method} {path} -> {e.code} {detail}")
        return None


def login(user, password):
    global TOKEN
    res = api("POST", "/api/auth/login", {"email": user, "password": password})
    if not res or "token" not in res:
        sys.exit("Login muvaffaqiyatsiz — server ishlayaptimi va login/parol to'g'rimi?")
    TOKEN = res["token"]
    print(f"Kirildi: {res['user']['fullName']} ({res['user']['role']})")


def find(items, value, *keys):
    """Match a record in a list response by any of the given keys."""
    for it in items or []:
        for k in keys:
            if str(it.get(k, "")).strip().lower() == str(value).strip().lower():
                return it
    return None


def get_or_create(list_path, create_path, payload, match_value, *match_keys):
    """Create only when a record with the same name is not already there."""
    existing = find(api("GET", list_path) or [], match_value, *match_keys)
    if existing:
        return existing
    api("POST", create_path, payload)
    return find(api("GET", list_path) or [], match_value, *match_keys)


# --------------------------------------------------------------------------- data

SUBJECTS = [
    "Matematika", "Ona tili va adabiyot", "Ingliz tili", "Rus tili", "Fizika",
    "Kimyo", "Biologiya", "Tarix", "Geografiya", "Informatika",
]

CLASSES = [
    ("1-A", 1, "uz", 1_800_000), ("1-B", 1, "uz", 1_800_000),
    ("2-A", 2, "uz", 1_900_000), ("2-B", 2, "ru", 1_900_000),
    ("3-A", 3, "uz", 2_000_000), ("3-B", 3, "uz", 2_000_000),
    ("4-A", 4, "uz", 2_100_000), ("5-A", 5, "uz", 2_300_000),
    ("6-A", 6, "uz", 2_400_000), ("7-A", 7, "ru", 2_500_000),
]

TEACHERS = [
    ("Karimova Dilnoza Akmalovna",    "female", "1988-03-14", "oliy",      6_500_000),
    ("Rahimov Sardor Bahodirovich",   "male",   "1985-07-22", "1",         6_200_000),
    ("Yusupova Nigora Ravshanovna",   "female", "1990-11-05", "1",         5_900_000),
    ("To'xtayev Jasur Alisherovich",  "male",   "1983-01-30", "oliy",      6_800_000),
    ("Ergasheva Malika Botirovna",    "female", "1992-06-18", "2",         5_400_000),
    ("Nazarov Otabek Farhodovich",    "male",   "1987-09-09", "1",         6_000_000),
    ("Qodirova Zilola Shuhratovna",   "female", "1994-02-27", "mutaxasis", 5_000_000),
    ("Aliyev Bekzod Ulug'bekovich",   "male",   "1986-12-11", "oliy",      6_600_000),
    ("Sharipova Gulnora Anvarovna",   "female", "1991-04-03", "2",         5_500_000),
    ("Mirzayev Shohruh Davronovich",  "male",   "1989-08-25", "1",         6_100_000),
]

STUDENTS = [
    ("Abdullayev Amir Sardorovich",      "male",   "2019-04-12", "Abdullayev Sardor",    "+998 90 123 45 67"),
    ("Baxtiyorova Zilola Baxtiyorovna",  "female", "2019-06-30", "Baxtiyorov Anvar",     "+998 91 234 56 78"),
    ("G'aniyev Doniyor Rustamovich",     "male",   "2019-02-08", "G'aniyev Rustam",      "+998 93 345 67 89"),
    ("Do'stmurodova Oysha Farhodovna",   "female", "2019-09-19", "Do'stmurodov Farhod",  "+998 94 456 78 90"),
    ("Ismoilov Muhammad Jasurovich",     "male",   "2019-11-02", "Ismoilov Jasur",       "+998 97 567 89 01"),
    ("Jo'rayeva Sevinch Baxrom qizi",    "female", "2019-01-25", "Jo'rayev Baxrom",      "+998 88 678 90 12"),
    ("Komilov Asadbek Otabekovich",      "male",   "2019-07-14", "Komilov Otabek",       "+998 99 789 01 23"),
    ("Mahmudova Robiya Alisher qizi",    "female", "2019-05-06", "Mahmudov Alisher",     "+998 90 890 12 34"),
    ("Nurmatov Islom Shuhratovich",      "male",   "2019-10-21", "Nurmatov Shuhrat",     "+998 91 901 23 45"),
    ("O'rozova Xadicha Bekzod qizi",     "female", "2019-03-17", "O'rozov Bekzod",       "+998 93 012 34 56"),
]

STAFF = [
    ("Sobirova Nasiba Qahramonovna", "Bosh hisobchi"),
    ("Tursunov Aziz Nodirovich",     "Kassir"),
    ("Xolmatova Sitora Ilhomovna",   "Metodist"),
    ("Rustamov Ulug'bek Sobirovich", "Psixolog"),
    ("Yo'ldosheva Feruza Tolibovna", "Tibbiyot hamshirasi"),
    ("Sattorov Javohir Baxtiyorovich", "IT-administrator"),
    ("Ochilova Madina Zafarovna",    "Kutubxonachi"),
    ("Hamidov Rustam Odilovich",     "Xo'jalik mudiri"),
    ("Qurbonova Nodira Ergashevna",  "Kotiba"),
    ("Eshonqulov Doston Baxodirovich", "Oshxona mudiri"),
]

# Rang — stageColors kalitlari (slate/blue/emerald/amber/violet/rose/cyan/orange).
# Hex kod BERILMAYDI: frontend uni topa olmay sahifani buzadi.
LEAD_STAGES = [
    ("Yangi murojaat", "slate"), ("Bog'lanildi", "cyan"),
    ("Suhbatga taklif", "violet"), ("Sinov darsi", "amber"),
    ("Shartnoma", "emerald"),
]

LEADS = [
    ("Alimov Sanjar Otabekovich",   "male",   "2019-05-11", "Alimov Otabek",    "+998 90 111 22 33", 1, "Instagram orqali murojaat"),
    ("Bekmurodova Zaynab Alisher qizi", "female", "2018-08-23", "Bekmurodov Alisher", "+998 91 222 33 44", 2, "Tanishlar tavsiyasi"),
    ("Davronov Amirbek Shavkatovich", "male", "2019-01-09", "Davronov Shavkat", "+998 93 333 44 55", 1, "Telefon orqali"),
    ("Egamberdiyeva Ruxshona Botir qizi", "female", "2017-12-04", "Egamberdiyev Botir", "+998 94 444 55 66", 3, "Ochiq eshiklar kuni"),
    ("Fayzullayev Islombek Anvarovich", "male", "2019-07-27", "Fayzullayev Anvar", "+998 97 555 66 77", 1, "Telegram kanal"),
    ("G'ulomova Sabina Rustam qizi", "female", "2018-03-15", "G'ulomov Rustam",  "+998 88 666 77 88", 2, "Reklama bannerdan"),
    ("Hakimov Jahongir Doniyorovich", "male", "2016-10-30", "Hakimov Doniyor",  "+998 99 777 88 99", 4, "Boshqa maktabdan ko'chish"),
    ("Ibrohimova Sevara Jamshid qizi", "female", "2019-02-19", "Ibrohimov Jamshid", "+998 90 888 99 00", 1, "Sayt orqali ariza"),
    ("Jamolov Umidjon Farrux o'g'li", "male", "2015-06-08", "Jamolov Farrux",   "+998 91 999 00 11", 5, "Aka-ukasi shu maktabda"),
    ("Kamolova Nilufar Sardor qizi", "female", "2018-11-12", "Kamolov Sardor",  "+998 93 000 11 22", 2, "Ota-onalar chatidan"),
]

DISCIPLINE_REASONS = [
    ("Darsga kechikish", -5), ("Uy vazifasini bajarmaslik", -5),
    ("Darsda tartib buzish", -10), ("Forma qoidasiga rioya qilmaslik", -3),
    ("Tengdoshiga nisbatan qo'pollik", -15), ("Maktab mulkiga zarar", -20),
    ("Olimpiadada g'olib", 20), ("Sinf tadbirida faol ishtirok", 10),
    ("Kutubxona ishida yordam", 5), ("Bir chorak to'liq davomat", 15),
]

EVALUATION_TYPES = [
    ("Faollik", "Darsdagi ishtiroki va savollarga javobi"),
    ("Intizom", "Dars davomida o'zini tutishi"),
    ("Uy vazifa", "Uy vazifasini muntazam bajarishi"),
    ("Darsga tayyorgarlik", "Kitob, daftar, qurol-yarog' bilan kelishi"),
    ("Jamoada ishlash", "Guruh topshiriqlaridagi hissasi"),
    ("Ijodkorlik", "Nostandart yechim topa olishi"),
    ("Mustaqillik", "Yordamsiz ishlay olishi"),
    ("Kitobxonlik", "Darsdan tashqari o'qishi"),
    ("Sport faolligi", "Jismoniy tarbiya va musobaqalar"),
    ("Odob-axloq", "Muomala madaniyati"),
]

ABSENCE_REASONS = [
    ("Sababsiz", "S", False), ("Kasal", "K", False), ("Kech qoldi", "KQ", True),
    ("Oilaviy sabab", "O", False), ("Musobaqa / olimpiada", "M", False),
    ("Ruxsat bilan", "R", False),
]

HOLIDAYS = [
    ("2026-09-01", "Mustaqillik kuni"),
    ("2026-10-01", "O'qituvchi va murabbiylar kuni"),
    ("2026-10-31", "Kuzgi ta'til boshlanishi"),
    ("2026-12-08", "Konstitutsiya kuni"),
    ("2026-12-31", "Qishki ta'til"),
    ("2027-01-01", "Yangi yil"),
    ("2027-01-14", "Vatan himoyachilari kuni"),
    ("2027-03-08", "Xotin-qizlar kuni"),
    ("2027-03-21", "Navro'z bayrami"),
    ("2027-05-09", "Xotira va qadrlash kuni"),
]

QUARTERS = [
    (1, "2026-09-01", "2026-10-30"), (2, "2026-11-09", "2026-12-30"),
    (3, "2027-01-11", "2027-03-19"), (4, "2027-03-30", "2027-05-25"),
]

DISHES = [
    ("Sutli bo'tqa", "Guruch, sut, sariyog', shakar"),
    ("Tuxum va non", "Qaynatilgan tuxum, non, sariyog'"),
    ("Pishiriq va kakao", "Non pishiriq, kakao, sut"),
    ("Mastava", "Guruch, go'sht, sabzavot, ko'kat"),
    ("Osh", "Guruch, mol go'shti, sabzi, piyoz"),
    ("Lag'mon", "Qo'l uzilgan xamir, go'sht, sabzavot"),
    ("Kartoshka pyuresi va kotlet", "Kartoshka, sut, tovuq kotleti"),
    ("Sho'rva", "Mol go'shti, kartoshka, sabzi"),
    ("Makaron va tovuq", "Makaron, tovuq go'shti, pomidor sousi"),
    ("Mevali salat", "Olma, banan, uzum, yogurt"),
]

BRANCHES = [
    ("Bosh bino — Yunusobod", "Toshkent sh., Yunusobod t., Amir Temur ko'chasi 108", 41.3450, 69.2860, 250),
    ("Filial — Mirzo Ulug'bek", "Toshkent sh., Mirzo Ulug'bek t., Buyuk Ipak Yo'li 45", 41.3255, 69.3350, 200),
    ("Filial — Chilonzor", "Toshkent sh., Chilonzor t., Bunyodkor shoh ko'chasi 12", 41.2750, 69.2040, 200),
]

BUSES = [
    ("1-avtobus", "01 A 123 BC", "Aliyev Vohid",     "+998 90 100 10 10", "Yunusobod — Maktab"),
    ("2-avtobus", "01 B 456 DE", "Karimov Sanjar",   "+998 90 200 20 20", "Chilonzor — Maktab"),
    ("3-avtobus", "01 C 789 FG", "Sultonov Rustam",  "+998 90 300 30 30", "Sergeli — Maktab"),
    ("4-avtobus", "01 D 012 HI", "Qosimov Baxtiyor", "+998 90 400 40 40", "Mirzo Ulug'bek — Maktab"),
    ("5-avtobus", "01 E 345 JK", "Toshmatov Islom",  "+998 90 500 50 50", "Olmazor — Maktab"),
]

CAMERAS = [
    ("Asosiy kirish", "1-qavat, kirish eshigi"), ("Kirish holli", "1-qavat, hol"),
    ("Koridor 1-qavat", "1-qavat, koridor"),     ("Koridor 2-qavat", "2-qavat, koridor"),
    ("Oshxona", "1-qavat, oshxona"),             ("Sport zali", "Sport majmuasi"),
    ("Aktzal", "2-qavat, aktzal"),               ("Hovli — old tomon", "Tashqi hudud"),
    ("Hovli — orqa tomon", "Tashqi hudud"),      ("Avtoturargoh", "Tashqi hudud"),
]

FINANCE = [
    ("income",  "donation", 15_000_000, "Homiy tashkilotdan kutubxona uchun"),
    ("income",  "rent_in",   3_500_000, "Sport zalini ijaraga berish (sentabr)"),
    ("income",  "other",     1_200_000, "Maktab yarmarkasidan tushum"),
    ("expense", "salary",   58_000_000, "Avgust oyi maoshi"),
    ("expense", "utilities", 7_400_000, "Elektr, suv, gaz — avgust"),
    ("expense", "supplies",  9_800_000, "Darslik va kanselyariya"),
    ("expense", "rent",     12_000_000, "Bino ijarasi — sentabr"),
    ("expense", "repair",    4_600_000, "Sinf xonalarini ta'mirlash"),
    ("expense", "supplies",  3_200_000, "Oshxona jihozlari"),
    ("expense", "other",     1_900_000, "Transport xarajatlari"),
]

TOPIC_TITLES = [
    "Kirish va asosiy tushunchalar", "Amaliy mashg'ulot", "Mustahkamlash darsi",
    "Yangi mavzu bayoni", "Takrorlash va nazorat",
]

CHAT_MESSAGES = [
    "Assalomu alaykum, hurmatli ota-onalar!",
    "Ertaga 3-darsdan keyin sinf soati bo'lib o'tadi.",
    "Matematika bo'yicha nazorat ishi juma kuni.",
    "Iltimos, farzandingizga sport formasini bering.",
    "Kutubxonadan olingan kitoblarni qaytaring.",
    "Ota-onalar yig'ilishi 20-sentabr, soat 17:00 da.",
    "Oshxona menyusi ilovada yangilandi.",
    "Kuzgi ta'til 31-oktabrdan boshlanadi.",
    "Rasm uchun oq ko'ylak kerak bo'ladi.",
    "Savollaringiz bo'lsa shu yerda yozing.",
]

TODAY = date(2026, 9, 9)
DEMO_PASSWORD = "Demo123!"


def school_days(start, end):
    """Mon-Sat, excluding holiday dates."""
    holiday = {h[0] for h in HOLIDAYS}
    d, out = start, []
    while d <= end:
        if d.weekday() < 6 and d.isoformat() not in holiday:
            out.append(d)
        d += timedelta(days=1)
    return out


# --------------------------------------------------------------------------- seed

FEEDBACKS = [
    ("suggestion", "Oshxona menyusiga ko'proq meva qo'shilsa yaxshi bo'lardi."),
    ("suggestion", "Kutubxonada ingliz tilidagi kitoblar ko'paytirilsa."),
    ("complaint",  "Sport zalining shifti nam tortyapti."),
    ("suggestion", "Dars jadvalini ilovada oldindan ko'rsatish qulay bo'lardi."),
    ("complaint",  "Avtobus ba'zida kechikib kelmoqda."),
    ("suggestion", "Robototexnika to'garagini ochish taklifi."),
    ("complaint",  "2-qavat hojatxonasida chiroq ishlamayapti."),
    ("suggestion", "Ota-onalar uchun ochiq dars tashkil qilinsa."),
    ("suggestion", "Uy vazifalari haqida push xabar kelsa qulay."),
    ("complaint",  "Sinf xonasi ertalab sovuq bo'lyapti."),
]


def seed_student_side(students):
    """Taklif/shikoyat va pickup — o'quvchi akkaunti orqali (real oqim)."""
    global TOKEN
    if not students:
        return
    admin_token = TOKEN
    if len(api("GET", "/api/admin/feedback", quiet=True) or []) > 0:
        print("Taklif va shikoyatlar: allaqachon kiritilgan (o'tkazib yuborildi)")
        return
    sent = 0
    for i, st in enumerate(students[:len(FEEDBACKS)]):
        TOKEN = admin_token          # keyingi o'quvchi uchun admin huquqi kerak
        cred = api("GET", f"/api/admin/students/{st['id']}/credentials", quiet=True)
        if not cred or not cred.get("login"):
            continue
        # Login endpointi daqiqasiga 10 ta so'rovga cheklangan — 429 bo'lsa kutib qayta urinamiz.
        res = None
        for attempt in range(3):
            res = api("POST", "/api/auth/login",
                      {"email": cred["login"], "password": DEMO_PASSWORD}, quiet=True)
            if res and "token" in res:
                break
            time.sleep(20)
        if not res or "token" not in res:
            continue
        TOKEN = res["token"]
        ftype, text = FEEDBACKS[i]
        api("POST", "/api/student/feedback", {"type": ftype, "text": text},
            quiet=True, form=True)
        if LAST_OK:
            sent += 1
        if i < 3:
            api("POST", "/api/student/pickup", {"studentId": None}, quiet=True)
    TOKEN = admin_token
    print(f"Taklif va shikoyatlar: {sent} · Pickup so'rovlari: 3")


def write_credentials(students, teachers):
    """Demo akkauntlarni bitta faylga yozadi (parollar hali almashtirilmagan bo'lsa)."""
    lines = ["Wunderkind International School — demo akkauntlar",
             "=" * 52, "",
             f"Admin:      admin / Admin123!", ""]
    lines.append("O'qituvchilar (parol: %s)" % DEMO_PASSWORD)
    for name, tid in teachers.items():
        c = api("GET", f"/api/admin/teachers/{tid}/credentials", quiet=True) or {}
        lines.append(f"  {c.get('login', '?'):<24} {name}")
    lines += ["", "O'quvchilar / ota-onalar (parol: %s)" % DEMO_PASSWORD]
    for st in students:
        c = api("GET", f"/api/admin/students/{st['id']}/credentials", quiet=True) or {}
        lines.append(f"  {c.get('login', '?'):<24} {st['fullName']}")
    lines += ["", "Xodimlar (parol: %s) — admin panelda 'Xodimlar va rollar'" % DEMO_PASSWORD, ""]
    path = "tools/demo-credentials.txt"
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print(f"Login/parollar yozildi: {path}")


def main():
    global BASE
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default=BASE)
    ap.add_argument("--user", default="admin")
    ap.add_argument("--password", default="Admin123!")
    args = ap.parse_args()
    BASE = args.base.rstrip("/")

    login(args.user, args.password)

    # 1 ---------------------------------------------------------------- maktab
    api("PUT", "/api/admin/settings/school", {
        # Yon menyu shu qiymatni ko'rsatadi — qisqa nom (to'liq nom sarlavha,
        # login va landing sahifalarida turadi).
        "name": "Wunderkind School",
        "director": "Abbosxon",
        "phone": "+998 71 200 10 10",
        "email": "info@wunderkindschool.uz",
        "address": "Toshkent sh., Yunusobod t., Amir Temur ko'chasi 108",
        "region": "Toshkent shahri",
        "district": "Yunusobod tumani",
    })
    print("Maktab ma'lumotlari saqlandi")

    # 2 --------------------------------------------------- choraklar / vaqtlar
    api("PUT", "/api/admin/settings/quarters", {"quarters": [
        {"quarter": q, "startDate": s, "endDate": e, "gradesOpen": q == 1}
        for q, s, e in QUARTERS
    ]})
    api("PUT", "/api/admin/settings/lesson-times", {"lessonTimes": [
        {"period": p,
         "startTime": f"{8 + (p - 1) * 45 // 60:02d}:{(30 + (p - 1) * 45) % 60:02d}",
         "endTime":   f"{8 + ((p - 1) * 45 + 40) // 60:02d}:{(30 + (p - 1) * 45 + 40) % 60:02d}"}
        for p in range(1, 11)
    ]})
    api("PUT", "/api/admin/settings/absence-reasons", {"absenceReasons": [
        {"id": "", "name": n, "short": s, "isLate": late} for n, s, late in ABSENCE_REASONS
    ]})
    print(f"Choraklar: 4 · Dars vaqtlari: 10 · Davomat sabablari: {len(ABSENCE_REASONS)}")

    for d, name in HOLIDAYS:
        api("PUT", "/api/admin/holidays", {"date": d, "name": name})
    print(f"Bayram kunlari: {len(HOLIDAYS)}")

    # 3 --------------------------------------------------------------- fanlar
    subjects = {}
    for name in SUBJECTS:
        rec = get_or_create("/api/admin/subjects", "/api/admin/subjects",
                            {"name": name}, name, "name")
        if rec:
            subjects[name] = rec["id"]
    print(f"Fanlar: {len(subjects)}")

    # 4 --------------------------------------------------------------- sinflar
    classes = {}
    for name, grade, lang, fee in CLASSES:
        rec = get_or_create("/api/admin/classes", "/api/admin/classes",
                            {"name": name, "grade": grade, "language": lang,
                             "monthlyFee": fee, "room": f"{grade}0{name[-1]}-xona"},
                            name, "name")
        if rec:
            classes[name] = rec["id"]
    print(f"Sinflar: {len(classes)}")

    # 5 ---------------------------------------------------------- o'qituvchilar
    subject_ids = [subjects[s] for s in SUBJECTS]
    teachers = {}
    for i, (full, gender, birth, cat, salary) in enumerate(TEACHERS):
        rec = get_or_create("/api/admin/teachers", "/api/admin/teachers", {
            "fullName": full, "birthDate": birth,
            "address": "Toshkent sh., Yunusobod tumani",
            "gender": gender,
            "homeroomClass": CLASSES[i][0],
            "subjectIds": [subject_ids[i]],
            "salary": salary,
            "salaryStartMonth": "2026-09",
            "newPassword": DEMO_PASSWORD,
            "permissions": ["journal", "assignments", "schedule", "messages", "salary"],
            "phone": f"+998 90 {100 + i:03d} {10 + i:02d} {20 + i:02d}",
            "category": cat,
            "salaryStartDate": "2026-09-01",
        }, full, "fullName")
        if rec:
            teachers[full] = rec["id"]
            api("PUT", f"/api/admin/teachers/{rec['id']}", {
                "fullName": full, "birthDate": birth,
                "address": "Toshkent sh., Yunusobod tumani", "gender": gender,
                "homeroomClass": CLASSES[i][0], "subjectIds": [subject_ids[i]],
                "salary": salary, "salaryStartMonth": "2026-09",
                "newPassword": DEMO_PASSWORD,
                "permissions": ["journal", "assignments", "schedule", "messages", "salary"],
                "phone": f"+998 90 {100 + i:03d} {10 + i:02d} {20 + i:02d}",
                "category": cat, "salaryStartDate": "2026-09-01",
            }, quiet=True)
    print(f"O'qituvchilar: {len(teachers)}  (parol: {DEMO_PASSWORD})")

    # 6 ------------------------------------------------------------ o'quvchilar
    students = []
    for i, (full, gender, birth, parent, phone) in enumerate(STUDENTS):
        rec = get_or_create("/api/admin/students", "/api/admin/students", {
            "fullName": full, "birthDate": birth,
            "address": "Toshkent sh., Yunusobod tumani",
            "gender": gender,
            "parentFullName": parent, "parentPhone": phone,
            "className": "1-A", "enrollmentDate": "2026-09-01",
            "newPassword": DEMO_PASSWORD,
            "discountPct": 10 if i in (2, 7) else 0,
            "discountAmount": 200_000 if i == 5 else 0,
            "discountNote": "Ko'p bolali oila" if i in (2, 5, 7) else "",
            "subGroup": 1 if i % 2 == 0 else 2,
        }, full, "fullName")
        if rec:
            students.append(rec)
            api("PUT", f"/api/admin/students/{rec['id']}", {
                "fullName": full, "birthDate": birth,
                "address": "Toshkent sh., Yunusobod tumani", "gender": gender,
                "parentFullName": parent, "parentPhone": phone,
                "className": "1-A", "enrollmentDate": "2026-09-01",
                "newPassword": DEMO_PASSWORD,
                "discountPct": 10 if i in (2, 7) else 0,
                "discountAmount": 200_000 if i == 5 else 0,
                "discountNote": "Ko'p bolali oila" if i in (2, 5, 7) else "",
                "subGroup": 1 if i % 2 == 0 else 2,
            }, quiet=True)
    print(f"O'quvchilar: {len(students)}  (barchasi 1-A sinfda, parol: {DEMO_PASSWORD})")

    # 7 ---------------------------------------------------------------- xodimlar
    for full, position in STAFF:
        get_or_create("/api/admin/staff", "/api/admin/staff",
                      {"fullName": full, "position": position, "newPassword": DEMO_PASSWORD},
                      full, "fullName")
    print(f"Xodimlar: {len(STAFF)}")

    # 8 ------------------------------------------------------------------ lidlar
    stages = {}
    for i, (title, color) in enumerate(LEAD_STAGES):
        rec = get_or_create("/api/admin/lead-stages", "/api/admin/lead-stages",
                            {"title": title, "color": color}, title, "title", "name")
        if rec:
            stages[i + 1] = rec["id"]
    for full, gender, birth, parent, phone, stage_no, note in LEADS:
        get_or_create("/api/admin/leads", "/api/admin/leads", {
            "fullName": full, "gender": gender, "birthDate": birth,
            "parentFullName": parent, "parentPhone": phone,
            "targetGrade": max(1, TODAY.year - int(birth[:4]) - 6),
            "note": note, "stage": stages.get(stage_no, ""),
        }, full, "fullName")
    print(f"Lid bosqichlari: {len(LEAD_STAGES)} · Lidlar: {len(LEADS)}")

    # 9 ------------------------------------------------------------- intizom
    reasons = {}
    for name, pts in DISCIPLINE_REASONS:
        rec = get_or_create("/api/admin/discipline/reasons", "/api/admin/discipline/reasons",
                            {"name": name, "points": pts}, name, "name")
        if rec:
            reasons[name] = rec["id"]
    have_points = len(api("GET", f"/api/admin/discipline/points?studentId={students[0]['id']}",
                          quiet=True) or []) if students else 0
    if students and reasons and have_points == 0:
        rids = list(reasons.values())
        for i in range(10):
            api("POST", "/api/admin/discipline/points", {
                "studentId": students[i % len(students)]["id"],
                "reasonId": rids[i % len(rids)],
                "note": "Namunaviy yozuv",
            }, quiet=True)
    print(f"Intizom sabablari: {len(reasons)} · Ball yozuvlari: 10")

    # 10 --------------------------------------------------------- feedback turlari
    etypes = []
    for name, desc in EVALUATION_TYPES:
        rec = get_or_create("/api/admin/student-evaluation/types",
                            "/api/admin/student-evaluation/types",
                            {"name": name, "description": desc}, name, "name")
        if rec:
            etypes.append(rec["id"])
    n_eval = 0
    for st in students:
        for tid in etypes[:5]:
            api("POST", "/api/admin/student-evaluation/grade", {
                "studentId": st["id"], "typeId": tid, "month": "2026-09",
                "week": 1, "score": random.randint(3, 5),
            }, quiet=True)
            if LAST_OK:
                n_eval += 1
    print(f"Feedback turlari: {len(etypes)} · Qo'yilgan baholar: {n_eval}")

    # 11 ------------------------------------------------------------ dars jadvali
    cls_1a = classes.get("1-A")

    def week_table(shift):
        """(kun, dars) -> (fan, o'qituvchi). Har sinf uchun surilgan — o'qituvchi mojarosi bo'lmaydi."""
        t = {}
        for day in range(1, 7):
            for period in range(1, 6):
                idx = ((day - 1) * 5 + period - 1 + shift) % 10
                t[(day, period)] = (SUBJECTS[idx], TEACHERS[idx][0])
        return t

    timetable = week_table(0)                       # 1-A — jurnal shu jadval bo'yicha
    n_slots = 0
    for c, (cname, _g, _l, _f) in enumerate(CLASSES):
        cid = classes.get(cname)
        if not cid:
            continue
        tpls = api("GET", f"/api/admin/classes/{cid}/schedule-templates") or []
        tpl = find(tpls, "Asosiy jadval", "name")
        if not tpl:
            api("POST", f"/api/admin/classes/{cid}/schedule-templates", {"name": "Asosiy jadval"})
            tpls = api("GET", f"/api/admin/classes/{cid}/schedule-templates") or []
            tpl = find(tpls, "Asosiy jadval", "name")
        if not tpl:
            continue
        for (day, period), (subj, teach) in week_table(c).items():
            api("PUT", f"/api/admin/classes/{cid}/schedule-templates/{tpl['id']}/{day}/{period}", {
                "day": day, "period": period,
                "subjectId": subjects[subj], "teacherId": teachers[teach], "subGroup": 0,
            }, quiet=True)
            if LAST_OK:
                n_slots += 1
        # Jadvalni chorak haftalariga biriktirish — busiz "Bugungi dars jadvali" bo'sh turadi.
        api("PUT", f"/api/admin/classes/{cid}/week-assignments", {
            "quarter": 1,
            "assignments": [{"week": w, "templateId": tpl["id"]} for w in range(1, 13)],
        }, quiet=True)
    print(f"Dars jadvali: {len(CLASSES)} sinf × 30 dars = {n_slots} ta katak, 1-chorak haftalariga biriktirildi")

    # 12 ------------------------------------------------------------------ jurnal
    days = school_days(date(2026, 9, 1), TODAY)
    rnd = random.Random(7)           # jurnal uchun alohida, barqaror tasodif
    n_notes = n_marks = 0
    if cls_1a and students:
        for d in days:
            wd = d.weekday() + 1                     # 1 = Monday
            for period in range(1, 6):
                subj, teach = timetable[(wd, period)]
                sid = subjects[subj]
                api("PUT", "/api/admin/journal/notes", {
                    "classId": cls_1a, "subjectId": sid, "quarter": 1,
                    "date": d.isoformat(), "period": period,
                    "topic": f"{subj}: {TOPIC_TITLES[(period - 1) % len(TOPIC_TITLES)]}",
                    "homework": "Darslikdan mashqlarni bajarish",
                    "conducted": True, "subGroup": 0,
                }, quiet=True)
                n_notes += 1
                for st in rnd.sample(students, 6):
                    absent = rnd.random() < 0.06
                    api("PUT", "/api/admin/journal", {
                        "classId": cls_1a, "subjectId": sid, "quarter": 1,
                        "studentId": st["id"], "date": d.isoformat(), "period": period,
                        "grade": None if absent else rnd.choice([3, 4, 4, 5, 5, 5]),
                        "reasonId": None,
                        "homework": rnd.choice([0, 1, 1]),
                        "behavior": rnd.choice([0, 0, 1]),
                    }, quiet=True)
                    n_marks += 1
    print(f"Jurnal: {len(days)} o'quv kuni · {n_notes} dars mavzusi · {n_marks} baho/belgi")

    # 13 ------------------------------------------------------------ topshiriqlar
    formats = ["written", "file", "test", "video"]
    have_asg = api("GET", "/api/admin/assignments") or []
    n_asg = 0
    for i, subj in enumerate(SUBJECTS):
        if find(have_asg, f"{subj} — {i + 1}-topshiriq", "title"):
            continue
        body = {
            "subjectId": subjects[subj],
            "title": f"{subj} — {i + 1}-topshiriq",
            "description": "Namunaviy topshiriq: mavzu bo'yicha vazifalarni bajaring.",
            "format": formats[i % 4],
            "classIds": [cls_1a] if cls_1a else [],
            "startDate": (TODAY - timedelta(days=3)).isoformat(),
            "dueDate": (TODAY + timedelta(days=4 + i)).isoformat(),
            "lateAccept": True, "latePenaltyPct": 10, "maxScore": 100,
            "autoGrade": formats[i % 4] == "test",
            "materials": [],
            "questions": [] if formats[i % 4] != "test" else [
                {"text": "2 + 2 nechchi?", "options": ["3", "4", "5"], "correctIndex": 1},
                {"text": "Haftada nechta kun bor?", "options": ["5", "6", "7"], "correctIndex": 2},
            ],
        }
        api("POST", "/api/admin/assignments", body, quiet=True)
        if LAST_OK:
            n_asg += 1
    print(f"Topshiriqlar: {len(have_asg) + n_asg} (yangi: {n_asg})")

    # 14 ------------------------------------------------------------------- LMS
    n_topics = 0
    existing_lms = api("GET", "/api/admin/lms/subjects") or []
    for subj in SUBJECTS:
        title = f"{subj} — mustaqil ta'lim"
        rec = find(existing_lms, title, "title")
        if not rec:
            api("POST", "/api/admin/lms/subjects", {
                "classId": cls_1a, "title": title,
                "description": f"{subj} fanidan video va matnli darslar",
                "unlockMode": "sequential", "batchSize": 3,
            }, quiet=True)
            rec = find(api("GET", "/api/admin/lms/subjects") or [], title, "title")
        if not rec:
            continue
        mods = api("GET", f"/api/admin/lms/subjects/{rec['id']}/modules") or []
        mod = find(mods, "1-modul", "title")
        if not mod:
            api("POST", f"/api/admin/lms/subjects/{rec['id']}/modules",
                {"title": "1-modul", "description": "Boshlang'ich mavzular"}, quiet=True)
            mods = api("GET", f"/api/admin/lms/subjects/{rec['id']}/modules") or []
            mod = find(mods, "1-modul", "title")
        if not mod:
            continue
        tops = api("GET", f"/api/admin/lms/modules/{mod['id']}/topics") or []
        for k in range(3):
            t = f"{k + 1}-mavzu: {TOPIC_TITLES[k]}"
            if find(tops, t, "title"):
                continue
            if api("POST", f"/api/admin/lms/modules/{mod['id']}/topics", {
                "title": t, "description": "Namunaviy mavzu tavsifi",
                "videoUrl": "", "textContent": "Mavzu matni shu yerda bo'ladi.",
                "materials": [],
            }, quiet=True) is not None:
                n_topics += 1
    print(f"LMS: 10 fan · 10 modul · {n_topics} mavzu")

    # 15 ------------------------------------------------------------------ moliya
    seeded_fin = find(api("GET", "/api/admin/finance/transactions") or [],
                      FINANCE[0][3], "note")
    for i, (direction, cat, amount, note) in enumerate([] if seeded_fin else FINANCE):
        api("POST", "/api/admin/finance/transactions", {
            "date": (TODAY - timedelta(days=i * 2)).isoformat(),
            "direction": direction, "category": cat, "amount": amount,
            "note": note, "studentId": None, "teacherId": None,
        }, quiet=True)
    api("POST", "/api/admin/finance/accrue?month=2026-09")
    for i, st in enumerate([] if seeded_fin else students):
        api("POST", f"/api/admin/students/{st['id']}/payments",
            {"amount": [1_800_000, 1_800_000, 900_000, 1_800_000, 1_620_000,
                        1_600_000, 1_800_000, 0, 1_800_000, 450_000][i],
             "month": "2026-09"}, quiet=True)
    for name, tid in ([] if seeded_fin else list(teachers.items())[:10]):
        api("POST", f"/api/admin/teachers/{tid}/salary-payments",
            {"amount": 3_000_000, "note": "Sentabr — avans"}, quiet=True)
    print("Moliya: " + ("allaqachon kiritilgan (o'tkazib yuborildi)" if seeded_fin
          else f"{len(FINANCE)} tranzaksiya · oylik hisob · {len(students)} to'lov · 10 maosh avansi"))

    # 16 ----------------------------------------------------------------- oshxona
    meals = ["breakfast", "lunch", "dinner"]
    for i, (name, ing) in enumerate(DISHES):
        d = (TODAY + timedelta(days=i // 3)).isoformat()
        meal = meals[i % 3]
        day = api("GET", f"/api/admin/canteen/{d}", quiet=True) or {}
        if find((day.get("meals") or {}).get(meal) or [], name, "name"):
            continue
        api("POST", f"/api/admin/canteen/{d}/{meal}",
            {"name": name, "ingredients": ing, "imageUrl": None}, quiet=True)
    print(f"Oshxona: {len(DISHES)} taom (bugundan boshlab)")

    # 17 -------------------------------------------------- filial / avtobus / kamera
    for name, addr, lat, lon, rad in BRANCHES:
        get_or_create("/api/admin/branches", "/api/admin/branches",
                      {"name": name, "address": addr, "latitude": lat,
                       "longitude": lon, "radiusMeters": rad}, name, "name")
    have_buses = [b.get("bus", b) for b in (api("GET", "/api/admin/gps/buses") or [])]
    for j, (name, plate, driver, phone, route) in enumerate(BUSES):
        if find(have_buses, name, "name"):
            continue
        api("POST", "/api/admin/gps/buses",
            {"name": name, "plateNumber": plate, "driverName": driver,
             "driverPhone": phone, "deviceId": f"IMEI8600000008{500 + j:04d}",
             "route": route, "isActive": True, "note": None}, quiet=True)
    for i, (name, loc) in enumerate(CAMERAS):
        get_or_create("/api/admin/cameras", "/api/admin/cameras",
                      {"name": name, "location": loc,
                       "rtspUrl": f"rtsp://192.168.1.{20 + i}:554/stream1",
                       "rtspSubUrl": f"rtsp://192.168.1.{20 + i}:554/stream2",
                       "retentionDays": 7, "isActive": True, "note": None}, name, "name")
    print(f"Filiallar: {len(BRANCHES)} · Avtobuslar: {len(BUSES)} · Kameralar: {len(CAMERAS)}")

    # 18 ------------------------------------------------------------------ xabarlar
    have_chat = api("GET", "/api/admin/messages/chat/1-A") or []
    if len(have_chat) < len(CHAT_MESSAGES):
        for msg in CHAT_MESSAGES:
            api("POST", "/api/admin/messages/chat/1-A", {"text": msg}, quiet=True)
    print(f"Sinf chati: {len(CHAT_MESSAGES)} xabar (1-A)")

    # 19 ------------------------------------- taklif/shikoyat va pickup (o'quvchi tomonidan)
    seed_student_side(students)

    # 20 ------------------------------------------------- login/parollarni faylga yozish
    write_credentials(students, teachers)

    print(f"\nTayyor. {BASE} — {args.user} / {args.password}")
    print(f"Qolgan barcha demo akkauntlar paroli: {DEMO_PASSWORD}")


if __name__ == "__main__":
    main()
