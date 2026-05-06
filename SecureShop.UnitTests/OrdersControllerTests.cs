using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SecureShop.API.Controllers;
using SecureShop.Application.DTOs.Orders;
using SecureShop.Application.Interfaces;
using SecureShop.Application.Services;
using SecureShop.Domain.Entities;
using SecureShop.Domain.Enums;
using SecureShop.Domain.Exceptions;
using System.Security.Claims;

namespace SecureShop.UnitTests;

public class OrdersControllerTests
{
    private static OrderService MakeSvc(Mock<IOrderRepository> orderRepo, Mock<IProductRepository> productRepo, Mock<IPaymentService> payment)
        => new(orderRepo.Object, productRepo.Object, payment.Object);

    private static OrdersController MakeController(OrderService svc, string userId = "user-1")
    {
        var controller = new OrdersController(svc, NullLogger<OrdersController>.Instance);
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
            }
        };
        return controller;
    }

    private static Order MakeOrder(string userId = "user-1")
    {
        var items = new List<OrderItem> { OrderItem.Create(Guid.NewGuid(), 1, 9.99m) };
        return Order.Create(userId, items);
    }

    // ─── GET /api/orders/my ───────────────────────────────────────────────────

    [Fact]
    public async Task GetMine_ReturnsOkWithUserOrders()
    {
        var order = MakeOrder("user-1");
        var orderRepo = new Mock<IOrderRepository>();
        orderRepo.Setup(r => r.GetByUserIdAsync("user-1")).ReturnsAsync([order]);

        var controller = MakeController(MakeSvc(orderRepo, new Mock<IProductRepository>(), new Mock<IPaymentService>()));
        var result = await controller.GetMine();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var orders = ok.Value.Should().BeAssignableTo<IEnumerable<OrderResponseDto>>().Subject;
        orders.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetMine_WhenExceptionThrown_Returns500()
    {
        var orderRepo = new Mock<IOrderRepository>();
        orderRepo.Setup(r => r.GetByUserIdAsync(It.IsAny<string>())).ThrowsAsync(new Exception("DB error"));

        var controller = MakeController(MakeSvc(orderRepo, new Mock<IProductRepository>(), new Mock<IPaymentService>()));
        var result = await controller.GetMine();

        result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(500);
    }

    // ─── GET /api/orders/{id} ─────────────────────────────────────────────────

    [Fact]
    public async Task GetById_OrderExists_ReturnsOk()
    {
        var order = MakeOrder("user-1");
        var orderRepo = new Mock<IOrderRepository>();
        orderRepo.Setup(r => r.GetByIdAsync(order.Id)).ReturnsAsync(order);

        var controller = MakeController(MakeSvc(orderRepo, new Mock<IProductRepository>(), new Mock<IPaymentService>()));
        var result = await controller.GetById(order.Id);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetById_OrderNotFound_Returns404()
    {
        var id = Guid.NewGuid();
        var orderRepo = new Mock<IOrderRepository>();
        orderRepo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((Order?)null);

        var controller = MakeController(MakeSvc(orderRepo, new Mock<IProductRepository>(), new Mock<IPaymentService>()));
        var result = await controller.GetById(id);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetById_WrongUser_ReturnsForbid()
    {
        var order = MakeOrder("other-user");
        var orderRepo = new Mock<IOrderRepository>();
        orderRepo.Setup(r => r.GetByIdAsync(order.Id)).ReturnsAsync(order);

        // controller runs as "user-1" trying to access "other-user"'s order
        var controller = MakeController(MakeSvc(orderRepo, new Mock<IProductRepository>(), new Mock<IPaymentService>()), "user-1");
        var result = await controller.GetById(order.Id);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    // ─── POST /api/orders ─────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ValidOrder_Returns201()
    {
        var product = Product.Create("Widget", "desc", 9.99m, 10, "Electronics");
        var productRepo = new Mock<IProductRepository>();
        productRepo.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync(product);
        productRepo.Setup(r => r.UpdateAsync(It.IsAny<Product>())).Returns(Task.CompletedTask);

        var payment = new Mock<IPaymentService>();
        payment.Setup(p => p.CreatePaymentIntentAsync(It.IsAny<decimal>(), "usd", It.IsAny<Guid>()))
               .ReturnsAsync("pi_secret");

        var orderRepo = new Mock<IOrderRepository>();
        orderRepo.Setup(r => r.CreateAsync(It.IsAny<Order>())).ReturnsAsync((Order o) => o);

        var controller = MakeController(MakeSvc(orderRepo, productRepo, payment));
        var dto = new CreateOrderDto([new OrderItemDto(product.Id, 1)]);

        var result = await controller.Create(dto);

        result.Result.Should().BeOfType<CreatedAtActionResult>();
    }

    [Fact]
    public async Task Create_ProductNotFound_Returns400()
    {
        var missingId = Guid.NewGuid();
        var productRepo = new Mock<IProductRepository>();
        productRepo.Setup(r => r.GetByIdAsync(missingId)).ReturnsAsync((Product?)null);

        var controller = MakeController(MakeSvc(new Mock<IOrderRepository>(), productRepo, new Mock<IPaymentService>()));
        var dto = new CreateOrderDto([new OrderItemDto(missingId, 1)]);

        var result = await controller.Create(dto);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ─── PUT /api/orders/{id}/status ──────────────────────────────────────────

    [Fact]
    public async Task UpdateStatus_OrderExists_ReturnsNoContent()
    {
        var order = MakeOrder();
        var orderRepo = new Mock<IOrderRepository>();
        orderRepo.Setup(r => r.GetByIdAsync(order.Id)).ReturnsAsync(order);
        orderRepo.Setup(r => r.UpdateAsync(It.IsAny<Order>())).Returns(Task.CompletedTask);

        var controller = MakeController(MakeSvc(orderRepo, new Mock<IProductRepository>(), new Mock<IPaymentService>()));
        var result = await controller.UpdateStatus(order.Id, new UpdateOrderStatusDto((int)OrderStatus.Shipped));

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task UpdateStatus_OrderNotFound_Returns404()
    {
        var id = Guid.NewGuid();
        var orderRepo = new Mock<IOrderRepository>();
        orderRepo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((Order?)null);

        var controller = MakeController(MakeSvc(orderRepo, new Mock<IProductRepository>(), new Mock<IPaymentService>()));
        var result = await controller.UpdateStatus(id, new UpdateOrderStatusDto((int)OrderStatus.Paid));

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ─── GET /api/orders (admin) ──────────────────────────────────────────────

    [Fact]
    public async Task GetAll_ReturnsAllOrders()
    {
        var orderRepo = new Mock<IOrderRepository>();
        orderRepo.Setup(r => r.GetAllAsync()).ReturnsAsync([MakeOrder("a"), MakeOrder("b")]);

        var controller = MakeController(MakeSvc(orderRepo, new Mock<IProductRepository>(), new Mock<IPaymentService>()));
        var result = await controller.GetAll();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var orders = ok.Value.Should().BeAssignableTo<IEnumerable<OrderResponseDto>>().Subject;
        orders.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAll_WhenExceptionThrown_Returns500()
    {
        var orderRepo = new Mock<IOrderRepository>();
        orderRepo.Setup(r => r.GetAllAsync()).ThrowsAsync(new Exception("db down"));

        var controller = MakeController(MakeSvc(orderRepo, new Mock<IProductRepository>(), new Mock<IPaymentService>()));
        var result = await controller.GetAll();

        result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(500);
    }

    // ─── POST /api/orders – additional error paths ────────────────────────────

    [Fact]
    public async Task Create_InvalidModel_Returns400()
    {
        var controller = MakeController(MakeSvc(new Mock<IOrderRepository>(), new Mock<IProductRepository>(), new Mock<IPaymentService>()));
        controller.ModelState.AddModelError("Items", "Required");

        var result = await controller.Create(new CreateOrderDto([]));

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Create_InvalidOperationException_Returns400()
    {
        var product = Product.Create("Widget", "desc", 9.99m, 5, "Electronics");
        var productRepo = new Mock<IProductRepository>();
        productRepo.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync(product);
        productRepo.Setup(r => r.UpdateAsync(It.IsAny<Product>()))
                   .ThrowsAsync(new InvalidOperationException("concurrent update conflict"));

        var controller = MakeController(MakeSvc(new Mock<IOrderRepository>(), productRepo, new Mock<IPaymentService>()));
        var dto = new CreateOrderDto([new OrderItemDto(product.Id, 1)]);

        var result = await controller.Create(dto);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Create_UnexpectedException_Returns500()
    {
        var productRepo = new Mock<IProductRepository>();
        productRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ThrowsAsync(new Exception("unexpected"));

        var controller = MakeController(MakeSvc(new Mock<IOrderRepository>(), productRepo, new Mock<IPaymentService>()));
        var dto = new CreateOrderDto([new OrderItemDto(Guid.NewGuid(), 1)]);

        var result = await controller.Create(dto);

        result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(500);
    }

    // ─── GET /api/orders/{id} – admin and error paths ─────────────────────────

    [Fact]
    public async Task GetById_AsAdmin_CanViewAnyUsersOrder()
    {
        var order = MakeOrder("other-user");
        var orderRepo = new Mock<IOrderRepository>();
        orderRepo.Setup(r => r.GetByIdAsync(order.Id)).ReturnsAsync(order);

        var svc = MakeSvc(orderRepo, new Mock<IProductRepository>(), new Mock<IPaymentService>());
        var controller = new OrdersController(svc, NullLogger<OrdersController>.Instance);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "admin-user"),
            new Claim(ClaimTypes.Role, "Admin")
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
            }
        };

        var result = await controller.GetById(order.Id);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetById_UnexpectedException_Returns500()
    {
        var id = Guid.NewGuid();
        var orderRepo = new Mock<IOrderRepository>();
        orderRepo.Setup(r => r.GetByIdAsync(id)).ThrowsAsync(new Exception("db error"));

        var controller = MakeController(MakeSvc(orderRepo, new Mock<IProductRepository>(), new Mock<IPaymentService>()));
        var result = await controller.GetById(id);

        result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(500);
    }

    // ─── PUT /api/orders/{id}/status – error path ────────────────────────────

    [Fact]
    public async Task UpdateStatus_UnexpectedException_Returns500()
    {
        var id = Guid.NewGuid();
        var orderRepo = new Mock<IOrderRepository>();
        orderRepo.Setup(r => r.GetByIdAsync(id)).ThrowsAsync(new Exception("db error"));

        var controller = MakeController(MakeSvc(orderRepo, new Mock<IProductRepository>(), new Mock<IPaymentService>()));
        var result = await controller.UpdateStatus(id, new UpdateOrderStatusDto((int)OrderStatus.Shipped));

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(500);
    }
}
