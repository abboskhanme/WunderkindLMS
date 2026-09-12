-- ===========================================================================
--  Vasiylar va Telegram bog'lanishi — `app_rw` grantlari
--  Migratsiya: GuardiansAndTelegramLink · Faza 3
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
--  `app_rw` siz qo'llaydi, docs/TESTING.md).
--
--  NEGA `telegram_link_codes` DA DELETE YO'Q
--  -----------------------------------------
--  Kod jadvali AUDIT IZI: kim, kimga, qachon kod chiqardi va uni qaysi
--  Telegram akkaunti ishlatdi. "Akkauntni kim bog'ladi" degan savolga javob
--  faqat shu yerda. O'chirish imkoni bo'lsa, noto'g'ri bog'langan akkauntning
--  izi ham u bilan birga yo'qolardi. UPDATE kerak: kod ishlatilganda
--  `used_at` yoziladi va yangi kod chiqarilganda eskisining `expires_at` i
--  qisqartiriladi.
--
--  Qolgan uch jadval — joriy holat, tarix emas: vasiy ma'lumoti to'g'rilanadi,
--  farzand bog'lanishi uziladi, Telegram bog'lanishi bekor qilinadi, chat
--  o'qish belgisi ustiga yoziladi. Ularda to'liq CRUD.
-- ===========================================================================

DO $guards$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_rw') THEN
        RAISE NOTICE '[guardian_guards] `app_rw` roli topilmadi — GRANT/REVOKE o''tkazib yuborildi. '
                     'deploy/init-roles.sql ni ishga tushiring.';
        RETURN;
    END IF;

    -- Joriy holat jadvallari — to'liq CRUD.
    EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON '
            'public.guardians, public.student_guardians, '
            'public.telegram_accounts, public.chat_reads '
            'TO app_rw';

    -- Bog'lanish kodlari — audit izi: o'chirib bo'lmaydi.
    EXECUTE 'GRANT SELECT, INSERT, UPDATE ON public.telegram_link_codes TO app_rw';
    EXECUTE 'REVOKE DELETE, TRUNCATE ON public.telegram_link_codes FROM app_rw';
END
$guards$;
