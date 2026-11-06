namespace SecureShop.Application.Services;

/// <summary>
/// Helpers for Stripe PaymentIntent identifiers. Older orders may have stored
/// the PaymentIntent <em>client secret</em> (which embeds the real ID followed
/// by "_secret_...") in the intent-ID column; these helpers strip that suffix
/// before calling Stripe and mask values before logging so secrets never
/// reach log sinks.
/// </summary>
public static class PaymentIntentIdHelper
{
    private const string SecretMarker = "_secret_";
    private const int MaskPrefixLength = 8;

    /// <summary>
    /// Returns the bare PaymentIntent ID, stripping any "_secret_..." suffix a
    /// legacy client-secret value may carry. Returns the input unchanged when
    /// no marker is present.
    /// </summary>
    public static string Sanitize(string? paymentIntentId)
    {
        if (string.IsNullOrEmpty(paymentIntentId))
            return paymentIntentId ?? string.Empty;

        var index = paymentIntentId.IndexOf(SecretMarker, StringComparison.Ordinal);
        return index >= 0 ? paymentIntentId[..index] : paymentIntentId;
    }

    /// <summary>
    /// Returns a log-safe representation: the first
    /// <see cref="MaskPrefixLength"/> characters followed by an ellipsis for
    /// longer values. Never returns the full identifier.
    /// </summary>
    public static string Mask(string? paymentIntentId)
    {
        if (string.IsNullOrEmpty(paymentIntentId))
            return "(none)";

        var sanitized = Sanitize(paymentIntentId);
        return sanitized.Length <= MaskPrefixLength
            ? sanitized
            : sanitized[..MaskPrefixLength] + "…";
    }
}
