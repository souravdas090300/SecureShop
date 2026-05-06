using FluentAssertions;
using SecureShop.Application.DTOs.Auth;
using SecureShop.Application.DTOs.Products;
using SecureShop.Application.Validators;

namespace SecureShop.UnitTests;

public class RegisterValidatorTests
{
    private readonly RegisterValidator _validator = new();

    [Fact]
    public void Validate_ValidDto_Passes()
    {
        var dto = new RegisterDto("John", "Doe", "john@example.com", "P@ssw0rd1");
        _validator.Validate(dto).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "Doe", "john@example.com", "P@ssw0rd1")]   // empty first name
    [InlineData("John", "", "john@example.com", "P@ssw0rd1")]   // empty last name
    public void Validate_MissingName_Fails(string first, string last, string email, string password)
    {
        var dto = new RegisterDto(first, last, email, password);
        _validator.Validate(dto).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("notanemail")]
    [InlineData("missing@")]
    [InlineData("@domain.com")]
    [InlineData("")]
    public void Validate_InvalidEmail_Fails(string email)
    {
        var dto = new RegisterDto("John", "Doe", email, "P@ssw0rd1");
        _validator.Validate(dto).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("short")]           // too short
    [InlineData("alllowercase1!")]  // no uppercase
    [InlineData("ALLUPPERCASE1!")]  // no lowercase
    [InlineData("NoSpecialChar1")]  // no special char
    [InlineData("NoNumber@Abc")]    // no digit
    public void Validate_WeakPassword_Fails(string password)
    {
        var dto = new RegisterDto("John", "Doe", "john@example.com", password);
        _validator.Validate(dto).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_PasswordTooShort_ContainsExpectedError()
    {
        var dto = new RegisterDto("John", "Doe", "john@example.com", "Ab1!");
        var result = _validator.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Password");
    }

    [Fact]
    public void Validate_FirstNameTooLong_Fails()
    {
        var dto = new RegisterDto(new string('A', 101), "Doe", "john@example.com", "P@ssw0rd1");
        _validator.Validate(dto).IsValid.Should().BeFalse();
    }
}

public class CreateProductValidatorTests
{
    private readonly CreateProductValidator _validator = new();

    [Fact]
    public void Validate_ValidDto_Passes()
    {
        var dto = new CreateProductDto("Widget", "A nice widget", 9.99m, 10, "Electronics");
        _validator.Validate(dto).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyName_Fails(string name)
    {
        var dto = new CreateProductDto(name, "desc", 9.99m, 10, "Electronics");
        _validator.Validate(dto).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_NameTooLong_Fails()
    {
        var dto = new CreateProductDto(new string('A', 201), "desc", 9.99m, 10, "Electronics");
        _validator.Validate(dto).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_PriceNotPositive_Fails(decimal price)
    {
        var dto = new CreateProductDto("Widget", "desc", price, 10, "Electronics");
        var result = _validator.Validate(dto);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Price must be > 0"));
    }

    [Fact]
    public void Validate_NegativeStock_Fails()
    {
        var dto = new CreateProductDto("Widget", "desc", 9.99m, -1, "Electronics");
        _validator.Validate(dto).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ZeroStock_Passes()
    {
        var dto = new CreateProductDto("Widget", "desc", 9.99m, 0, "Electronics");
        _validator.Validate(dto).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyCategory_Fails()
    {
        var dto = new CreateProductDto("Widget", "desc", 9.99m, 5, "");
        _validator.Validate(dto).IsValid.Should().BeFalse();
    }
}
