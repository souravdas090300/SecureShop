using FluentAssertions;
using SecureShop.Application.Services;

namespace SecureShop.UnitTests;

public class PaymentIntentIdHelperTests
{
    [Fact]
    public void Sanitize_PlainIntentId_ReturnsUnchanged()
    {
        PaymentIntentIdHelper.Sanitize("pi_3Qxample0000000000000000")
            .Should().Be("pi_3Qxample0000000000000000");
    }

    [Fact]
    public void Sanitize_LegacyClientSecret_StripsSecretSuffix()
    {
        PaymentIntentIdHelper.Sanitize("pi_3Qxample0000000000000000_secret_abc123")
            .Should().Be("pi_3Qxample0000000000000000");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Sanitize_NullOrEmpty_ReturnsEmpty(string? value)
    {
        PaymentIntentIdHelper.Sanitize(value).Should().BeEmpty();
    }

    [Fact]
    public void Mask_LongValue_ReturnsPrefixOnly()
    {
        var masked = PaymentIntentIdHelper.Mask("pi_3Qxample0000000000000000_secret_abc123");
        masked.Should().Be("pi_3Qxam…");
        masked.Should().NotContain("secret");
    }

    [Fact]
    public void Mask_ShortValue_ReturnsFullValue()
    {
        PaymentIntentIdHelper.Mask("pi_123").Should().Be("pi_123");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Mask_NullOrEmpty_ReturnsPlaceholder(string? value)
    {
        PaymentIntentIdHelper.Mask(value).Should().Be("(none)");
    }
}
