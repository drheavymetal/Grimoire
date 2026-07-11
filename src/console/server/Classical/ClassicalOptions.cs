namespace Grimoire.Worker.Classical;

/// <summary>Options for the <c>classical</c> verb (movement VII, D11).</summary>
public sealed class ClassicalOptions
{
    /// <summary>
    /// Maximum works imported per composer in one browse page. Canonical composers have thousands
    /// of works; a bounded page gives a composer's page substance without pulling the whole catalogue.
    /// Default 100; override with <c>Classical:WorksPerComposer</c> or GRIMOIRE_CLASSICAL_WORKS.
    /// </summary>
    public int WorksPerComposer { get; init; } = 100;
}
