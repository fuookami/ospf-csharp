#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Plugin.Persistence.EfCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Framework.Plugin.Tests.Persistence.Expression;

public class EfCoreRepositoryTests {
    private record TestEntity(string Name, int Value);

    [Fact]
    public async Task AddAsync_Should_Add_Entity() {
        var repo = new EfCoreRepository<TestEntity>();
        var entity = new TestEntity("test", 42);
        await repo.AddAsync(entity);
        IReadOnlyList<TestEntity> all = await repo.GetAllAsync();
        all.Should().ContainSingle();
    }

    [Fact]
    public async Task FindAsync_Should_Filter() {
        var repo = new EfCoreRepository<TestEntity>();
        await repo.AddAsync(new TestEntity("a", 1));
        await repo.AddAsync(new TestEntity("b", 2));
        IReadOnlyList<TestEntity> result = await repo.FindAsync(e => e.Value > 1);
        result.Should().ContainSingle();
    }
}
