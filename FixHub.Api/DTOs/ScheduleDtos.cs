using System.ComponentModel.DataAnnotations;

namespace FixHub.Api.DTOs;

// Рабочие часы в один день недели (1 = понедельник ... 7 = воскресенье), время "HH:mm"
public class WorkingDayDto
{
    [Range(1, 7)]
    public int DayOfWeek { get; set; }

    [Required, RegularExpression(Common.TimeFormat.Pattern, ErrorMessage = "Время указывается в формате ЧЧ:ММ")]
    public string StartTime { get; set; } = null!;

    [Required, RegularExpression(Common.TimeFormat.Pattern, ErrorMessage = "Время указывается в формате ЧЧ:ММ")]
    public string EndTime { get; set; } = null!;
}

// Дней, которых нет в списке, у мастера выходные
public class SaveScheduleRequest
{
    [Required, MaxLength(7)]
    public List<WorkingDayDto> Days { get; set; } = new();
}

public class SlotDto
{
    public string Time { get; set; } = null!;
    public bool IsFree { get; set; }
}

public class SlotsDto
{
    public DateOnly Date { get; set; }

    // false: мастер не ведёт расписание, время выбирать не нужно
    public bool HasSchedule { get; set; }
    public List<SlotDto> Slots { get; set; } = new();
}
