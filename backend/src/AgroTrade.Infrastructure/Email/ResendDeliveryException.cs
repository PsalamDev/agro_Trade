namespace AgroTrade.Infrastructure.Email;

/// <summary>
/// Raised when the Resend API refuses a message or cannot be reached. Deliberately not an
/// <c>ApiException</c>: a delivery failure is an infrastructure problem, not a rule the caller
/// broke, and the callers that must not fail their business action catch this and log it.
/// </summary>
public sealed class ResendDeliveryException : Exception
{
    public ResendDeliveryException(string message)
        : base(message)
    {
    }

    public ResendDeliveryException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
