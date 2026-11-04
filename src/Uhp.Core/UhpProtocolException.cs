namespace Uhp.Core;

public class UhpProtocolException(string message, Exception? innerException = null) : Exception(message, innerException);
