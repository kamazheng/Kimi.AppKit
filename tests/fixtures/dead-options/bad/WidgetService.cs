namespace Fixture;

public sealed class WidgetService(WidgetOptions options)
{
    public int Limit => options.LiveLimit;
}
