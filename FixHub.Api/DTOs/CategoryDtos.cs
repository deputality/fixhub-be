using System.ComponentModel.DataAnnotations;

namespace FixHub.Api.DTOs;

public class CategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public int? ParentId { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
}

public class CategoryRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = null!;

    public int? ParentId { get; set; }

    // Ключ иконки (латиницей, цифры и дефис), пусто = подобрать автоматически
    [MaxLength(40), RegularExpression("^[a-z0-9-]*$", ErrorMessage = "Некорректная иконка")]
    public string? Icon { get; set; }

    // Цвет в формате #RRGGBB, пусто = подобрать автоматически
    [RegularExpression("^(#[0-9a-fA-F]{6})?$", ErrorMessage = "Цвет должен быть в формате #RRGGBB")]
    public string? Color { get; set; }
}

// Категория для админки. Услуги и исполнители считаются вместе с подкатегориями
public class AdminCategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public int? ParentId { get; set; }
    public bool IsHidden { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public int ChildrenCount { get; set; }
    public int ServicesCount { get; set; }
    public int PerformersCount { get; set; }
}
