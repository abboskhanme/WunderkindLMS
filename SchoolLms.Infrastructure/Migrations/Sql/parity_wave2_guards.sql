-- ===========================================================================
--  Ikkinchi to'lqin sxemasi — `app_rw` grantlari
--  Migratsiya: ParityWave2Schema
--  Manba: docs/modules/existing-module-gaps.md §2.2, §2.3, §3.5
-- ===========================================================================
--
--  BU FAYLNI TAHRIRLAMANG.
--  Migratsiya bir marta qo'llanadi va shundan keyin muzlaydi. O'zgartirish
--  kerak bo'lsa — YANGI migratsiya va yangi .sql fayl.
--  (Qoida `billing_guards.sql` dan aynan olingan — P1-05.)
--
--  NEGA MIGRATSIYADA, `deploy/init-roles.sql` KUTILMAYDI
--  ----------------------------------------------------
--  Migratsiyani `schoollms_owner` bajaradi, ilova esa `app_rw` bilan ishlaydi
--  va u hech narsaga EGA emas. `init-roles.sql` dagi `ALTER DEFAULT PRIVILEGES`
--  yangi jadvalga huquq beradi — LEKIN u faqat o'sha fayl qayta ishga
--  tushirilganda amal qiladi va yangi bazada tartib boshqacha bo'lishi mumkin.
--  Grant unutilsa ilova ishga tushadi va faqat BIRINCHI so'rovda
--  SQLSTATE 42501 bilan yiqiladi. Shuning uchun grantlar jadval bilan birga,
--  shu migratsiyaning o'zida keladi.
--
--  ROL HALI YO'Q BO'LSA — jim o'tib ketamiz (test harness'i migratsiyani
--  `app_rw` siz ham qo'llay olishi kerak, docs/TESTING.md).
--
--  ==========================================================================
--  NEGA BU YERDA BIRORTA `REVOKE` YO'Q — ATAYLAB
--  ==========================================================================
--  Beshta jadvalning HECH BIRI moliyaviy emas: ichlarida summa ham, jurnal
--  (ledger) yozuvi ham, kassa smenasiga havola ham yo'q. SPEC §4.1 ning
--  o'zgarmaslik qoidasi PULGA tegishli, "muhim ko'ringan hamma narsaga" emas,
--  va uni keraksiz joyga cho'zish qimmatga tushadi: `UPDATE` huquqi yo'q
--  jadvalda xato yozilgan izohni tuzatib bo'lmaydi, ya'ni administrator
--  yangi qator qo'shib eskisini "e'tiborsiz qoldiring" deb yozishga majbur
--  bo'ladi. Bu tarixni tozalamaydi — uni ifloslantiradi.
--
--  Xususan `debtor_actions` — §3.5 buni AYNIQSA aniq yozadi: u
--  "append-only in behaviour but not in grants". Ya'ni jadval
--  `deploy/init-roles.sql` §5 dagi ro'yxatga QO'SHILMAYDI. Nima YO'QOLMASLIGI
--  kerak — VA'DA QILINGAN SANA tarixi, va u `deleted_at` bilan himoyalanadi
--  (yumshoq o'chirish), DELETE ni tortib olish bilan emas. Ilova qatori
--  o'chirmaydi; agar kimdir baribir o'chirsa, bu grant emas, kod xatosi.
--
--  Mavjud jadvallarga (`students`, `discipline_reasons`, `school_meta`,
--  `turnstile_events`) YANGI GRANT KERAK EMAS: PostgreSQL'da huquq JADVAL
--  darajasida beriladi va yangi ustun avtomatik ravishda o'sha huquq ostiga
--  tushadi.
-- ===========================================================================

DO $guards$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_rw') THEN
        RAISE NOTICE '[parity_wave2_guards] `app_rw` roli topilmadi — GRANT o''tkazib yuborildi. '
                     'deploy/init-roles.sql ni ishga tushiring.';
        RETURN;
    END IF;

    -- Uchta ma'lumotnoma va ikkita ish jadvali — hammasi JORIY HOLAT, tarix
    -- emas: holat nomi tahrirlanadi, sertifikat raqami to'g'rilanadi, noto'g'ri
    -- kiritilgan sertifikat o'chiriladi. To'liq CRUD.
    EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON '
            'public.debtor_statuses, public.debtor_actions, '
            'public.certificate_types, public.certificates, '
            'public.student_archive_reasons '
            'TO app_rw';
END
$guards$;
