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