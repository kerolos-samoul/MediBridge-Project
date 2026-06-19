namespace MediBridge.Services.Interfaces;

public abstract class Phase5WorkflowException : Exception
{
    protected Phase5WorkflowException(string message)
        : base(message)
    {
    }
}

public sealed class Phase5ValidationException : Phase5WorkflowException
{
    public Phase5ValidationException(string message, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        Errors = errors ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> Errors { get; }
}

public sealed class Phase5ConflictException : Phase5WorkflowException
{
    public Phase5ConflictException(string message)
        : base(message)
    {
    }
}

public sealed class Phase5NotFoundException : Phase5WorkflowException
{
    public Phase5NotFoundException(string message)
        : base(message)
    {
    }
}

public sealed class Phase5ForbiddenException : Phase5WorkflowException
{
    public Phase5ForbiddenException(string message)
        : base(message)
    {
    }
}
