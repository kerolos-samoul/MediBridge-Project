namespace MediBridge.Services.Interfaces;

public class WorkflowValidationException : Exception
{
    public WorkflowValidationException(string message = "Validation failed.") : base(message)
    {
    }
}

public class WorkflowConflictException : Exception
{
    public WorkflowConflictException(string message = "Conflict.") : base(message)
    {
    }
}

public class WorkflowNotFoundException : Exception
{
    public WorkflowNotFoundException(string message = "Not found.") : base(message)
    {
    }
}

public class WorkflowForbiddenException : Exception
{
    public WorkflowForbiddenException(string message = "Forbidden.") : base(message)
    {
    }
}

public class WorkflowUnauthorizedException : Exception
{
    public WorkflowUnauthorizedException(string message = "Authentication denied.") : base(message)
    {
    }
}
