using System.Linq.Expressions;
using FixHub.Api.Models;

namespace FixHub.Api.Services;

// Расстояния для геопоиска. Считаем по упрощённой формуле (плоская проекция
// со средней широтой): на расстояниях в десятки километров ошибка ничтожна,
// а в SQL нет асимптотик вроде acos, которые падают из-за ошибок округления.
public static class GeoMath
{
    private const double EarthRadiusKm = 6371.0088;
    private const double Rad = Math.PI / 180.0;

    // ~1 км: этого хватает, чтобы показать район, и не раскрывает, где живёт мастер
    public static double? RoundCoordinate(double? value) =>
        value == null ? null : Math.Round(value.Value, 2);

    public static double DistanceKm(double lat1, double lng1, double lat2, double lng2)
    {
        var dLat = (lat2 - lat1) * Rad;
        var dLng = (lng2 - lng1) * Rad * Math.Cos((lat1 + lat2) * Rad / 2);
        return EarthRadiusKm * Math.Sqrt(dLat * dLat + dLng * dLng);
    }

    // То же самое для SQL. NULL получается сам, если у мастера нет точки на карте
    public static Expression<Func<Service, double?>> DistanceFrom(double lat, double lng) =>
        s => (double?)(EarthRadiusKm * Math.Sqrt(
            Math.Pow((s.Performer.Latitude!.Value - lat) * Rad, 2)
            + Math.Pow((s.Performer.Longitude!.Value - lng) * Rad
                * Math.Cos((s.Performer.Latitude!.Value + lat) * Rad / 2), 2)));

    // Мастер в радиусе radiusKm от точки; без точки на карте условие ложно
    public static Expression<Func<Service, bool>> Within(double lat, double lng, double radiusKm)
    {
        var distance = DistanceFrom(lat, lng);
        var body = Expression.LessThanOrEqual(
            distance.Body, Expression.Constant(radiusKm, typeof(double?)));
        return Expression.Lambda<Func<Service, bool>>(body, distance.Parameters);
    }
}
