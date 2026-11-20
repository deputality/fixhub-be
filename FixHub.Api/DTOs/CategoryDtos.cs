using System.ComponentModel.DataAnnotations;

namespace FixHub.Api.DTOs;

public class CategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public int? ParentId { get; set; }
}

public class CategoryRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = null!;

    public int? ParentId { get; set; }
}

// Категория для админки. Услуги и исполнители считаются вместе с подкатегориями
public class AdminCategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public int? ParentId { get; set; }
    public bool IsHidden { get; set; }
    public int ChildrenCount { get; set; }
    public int ServicesCount { get; set; }
    public int PerformersCount { get; set; }
}
