namespace MediBridge.Services.Interfaces;

public abstract class Phase7WorkflowException : Exception
{
    protected Phase7WorkflowException(string message) : base(message) { }
    protected Phase7WorkflowException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class Phase7BadRequestException : Phase7WorkflowException
{
    public Phase7BadRequestException(string message) : base(message) { }
}

public sealed class Phase7ForbiddenException : Phase7WorkflowException
{
    public Phase7ForbiddenException(string message) : base(message) { }
}

public sealed class Phase7NotFoundException : Phase7WorkflowException
{
    public Phase7NotFoundException(string message) : base(message) { }
}

public sealed class Phase7ConflictException : Phase7WorkflowException
{
    public Phase7ConflictException(string message) : base(message) { }
}

public sealed class Phase7StorageUnavailableException : Phase7WorkflowException
{
    public Phase7StorageUnavailableException(string message, Exception innerException) : base(message, innerException) { }
}
