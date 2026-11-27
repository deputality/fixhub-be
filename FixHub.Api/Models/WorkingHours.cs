namespace FixHub.Api.Models;

// Рабочие часы мастера в один день недели. Нет строки на день, значит, у мастера выходной.
public class WorkingHours
{
    public int Id { get; set; }

    public int PerformerId { get; set; }
    public User Performer { get; set; } = null!;

    // ISO: 1 = понедельник ... 7 = воскресенье
    public int DayOfWeek { get; set; }

    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
}
