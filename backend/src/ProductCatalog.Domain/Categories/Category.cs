namespace ProductCatalog.Domain.Categories;

/// <summary>Reference data used to group products (e.g. lenses, frames).</summary>
public sealed class Category
{
    public const int NameMaxLength = 60;

    private Category()
    {
    }

    public Category(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
}
