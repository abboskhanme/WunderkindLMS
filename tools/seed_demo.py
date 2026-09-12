#!/usr/bin/env python3
"""
Wunderkind International School — demo data seeder.

Fills every admin section with realistic sample data through the public API,
so the system can be demonstrated end to end. Re-runnable: existing records are
reused (matched by name), never duplicated.

    python3 tools/seed_demo.py [--base http://localhost:8080] [--user admin] [--password Admin123!]

Counts: 10 per kind where 10 makes sense; natural counts elsewhere
(4 quarters, 6 absence reasons, 3 branches, 5 buses).

O'quvchilar HAR SINFGA 10 tadan yoziladi (10 sinf = 100 o'quvchi): har o'qituvchi
o'z sinf rahbarligi, jurnali, progressi va topshiriqlarini to'la ko'rishi kerak.
Topshiriq va o'qituvchi chat xabarlari o'qituvchi akkaunti orqali, taklif/pickup/LMS
tugatish esa o'quvchi akkaunti orqali yoziladi — chunki bu ro'yxatlar egasi bo'yicha
filtrlanadi (admin yozganini o'qituvchi ilovasi ko'rmaydi).
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
LAST_STATUS = 0


def api(method, path, body=None, quiet=False, form=False):
    global LAST_OK, LAST_STATUS
    LAST_OK = False
    LAST_STATUS = 0
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
            LAST_STATUS = r.status
            LAST_OK = 200 <= r.status < 300
            return json.loads(raw) if raw else None
    except urllib.error.HTTPError as e:
        LAST_STATUS = e.code
        detail = e.read().decode(errors="replace")[:300]
        if not quiet:
            print(f"  !! {method} {path} -> {e.code} {detail}")
        return None


def demo_login(login, password=None):
    """
    Demo akkaunt tokeni (topilmasa None). Login endpointi IP bo'yicha daqiqasiga 10 ta
    so'rovga cheklangan — seeder o'nlab akkauntga kirgani uchun 429 bo'lsa oyna
    yangilanishini kutib qayta uriniladi. Boshqa xatoda (noto'g'ri parol) kutilmaydi.
    """
    if not login:
        return None
    for attempt in range(6):
        res = api("POST", "/api/auth/login",
                  {"email": login, "password": password or DEMO_PASSWORD}, quiet=True)
        if res and "token" in res:
            return res["token"]
        if LAST_STATUS != 429:
            return None
        if attempt < 5:
            time.sleep(12)
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


def index_by(list_path, key="fullName"):
    """Ro'yxatni BIR marta o'qib nom -> yozuv lug'atiga aylantiradi (yuzlab yozuv uchun)."""
    return {str(r.get(key, "")).strip().lower(): r for r in (api("GET", list_path) or [])}


def path_seg(part):
    """Yo'l bo'lagini xavfsiz kodlash (sinf nomlari va xizmat kanallari uchun)."""
    return urllib.parse.quote(str(part), safe="")


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

# Toifa bo'yicha bir soat dars narxi (so'm). Busiz o'qituvchi ilovasidagi "Maosh"
# bo'limi 0 ko'rsatadi — oylik dars jadvali × shu narxdan hisoblanadi.
SALARY_RATES = {"oliy": 62_000, "t1": 54_000, "t2": 47_000, "mutaxasis": 40_000}

# 1-A sinfi ro'yxati qo'lda yozilgan (eski demo shu nomlarga tayangan).
# Qolgan sinflar FAMILIES/…_GIVEN dan barqaror tasodif bilan to'ldiriladi.
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

SIBLING_FAMILIES = 5          # nechta ota-ona bir nechta farzandli bo'sin (Mini App almashtirgichi uchun)
STUDENTS_PER_CLASS = 10

# Ikki farzandli oila — Telegram Mini App'dagi "farzandni almashtirish" tugmasining
# yagona demo tayanchi (SPEC §6 Faza 3 qabul mezoni). Ikkala farzand ATAYLAB har xil
# sinfda: bitta sinfdagi aka-uka jadval, jurnal va oshxonani bir xil ko'rsatib,
# almashtirgich ishlayotganini isbotlamasdi.
#
# Bu bolalar sinfga QO'SHILMAYDI — har sinfning OXIRGI generatsiya qilingan qatorini
# almashtiradi, ya'ni sinf hajmi baribir 10 ta bo'lib qoladi va boshqa hech qanday
# hisob (progress, reyting, moliya) siljimaydi. 1-A tegilmaydi: uning ro'yxati qo'lda
# yozilgan va eski skript/skrinshotlar shu nomlarga tayanadi.
SHARED_PARENT = ("Sultonov Jahongir Baxtiyorovich", "+998 90 777 66 55")
SHARED_CHILDREN = {
    "2-A": ("Sultonov Amirbek Jahongirovich", "male", "2018-08-08"),
    "6-A": ("Sultonova Nilufar Jahongir qizi", "female", "2014-03-21"),
}

# Demo Telegram id'lari. Telegram haqiqiy foydalanuvchilarga ~10^10 gacha id beradi,
# shuning uchun 10^12 dan yuqorisi HECH QACHON haqiqiy akkaunt bilan to'qnashmaydi —
# bu qiymatlar ochiq-oydin sun'iy. Ko'rgazma tirik telefonga bog'liq bo'lmasligi uchun
# ular oldindan bog'lanadi (`POST /api/admin/telegram/links`).
DEMO_TG_PARENT = 999_000_000_001
DEMO_TG_TEACHER = 999_000_000_002

# (familiya o'zagi, otasining ismi) — familiya ayol uchun "-a" bilan, otasining ismi
# sharifga aylanadi: o'g'il "…ovich", qiz "… qizi".
FAMILIES = [
    ("Abdurahmonov", "Jamshid"), ("Axmedov", "Ravshan"),   ("Ashurov", "Qahramon"),
    ("Bekmurodov", "Ulug'bek"),  ("Boltayev", "Nodir"),    ("Burhonov", "Mansur"),
    ("Davlatov", "Shavkat"),     ("Ergashev", "Botir"),    ("Eshmatov", "Zafar"),
    ("Fozilov", "Muzaffar"),     ("G'afurov", "Sanjar"),   ("Halilov", "Ilhom"),
    ("Hasanov", "Nodirbek"),     ("Ibragimov", "Tohir"),   ("Inoyatov", "Bahodir"),
    ("Jalilov", "Xurshid"),      ("Kamolov", "Sardorbek"), ("Xolmatov", "Akmal"),
    ("Latipov", "Shuhrat"),      ("Mamatqulov", "Erkin"),  ("Mirzayev", "Davron"),
    ("Nabiyev", "Farrux"),       ("Normatov", "Bekzod"),   ("Olimov", "Sherzod"),
    ("Ostonov", "Gulom"),        ("Po'latov", "Islom"),    ("Qobilov", "Tolib"),
    ("Rashidov", "Alisher"),     ("Ruziyev", "Sobir"),     ("Saidov", "Jasur"),
    ("Salimov", "Baxrom"),       ("Tolipov", "Doniyor"),   ("Turg'unov", "Rustam"),
    ("Umarov", "Anvar"),         ("Usmonov", "Otabek"),    ("Vohidov", "Shermat"),
    ("Yoqubov", "Zohid"),        ("Zaripov", "Murod"),     ("Ziyodullayev", "Qodir"),
    ("Shomurodov", "Feruz"),
]

MALE_GIVEN = [
    "Amirbek", "Asilbek", "Azizbek", "Behruz", "Bilol", "Diyorbek", "Doston",
    "Eldor", "Elyor", "Firdavs", "Humoyun", "Ibrohim", "Jahongir", "Javohir",
    "Komronbek", "Mirjalol", "Muhammadali", "Nurbek", "Ozodbek", "Sanjarbek",
    "Shohjahon", "Temurbek", "Ulug'bek", "Yusufbek", "Zayniddin",
]

FEMALE_GIVEN = [
    "Aziza", "Barchinoy", "Dildora", "Dilnura", "Farangiz", "Gulbahor",
    "Iroda", "Kamola", "Laylo", "Madina", "Mohira", "Muslima", "Nafisa",
    "Nilufar", "Odina", "Ozoda", "Ra'no", "Sarvinoz", "Shahnoza", "Shohsanam",
    "Sitora", "Umida", "Yulduz", "Zarina", "Zebo",
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

ASSIGNMENT_TYPES = [
    "Uy vazifasi", "Nazorat ishi", "Mustaqil ish", "Loyiha", "Test",
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

# Chiqimlar — `POST /api/admin/expenses` uchun (P1-21).
#
# DIQQAT — TASDIQ CHEGARASI. `billing_settings.expense_approval_threshold`
# sukut bo'yicha 5 000 000 so'm; undan KATTA chiqim jurnalga darhol tushmaydi,
# direktor tasdig'ini kutadi (SPEC §4.5) va hisobotda ko'rinmaydi. Demo
# ma'lumot "bo'sh hisobot" bo'lib qolmasligi uchun katta summalar bo'laklarga
# bo'lingan: bu sun'iy emas — maktab ham elektr pulini bir chekda to'lamaydi.
#
# Maosh bu ro'yxatda YO'Q: u o'qituvchiga bog'lanishi kerak, shuning uchun
# `/api/admin/teachers/{id}/salary-payments` orqali kiritiladi (pastda).
EXPENSES = [
    ("utilities", 2_400_000, "Elektr — sentabr"),
    ("utilities", 1_900_000, "Suv va kanalizatsiya — sentabr"),
    ("utilities", 3_100_000, "Tabiiy gaz — sentabr"),
    ("supplies",  4_800_000, "Darsliklar (1-yarim yillik)"),
    ("supplies",  3_200_000, "Oshxona jihozlari"),
    ("supplies",  1_800_000, "Kanselyariya"),
    ("rent",      4_000_000, "Bino ijarasi — 1-to'lov"),
    ("rent",      4_000_000, "Bino ijarasi — 2-to'lov"),
    ("repair",    4_600_000, "Sinf xonalarini ta'mirlash"),
    ("other",     1_900_000, "Transport xarajatlari"),
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

# Xodimlar kanali (o'qituvchi + admin) — ChatService.StaffChannel bilan bir xil kalit.
STAFF_CHANNEL = "__xodimlar__"

STAFF_MESSAGES = [
    "Hurmatli hamkasblar, dushanba kuni pedkengash bo'ladi.",
    "Jurnal mavzularini har hafta yakunida to'ldirib boring.",
    "Chorak baholari 25-oktabrgacha kiritilishi kerak.",
    "Oshxona jadvali yangilandi — e'lonlar taxtasida.",
]

# Kassa smenasi va to'lovlar kassir nomidan yoziladi (SPEC §4.4: `cashier_id`
# JWT'dan olinadi, so'rov tanasidan EMAS). Akkaunt bo'lmasa to'lov bosqichi
# o'tkazib yuboriladi — qolgan hamma narsa baribir seed qilinadi.
CASHIER_LOGIN = "kassir"
CASHIER_PASSWORD = "Kassir@Wk2026!"

# Sinf rahbari o'z sinf chatiga yozadigan xabar (har o'qituvchiga bittadan).
TEACHER_CHAT_MESSAGES = [
    "Bugungi dars mavzusini daftarga yozib oling, ota-onalar nazorat qilsin.",
    "Ertangi darsga chizg'ich va transportir kerak bo'ladi.",
    "Uy vazifasini bajarmaganlar ro'yxatini kechqurun yuboraman.",
    "Sinf xonasini tozalash navbati 2-guruhda.",
    "Kelasi hafta og'zaki so'rov bo'ladi, tayyorgarlik ko'ring.",
    "Farzandingiz darsga kechikmasligini iltimos qilaman.",
    "Nazorat ishi natijalari jurnalga kiritildi.",
    "Sinf tadbiriga ota-onalarni ham kutamiz.",
    "Kitoblarni muqovalab olib kelishni unutmang.",
    "Savollaringizni shu yerda yozsangiz, kechqurun javob beraman.",
]

# O'qituvchi o'zi yaratadigan topshiriqlar (nom qo'shimchasi, format).
TEACHER_ASSIGNMENTS = [
    ("mavzu bo'yicha yozma ish", "written"),
    ("nazorat testi", "test"),
    ("amaliy uy vazifasi", "file"),
]

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

# Demo kalendar 2026/2027 o'quv yiliga bog'langan. "Bugun" — haqiqiy sana, lekin
# 1-chorak ichida ushlab turiladi: jurnal, progress va oshxona shu chorakka tayanadi.
Q1_START, Q1_END = date(2026, 9, 1), date(2026, 10, 30)
TODAY = min(max(date.today(), date(2026, 9, 9)), Q1_END)
DEMO_PASSWORD = "Demo123!"

# Dars kuni 08:30 da boshlanadi; har dars 40 daqiqa, tanaffus bilan qadam 45 daqiqa.
FIRST_LESSON_MIN = 8 * 60 + 30
LESSON_MIN = 40
LESSON_STEP_MIN = 45
PERIODS_PER_DAY = 10        # sozlamalardagi dars vaqtlari soni
LESSONS_PER_DAY = 5         # jadvalga haqiqatda qo'yiladigan darslar
SCHOOL_DAYS_PER_WEEK = 6    # dushanba–shanba


def lesson_time(period):
    """(boshlanish, tugash) "HH:MM" — yarim tundan boshlab DAQIQAda hisoblanadi.
    Soatni alohida hisoblash 08:30 siljishini yo'qotib, tugash vaqtini boshlanishdan
    oldinga tashlab yuborardi (08:30–08:10), shuning uchun bitta manbadan olinadi."""
    start = FIRST_LESSON_MIN + (period - 1) * LESSON_STEP_MIN
    end = start + LESSON_MIN
    return f"{start // 60:02d}:{start % 60:02d}", f"{end // 60:02d}:{end % 60:02d}"


def school_days(start, end):
    """Mon-Sat, excluding holiday dates."""
    holiday = {h[0] for h in HOLIDAYS}
    d, out = start, []
    while d <= end:
        if d.weekday() < SCHOOL_DAYS_PER_WEEK and d.isoformat() not in holiday:
            out.append(d)
        d += timedelta(days=1)
    return out


def week_table(shift):
    """
    (kun, dars) -> (fan, o'qituvchi). Kun 0 = dushanba … 5 = shanba — ScheduleLesson.Day
    bilan BIR XIL sanoq (backend kunni dushanbadan nolga hisoblaydi). Har sinf uchun
    jadval suriladi, shuning uchun bitta o'qituvchi bir vaqtda ikki sinfda turmaydi.
    """
    t = {}
    for day in range(SCHOOL_DAYS_PER_WEEK):
        for period in range(1, LESSONS_PER_DAY + 1):
            idx = (day * LESSONS_PER_DAY + period - 1 + shift) % len(SUBJECTS)
            t[(day, period)] = (SUBJECTS[idx], TEACHERS[idx][0])
    return t


def build_roster():
    """
    Sinf nomi -> o'quvchilar ro'yxati [(fish, jins, tug'ilgan sana, ota-ona, telefon)].
    Nomlar QAT'IY seed bilan yig'iladi: seeder qayta yurganda aynan o'sha ro'yxat chiqadi,
    shuning uchun nom bo'yicha solishtirish ishlaydi va dublikat yaratilmaydi.
    """
    rnd = random.Random(20260910)
    roster = {CLASSES[0][0]: list(STUDENTS)}
    used = {s[0] for s in STUDENTS}
    for cname, grade, _lang, _fee in CLASSES[1:]:
        rows = []
        while len(rows) < STUDENTS_PER_CLASS:
            stem, father = FAMILIES[rnd.randrange(len(FAMILIES))]
            female = len(rows) % 2 == 1
            given = rnd.choice(FEMALE_GIVEN if female else MALE_GIVEN)
            surname = (stem + "a") if female else stem
            patronymic = f"{father} qizi" if female else f"{father}ovich"
            full = f"{surname} {given} {patronymic}"
            month, day = rnd.randint(1, 12), rnd.randint(1, 28)
            if full in used:
                continue
            used.add(full)
            # 1-sinfga 7 yosh: 2026/2027 o'quv yilidan sinf darajasini ayiramiz.
            year = Q1_START.year - 6 - grade
            n = len(used)
            rows.append((
                full, "female" if female else "male", f"{year}-{month:02d}-{day:02d}",
                f"{stem} {father}",
                f"+998 9{n % 5} {100 + n:03d} {10 + (n * 3) % 80:02d} {10 + (n * 7) % 70:02d}",
            ))
        roster[cname] = rows

    # AKA-UKA, OPA-SINGIL. Yuqoridagi tsikl har o'quvchiga o'z telefonini beradi,
    # ya'ni har bir ota-ona bitta farzandli bo'lib chiqardi — Mini App'dagi
    # FARZAND ALMASHTIRGICHNI ko'rsatib bo'lmasdi.
    #
    # `GuardianSync.EnsureManyAsync` vasiylarni TELEFON kaliti bo'yicha
    # birlashtiradi, shuning uchun bir nechta o'quvchiga bitta ota-ona nomi va
    # bitta raqamni bersak — bazada bitta vasiy va bir nechta bog'lanish hosil
    # bo'ladi. Aynan shu ish qilinadi: har oilaning farzandlari HAR XIL sinfda,
    # chunki demo'da almashtirgich sinfni ham o'zgartirib ko'rsatishi kerak.
    _make_siblings(roster)
    return roster


def _make_siblings(roster):
    """
    Turli sinflardagi, AYNI OTA-ONA nomiga ega o'quvchilarni bitta oilaga
    biriktiradi (joyida o'zgartiradi).

    Nega ota-ona nomi bo'yicha: yuqoridagi generator familiyani ham, ota
    ismini ham bitta `FAMILIES` yozuvidan oladi, ya'ni ota-ona nomi bir xil
    chiqqan o'quvchilar allaqachon HAQIQIY aka-uka/opa-singil. Ularni shunchaki
    tasodifiy tanlash "Salimov Baxrom -> Abdullayev Amir" degan oilalarni
    yasardi — demo'da bu darrov ko'zga tashlanadi.
    """
    by_parent = {}
    for cname, rows in roster.items():
        for idx, (_full, _gender, _birth, parent, _phone) in enumerate(rows):
            by_parent.setdefault(parent, []).append((cname, idx))

    # Faqat HAR XIL sinfdagilar: almashtirgich sinfni ham o'zgartirib ko'rsatsin.
    candidates = []
    for parent, slots in sorted(by_parent.items()):
        seen, picked = set(), []
        for cname, idx in slots:
            if cname in seen:
                continue
            seen.add(cname)
            picked.append((cname, idx))
        if len(picked) > 1:
            candidates.append((parent, picked[:3]))

    for n, (parent, slots) in enumerate(candidates[:SIBLING_FAMILIES], start=1):
        phone = f"+998 90 777 {10 + n:02d} {n:02d}"
        for cname, idx in slots:
            full, gender, birth, _p, _ph = roster[cname][idx]
            roster[cname][idx] = (full, gender, birth, parent, phone)


# --------------------------------------------------------------------------- seed


def seed_teacher_side(teachers, classes, subjects, by_class):
    """
    Topshiriqlar va sinf chatidagi o'qituvchi xabari — O'QITUVCHI akkaunti orqali.
    /api/teacher/assignments ro'yxati CreatedByUserId bo'yicha filtrlanadi: admin
    yaratgan topshiriq o'qituvchi ilovasida KO'RINMAYDI, shuning uchun har o'qituvchi
    o'z topshirig'ini o'zi yozadi va natijalarini o'zi belgilaydi.
    """
    global TOKEN
    admin_token = TOKEN
    n_asg = n_sub = n_msg = 0
    for i, (full, _g, _b, _cat, _sal) in enumerate(TEACHERS):
        tid = teachers.get(full)
        if not tid:
            continue
        TOKEN = admin_token
        cred = api("GET", f"/api/admin/teachers/{tid}/credentials", quiet=True) or {}
        token = demo_login(cred.get("login"))
        if not token:
            print(f"  !! {full}: o'qituvchi akkauntiga kirib bo'lmadi (o'tkazib yuborildi)")
            continue
        TOKEN = token

        home = CLASSES[i][0]
        second = CLASSES[(i + 3) % len(CLASSES)][0]
        target = [classes[c] for c in dict.fromkeys([home, second]) if classes.get(c)]
        mine = api("GET", "/api/teacher/assignments", quiet=True) or []
        for j, (suffix, fmt) in enumerate(TEACHER_ASSIGNMENTS):
            title = f"{SUBJECTS[i]} — {suffix}"
            if find(mine, title, "title") or not target:
                continue
            api("POST", "/api/teacher/assignments", {
                "subjectId": subjects[SUBJECTS[i]],
                "title": title,
                "description": "Namunaviy topshiriq: mavzu bo'yicha vazifalarni bajaring.",
                "format": fmt,
                "classIds": target,
                "startDate": (TODAY - timedelta(days=5 - j)).isoformat(),
                "dueDate": (TODAY + timedelta(days=2 + j * 3)).isoformat(),
                "lateAccept": True, "latePenaltyPct": 10, "maxScore": 100,
                "autoGrade": fmt == "test",
                "materials": [],
                "questions": [] if fmt != "test" else [
                    {"text": "2 + 2 nechchi?", "options": ["3", "4", "5"], "correctIndex": 1},
                    {"text": "Haftada nechta dars kuni bor?", "options": ["5", "6", "7"], "correctIndex": 1},
                ],
            }, quiet=True)
            if LAST_OK:
                n_asg += 1

        # Natijalar bo'sh qolmasin: birinchi ikki topshiriqda o'quvchilarning uchdan
        # ikkisi bajargan (ball bilan), qolgani bajarmagan bo'lib turadi.
        mine = api("GET", "/api/teacher/assignments", quiet=True) or []
        for a in mine[:2]:
            res = api("GET", f"/api/teacher/assignments/{a['id']}/results", quiet=True) or {}
            for k, row in enumerate(res.get("rows") or []):
                if row.get("completed") or k % 3 == 2:
                    continue
                api("PUT", f"/api/teacher/assignments/{a['id']}/submissions/{row['studentId']}",
                    {"completed": True, "score": 60 + (k * 7) % 41}, quiet=True)
                if LAST_OK:
                    n_sub += 1

        # Sinf chatida oxirgi xabar sinf rahbaridan bo'lsin (ota-onalar shuni ko'radi).
        if by_class.get(home):
            text = TEACHER_CHAT_MESSAGES[i % len(TEACHER_CHAT_MESSAGES)]
            msgs = api("GET", f"/api/teacher/chat/{path_seg(home)}", quiet=True) or []
            if not any((m.get("text") or "") == text for m in msgs):
                api("POST", f"/api/teacher/chat/{path_seg(home)}", {"text": text}, quiet=True)
                if LAST_OK:
                    n_msg += 1

    TOKEN = admin_token
    print(f"O'qituvchi akkaunti orqali: {n_asg} topshiriq · {n_sub} bajarish belgisi · "
          f"{n_msg} chat xabari")


def seed_student_side(by_class, lms_topics):
    """
    Taklif/shikoyat, pickup va LMS mavzu tugatish — O'QUVCHI akkaunti orqali (real oqim).
    Har sinfdan bittadan o'quvchi olinadi: shunda har bir sinf rahbari o'z "Sinf rahbarligi"
    ekranida ota-ona kelganini, admin esa taklif/shikoyatlarni ko'radi.
    """
    global TOKEN
    admin_token = TOKEN
    picked = [(cname, rows[0]) for cname, rows in by_class.items() if rows]
    if not picked:
        return
    have_feedback = len(api("GET", "/api/admin/feedback", quiet=True) or []) > 0
    n_fb = n_pickup = n_lms = 0
    for i, (cname, st) in enumerate(picked):
        TOKEN = admin_token          # keyingi o'quvchi uchun admin huquqi kerak
        cred = api("GET", f"/api/admin/students/{st['id']}/credentials", quiet=True)
        token = demo_login((cred or {}).get("login"))
        if not token:
            continue
        TOKEN = token

        if not have_feedback and i < len(FEEDBACKS):
            ftype, text = FEEDBACKS[i]
            api("POST", "/api/student/feedback", {"type": ftype, "text": text},
                quiet=True, form=True)
            if LAST_OK:
                n_fb += 1

        # Har sinfda bittadan — har sinf rahbari "ota-onasi kelgan" holatini ko'rsin.
        # Pickup KUNLIK: ertaga ekran yana toza bo'ladi, demo uchun seeder qayta yuriladi.
        api("POST", "/api/student/pickup", {"studentId": None}, quiet=True)
        if LAST_OK:
            n_pickup += 1

        # LMS progress matritsasi bo'sh qolmasin — dastlabki mavzular tugatilgan bo'lsin.
        for topic_id in (lms_topics.get(cname) or [])[:3]:
            api("POST", f"/api/student/lms/topics/{topic_id}/complete", quiet=True)
            if LAST_OK:
                n_lms += 1

    TOKEN = admin_token
    # Pickup endpointi bugungi mavjud so'rovni qaytaradi (yangisini yaratmaydi), shuning
    # uchun bu yerda "nechta sinfda so'rov bor" sanaladi — yangi yozuvlar soni emas.
    print(f"O'quvchi akkaunti orqali: {n_fb} taklif/shikoyat · {n_pickup} sinfda pickup so'rovi · "
          f"{n_lms} tugatilgan LMS mavzu")


def write_credentials(by_class, teachers):
    """Demo akkauntlarni bitta faylga yozadi (parollar hali almashtirilmagan bo'lsa)."""
    lines = ["Wunderkind International School — demo akkauntlar",
             "=" * 52, "",
             "Admin:      admin / Admin123!", ""]
    lines.append("O'qituvchilar (parol: %s)" % DEMO_PASSWORD)
    for name, tid in teachers.items():
        c = api("GET", f"/api/admin/teachers/{tid}/credentials", quiet=True) or {}
        lines.append(f"  {c.get('login', '?'):<24} {name}")
    lines += ["", "O'quvchilar / ota-onalar (parol: %s)" % DEMO_PASSWORD]
    for cname, rows in by_class.items():
        lines.append(f"  --- {cname} ---")
        for st in rows:
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
        {"period": p, "startTime": lesson_time(p)[0], "endTime": lesson_time(p)[1]}
        for p in range(1, PERIODS_PER_DAY + 1)
    ]})
    # Sabab id'lari SAQLANADI: jurnal yozuvlari reasonId orqali bog'langan, yangi id
    # berilsa eski davomat yozuvlaridagi sabab ko'rinmay qoladi.
    old_reasons = (api("GET", "/api/admin/settings") or {}).get("absenceReasons") or []
    api("PUT", "/api/admin/settings/absence-reasons", {"absenceReasons": [
        {"id": (find(old_reasons, n, "name") or {}).get("id", ""),
         "name": n, "short": s, "isLate": late}
        for n, s, late in ABSENCE_REASONS
    ]})
    absence_reasons = (api("GET", "/api/admin/settings") or {}).get("absenceReasons") or []
    # Topshiriq turlari — bu yerda ham id saqlanadi (topshiriqlar TypeId orqali bog'langan).
    old_types = api("GET", "/api/admin/settings/assignment-types") or []
    api("PUT", "/api/admin/settings/assignment-types", {"types": [
        {"id": (find(old_types, n, "name") or {}).get("id", ""), "name": n}
        for n in ASSIGNMENT_TYPES
    ]})
    api("PUT", "/api/admin/salary-rates", SALARY_RATES)
    print(f"Choraklar: 4 · Dars vaqtlari: {PERIODS_PER_DAY} "
          f"(1-dars {lesson_time(1)[0]}–{lesson_time(1)[1]}) · "
          f"Davomat sabablari: {len(absence_reasons)} · "
          f"Topshiriq turlari: {len(ASSIGNMENT_TYPES)} · Toifa soat narxlari saqlandi")

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
    # Har sinfga 10 tadan — o'qituvchining sinf rahbarligi, jurnali va topshiriq
    # natijalari bo'sh qolmasligi uchun.
    #
    # Chegirma bu yerda YO'Q (P1-21): u `discounts` jadvalida va direktor
    # tasdig'ini talab qiladi — pastdagi "moliya" bo'limiga qarang.
    roster = build_roster()
    wanted = []                          # (sinf, FISH, payload)
    for cname, _grade, _lang, _fee in CLASSES:
        for i, (full, gender, birth, parent, phone) in enumerate(roster[cname]):
            wanted.append((cname, full, {
                "fullName": full, "birthDate": birth,
                "address": "Toshkent sh., Yunusobod tumani",
                "gender": gender,
                "parentFullName": parent, "parentPhone": phone,
                "className": cname, "enrollmentDate": "2026-09-01",
                "newPassword": DEMO_PASSWORD,
                "subGroup": 1 if i % 2 == 0 else 2,
            }))
    known = index_by("/api/admin/students")
    n_new = 0
    for _cname, full, payload in wanted:
        if full.strip().lower() in known:
            continue
        api("POST", "/api/admin/students", payload, quiet=True)
        n_new += 1
    if n_new:
        known = index_by("/api/admin/students")
    students, by_class = [], {}
    for cname, full, payload in wanted:
        rec = known.get(full.strip().lower())
        if not rec:
            continue
        # POST parol/guruh/chegirmani o'rnatmaydi — holatni PUT bilan tekislaymiz.
        api("PUT", f"/api/admin/students/{rec['id']}", payload, quiet=True)
        students.append(rec)
        by_class.setdefault(cname, []).append(rec)
    print(f"O'quvchilar: {len(students)} — {len(by_class)} sinfda "
          f"(yangi: {n_new}, parol: {DEMO_PASSWORD})")

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
    n_points = 0
    if reasons:
        rids = list(reasons.values())
        # Har sinfdan bittadan o'quvchi — ball nazorati bo'limi barcha sinfni ko'rsatsin.
        for i, rows in enumerate(by_class.values()):
            st = rows[i % len(rows)]
            if api("GET", f"/api/admin/discipline/points?studentId={st['id']}", quiet=True):
                continue
            api("POST", "/api/admin/discipline/points", {
                "studentId": st["id"], "reasonId": rids[i % len(rids)],
                "note": "Namunaviy yozuv",
            }, quiet=True)
            if LAST_OK:
                n_points += 1
    print(f"Intizom sabablari: {len(reasons)} · Yangi ball yozuvlari: {n_points}")

    # 10 --------------------------------------------------------- feedback turlari
    etypes = []
    for name, desc in EVALUATION_TYPES:
        rec = get_or_create("/api/admin/student-evaluation/types",
                            "/api/admin/student-evaluation/types",
                            {"name": name, "description": desc}, name, "name")
        if rec:
            etypes.append(rec["id"])
    # Baholash FAN kesimida saqlanadi: o'qituvchi ilovasidagi baholash jadvali
    # (evaluation/board) subjectId bo'yicha filtrlaydi — subjectsiz baho ko'rinmaydi.
    eval_rnd = random.Random(11)
    eval_month = TODAY.strftime("%Y-%m")
    n_eval = 0
    for ci, (cname, _g, _l, _f) in enumerate(CLASSES):
        subj_id = subjects[SUBJECTS[ci]]          # sinf rahbari o'qitadigan fan
        for st in by_class.get(cname, []):
            for tid in etypes[:5]:
                api("POST", "/api/admin/student-evaluation/grade", {
                    "studentId": st["id"], "typeId": tid,
                    "subjectId": subj_id, "classId": classes.get(cname),
                    "month": eval_month, "week": 1, "score": eval_rnd.randint(3, 5),
                }, quiet=True)
                if LAST_OK:
                    n_eval += 1
    print(f"Feedback turlari: {len(etypes)} · Qo'yilgan baholar: {n_eval} ({eval_month})")

    # 11 ------------------------------------------------------------ dars jadvali
    n_slots = n_stale = 0
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
        table = week_table(c)
        for (day, period), (subj, teach) in table.items():
            api("PUT", f"/api/admin/classes/{cid}/schedule-templates/{tpl['id']}/{day}/{period}", {
                "day": day, "period": period,
                "subjectId": subjects[subj], "teacherId": teachers[teach], "subGroup": 0,
            }, quiet=True)
            if LAST_OK:
                n_slots += 1
        # Jadvaldan tashqarida qolgan kataklarni tozalaymiz — ilgari kun 1..6 deb
        # yozilgani uchun yakshanba (6) darslari osilib qolgan edi.
        fresh = find(api("GET", f"/api/admin/classes/{cid}/schedule-templates") or [],
                     "Asosiy jadval", "name") or {}
        for stale in {(l["day"], l["period"]) for l in (fresh.get("lessons") or [])} - set(table):
            api("DELETE",
                f"/api/admin/classes/{cid}/schedule-templates/{tpl['id']}/{stale[0]}/{stale[1]}",
                quiet=True)
            n_stale += 1
        # Jadvalni chorak haftalariga biriktirish — busiz "Bugungi dars jadvali" bo'sh turadi.
        api("PUT", f"/api/admin/classes/{cid}/week-assignments", {
            "quarter": 1,
            "assignments": [{"week": w, "templateId": tpl["id"]} for w in range(1, 13)],
        }, quiet=True)
    print(f"Dars jadvali: {len(CLASSES)} sinf × {SCHOOL_DAYS_PER_WEEK * LESSONS_PER_DAY} dars "
          f"= {n_slots} ta katak, 1-chorak haftalariga biriktirildi"
          + (f" (eskirgan {n_stale} katak tozalandi)" if n_stale else ""))

    # 12 ------------------------------------------------------------------ jurnal
    # HAR SINF uchun — o'qituvchining "Dars o'tilishi" progressi 0 dan chiqishi uchun
    # dars o'tilgani (LessonNote.Conducted) sinf jadvalidagi AYNI sana/darsga yozilishi shart.
    days = school_days(Q1_START, TODAY)
    rnd = random.Random(7)           # jurnal uchun alohida, barqaror tasodif
    # Kech qolish emas, haqiqiy yo'qlik sabablari (ekranda sabab qisqartmasi ko'rinadi).
    absent_reason_ids = [r["id"] for r in absence_reasons if not r.get("isLate")]
    n_notes = n_marks = n_absent = 0
    for c, (cname, _g, _l, _f) in enumerate(CLASSES):
        cid = classes.get(cname)
        rows = by_class.get(cname) or []
        if not cid or not rows:
            continue
        table = week_table(c)
        for d in days:
            wd = d.weekday()                         # 0 = dushanba — jadval bilan bir xil
            for period in range(1, LESSONS_PER_DAY + 1):
                subj, _teach = table[(wd, period)]
                sid = subjects[subj]
                api("PUT", "/api/admin/journal/notes", {
                    "classId": cid, "subjectId": sid, "quarter": 1,
                    "date": d.isoformat(), "period": period,
                    "topic": f"{subj}: {TOPIC_TITLES[(period - 1) % len(TOPIC_TITLES)]}",
                    "homework": "Darslikdan mashqlarni bajarish",
                    "conducted": True, "subGroup": 0,
                }, quiet=True)
                if LAST_OK:
                    n_notes += 1
                if period > 3:
                    continue                          # baho har darsda emas — kuniga 3 fandan
                for st in rnd.sample(rows, min(6, len(rows))):
                    # Tashlanma har doim olinadi — tasodif ketma-ketligi qayta yurishda bir xil qolsin.
                    absent = rnd.random() < 0.07 and bool(absent_reason_ids)
                    api("PUT", "/api/admin/journal", {
                        "classId": cid, "subjectId": sid, "quarter": 1,
                        "studentId": st["id"], "date": d.isoformat(), "period": period,
                        "grade": None if absent else rnd.choice([3, 4, 4, 5, 5, 5]),
                        "reasonId": rnd.choice(absent_reason_ids) if absent else None,
                        "homework": rnd.choice([0, 1, 1]),
                        "behavior": rnd.choice([0, 0, 1]),
                    }, quiet=True)
                    if LAST_OK:
                        n_marks += 1
                        n_absent += 1 if absent else 0
    print(f"Jurnal: {len(days)} o'quv kuni × {len(by_class)} sinf · {n_notes} o'tilgan dars · "
          f"{n_marks} baho/belgi (shundan {n_absent} sababli yo'qlik)")

    # 13 ------------------------------------------------------------ topshiriqlar
    # Admin nomidan — admin panelidagi "Topshiriqlar" bo'limi uchun. O'qituvchi
    # ilovasidagi ro'yxat esa 19-bosqichda o'qituvchining o'zi tomonidan to'ldiriladi.
    cls_1a = classes.get("1-A")
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
    print(f"Topshiriqlar (admin nomidan): {len(SUBJECTS)} (yangi: {n_asg}) · "
          f"jami bazada: {len(have_asg) + n_asg}")

    # 14 ------------------------------------------------------------------- LMS
    # 1-A da barcha 10 fan (eski demo shu sinfga qurilgan), qolgan sinflarda 3 tadan —
    # o'qituvchi ilovasidagi "Ta'lim" ekranining sinf filtri bo'sh qolmasligi uchun.
    lms_topics = {}
    n_lms_subjects = n_topics = 0
    existing_lms = api("GET", "/api/admin/lms/subjects") or []

    def lms_find(title, class_id):
        return next((s for s in existing_lms
                     if s.get("title") == title and s.get("classId") == class_id), None)

    for ci, (cname, _g, _l, _f) in enumerate(CLASSES):
        cid = classes.get(cname)
        if not cid:
            continue
        picks = SUBJECTS if ci == 0 else [SUBJECTS[(ci + k) % len(SUBJECTS)] for k in range(3)]
        for subj in picks:
            title = f"{subj} — mustaqil ta'lim"
            rec = lms_find(title, cid)
            if not rec:
                api("POST", "/api/admin/lms/subjects", {
                    "classId": cid, "title": title,
                    "description": f"{subj} fanidan video va matnli darslar",
                    "unlockMode": "sequential", "batchSize": 3,
                }, quiet=True)
                existing_lms = api("GET", "/api/admin/lms/subjects") or []
                rec = lms_find(title, cid)
                if rec:
                    n_lms_subjects += 1
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
                api("POST", f"/api/admin/lms/modules/{mod['id']}/topics", {
                    "title": t, "description": "Namunaviy mavzu tavsifi",
                    "videoUrl": "", "textContent": "Mavzu matni shu yerda bo'ladi.",
                    "materials": [],
                }, quiet=True)
                if LAST_OK:
                    n_topics += 1
            tops = api("GET", f"/api/admin/lms/modules/{mod['id']}/topics") or []
            lms_topics.setdefault(cname, []).extend(t["id"] for t in tops)
    print(f"LMS: {len(lms_topics)} sinfda material · yangi {n_lms_subjects} fan · "
          f"{n_topics} mavzu")

    # 15 ------------------------------------------------------------------ moliya
    #
    #  P1-21 dan keyingi model (SPEC §3.7). Eski yassi kassa kitobi o'chdi;
    #  pul endi to'rt bosqichdan o'tadi va HAR BIRI alohida jadval:
    #
    #      obuna  -> hisob-faktura -> (kassa smenasi) -> to'lov + taqsimot
    #
    #  Tartib MUHIM: obunasiz hisob-faktura yozilmaydi, hisob-fakturasiz
    #  to'lovni taqsimlab bo'lmaydi, ochiq smenasiz esa to'lov qabul
    #  qilinmaydi (SPEC §4.2). Shuning uchun quyidagi bloklar ketma-ket.
    seed_billing(by_class, teachers)

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
    # Har kanalga yetishmagan xabarlarnigina qo'shamiz (qayta yurishda takrorlanmaydi).
    n_chat = 0
    for cname in list(by_class) + [STAFF_CHANNEL]:
        wanted_msgs = STAFF_MESSAGES if cname == STAFF_CHANNEL else (
            CHAT_MESSAGES if cname == CLASSES[0][0] else CHAT_MESSAGES[:5])
        have = api("GET", f"/api/admin/messages/chat/{path_seg(cname)}", quiet=True) or []
        for msg in wanted_msgs[len(have):]:
            api("POST", f"/api/admin/messages/chat/{path_seg(cname)}", {"text": msg}, quiet=True)
            if LAST_OK:
                n_chat += 1
    print(f"Chat: {len(by_class)} sinf + xodimlar kanali · yangi {n_chat} xabar")

    # 19 --------------------------- topshiriq va chat (o'qituvchi akkaunti orqali)
    seed_teacher_side(teachers, classes, subjects, by_class)

    # 20 ------------------------------------- taklif/pickup/LMS (o'quvchi tomonidan)
    seed_student_side(by_class, lms_topics)

    # 21 ------------------------------------------------- login/parollarni faylga yozish
    write_credentials(by_class, teachers)

    print(f"\nTayyor. {BASE} — {args.user} / {args.password}")
    print(f"Qolgan barcha demo akkauntlar paroli: {DEMO_PASSWORD}")


# ---------------------------------------------------------------------------
#  Moliya (P1-21): obuna -> hisob-faktura -> smena -> to'lov -> chiqim -> maosh
# ---------------------------------------------------------------------------

def seed_billing(by_class, teachers):
    """Namunaviy moliya ma'lumoti — SPEC §3.7 modelida (P1-21).

    Ketma-ketlik: obuna -> hisob-faktura -> kassa smenasi -> to'lov + taqsimot,
    yonida chiqim va maosh. Har bosqich alohida jadval, va tartibni buzib
    bo'lmaydi: obunasiz hisob-faktura yozilmaydi, ochiq smenasiz to'lov qabul
    qilinmaydi (SPEC §4.2).

    QAYTA YURGIZISH XAVFSIZ. Obuna (o'quvchi, toifa) bo'yicha, hisob-faktura
    bazadagi unikal indeks bilan, chiqim va maosh esa (toifa, summa, izoh)
    bo'yicha tekshiriladi. Bu shunchaki qulaylik emas: pul yozuvini o'chirib
    bo'lmaydi (SPEC §4.1), ya'ni ikki marta yozilgan maoshni faqat storno
    bilan tuzatish mumkin bo'lardi.

    `by_class` — {sinf nomi: [o'quvchi, ...]}; narx sinfga bog'liq (CLASSES).
    """
    cats = {c["code"]: c for c in (api("GET", "/api/admin/billing/categories") or [])}
    if not cats:
        print("Moliya: to'lov toifalari topilmadi (migratsiya seed qilinmaganmi?)"
              " — o'tkazib yuborildi")
        return

    fee_by_class = {name: fee for name, _g, _l, fee in CLASSES}
    students = [st for rows in by_class.values() for st in rows]

    # --- obunalar: hammasi o'qiydi, yarmi avtobusdan, chorak qismi yotoqxonada ---
    #  O'qish narxi SINFNIKI — obuna aynan shu summani muzlatadi, keyin sinf
    #  narxi o'zgarsa ham obuna o'zgarmaydi (SPEC §3.7).
    have = api("GET", "/api/admin/billing/subscriptions") or []
    existing = {(x.get("studentId"), x.get("categoryId")) for x in have}
    n_sub = 0
    for cname, rows in by_class.items():
        tuition = fee_by_class.get(cname, 1_800_000)
        for i, st in enumerate(rows):
            plan = [("tuition", tuition)]
            if i % 2 == 0:
                plan.append(("bus", 450_000))
            if i % 4 == 1:
                plan.append(("dormitory", 1_200_000))
            for code, amount in plan:
                cat = cats.get(code)
                if not cat or (st["id"], cat["id"]) in existing:
                    continue
                api("POST", "/api/admin/billing/subscriptions", {
                    "studentId": st["id"], "categoryId": cat["id"],
                    "monthlyAmount": amount,
                    "detail": {"bus": "Yunusobod yo'nalishi",
                               "dormitory": "2-blok"}.get(code),
                    "startsOn": f"{TODAY:%Y-%m}-01", "endsOn": None,
                }, quiet=True)
                if LAST_OK:
                    n_sub += 1

    # --- chegirma: TASDIQ KUTIB turadigan holatda yaratiladi (SPEC §8.1 Q5) ---
    #
    #  Seed ularni TASDIQLAMAYDI va tasdiqlay olmaydi: `approved_by <> created_by`
    #  baza tekshiruvi ikkinchi, BOSHQA odamni talab qiladi, seed esa bitta
    #  akkaunt bilan ishlaydi. Bu kamchilik emas — demo ma'lumot direktorga
    #  "tasdiq kutmoqda" navbatini ko'rsatadi ("Moliya → Chegirmalar"), va
    #  tasdiqlanmagan chegirma hisob-fakturaga ta'sir qilmasligi ham ko'rinadi.
    have_disc = api("GET", "/api/admin/billing/discounts") or []
    with_disc = {d.get("studentId") for d in have_disc}
    n_disc = 0
    for i in (2, 5, 7, 23, 41):
        if i >= len(students):
            continue
        st = students[i]
        if st["id"] in with_disc:
            continue
        api("POST", "/api/admin/billing/discounts", {
            "studentId": st["id"], "categoryId": cats["tuition"]["id"],
            "percent": 0 if i == 5 else 10,
            "amount": 200_000 if i == 5 else 0,
            "reason": "Ko'p bolali oila", "startsOn": f"{TODAY:%Y-%m}-01", "endsOn": None,
        }, quiet=True)
        if LAST_OK:
            n_disc += 1

    # --- hisob-fakturalar: fon xizmatini kutmasdan darhol hisoblaymiz ---
    #  `BillingAccrualService` startupda va har 12 soatda yuradi; obuna hozir
    #  ochilgani uchun uni kutish demo bazani bo'sh qoldirardi. Idempotent.
    accrued = api("POST", "/api/admin/billing/accrual/run", None, quiet=True) or []
    n_inv = sum(r.get("created", 0) for r in accrued)

    # --- kassa: smena + to'lovlar ---
    #  To'lov KASSIR nomidan yuboriladi (SPEC §4.4: `cashier_id` JWT'dan,
    #  so'rov tanasidan EMAS). Kassir akkaunti bo'lmasa hisob-fakturalar
    #  qoladi — qarzdorlar hisoboti baribir to'ladi.
    paid = 0
    admin_token = TOKEN
    cashier = api("POST", "/api/auth/login",
                  {"email": CASHIER_LOGIN, "password": CASHIER_PASSWORD}, quiet=True)
    if cashier and "token" in cashier:
        globals()["TOKEN"] = cashier["token"]
        if not api("GET", "/api/cash/shifts/current", quiet=True):
            api("POST", "/api/cash/shifts/open", {"openingFloat": 0}, quiet=True)

        # To'lovi BOR o'quvchi ikkinchi marta to'lamaydi. Bu shunchaki
        # qulaylik emas: to'lov qatorini O'CHIRIB BO'LMAYDI (SPEC §4.1),
        # ya'ni skriptni ikki marta yurgizish qarzni asta-sekin nolga olib
        # borardi va "Qarzdorlar" ekrani demo bazada bo'shab qolardi.
        already_paid = {x.get("studentId") for x in (
            api("GET", "/api/billing/payments", quiet=True) or [])}

        methods = ["cash", "cash", "cash", "card", "transfer", "online"]
        # Hamma qarz to'liq to'lanmaydi — ATAYLAB: aks holda "Qarzdorlar" tabi
        # va yig'ilish foizi ekranlari demo bazada bo'sh chiqardi.
        shares = [1.0, 1.0, 0.5, 1.0, 0.9, 0.85, 1.0, 0.0, 1.0, 0.25]
        for i, st in enumerate(students):
            share = shares[i % len(shares)]
            if share == 0.0 or st["id"] in already_paid:
                continue
            # Katta "zond" summa: javobda har ochiq hisob-fakturaning TO'LIQ
            # qoldig'i keladi, keyin undan ulush olinadi.
            sug = api("GET", "/api/cash/payments/suggest-allocation"
                             f"?studentId={st['id']}&amount=100000000", quiet=True)
            rows = sug if isinstance(sug, list) else ((sug or {}).get("allocations") or [])
            allocs = [{"invoiceId": a["invoiceId"], "amount": round(a["suggested"] * share, 2)}
                      for a in rows if a.get("suggested", 0) > 0]
            allocs = [a for a in allocs if a["amount"] > 0]
            if not allocs:
                continue
            api("POST", "/api/cash/payments", {
                "studentId": st["id"],
                "amount": sum(a["amount"] for a in allocs),
                "method": methods[i % len(methods)],
                "note": f"{TODAY:%Y-%m} to'lovi",
                "allocations": allocs,
            }, quiet=True)
            if LAST_OK:
                paid += 1
        globals()["TOKEN"] = admin_token
    else:
        print(f"  kassir akkaunti yo'q ({CASHIER_LOGIN}) — to'lovlar o'tkazib yuborildi;"
              f" yaratish: tools/create_user.py --login {CASHIER_LOGIN} --role cashier")

    # --- chiqimlar ---
    #  Izoh ham kalitga kiradi: ikkita ijara to'lovi bir xil summada va faqat
    #  izohi bilan farq qiladi — usiz ulardan biri hech qachon yozilmasdi.
    have_exp = api("GET", "/api/admin/expenses") or []
    seen_exp = {(e.get("category"), e.get("amount"), e.get("note")) for e in have_exp}
    n_exp = 0
    for i, (cat, amount, note) in enumerate(EXPENSES):
        if (cat, float(amount), note) in seen_exp:
            continue
        api("POST", "/api/admin/expenses", {
            "onDate": (TODAY - timedelta(days=i)).isoformat(),
            "category": cat, "amount": amount, "method": "transfer", "note": note,
        }, quiet=True)
        if LAST_OK:
            n_exp += 1

    # --- maosh: `expenses` qatori + `expense:salary` jurnal juftligi ---
    #  Summa tasdiq chegarasidan (5 000 000) PAST — aks holda har maosh
    #  direktor tasdig'ini kutib turardi va hisobotda ko'rinmasdi (SPEC §4.5).
    salary_note = f"{TODAY:%Y-%m} — avans"
    paid_teachers = {e.get("teacherId") for e in (
        api("GET", "/api/admin/expenses?category=salary", quiet=True) or [])
        if e.get("note") == salary_note}
    n_sal = 0
    for tid in teachers.values():
        if tid in paid_teachers:
            continue
        api("POST", f"/api/admin/teachers/{tid}/salary-payments",
            {"amount": 3_000_000, "note": salary_note}, quiet=True)
        if LAST_OK:
            n_sal += 1

    print(f"Moliya: {n_sub} obuna · {n_disc} chegirma · {n_inv} hisob-faktura · "
          f"{paid} to'lov · {n_exp} chiqim · {n_sal} maosh")


if __name__ == "__main__":
    main()
