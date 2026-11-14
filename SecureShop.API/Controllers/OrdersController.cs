using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureShop.Application.DTOs.Orders;
using SecureShop.Application.Services;
using SecureShop.Domain.Exceptions;

namespace SecureShop.API.Controllers;

/// <summary>
/// REST API controller for order management.
/// All endpoints require authentication (Bearer JWT or AdminCookie).
/// Admins can access any order; regular users are restricted to their own.
/// </summary>
[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly OrderService _svc;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(OrderService svc, ILogger<OrdersController> logger)
    {
        _svc = svc;
        _logger = logger;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>
    /// Places a new order for the authenticated user.
    /// </summary>
    [HttpPost]
    [Authorize(AuthenticationSchemes = "Bearer")]
    public async Task<ActionResult<OrderResponseDto>> Create([FromBody] CreateOrderDto dto)
    {
        try
        {
            _logger.LogInformation("Creating order for user: {UserId}", UserId);
            
            if (!ModelState.IsValid)
            {
                return BadRequest(new { message = "Invalid order data", errors = ModelState });
            }
            
            var result = await _svc.CreateAsync(dto, UserId);
            _logger.LogInformation("Order created successfully: {OrderId}", result.Id);
            
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (DomainException ex)
        {
            _logger.LogWarning("Order creation validation error: {Message}", ex.Message);
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Order creation failed for user {UserId}: {Message}", UserId, ex.Message);
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating order for user {UserId}", UserId);
            return StatusCode(500, new { message = "An error occurred while creating the order" });
        }
    }

    /// <summary>
    /// Retrieves a single order by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(AuthenticationSchemes = "AdminCookie,Bearer")]
    public async Task<ActionResult<OrderResponseDto>> GetById(Guid id)
    {
        try
        {
            var isAdmin = User.IsInRole("Admin");
            string? userIdFilter = isAdmin ? null : UserId;
            _logger.LogInformation("Fetching order {OrderId}", id);

            var result = await _svc.GetByIdAsync(id, userIdFilter);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching order {OrderId}", id);
            return StatusCode(500, new { message = "An error occurred while fetching the order" });
        }
    }

    /// <summary>
    /// Exports all orders as a CSV file (admin only).
    /// </summary>
    [HttpGet("export")]
    [Authorize(AuthenticationSchemes = "Bearer", Roles = "Admin")]
    public async Task<IActionResult> ExportCsv()
    {
        var orders = await _svc.GetAllOrdersAsync();
        
        var csv = new StringBuilder();
        csv.AppendLine("Order ID,Date,Customer,Status,Total Amount");
        
        foreach (var order in orders)
        {
            csv.AppendLine($"{order.Id},{order.CreatedAt:yyyy-MM-dd HH:mm},{order.CustomerEmail},{order.Status},{order.TotalAmount}");
        }
        
        var bytes = Encoding.UTF8.GetBytes(csv.ToString());
        return File(bytes, "text/csv", $"orders-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    /// <summary>
    /// Updates the lifecycle status of an order (admin only).
    /// </summary>
    [HttpPut("{id:guid}/status")]
    [Authorize(AuthenticationSchemes = "AdminCookie,Bearer", Roles = "Admin")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateOrderStatusDto dto)
    {
        try
        {
            await _svc.UpdateOrderStatusAsync(id, dto.Status);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (DomainException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating order status {OrderId}", id);
            return StatusCode(500, new { message = "An error occurred while updating the order" });
        }
    }

    /// <summary>Returns all orders belonging to the currently authenticated user.</summary>
    [HttpGet("my")]
    [Authorize(AuthenticationSchemes = "Bearer")]
    public async Task<ActionResult<IEnumerable<OrderResponseDto>>> GetMine()
    {
        try
        {
            _logger.LogInformation("Fetching orders for user: {UserId}", UserId);
            
            var result = await _svc.GetMyOrdersAsync(UserId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching orders for user {UserId}", UserId);
            return StatusCode(500, new { message = "An error occurred while fetching your orders" });
        }
    }
    
    /// <summary>Returns every order in the system (admin only).</summary>
    [HttpGet]
    [Authorize(AuthenticationSchemes = "AdminCookie,Bearer", Roles = "Admin")]
    public async Task<ActionResult<IEnumerable<OrderResponseDto>>> GetAll()
    {
        try
        {
            _logger.LogInformation("Admin fetching all orders");
            
            var result = await _svc.GetAllOrdersAsync();
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching all orders");
            return StatusCode(500, new { message = "An error occurred while fetching orders" });
        }
    }
}