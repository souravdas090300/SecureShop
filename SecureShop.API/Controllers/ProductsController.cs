using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureShop.Application.DTOs.Products;
using SecureShop.Application.Services;
using SecureShop.Domain.Exceptions;

namespace SecureShop.API.Controllers;

[ApiController]
[Route("api/products")]
/// <summary>
/// REST API controller for the product catalogue.
/// Read operations (list, get by ID) are public (anonymous access allowed).
/// Write operations (create, update, delete) require the Admin role.
/// </summary>
public class ProductsController : ControllerBase
{
    private readonly ProductService _svc;
    private readonly ILogger<ProductsController> _logger;

    /// <summary>Injects the product service and logger.</summary>
    public ProductsController(ProductService svc, ILogger<ProductsController> logger)
    {
        _svc = svc;
        _logger = logger;
    }

    /// <summary>
    /// Returns a paginated list of active products, optionally filtered by category or search term.
    /// Results are served from the Redis cache when available.
    /// </summary>
    /// <param name="category">Optional category filter.</param>
    /// <param name="search">Optional free-text search term.</param>
    /// <param name="page">1-based page index (default: 1).</param>
    /// <param name="pageSize">Items per page (default: 10).</param>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<PagedProductsDto>> GetAll(
        [FromQuery] string? category, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        try
        {
            _logger.LogInformation("Fetching products - Category: {Category}, Search: {Search}, Page: {Page}, PageSize: {PageSize}", 
                category ?? "All", search ?? "", page, pageSize);
            
            var result = await _svc.GetAllAsync(category, search, page, pageSize);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching products");
            return StatusCode(500, new { message = "An error occurred while fetching products" });
        }
    }

    /// <summary>Returns a single product by its GUID (public endpoint).</summary>
    /// <param name="id">Product identifier.</param>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<ProductResponseDto>> GetById(Guid id)
    {
        try
        {
            _logger.LogInformation("Fetching product by ID: {ProductId}", id);
            
            var result = await _svc.GetByIdAsync(id);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning("Product not found: {ProductId}", id);
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching product {ProductId}", id);
            return StatusCode(500, new { message = "An error occurred while fetching the product" });
        }
    }

    /// <summary>
    /// Creates a new product (admin only).
    /// Cache is invalidated for all product list pages after creation.
    /// </summary>
    /// <param name="dto">Product data to persist.</param>
    [HttpPost]
    [Authorize(AuthenticationSchemes = "AdminCookie,Bearer", Roles = "Admin")]
    public async Task<ActionResult<ProductResponseDto>> Create([FromBody] CreateProductDto dto)
    {
        try
        {
            _logger.LogInformation("Creating new product: {ProductName}", dto.Name);
            
            if (!ModelState.IsValid)
            {
                return BadRequest(new { message = "Invalid product data", errors = ModelState });
            }
            
            var result = await _svc.CreateAsync(dto);
            _logger.LogInformation("Product created successfully: {ProductId}", result.Id);
            
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (DomainException ex)
        {
            _logger.LogWarning("Product creation validation error: {Message}", ex.Message);
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating product");
            return StatusCode(500, new { message = "An error occurred while creating the product" });
        }
    }

    /// <summary>
    /// Updates an existing product's details (admin only).
    /// Individual product cache and all list cache entries are invalidated.
    /// </summary>
    /// <param name="id">Product GUID to update.</param>
    /// <param name="dto">New field values.</param>
    [HttpPut("{id:guid}")]
    [Authorize(AuthenticationSchemes = "AdminCookie,Bearer", Roles = "Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProductDto dto)
    {
        try
        {
            _logger.LogInformation("Updating product: {ProductId}", id);
            
            if (!ModelState.IsValid)
            {
                return BadRequest(new { message = "Invalid product data", errors = ModelState });
            }
            
            await _svc.UpdateAsync(id, dto);
            _logger.LogInformation("Product updated successfully: {ProductId}", id);
            
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning("Product not found for update: {ProductId}", id);
            return NotFound(new { message = ex.Message });
        }
        catch (DomainException ex)
        {
            _logger.LogWarning("Product update validation error: {Message}", ex.Message);
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating product {ProductId}", id);
            return StatusCode(500, new { message = "An error occurred while updating the product" });
        }
    }

    /// <summary>
    /// Soft-deletes a product by setting it inactive (admin only).
    /// The product record is retained in the database for historical order references.
    /// </summary>
    /// <param name="id">Product GUID to deactivate.</param>
    [HttpDelete("{id:guid}")]
    [Authorize(AuthenticationSchemes = "AdminCookie,Bearer", Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            _logger.LogInformation("Deleting product: {ProductId}", id);
            
            await _svc.DeleteAsync(id);
            _logger.LogInformation("Product deleted successfully: {ProductId}", id);
            
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning("Product not found for deletion: {ProductId}", id);
            return NotFound(new { message = ex.Message });
        }
        catch (DomainException ex)
        {
            _logger.LogWarning("Product deletion error: {Message}", ex.Message);
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting product {ProductId}", id);
            return StatusCode(500, new { message = "An error occurred while deleting the product" });
        }
    }
}