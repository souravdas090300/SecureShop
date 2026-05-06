namespace SecureShop.Domain.Exceptions;

/// <summary>
/// Represents a business-rule violation raised by the domain layer.
/// These exceptions are caught by the global exception handler and
/// returned to the caller as HTTP 400 Bad Request responses.
/// </summary>
public class DomainException : Exception
{
    /// <summary>
    /// Initialises a new <see cref="DomainException"/> with the
    /// human-readable <paramref name="message"/> describing the rule violation.
    /// </summary>
    /// <param name="message">Description of the violated domain rule.</param>
    public DomainException(string message) : base(message) { }
}
