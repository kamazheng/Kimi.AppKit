namespace Fixture;

public static class TagSetup
{
    public static GadgetOptions Configure(TagOptions tags)
    {
        tags.Tags.Add("a");
        return new GadgetOptions { Label = "x" };
    }
}
