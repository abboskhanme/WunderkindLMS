using SchoolLms.Application.Dtos;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// Avtobus GPS izini tahlil qiladi: to'xtash joylari (qayerda qancha turgan), bosib o'tilgan masofa,
/// harakatda va to'xtab turgan vaqt. To'xtash = belgilangan radius (metr) ichida belgilangan
/// daqiqadan ko'p turib qolish.
/// </summary>
public static class GpsService
{
    /// <summary>Berilgan (vaqt bo'yicha tartiblangan) nuqtalardan kunlik iz tahlilini quradi.</summary>
    public static BusTrackDto Analyze(string date, IReadOnlyList<BusLocation> points, int stopRadiusM, int stopMinMinutes)
    {
        var pts = points
            .OrderBy(p => p.RecordedAt, StringComparer.Ordinal)
            .Select(p => new TrackPointDto(p.Latitude, p.Longitude, p.Speed, p.RecordedAt))
            .ToList();

        var stops = new List<BusStopDto>();
        double distanceKm = 0;
        var movingMin = 0;
        var stoppedMin = 0;

        for (var i = 1; i < pts.Count; i++)
            distanceKm += HaversineM(pts[i - 1].Lat, pts[i - 1].Lng, pts[i].Lat, pts[i].Lng) / 1000.0;

        // To'xtashlarni klaster bilan topamiz: anchor nuqtadan radius ichida turgan ketma-ket nuqtalar.
        var n = pts.Count;
        var idx = 0;
        while (idx < n)
        {
            var j = idx + 1;
            while (j < n && HaversineM(pts[idx].Lat, pts[idx].Lng, pts[j].Lat, pts[j].Lng) <= stopRadiusM)
                j++;
            // [idx .. j-1] — bitta joyda turgan klaster
            var first = pts[idx];
            var last = pts[j - 1];
            var mins = MinutesBetween(first.Time, last.Time);
            if (j - idx >= 2 && mins >= stopMinMinutes)
            {
                var lat = (first.Lat + last.Lat) / 2;
                var lng = (first.Lng + last.Lng) / 2;
                stops.Add(new BusStopDto(Math.Round(lat, 6), Math.Round(lng, 6), first.Time, last.Time, mins));
                stoppedMin += mins;
                idx = j; // klasterdan keyin davom etamiz
            }
            else
            {
                idx++; // to'xtash emas — keyingi nuqtaga
            }
        }

        // Harakat vaqti = umumiy davr − to'xtab turgan vaqt.
        if (pts.Count >= 2)
        {
            var total = MinutesBetween(pts[0].Time, pts[^1].Time);
            movingMin = Math.Max(0, total - stoppedMin);
        }

        return new BusTrackDto(date, pts, stops, Math.Round(distanceKm, 2), movingMin, stoppedMin);
    }

    private static int MinutesBetween(string fromIso, string toIso)
    {
        if (!DateTime.TryParse(fromIso, out var a) || !DateTime.TryParse(toIso, out var b)) return 0;
        return Math.Max(0, (int)(b - a).TotalMinutes);
    }

    /// <summary>Ikki koordinata orasidagi masofa (metr) — haversine.</summary>
    private static double HaversineM(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6371000; // Yer radiusi, metr
        var dLat = Deg2Rad(lat2 - lat1);
        var dLon = Deg2Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(Deg2Rad(lat1)) * Math.Cos(Deg2Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return r * (2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)));
    }

    private static double Deg2Rad(double d) => d * Math.PI / 180.0;
}
