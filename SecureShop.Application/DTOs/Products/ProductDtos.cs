namespace SecureShop.Application.DTOs.Products;

/// <summary>Admin request to add a new product to the catalogue.</summary>
public record CreateProductDto(string Name, string Description, decimal Price, int StockQuantity, string Category, string? ImageUrl = null);

/// <summary>Admin request to replace a product's mutable fields.</summary>
public record UpdateProductDto(string Name, string Description, decimal Price, string Category, int StockQuantity, string? ImageUrl = null);

/// <summary>Read-model for a single catalogue product; safe to expose to anonymous users.</summary>
public record ProductResponseDto(Guid Id, string Name, string Description, decimal Price, int StockQuantity, string Category, bool IsActive, DateTime CreatedAt, string? ImageUrl = null);

/// <summary>
/// Paginated product list result. Includes the current page, page size, total item count,
/// and total page count so clients can render pagination controls.
/// </summary>
public record PagedProductsDto(IEnumerable<ProductResponseDto> Items, int TotalCount, int Page, int PageSize, int TotalPages);