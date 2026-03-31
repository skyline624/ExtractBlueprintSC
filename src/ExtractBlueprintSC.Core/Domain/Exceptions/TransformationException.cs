namespace ExtractBlueprintSC.Core.Domain.Exceptions;

public sealed class TransformationException : Exception
{
    public TransformationException(string message) : base(message) { }
    public TransformationException(string message, Exception innerException) : base(message, innerException) { }
}
