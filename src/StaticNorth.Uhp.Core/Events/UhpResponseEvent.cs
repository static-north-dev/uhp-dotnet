namespace StaticNorth.Uhp.Core;

public abstract record UhpResponseEvent(string Type, long SequenceNumber, UhpResponse Response) : UhpEvent(Type, SequenceNumber);
