namespace Commitcollect.api.Exceptions;

public sealed class StravaAlreadyConnectedException : Exception
{
    public StravaAlreadyConnectedException(string message, Exception inner)
        : base(message, inner) { }
}
