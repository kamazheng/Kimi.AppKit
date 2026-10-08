namespace Fixture;

public sealed class GadgetService(GadgetOptions options)
{
    public string Describe() => $"{options.Label}:{options.Capacity}";
}
