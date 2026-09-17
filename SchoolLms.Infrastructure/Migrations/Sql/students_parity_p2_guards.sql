-- ===========================================================================
--  O'quv bo'limi pariteti (P2) — `app_rw` grantlari
--  Migratsiya: StudentsParityP2
--  Manba: docs/modules/students-parity.md §3.3 (Batch C)
-- ===========================================================================
--
--  BU FAYLNI TAHRIRLAMANG.
--  Migratsiya bir marta qo'llanadi va shundan keyin muzlaydi. O'zgartirish
--  kerak bo'lsa — YANGI migratsiya va yangi .sql fayl.
--  (Qoida `billing_guards.sql` dan olingan — P1-05.)
--
--  NEGA GRANT MIGRATSIYANING O'ZIDA
--  --------------------------------
--  Migratsiyani `schoollms_owner` bajaradi, ilova esa `app_rw` bilan ulanadi
--  va u hech narsaga EGA emas. Grant unutilsa ilova muammosiz ishga tushadi va
--  faqat BIRINCHI so'rovda SQLSTATE 42501 bilan yiqiladi — ya'ni xato ishlab
--  chiqishda emas, foydalanuvchida chiqadi. Shuning uchun grant jadval bilan
--  birga keladi. Rol hali yo'q bo'lsa (test harness'i) — jim o'tib ketamiz.
--  (`students_parity_p1_guards.sql` bilan AYNAN bir xil naqsh.)
--
--  NEGA BIRORTA `REVOKE` YO'Q — ATAYLAB
--  ------------------------------------
--  Uchala jadval ham moliyaviy EMAS: ichlarida summa ham, jurnal (ledger)
--  yozuvi ham, hisob-faktura yoki kassa smenasiga havola ham yo'q.
--    · `certificate_subjects` — hujjat ↔ fan bog'lanishi;
--    · `student_locations`    — xaritadagi nuqta va olib ketish vaqti;
--    · `user_table_settings`  — xodimning O'Z ekran ko'rinishi.
--  Uchalasini ham TUZATISH kerak bo'ladi (noto'g'ri fan tanlandi, uy
--  ko'chdi, ustun tartibi o'zgardi) — ya'ni bu yerda UPDATE va DELETE
--  funksiyaning O'ZI, teshik emas. SPEC §4.1 ning o'zgarmaslik qoidasi
--  PULGA tegishli; uni "muhim ko'ringan hamma narsaga" cho'zish qimmatga
--  tushardi. Uchalasi `deploy/init-roles.sql` §5 dagi ro'yxatga ham
--  kirmaydi.
--
--  YANGI USTUNLARGA ALOHIDA GRANT KERAK EMAS
--  -----------------------------------------
--  `classes.capacity`, `subjects.color`, `subjects.is_active`,
--  `students.target_grade`, `school_meta.contract_number_mode`,
--  `assignments.owner_kind` va `rooms.is_active` — MAVJUD jadvallarda.
--  PostgreSQL'da huquq JADVAL darajasida beriladi
--  (`GRANT ... ON <jadval>`), ya'ni keyin qo'shilgan ustun avtomatik
--  ravishda o'sha jadvalning mavjud huquqi ostiga tushadi
--  (`finance_parity_guards.sql` dagi A1 izohi). Yettala jadval ham
--  moliyaviy emas va `app_rw` da allaqachon to'liq CRUD ostida.
--
--  `cash_handovers` GA USTUN QO'SHILMADI
--  -------------------------------------
--  Vazifada "agar xavfsiz deb hisoblasang" degan shart bilan
--  `approved_by`/`approved_at` so'ralgandi. Qo'shilmadi — sabab
--  migratsiya faylining boshidagi izohda va hisobotda.
-- ===========================================================================

DO $guards$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_rw') THEN
        RAISE NOTICE '[students_parity_p2_guards] `app_rw` roli topilmadi — GRANT o''tkazib '
                     'yuborildi. deploy/init-roles.sql ni ishga tushiring.';
        RETURN;
    END IF;

    EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON '
            'public.certificate_subjects, public.student_locations, '
            'public.user_table_settings '
            'TO app_rw';
END
$guards$;
