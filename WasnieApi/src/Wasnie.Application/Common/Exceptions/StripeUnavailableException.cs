namespace Wasnie.Application.Common.Exceptions;

public sealed class StripeUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
