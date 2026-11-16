namespace WattWatch.Core.Monitor;

/// <summary>A device the app can see but has no battery reader for.</summary>
public sealed record OtherDevice(string Name, string Id, string Transport, string Reason);
