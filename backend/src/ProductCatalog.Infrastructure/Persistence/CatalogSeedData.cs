using ProductCatalog.Domain.Categories;

namespace ProductCatalog.Infrastructure.Persistence;

internal static class CatalogSeedData
{
    public static readonly Category[] Categories =
    [
        new(1, "Spectacle Lenses"),
        new(2, "Lens Coatings"),
        new(3, "Frames"),
        new(4, "Sunglasses"),
        new(5, "Lens Care"),
    ];

    /// <summary>Sample products for local testing. Ids come from the sequence, like any other product.</summary>
    public static readonly SampleProduct[] Products =
    [
        new("SmartLife Progressive 1.60", "Progressive lenses designed for today's connected lifestyle.", "LNS-SLP-160", 289.00m, 120, 1),
        new("SmartLife Single Vision 1.50", "Single vision lenses optimised for dynamic viewing.", "LNS-SLS-150", 129.00m, 250, 1),
        new("MyoCare Kids 1.59", "Myopia management lenses for children.", "LNS-MYO-159", 199.00m, 40, 1),
        new("ClearView Single Vision 1.67", "Thinner, flatter lenses with clearer peripheral vision.", "LNS-CLV-167", 159.00m, 8, 1),
        new("DuraVision Platinum UV", "Premium anti-reflective coating with UV protection.", "COT-DVP-UV", 59.00m, 500, 2),
        new("BlueGuard", "Lens material that reflects part of the blue-violet light.", "COT-BLG-01", 79.00m, 0, 2),
        new("PhotoFusion X Grey", "Photochromic lenses that darken and clear quickly.", "COT-PFX-GRY", 99.00m, 65, 2),
        new("Classic Titanium Frame", "Lightweight titanium full-rim frame.", "FRM-TIT-001", 189.00m, 15, 3),
        new("Acetate Round Frame", "Hand-polished acetate frame in tortoiseshell.", "FRM-ACE-002", 149.00m, 3, 3),
        new("Polarised Aviator", "Polarised sunglasses with ZEISS lenses.", "SUN-AVI-POL", 219.00m, 22, 4),
        new("Lens Cleaning Spray 120ml", "Alcohol-free cleaning spray for coated lenses.", "CAR-SPR-120", 9.90m, 900, 5),
        new("Lens Cleaning Wipes (pack of 30)", "Individually wrapped wipes for all lens types.", "CAR-WIP-030", 6.50m, 1500, 5),
    ];

    internal sealed record SampleProduct(string Name, string Description, string Sku, decimal Price, int Stock, int CategoryId);
}
