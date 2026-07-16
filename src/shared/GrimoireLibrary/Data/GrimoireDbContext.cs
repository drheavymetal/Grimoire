using System.Text.Json;
using Grimoire.Library.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Grimoire.Library.Data;

/// <summary>
/// EF Core context for Grimoire. Includes ASP.NET Identity tables, the domain tables
/// populated in movement I (artists, artist edges, releases, labels), and the movement II
/// discovery tables: user_taste and rites, written by The Rite. Credit remains modelled but
/// unmapped until the credits ETL lands.
/// </summary>
public class GrimoireDbContext : IdentityDbContext<GrimoireUser, IdentityRole<Guid>, Guid>
{
    public GrimoireDbContext(DbContextOptions<GrimoireDbContext> options)
        : base(options)
    {
    }

    public DbSet<Artist> Artists => Set<Artist>();

    public DbSet<ArtistBiography> ArtistBiographies => Set<ArtistBiography>();

    public DbSet<ArtistEdge> ArtistEdges => Set<ArtistEdge>();

    public DbSet<Release> Releases => Set<Release>();

    public DbSet<Label> Labels => Set<Label>();

    public DbSet<CorpusStat> CorpusStats => Set<CorpusStat>();

    public DbSet<UserTaste> UserTastes => Set<UserTaste>();

    public DbSet<Rite> Rites => Set<Rite>();

    public DbSet<Credit> Credits => Set<Credit>();

    public DbSet<Recording> Recordings => Set<Recording>();

    public DbSet<CoverVersion> CoverVersions => Set<CoverVersion>();

    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();

    public DbSet<TasteSnapshot> TasteSnapshots => Set<TasteSnapshot>();

    public DbSet<TasteAnchor> TasteAnchors => Set<TasteAnchor>();

    public DbSet<RefreshTokenRecord> RefreshTokens => Set<RefreshTokenRecord>();

    public DbSet<Friendship> Friendships => Set<Friendship>();

    public DbSet<Notification> Notifications => Set<Notification>();

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

        builder.Entity<ArtistBiography>(entity =>
        {
            entity.ToTable("artist_biographies");

            // One row per (band, language): the composite key makes a re-run's write idempotent and
            // IS the resume marker — a row means "already searched in this edition", matched or not.
            entity.HasKey(b => new { b.ArtistId, b.Language });

            // A bare language code ("es", "no", "fi"): the leading label of the article host.
            entity.Property(b => b.Language).HasMaxLength(16);

            entity.HasOne<Artist>()
                .WithMany(a => a.Biographies)
                .HasForeignKey(b => b.ArtistId)
                .OnDelete(DeleteBehavior.Cascade);
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

        builder.Entity<UserTaste>(entity =>
        {
            entity.ToTable("user_taste");

            // One taste row per user; the user id is the primary key (SPEC §10).
            entity.HasKey(t => t.UserId);
            entity.Property(t => t.UserId).ValueGeneratedNever();

            // Both vectors are already CENTRED (DECISIONS D26): they are built by averaging
            // stored artist embeddings, which the ETL already centred. Never re-centre them.
            entity.Property(t => t.Embedding).HasColumnType("vector(768)");
            entity.Property(t => t.Repulsion).HasColumnType("vector(768)");

            // Delete the taste when the account goes.
            entity.HasOne<GrimoireUser>()
                .WithOne()
                .HasForeignKey<UserTaste>(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Rite>(entity =>
        {
            entity.ToTable("rites");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.State).HasConversion<string>().HasMaxLength(16);

            // The engine's core query filters and the "already rited" exclusion both hit
            // (user_id, artist_id) — SPEC §10.
            entity.HasIndex(r => new { r.UserId, r.ArtistId });

            entity.HasOne<GrimoireUser>()
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Artist>()
                .WithMany()
                .HasForeignKey(r => r.ArtistId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Credit>(entity =>
        {
            entity.ToTable("credits");
            entity.HasKey(c => c.Id);

            // The artist a credit belongs to must exist; the release it is on may be null
            // (a recording-only credit), so that side is set-null on delete. No Recording
            // table exists yet, so RecordingId is a plain nullable column, not a foreign key.
            entity.HasOne<Artist>()
                .WithMany()
                .HasForeignKey(c => c.ArtistId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Release>()
                .WithMany()
                .HasForeignKey(c => c.ReleaseId)
                .OnDelete(DeleteBehavior.SetNull);

            // The credits ETL looks credits up by the artist and by the release they are on.
            entity.HasIndex(c => c.ArtistId);
            entity.HasIndex(c => c.ReleaseId);
        });

        builder.Entity<Recording>(entity =>
        {
            entity.ToTable("recordings");
            entity.HasKey(r => r.Id);

            // A recording MBID is NOT globally unique here (the same recording can be a track
            // on several releases), so the natural key is (release_id, position). Position is
            // 1-based across all of a release's media, so it is unique within a release.
            entity.HasIndex(r => new { r.ReleaseId, r.Position }).IsUnique();

            // The cover graph resolves recordings by MBID, and C7/C21 query them by release.
            entity.HasIndex(r => r.Mbid);

            entity.HasOne(r => r.Release)
                .WithMany()
                .HasForeignKey(r => r.ReleaseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CoverVersion>(entity =>
        {
            entity.ToTable("cover_versions");
            entity.HasKey(c => c.Id);

            // One edge per (original, cover) pair; a repeat import upserts on it.
            entity.HasIndex(c => new { c.OriginalRecordingId, c.CoverRecordingId }).IsUnique();

            // Both endpoints are recordings. PostgreSQL allows two cascade paths to the same
            // table, so deleting either recording drops the edge — no dangling versions.
            entity.HasOne(c => c.Original)
                .WithMany()
                .HasForeignKey(c => c.OriginalRecordingId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(c => c.Cover)
                .WithMany()
                .HasForeignKey(c => c.CoverRecordingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PushSubscription>(entity =>
        {
            entity.ToTable("push_subscriptions");
            entity.HasKey(p => p.Id);

            // A browser endpoint is globally unique; a repeat subscribe upserts on it.
            entity.HasIndex(p => p.Endpoint).IsUnique();
            entity.HasIndex(p => p.UserId);

            // Delete a user's push subscriptions when the account goes (feature B17 delivery).
            entity.HasOne<GrimoireUser>()
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TasteSnapshot>(entity =>
        {
            entity.ToTable("taste_snapshots");
            entity.HasKey(s => s.Id);

            // The snapshot vector is already CENTRED (DECISIONS D26); never re-centred.
            entity.Property(s => s.Embedding).HasColumnType("vector(768)");

            // The trajectory reads a user's snapshots in chronological order (feature C16).
            entity.HasIndex(s => new { s.UserId, s.CreatedAt });

            entity.HasOne<GrimoireUser>()
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TasteAnchor>(entity =>
        {
            entity.ToTable("taste_anchors");

            // One row per (user, band): the composite key makes adding an anchor idempotent
            // and removing it a plain delete (HYBRID taste model).
            entity.HasKey(a => new { a.UserId, a.ArtistId });

            entity.HasOne<GrimoireUser>()
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Artist>()
                .WithMany()
                .HasForeignKey(a => a.ArtistId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // A public handle for friend requests (FRIENDS wave). Unique WHEN SET: the filtered index
        // skips the nulls, so any number of users may have no handle while a claimed one is unique.
        builder.Entity<GrimoireUser>(entity =>
        {
            entity.Property(u => u.Handle).HasMaxLength(30);
            entity.HasIndex(u => u.Handle)
                .IsUnique()
                .HasFilter("handle IS NOT NULL");
        });

        builder.Entity<RefreshTokenRecord>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(r => r.Id);

            // We look a presented token up by its hash on every refresh/logout — unique so a hash
            // maps to exactly one session — and list a user's sessions by user id.
            entity.Property(r => r.TokenHash).HasMaxLength(64);
            entity.HasIndex(r => r.TokenHash).IsUnique();
            entity.HasIndex(r => r.UserId);

            entity.Property(r => r.ReplacedByTokenHash).HasMaxLength(64);

            // Revoke a user's sessions when the account goes.
            entity.HasOne<GrimoireUser>()
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Friendship>(entity =>
        {
            entity.ToTable("friendships");
            entity.HasKey(f => f.Id);
            entity.Property(f => f.Status).HasConversion<string>().HasMaxLength(16);

            // At most one edge per ordered pair; the request/accept/block flows upsert on it.
            entity.HasIndex(f => new { f.RequesterId, f.AddresseeId }).IsUnique();

            // Both endpoints are users. Two cascade paths from AspNetUsers to one table are more than
            // PostgreSQL allows, so these are Restrict: a friendship must be deleted before its users
            // (the app deletes friendships explicitly; accounts are not deleted in normal operation).
            entity.HasOne<GrimoireUser>()
                .WithMany()
                .HasForeignKey(f => f.RequesterId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<GrimoireUser>()
                .WithMany()
                .HasForeignKey(f => f.AddresseeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Notification>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(n => n.Id);
            entity.Property(n => n.Type).HasConversion<string>().HasMaxLength(24);
            entity.Property(n => n.PayloadJson).HasColumnType("jsonb");

            // The inbox lists a user's notifications newest first; the unread count filters on the
            // same user and read_at IS NULL. The filtered index keeps that count cheap.
            entity.HasIndex(n => new { n.UserId, n.CreatedAt })
                .IsDescending(false, true);
            entity.HasIndex(n => n.UserId)
                .HasFilter("read_at IS NULL");

            // The recipient's inbox goes when the account goes.
            entity.HasOne<GrimoireUser>()
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // The actor is a second path from AspNetUsers to this table; PostgreSQL forbids two
            // cascade paths, so this one is Restrict (and nullable — some notifications have no actor).
            entity.HasOne<GrimoireUser>()
                .WithMany()
                .HasForeignKey(n => n.ActorId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
