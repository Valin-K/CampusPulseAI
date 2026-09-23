using CampusPulse.Shared.Rag.Generation;

namespace CampusPulse.Shared.Rag.Tests;

public sealed class RagPersonasTests
{
    [Test]
    public void All_ContainsEveryFeatureCollection()
    {
        Assert.That(
            RagFeature.All.Select(feature => feature.Collection),
            Is.EquivalentTo(new[]
            {
                "academicrecords",
                "coursemanagement",
                "campushousing",
                "financialaidbilling",
                "eventscheduling",
            }));
    }

    [Test]
    public void EveryFeature_HasANonEmptyPersona()
    {
        Assert.That(RagFeature.All, Has.All.Property(nameof(RagFeature.Persona)).Not.Empty);
    }

    [Test]
    public void EventScheduling_MapsToItsCollection()
    {
        Assert.That(RagFeature.EventScheduling.Collection, Is.EqualTo("eventscheduling"));
    }
}
