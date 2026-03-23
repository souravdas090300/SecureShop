namespace SecureShop.Application.DTOs.Products;

public record CreateProductDto(string Name, string Description, decimal Price, int StockQuantity, string Category);
public record UpdateProductDto(string Name, string Description, decimal Price, string Category);
public record ProductResponseDto(Guid Id, string Name, string Description, decimal Price, int StockQuantity, string Category, bool IsActive, DateTime CreatedAt);
public record PagedProductsDto(IEnumerable<ProductResponseDto> Items, int TotalCount, int Page, int PageSize, int TotalPages);