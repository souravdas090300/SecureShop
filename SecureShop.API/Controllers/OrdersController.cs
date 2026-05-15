using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureShop.Application.DTOs.Orders;
using SecureShop.Application.Services;
using SecureShop.Domain.Exceptions;

namespace SecureShop.API.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
/// <summary>
/// REST API controller for order management.
/// All endpoints require authentication (Bearer or AdminCookie).
/// Admins can access any order; regular users are restricted to their own.
/// </summary>
public class OrdersController : ControllerBase
{
    private readonly OrderService _svc;
    private readonly ILogger<OrdersController> _logger;

    /// <summary>Injects the order service and logger.</summary>
    public OrdersController(OrderService svc, ILogger<OrdersController> logger)
    {
        _svc = svc;
        _logger = logger;
    }

    /// <summary>
    /// Convenience property that extracts the authenticated user's ID from the JWT claims.
    /// </summary>
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!
;
    /// <summary>
    /// Places a new order for the authenticated user.
    /// Validates stock availability, reduces inventory, and creates a Stripe payment intent.
    /// </summary>
    /// <param name="dto">List of products and quantities to order.</param>
    /// <returns>201 Created with the new order, or 400/500 on failure.</returns>
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
    /// Admins may view any order; regular users can only view their own.
    /// </summary>
    /// <param name="id">Order GUID.</param>
    /// <returns>200 OK with the order, 403 Forbidden if ownership check fails, 404 if not found.</returns>
    [HttpGet("{id:guid}")]
    [Authorize(AuthenticationSchemes = "AdminCookie,Bearer")]
    public async Task<ActionResult<OrderResponseDto>> GetById(Guid id)
    {
        try
        {
            // Admins can view any order; regular users only their own
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
    /// Updates the lifecycle status of an order (admin only).
    /// </summary>
    /// <param name="id">Order GUID.</param>
    /// <param name="dto">Numeric status value matching <see cref="Domain.Enums.OrderStatus"/>.</param>
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