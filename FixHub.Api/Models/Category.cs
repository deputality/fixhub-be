namespace FixHub.Api.Models;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;

    // Скрытая категория не видна клиентам; услуги и заказы в ней сохраняются
    public bool IsHidden { get; set; }

    // Оформление, которое админ задаёт сам. null = подбирается автоматически
    // Icon — ключ иконки из набора на фронтенде (например "spray-can"), Color — цвет вида #16a34a
    public string? Icon { get; set; }
    public string? Color { get; set; }

    public int? ParentId { get; set; }
    public Category? Parent { get; set; }
    public List<Category> Children { get; set; } = new();
}