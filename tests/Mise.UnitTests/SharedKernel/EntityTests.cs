namespace Mise.UnitTests.SharedKernel;

file sealed class TestEntity(Guid id) : Entity<Guid>(id);

public class EntityTests
{
    [Fact]
    public void Equals_SameTypeAndId_AreEqual()
    {
        var id = Guid.NewGuid();

        var left = new TestEntity(id);
        var right = new TestEntity(id);

        left.Should().Be(right, because: "entities are equal by identity, not by reference.");
    }

    [Fact]
    public void Equals_DifferentId_AreNotEqual()
    {
        var left = new TestEntity(Guid.NewGuid());
        var right = new TestEntity(Guid.NewGuid());

        left.Should().NotBe(right, because: "two entities with different ids are never the same entity.");
    }
}
