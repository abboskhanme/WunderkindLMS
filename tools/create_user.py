#!/usr/bin/env python3
"""
Birinchi admin akkauntini yaratadi (toza bazada hech kim yo'q — kirish imkoni bo'lmaydi).

Bazaga (PostgreSQL) to'g'ridan-to'g'ri yozadi, chunki `superadmin`/`admin` rollari uchun API'da yaratish
endpointi yo'q (ilgari Control Plane provisioning qilardi, u olib tashlangan).

Ishlatish (server yoki lokal, docker konteyner nomlari bilan):

    python3 tools/create_user.py --login director --password 'Parol123!' \\
        --name "Direktor F.I.SH" --role admin

    # boshqa konteyner / parol bilan:
    python3 tools/create_user.py --login admin --password '...' --name "..." \\
        --role superadmin --container wunderkind-database --db-password '...'

Parol PBKDF2-SHA256 (100 000 iteratsiya) bilan hash qilinadi — serverdagi
PasswordHasher formati bilan bir xil: {iterations}.{saltBase64}.{hashBase64}
"""

import argparse
import base64
import hashlib
import os
import subprocess
import sys
import uuid

ITERATIONS = 100_000


def hash_password(password: str) -> str:
    salt = os.urandom(16)
    key = hashlib.pbkdf2_hmac("sha256", password.encode(), salt, ITERATIONS, 32)
    return f"{ITERATIONS}.{base64.b64encode(salt).decode()}.{base64.b64encode(key).decode()}"


def psql_query(container: str, db_password: str, database: str, query: str) -> str:
    cmd = [
        "docker", "exec", "-e", f"PGPASSWORD={db_password}", container,
        "psql", "-U", "schoollms", "-d", database, "-t", "-A", "-c", query,
    ]
    r = subprocess.run(cmd, capture_output=True, text=True)
    if r.returncode != 0:
        sys.exit(f"psql xatosi:\n{r.stdout}\n{r.stderr}")
    return r.stdout.strip()


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--login", required=True, help="Kirish nomi (username, pochta emas)")
    p.add_argument("--password", required=True)
    p.add_argument("--name", required=True, help="To'liq F.I.SH")
    p.add_argument("--role", default="admin",
                   choices=["superadmin", "admin", "cashier", "staff"])
    p.add_argument("--position", default="", help="Lavozim yorlig'i (staff uchun)")
    p.add_argument("--container", default="wunderkind-database")
    p.add_argument("--db-password", required=True)
    p.add_argument("--database", default="schoollms")
    args = p.parse_args()

    login = args.login.strip().lower()
    exists = psql_query(args.container, args.db_password, args.database,
                    f"SELECT COUNT(*) FROM users WHERE email = '{login}';")
    if exists.strip() != "0":
        print(f"'{login}' allaqachon mavjud — o'zgartirilmadi.")
        return

    # Permissions — PostgreSQL text[] massivi; bo'sh massiv literali '{}'.
    psql_query(args.container, args.db_password, args.database, f"""
        INSERT INTO users (id, full_name, role, email, avatar_url, password_hash,
                           first_login_at, last_login_at, permissions, position, initial_password)
        VALUES ('{uuid.uuid4()}', '{args.name.replace("'", "''")}', '{args.role}', '{login}', NULL,
                '{hash_password(args.password)}', NULL, NULL, '{{}}',
                '{args.position.replace("'", "''")}', '{args.password.replace("'", "''")}');
    """)
    print(f"Yaratildi: {login}  ({args.role})  —  {args.name}")


if __name__ == "__main__":
    main()
