using Grimoire.Server.Dtos;
using Grimoire.Server.Services;
using Xunit;

namespace Grimoire.Tests;

/// <summary>
/// Grouping a composer's works by kind (movement VII, D11). These bite on the one decision that
/// matters: a null kind is a real "unclassified" group that is kept and shown last, never dropped
/// or hidden (1879 of 2291 works have no MusicBrainz type).
/// </summary>
public class WorkGroupingTests
{
    private static WorkGrouping.WorkRow Row(string title, string? kind)
    {
        return new WorkGrouping.WorkRow(Guid.NewGuid(), Guid.NewGuid(), title, kind);
    }

    [Fact]
    public void Group_KeepsUntypedWorksInTheirOwnGroupWithNullKind()
    {
        var rows = new[]
        {
            Row("Symphony No. 5", "Symphony"),
            Row("WoO 59", null),
            Row("Hess 40", null),
        };

        IReadOnlyList<WorkGroupDto> groups = WorkGrouping.Group(rows);

        // Two groups: the named one and the unclassified one — the nulls are not lost.
        Assert.Equal(2, groups.Count);
        WorkGroupDto unclassified = Assert.Single(groups, g => g.Kind is null);
        Assert.Equal(2, unclassified.Works.Count);
    }

    [Fact]
    public void Group_PutsTheUnclassifiedGroupLast()
    {
        var rows = new[]
        {
            Row("Untitled", null),
            Row("Prelude", "Prelude"),
            Row("Aria", "Opera"),
        };

        IReadOnlyList<WorkGroupDto> groups = WorkGrouping.Group(rows);

        // Named kinds come first (alphabetical), unclassified always trails. Move the null-first
        // ordering key and this fails.
        Assert.Equal("Opera", groups[0].Kind);
        Assert.Equal("Prelude", groups[1].Kind);
        Assert.Null(groups[2].Kind);
    }

    [Fact]
    public void Group_MergesSameKindCaseInsensitively()
    {
        var rows = new[]
        {
            Row("Sonata A", "Sonata"),
            Row("Sonata B", "sonata"),
        };

        IReadOnlyList<WorkGroupDto> groups = WorkGrouping.Group(rows);

        // One group, two works — "Sonata" and "sonata" are the same kind, not two.
        WorkGroupDto group = Assert.Single(groups);
        Assert.Equal(2, group.Works.Count);
    }

    [Fact]
    public void Group_TreatsBlankKindAsUnclassified()
    {
        var rows = new[]
        {
            Row("A", "   "),
            Row("B", ""),
        };

        IReadOnlyList<WorkGroupDto> groups = WorkGrouping.Group(rows);

        // Whitespace is not a real type; it folds into the unclassified group (kind null).
        WorkGroupDto group = Assert.Single(groups);
        Assert.Null(group.Kind);
        Assert.Equal(2, group.Works.Count);
    }

    [Fact]
    public void Group_OrdersWorksByTitleWithinAGroup()
    {
        var rows = new[]
        {
            Row("Zarabande", "Suite"),
            Row("Allemande", "Suite"),
            Row("Courante", "Suite"),
        };

        IReadOnlyList<WorkGroupDto> groups = WorkGrouping.Group(rows);

        WorkGroupDto suite = Assert.Single(groups);
        Assert.Equal(["Allemande", "Courante", "Zarabande"], suite.Works.Select(w => w.Title));
    }

    [Fact]
    public void Group_EmptyInputYieldsNoGroups()
    {
        Assert.Empty(WorkGrouping.Group([]));
    }
}
