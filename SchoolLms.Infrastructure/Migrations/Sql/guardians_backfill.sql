-- ===========================================================================
--  Vasiylarni `students.parent_phone` dan ko'chirish (SPEC §3.2)
--  Migratsiya: GuardiansAndTelegramLink · Faza 3
-- ===========================================================================
--
--  BU FAYLNI TAHRIRLAMANG.
--  Migratsiya bir marta qo'llanadi va shundan keyin muzlaydi. Bu yerni
--  o'zgartirsangiz, yangi bazalar eski bazalardan JIMGINA farq qila boshlaydi.
--  O'zgartirish kerak bo'lsa — YANGI migratsiya va yangi .sql fayl.
--  (Qoida `billing_guards.sql` dan aynan olingan — P1-05.)
--
--  NIMA QILADI
--  -----------
--  Uch qadam, hammasi IDEMPOTENT (`on conflict do nothing` / `where ... is null`):
--    1. Har bir NOYOB ota-ona raqami uchun bitta `guardians` qatori.
--    2. Har bir o'quvchini o'z vasiysiga bog'lash (`student_guardians`).
--    3. Mavjud `parent` rolidagi akkauntlarni vasiyga ulash — ular login
--       sifatida telefon raqamini ishlatadi (`users.email`).
--
--  `students.parent_phone` OLIB TASHLANMAYDI. Uni o'qiydigan kod (import,
--  e'lon mail-merge, admin kartochkasi, eski ota-ona portali) o'zgarmadi.
--  Ustunni yopish alohida vazifa — docs/PENDING_WIRING.md.
--
--  NORMALLASHTIRISH QOIDASI: OXIRGI 9 RAQAM.
--  `right(regexp_replace(phone, '[^0-9]', '', 'g'), 9)` — bu C# dagi
--  `PhoneUtil.Key` ning AYNAN o'zi va `guardians.phone_key` generated
--  column'ining ham o'zi. "+998 90 123 45 67" va "901234567" bitta odam;
--  aks holda backfill bitta oilaga ikkita vasiy yasab qo'yardi.
--
--  VAQT: `now() at time zone 'Asia/Tashkent'`. Barcha eski ustunlar kabi
--  `created_at` ham `timestamp without time zone` va unda MAKTAB devor
--  soati turadi (`AppClock.Now`), UTC emas.
-- ===========================================================================


-- ---------------------------------------------------------------------------
-- 1) VASIYLAR
--
--    Nomi: shu raqamdagi o'quvchilarning ota-ona ismlaridan alifbo bo'yicha
--    birinchisi. Hech birida ism bo'lmasa — "<o'quvchi> ning ota-onasi"
--    (bo'sh nom `ck_guardians_full_name` dan o'tmaydi). Aynan shu qoida
--    `GuardianSync.DisplayName` da ham yozilgan.
--
--    `phone_key` YOZILMAYDI — u generated stored column, baza o'zi hisoblaydi.
-- ---------------------------------------------------------------------------
insert into guardians (id, full_name, phone, passport_url, created_at)
select
    gen_random_uuid()::text,
    coalesce(
        min(nullif(btrim(s.parent_full_name), '')),
        min(s.full_name) || ' ning ota-onasi'),
    min(btrim(s.parent_phone)),
    min(s.parent_passport_url),
    now() at time zone 'Asia/Tashkent'
from students s
where length(regexp_replace(s.parent_phone, '[^0-9]', '', 'g')) >= 7
group by right(regexp_replace(s.parent_phone, '[^0-9]', '', 'g'), 9)
on conflict do nothing;


-- ---------------------------------------------------------------------------
-- 2) O'QUVCHI ↔ VASIY
--
--    `is_primary = true`: bu bosqichda har o'quvchida ko'pi bilan bitta vasiy
--    bor (bitta `parent_phone`), shuning uchun
--    `ux_student_guardians_one_primary` buzilmaydi. Ikkinchi vasiy (buvi,
--    ishonchli shaxs) keyin admin panelidan qo'shiladi va u asosiy bo'lmaydi.
-- ---------------------------------------------------------------------------
insert into student_guardians (student_id, guardian_id, relation, is_primary, created_at)
select s.id, g.id, 'parent', true, now() at time zone 'Asia/Tashkent'
from students s
join guardians g
  on g.phone_key = right(regexp_replace(s.parent_phone, '[^0-9]', '', 'g'), 9)
where length(regexp_replace(s.parent_phone, '[^0-9]', '', 'g')) >= 7
on conflict (student_id, guardian_id) do nothing;


-- ---------------------------------------------------------------------------
-- 3) MAVJUD `parent` AKKAUNTLARINI ULASH
--
--    Eski ota-ona yo'li login sifatida telefon raqamini ishlatadi
--    (`users.email` = raqam). Shu bog'lanishni saqlab qolamiz: aks holda
--    bugungacha ishlab turgan ota-ona akkaunti Mini App'da "farzand topilmadi"
--    ko'rsatardi, chunki yangi yo'l faqat `guardians.user_id` ga qaraydi.
--
--    Bir raqamga ikkita `parent` akkaunti bo'lsa (bo'lmasligi kerak, `users.email`
--    unikal) — bittasi tanlanadi; `guardians.phone_key` unikal bo'lgani uchun
--    teskarisi (bitta akkaunt ikkita vasiyga) mumkin emas.
-- ---------------------------------------------------------------------------
update guardians g
set user_id = u.id
from users u
where g.user_id is null
  and u.role = 'parent'
  and length(regexp_replace(u.email, '[^0-9]', '', 'g')) >= 7
  and right(regexp_replace(u.email, '[^0-9]', '', 'g'), 9) = g.phone_key
  -- Akkaunt boshqa vasiyga allaqachon ulangan bo'lsa tegmaymiz
  -- (`ux_guardians_user_id` buzilmasin).
  and not exists (select 1 from guardians x where x.user_id = u.id);
