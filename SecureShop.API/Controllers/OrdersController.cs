using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureShop.Application.DTOs.Orders;
using SecureShop.Application.Services;

namespace SecureShop.API.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly OrderService _svc;
    public OrdersController(OrderService svc) => _svc = svc;

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpPost]
    public async Task<ActionResult<OrderResponseDto>> Create([FromBody] CreateOrderDto dto)
    {
        var result = await _svc.CreateAsync(dto, UserId);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderResponseDto>> GetById(Guid id)
        => Ok(await _svc.GetByIdAsync(id, UserId));

    [HttpGet("my")]
    public async Task<ActionResult<IEnumerable<OrderResponseDto>>> GetMine()
        => Ok(await _svc.GetMyOrdersAsync(UserId));
}