using System.Text.Json;
using Grimoire.Library.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Grimoire.Library.Data;

/// <summary>
/// EF Core context for Grimoire. Includes ASP.NET Identity tables plus the domain
/// tables populated in movement I: artists, artist edges, releases and labels.
/// Credit, UserTaste and Rite are modelled but intentionally not mapped to tables
/// in this pass, because nothing writes them yet.
/// </summary>
public class GrimoireDbContext : IdentityDbContext<GrimoireUser, IdentityRole<Guid>, Guid>
{
    public GrimoireDbContext(DbContextOptions<GrimoireDbContext> options)
        : base(options)
    {
    }

    public DbSet<Artist> Artists => Set<Artist>();

    public DbSet<ArtistEdge> ArtistEdges => Set<ArtistEdge>();

    public DbSet<Release> Releases => Set<Release>();

    public DbSet<Label> Labels => Set<Label>();

    public DbSet<CorpusStat> CorpusStats => Set<CorpusStat>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasPostgresExtension("vector");
        builder.HasPostgresExtension("pg_trgm");

        var linksConverter = new ValueConverter<Dictionary<string, string>?, string?>(
            v => v == null ? null : JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => v == null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonSerializerOptions.Default));

        var linksComparer = new ValueComparer<Dictionary<string, string>?>(
            (a, b) => JsonSerializer.Serialize(a, JsonSerializerOptions.Default) == JsonSerializer.Serialize(b, JsonSerializerOptions.Default),
            v => v == null ? 0 : JsonSerializer.Serialize(v, JsonSerializerOptions.Default).GetHashCode(),
            v => v == null ? null : new Dictionary<string, string>(v));

        builder.Entity<Artist>(entity =>
        {
            entity.ToTable("artists");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Kind).HasConversion<string>().HasMaxLength(16);
            entity.Property(a => a.Rank).HasConversion<string>().HasMaxLength(16);
            entity.Property(a => a.Embedding).HasColumnType("vector(768)");
            entity.Property(a => a.Links)
                .HasConversion(linksConverter, linksComparer)
                .HasColumnType("jsonb");

            entity.HasIndex(a => a.Mbid).IsUnique();

            // Trigram index for fuzzy name search (feature B1).
            entity.HasIndex(a => a.Name)
                .HasMethod("gin")
                .HasOperators("gin_trgm_ops");

            // Approximate-nearest-neighbour index for the discovery engine (movement II).
            entity.HasIndex(a => a.Embedding)
                .HasMethod("hnsw")
                .HasOperators("vector_cosine_ops");
        });

        builder.Entity<ArtistEdge>(entity =>
        {
            entity.ToTable("artist_edges");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Kind).HasConversion<string>().HasMaxLength(24);

            entity.HasOne(e => e.From)
                .WithMany()
                .HasForeignKey(e => e.FromId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.To)
                .WithMany()
                .HasForeignKey(e => e.ToId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.FromId, e.ToId, e.Kind }).IsUnique();
        });

        builder.Entity<Release>(entity =>
        {
            entity.ToTable("releases");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Type).HasConversion<string>().HasMaxLength(16);

            entity.HasIndex(r => r.Mbid).IsUnique();

            entity.HasOne(r => r.Artist)
                .WithMany(a => a.Releases)
                .HasForeignKey(r => r.ArtistId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.Label)
                .WithMany(l => l.Releases)
                .HasForeignKey(r => r.LabelId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Label>(entity =>
        {
            entity.ToTable("labels");
            entity.HasKey(l => l.Id);
            entity.HasIndex(l => l.Mbid).IsUnique();
        });

        builder.Entity<CorpusStat>(entity =>
        {
            entity.ToTable("corpus_stats");
            entity.HasKey(c => c.Id);

            // Single fixed-key row: the id is set explicitly (to CorpusStat.SingletonId),
            // never generated, so upserting the mean is a plain find-or-insert on id = 1.
            entity.Property(c => c.Id).ValueGeneratedNever();
            entity.Property(c => c.MeanEmbedding).HasColumnType("vector(768)");
        });
    }
}
