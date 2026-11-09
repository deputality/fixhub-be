namespace FixHub.Api.Models;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;

    public int? ParentId { get; set; }
    public Category? Parent { get; set; }
    public List<Category> Children { get; set; } = new();
}