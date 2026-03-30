namespace SecureShop.Domain.Entities;

public class OrderItem
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public Product? Product { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }

    private OrderItem() { }

    public static OrderItem Create(Guid productId, int quantity, decimal unitPrice)
    {
        return new OrderItem
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            Quantity = quantity,
            UnitPrice = unitPrice
        };
    }
}