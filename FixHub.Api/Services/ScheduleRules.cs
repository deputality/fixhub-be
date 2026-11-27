using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Services;

// Расписание мастера и запись на время. Слот всегда длится один час и начинается
// с начала рабочего окна (09:00-18:00 даёт слоты 09:00, 10:00 ... 17:00).
public static class ScheduleRules
{
    public const int SlotMinutes = 60;

    // ISO-день недели: 1 = понедельник ... 7 = воскресенье
    public static int IsoDay(DateOnly date) => date.DayOfWeek == System.DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;

    public static IEnumerable<TimeOnly> SlotsOf(WorkingHours hours)
    {
        var t = hours.StartTime;
        while (t.AddMinutes(SlotMinutes) <= hours.EndTime && t.AddMinutes(SlotMinutes) > t)
        {
            yield return t;
            t = t.AddMinutes(SlotMinutes);
        }
    }

    public static Task<bool> HasScheduleAsync(AppDbContext db, int performerId) =>
        db.WorkingHours.AsNoTracking().AnyAsync(w => w.PerformerId == performerId);

    // Занятые слоты мастера в этот день: позиции заказов, которые не отменены
    public static async Task<HashSet<TimeOnly>> BusyAsync(AppDbContext db, int performerId, DateOnly date)
    {
        var busy = await db.OrderItems
            .AsNoTracking()
            .Where(i => i.Order.PerformerId == performerId
                && i.DesiredDate == date
                && i.DesiredTime != null
                && i.Order.Status != OrderStatus.Cancelled)
            .Select(i => i.DesiredTime!.Value)
            .ToListAsync();

        return busy.ToHashSet();
    }

    public static async Task<SlotsDto> GetSlotsAsync(AppDbContext db, int performerId, DateOnly date)
    {
        var result = new SlotsDto { Date = date };

        result.HasSchedule = await HasScheduleAsync(db, performerId);
        if (!result.HasSchedule)
            return result;

        var hours = await db.WorkingHours.AsNoTracking()
            .FirstOrDefaultAsync(w => w.PerformerId == performerId && w.DayOfWeek == IsoDay(date));
        if (hours == null)
            return result;

        var busy = await BusyAsync(db, performerId, date);

        result.Slots = SlotsOf(hours)
            .Select(t => new SlotDto { Time = t.ToString("HH:mm"), IsFree = !busy.Contains(t) })
            .ToList();

        return result;
    }

    // Проверка выбранного клиентом времени. Возвращает текст ошибки или null, если всё в порядке.
    // required: true при оформлении заказа, false при добавлении в корзину (время можно выбрать позже).
    // Без расписания время не проверяется и не сохраняется (вызывающий сам обнуляет его).
    public static async Task<string?> ValidateAsync(
        AppDbContext db, int performerId, string serviceTitle, DateOnly? date, TimeOnly? time, bool required)
    {
        if (!await HasScheduleAsync(db, performerId))
            return null;

        if (date == null && time == null)
            return required ? $"Выберите дату и время для услуги «{serviceTitle}»" : null;

        if (date == null || time == null)
            return $"Выберите дату и время для услуги «{serviceTitle}»";

        var hours = await db.WorkingHours.AsNoTracking()
            .FirstOrDefaultAsync(w => w.PerformerId == performerId && w.DayOfWeek == IsoDay(date.Value));

        if (hours == null || !SlotsOf(hours).Contains(time.Value))
            return $"Мастер не работает в выбранное время («{serviceTitle}»)";

        var busy = await BusyAsync(db, performerId, date.Value);
        if (busy.Contains(time.Value))
            return $"Выбранное время уже занято («{serviceTitle}»), выберите другое";

        return null;
    }
}
