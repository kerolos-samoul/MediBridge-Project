namespace MediBridge.Services.Interfaces;

public abstract class Phase9WorkflowException : Exception
{
    protected Phase9WorkflowException(string message) : base(message) { }
    protected Phase9WorkflowException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class Phase9ValidationException : Phase9WorkflowException
{
    public Phase9ValidationException(string message) : base(message) { }
}

public sealed class Phase9ForbiddenException : Phase9WorkflowException
{
    public Phase9ForbiddenException(string message) : base(message) { }
}

public sealed class Phase9NotFoundException : Phase9WorkflowException
{
    public Phase9NotFoundException(string message) : base(message) { }
}

public sealed class Phase9ConflictException : Phase9WorkflowException
{
    public Phase9ConflictException(string message) : base(message) { }
}

public sealed class Phase9ServiceUnavailableException : Phase9WorkflowException
{
    public Phase9ServiceUnavailableException(string message) : base(message) { }
    public Phase9ServiceUnavailableException(string message, Exception innerException) : base(message, innerException) { }
}
