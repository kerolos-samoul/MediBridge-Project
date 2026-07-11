namespace MediBridge.Services.Interfaces;

public abstract class Phase8InteractionException : Exception
{
    protected Phase8InteractionException(string message)
        : base(message)
    {
    }
}

public sealed class Phase8BadRequestException : Phase8InteractionException
{
    public Phase8BadRequestException(string message)
        : base(message)
    {
    }
}

public sealed class Phase8ForbiddenException : Phase8InteractionException
{
    public Phase8ForbiddenException(string message)
        : base(message)
    {
    }
}

public sealed class Phase8NotFoundException : Phase8InteractionException
{
    public Phase8NotFoundException(string message)
        : base(message)
    {
    }
}

public sealed class Phase8ConflictException : Phase8InteractionException
{
    public Phase8ConflictException(string message)
        : base(message)
    {
    }
}

public sealed class Phase8ConsistencyException : Phase8InteractionException
{
    public Phase8ConsistencyException(string message)
        : base(message)
    {
    }
}
