namespace ExtractBlueprintSC.Core.Domain.Exceptions;

public sealed class ExtractionException : Exception
{
    public ExtractionException(string message) : base(message) { }
    public ExtractionException(string message, Exception innerException) : base(message, innerException) { }
}
