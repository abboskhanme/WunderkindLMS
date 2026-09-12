#!/usr/bin/env python3
"""
Namunaviy moliya ma'lumoti — obunalar, hisob-fakturalar, to'lovlar, xarajatlar.

Faza 1 moduli qurilgandan keyin "Pul aylanmasi" halqasi va moliyaviy hisobotlar
bo'sh turmasligi uchun. `seed_demo.py` dan keyin ishga tushiriladi.

    python3 tools/seed_billing.py                       # lokal
    python3 tools/seed_billing.py --base https://... --user superadmin --password '...'

Idempotent: mavjud obuna va to'lovlar takrorlanmaydi.
"""

import argparse
import json
import random
import sys
import urllib.error
import urllib.parse
import urllib.request
from datetime import date

random.seed(20260911)

BASE = "http://localhost:8080"
TOKEN = None
LAST_OK = False


def api(method, path, body=None, quiet=False, token=None):
    global LAST_OK
    LAST_OK = False
    req = urllib.request.Request(
        BASE + path,
        data=json.dumps(body).encode() if body is not None else None,
        method=method,
    )
    req.add_header("Content-Type", "application/json")
    t = token if token is not None else TOKEN
    if t:
        req.add_header("Authorization", "Bearer " + t)
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            raw = r.read()
            LAST_OK = 200 <= r.status < 300
            return json.loads(raw) if raw else None
    except urllib.error.HTTPError as e:
        if not quiet:
            print(f"  !! {method} {path} -> {e.code} {e.read().decode(errors='replace')[:200]}")
        return None


def login(user, password):
    res = api("POST", "/api/auth/login", {"email": user, "password": password})
    if not res or "token" not in res:
        sys.exit(f"Login muvaffaqiyatsiz: {user}")
    return res["token"]


def main():
    global BASE, TOKEN
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default=BASE)
    ap.add_argument("--user", default="admin")
    ap.add_argument("--password", default="Admin123!")
    ap.add_argument("--cashier", default="kassir")
    ap.add_argument("--cashier-password", default="Kassir@Wk2026!")
    args = ap.parse_args()
    BASE = args.base.rstrip("/")

    TOKEN = login(args.user, args.password)
    admin_token = TOKEN
    print(f"Kirildi: {args.user}")

    # ---------------------------------------------------------------- toifalar
    cats = {c["code"]: c for c in (api("GET", "/api/admin/billing/categories") or [])}
    if not cats:
        sys.exit("Toifalar topilmadi — migratsiya seed qilinmaganmi?")
    print(f"Toifalar: {', '.join(sorted(cats))}")

    students = api("GET", "/api/admin/students") or []
    if not students:
        sys.exit("O'quvchi yo'q — avval tools/seed_demo.py ni ishga tushiring")

    # ------------------------------------------------------------- obunalar
    # Hamma o'quvchi o'qiydi; yarmi avtobusdan, uchtasi yotoqxonada.
    have = api("GET", "/api/admin/billing/subscriptions") or []
    existing = {(s.get("studentId"), s.get("categoryCode") or s.get("categoryId")) for s in have}
    start = date(2026, 9, 1).isoformat()
    made = 0
    for i, st in enumerate(students):
        plan = [("tuition", 1_800_000)]
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
                "detail": {"bus": "Yunusobod yo'nalishi", "dormitory": "2-blok"}.get(code),
                "startsOn": start, "endsOn": None,
            }, quiet=True)
            if LAST_OK:
                made += 1
    print(f"Obunalar: {made} ta yangi")

    # --------------------------------------------------------- hisob-fakturalar
    # Accrual fon xizmati startupda va har 12 soatda ishlaydi; obuna hozir
    # ochilgani uchun uni kutmasdan qo'lda chaqiramiz (idempotent).
    api("POST", "/api/admin/billing/accrual/run?month=2026-09", None, quiet=True)
    inv = api("GET", "/api/admin/finance/debtors", quiet=True) or []
    print(f"Qarzdorlar ro'yxati: {len(inv)} o'quvchi")

    # ------------------------------------------------------------------ kassir
    cashier_token = None
    res = api("POST", "/api/auth/login",
              {"email": args.cashier, "password": args.cashier_password}, quiet=True)
    if res and "token" in res:
        cashier_token = res["token"]
        print(f"Kassir: {args.cashier}")
    else:
        print(f"Kassir akkaunti yo'q ({args.cashier}) — to'lovlar o'tkazib yuborildi.")
        print("  Yaratish: python3 tools/create_user.py --login kassir --role cashier ...")

    # ------------------------------------------------------------------ to'lovlar
    paid = 0
    if cashier_token:
        TOKEN = cashier_token
        cur = api("GET", "/api/cash/shifts/current", quiet=True)
        if not cur:
            api("POST", "/api/cash/shifts/open", {"openingFloat": 0}, quiet=True)
        methods = ["cash", "cash", "cash", "card", "transfer", "online"]
        # HAMMA qarz to'liq to'lanmaydi — ATAYLAB. Aks holda "Qarzdorlar" tabi,
        # yig'ilish foizi va nomuvofiqlik ekranlari demo bazada BO'SH chiqardi
        # va ularni ko'z bilan tekshirib bo'lmasdi. Ulush jadvali barqaror
        # (tasodifiy emas), ya'ni qayta yurgizishda o'sha manzara qaytadi.
        shares = [1.0, 1.0, 0.5, 1.0, 0.9, 0.8, 1.0, 0.0, 1.0, 0.25]
        for i, st in enumerate(students):
            share = shares[i % len(shares)]
            if share == 0.0:
                continue
            # Katta "zond" summasi bilan so'raymiz: javobda har bir ochiq
            # hisob-fakturaning to'liq qoldig'i keladi, keyin ulushini olamiz.
            sug = api("GET", f"/api/cash/payments/suggest-allocation?studentId={st['id']}&amount=100000000",
                      quiet=True)
            allocs = sug if isinstance(sug, list) else ((sug or {}).get("allocations") or [])
            if not allocs:
                continue
            # suggest-allocation qatorlari: invoiceId + suggested
            allocs = [{"invoiceId": a["invoiceId"], "amount": round(a["suggested"] * share, 2)}
                      for a in allocs if a.get("suggested", 0) > 0]
            allocs = [a for a in allocs if a["amount"] > 0]
            if not allocs:
                continue
            total = sum(a["amount"] for a in allocs)
            if total <= 0:
                continue
            api("POST", "/api/cash/payments", {
                "studentId": st["id"], "amount": total,
                "method": methods[i % len(methods)],
                "note": "Namunaviy to'lov",
                "allocations": allocs,
            }, quiet=True)
            if LAST_OK:
                paid += 1
        TOKEN = admin_token
    print(f"To'lovlar: {paid}")

    # ------------------------------------------------------------------ xarajat
    #
    #  P1-21: eski `/api/admin/finance/transactions` o'chirildi. Chiqim endi
    #  `POST /api/admin/expenses` orqali kiritiladi va DARHOL jurnalga tushadi
    #  (`debit expense:<toifa> / credit cash|bank`) — ya'ni P&L va pul aylanmasi
    #  uni ko'radi. Eski yo'lda chiqim jurnalga umuman tushmasdi.
    #
    #  SUMMALAR CHEGARADAN PAST. `billing_settings.expense_approval_threshold`
    #  sukut bo'yicha 5 000 000 so'm; undan katta chiqim direktor tasdig'igacha
    #  jurnalga TUSHMAYDI (SPEC §4.5) va hisobotda ko'rinmaydi. Shuning uchun
    #  yirik xarajatlar haqiqiy hayotdagi kabi bo'laklarga bo'lingan.
    #
    #  Maosh bu ro'yxatda YO'Q: u o'qituvchiga bog'lanishi kerak
    #  (`expenses.teacher_id`), buni `/api/admin/teachers/{id}/salary-payments`
    #  qiladi — `tools/seed_demo.py` da.
    exp = [("utilities", 2_400_000, "Elektr — sentabr"),
           ("utilities", 1_900_000, "Suv va kanalizatsiya — sentabr"),
           ("utilities", 3_100_000, "Tabiiy gaz — sentabr"),
           ("supplies", 4_800_000, "Darsliklar"),
           ("supplies", 3_200_000, "Kanselyariya"),
           ("rent", 4_000_000, "Bino ijarasi — 1-to'lov"),
           ("rent", 4_000_000, "Bino ijarasi — 2-to'lov"),
           ("repair", 4_600_000, "Sinf ta'miri")]
    have_exp = api("GET", "/api/admin/expenses", quiet=True) or []
    seen = {(e.get("category"), e.get("amount"), e.get("note")) for e in have_exp}
    n_exp = 0
    for i, (cat, amount, note) in enumerate(exp):
        if (cat, float(amount), note) in seen:
            continue
        api("POST", "/api/admin/expenses", {
            "onDate": date(2026, 9, 2 + i).isoformat(),
            "category": cat, "amount": amount, "method": "transfer", "note": note,
        }, quiet=True)
        if LAST_OK:
            n_exp += 1
    print(f"Xarajatlar: {n_exp} ta yangi")

    flow = api("GET", "/api/admin/finance/money-flow?from=2026-09-01&to=2026-09-30", quiet=True)
    if flow:
        hub = next((n for n in flow["nodes"] if n["kind"] == "hub"), None)
        print(f"\nPul aylanmasi: {len(flow['nodes'])} tugun, {len(flow['links'])} bog'lanish"
              + (f", aylanma = {hub['value']:,.0f} so'm".replace(",", " ") if hub else ""))
    print(f"\nTayyor. {BASE}/admin/finance/money-flow")


if __name__ == "__main__":
    main()
