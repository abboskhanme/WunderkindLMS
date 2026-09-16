using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Application.Dtos;

namespace SchoolLms.Application.Services;

/// <summary>
/// Buyurtmalar voronkasi (docs/modules/existing-module-gaps.md §4, #9) — lidlar bosqichma-bosqich
/// qanday siljiyotganini ko'rsatadigan hisobot. Lidlar DOSKASIGA (dizayni muzlatilgan) tegmaydi:
/// bu faqat o'qish, alohida sahifa uchun.
///
/// <para>
/// <b>Nega davr (sana) filtri yo'q — o'qib chiqing.</b> <c>Lead</c> da vaqt belgisi ham,
/// bosqich o'zgarishi tarixi ham saqlanmaydi (<c>SchoolLms.Domain/Entities.cs</c>: Id, FullName,
/// Gender, BirthDate, ParentFullName, ParentPhone, TargetGrade, Note, Stage — tamom). Shuning
/// uchun "shu oyda nechta lid kirdi", "bosqichda o'rtacha necha kun turdi" degan savollarga
/// javob beradigan MA'LUMOT YO'Q. Ularni chiqarish uchun yangi jadval (lid hodisalari jurnali)
/// yoki hech bo'lmasa <c>leads.created_at</c> ustuni kerak — ya'ni migratsiya. Bu vazifa
/// doirasida migratsiya yozilmaydi, shuning uchun hisobot HOZIRGI HOLAT surati sifatida
/// quriladi. Yolg'on raqam chiqarishdan ko'ra kamroq raqam chiqargan yaxshi.
/// </para>
///
/// <para>
/// <b>Bitta taxmin bor va u shu yerda oshkor yozilgan:</b> lid voronkada faqat OLDINGA siljiydi.
/// Ya'ni "N-bosqichga yetgan" = shu bosqichda yoki undan keyingi bosqichlarda turganlar.
/// Doskada lidni orqaga sudrab qo'ysa bo'ladi, lekin buni bilish uchun yana o'sha tarix kerak.
/// </para>
///
/// <para>
/// <b>Yo'qotilgan lidlar.</b> Bazada "yo'qotildi" bayrog'i yo'q — amalda mijoz buning uchun
/// alohida ustun ochadi ("Rad etdi", "Telefon ko'tarmadi"). Shuning uchun qaysi ustunlar
/// yo'qotish ekanini CHAQIRUVCHI aytadi (<c>lostStageIds</c>), hisobot esa ularni voronkadan
/// chiqarib, sabab kesimi qilib beradi: sabab matni — ustun nomining o'zi.
/// </para>
/// </summary>
public static class LeadFunnelQuery
{
    private static double Pct(int part, int whole) =>
        whole <= 0 ? 0 : Math.Round((double)part / whole * 100, 1);

    /// <param name="db">Baza konteksti.</param>
    /// <param name="lostStageIds">"Yo'qotildi" deb hisoblanadigan ustunlar id'lari (ixtiyoriy).</param>
    public static async Task<LeadFunnelDto> BuildAsync(
        IAppDbContext db, IReadOnlyCollection<string>? lostStageIds = null)
    {
        // Bosqichlar ro'yxati ham, TARTIBI ham bazadan — kodda qattiq yozilgan ro'yxat yo'q.
        var stages = await db.LeadStages.AsNoTracking().OrderBy(s => s.Order).ToListAsync();
        var leads = await db.Leads.AsNoTracking()
            .Select(l => new { l.Stage, l.TargetGrade })
            .ToListAsync();

        var lost = lostStageIds is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(lostStageIds, StringComparer.Ordinal);

        var funnelStages = stages.Where(s => !lost.Contains(s.Id)).ToList();
        var lostStages = stages.Where(s => lost.Contains(s.Id)).ToList();

        var byStage = leads
            .GroupBy(l => l.Stage, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        int CountIn(string stageId) => byStage.TryGetValue(stageId, out var list) ? list.Count : 0;

        var totalLeads = leads.Count;
        var funnelLeads = funnelStages.Sum(s => CountIn(s.Id));
        var lostCount = lostStages.Sum(s => CountIn(s.Id));
        // Ustuni o'chirilgan (yoki hech qachon bo'lmagan) lid — jim yo'qolib ketmasin.
        var orphanCount = totalLeads - funnelLeads - lostCount;

        // "Yetib kelgan" — suffiks yig'indi: oxirgi bosqichdan boshiga qarab.
        var reached = new int[funnelStages.Count];
        var running = 0;
        for (var i = funnelStages.Count - 1; i >= 0; i--)
        {
            running += CountIn(funnelStages[i].Id);
            reached[i] = running;
        }

        var stageRows = new List<LeadFunnelStageDto>(funnelStages.Count);
        for (var i = 0; i < funnelStages.Count; i++)
        {
            var s = funnelStages[i];
            var current = CountIn(s.Id);
            var movedOn = i + 1 < funnelStages.Count ? reached[i + 1] : 0;
            double? step = i + 1 < funnelStages.Count ? Pct(movedOn, reached[i]) : null;
            stageRows.Add(new LeadFunnelStageDto(
                s.Id, s.Title, s.Color, s.Order,
                current, reached[i], movedOn,
                Pct(current, funnelLeads),
                step,
                step is null ? null : Math.Round(100 - step.Value, 1)));
        }

        var lossRows = lostStages
            .Select(s => new LeadFunnelLossDto(s.Id, s.Title, s.Color, CountIn(s.Id), Pct(CountIn(s.Id), lostCount)))
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Oxirgi bosqichga yetganlar — voronka oxiri; "sotildi" degan alohida bayroq yo'q.
        var finalStageId = funnelStages.Count > 0 ? funnelStages[^1].Id : null;
        var funnelStageIds = funnelStages.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var lostStageIdSet = lostStages.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);

        var grades = leads
            .GroupBy(l => l.TargetGrade)
            .Select(g =>
            {
                var inFunnel = g.Count(l => funnelStageIds.Contains(l.Stage));
                var final = finalStageId is null ? 0 : g.Count(l => l.Stage == finalStageId);
                var gradeLost = g.Count(l => lostStageIdSet.Contains(l.Stage));
                return new LeadFunnelGradeDto(
                    g.Key, g.Count(), inFunnel, final, gradeLost,
                    inFunnel > 0 ? Pct(final, inFunnel) : null);
            })
            .OrderBy(r => r.TargetGrade)
            .ToList();

        double? overall = funnelStages.Count > 0 && funnelLeads > 0
            ? Pct(CountIn(funnelStages[^1].Id), funnelLeads)
            : null;

        return new LeadFunnelDto(
            totalLeads, funnelLeads, lostCount, orphanCount, overall,
            stageRows, lossRows, grades);
    }
}
