using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Services;

namespace Persistence;

internal sealed class WishlistItemConfiguration : IEntityTypeConfiguration<WishlistItemEntity>
{
    public void Configure(EntityTypeBuilder<WishlistItemEntity> builder)
    {
        builder.ToTable("wishlists");
        builder.HasKey(entity => entity.id);
        builder.Property(entity => entity.id).HasColumnName("id").HasMaxLength(64);
        builder.Property(entity => entity.ChildId).HasColumnName("child_id").HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.DedupeKey).HasColumnName("dedupe_key").HasMaxLength(256).IsRequired();
        builder.Property(entity => entity.RequestType).HasColumnName("request_type").HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.Text).HasColumnName("text").IsRequired();
        builder.Property(entity => entity.Category).HasColumnName("category");
        builder.Property(entity => entity.BudgetEstimate).HasColumnName("budget_estimate");
        builder.Property(entity => entity.StatusChange).HasColumnName("status_change").HasMaxLength(64);
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Ignore(entity => entity.PartitionKeyValue);
        builder.HasIndex(entity => new { entity.ChildId, entity.CreatedAt })
            .HasDatabaseName("ix_wishlists_child_id_created_at");
    }
}

internal sealed class WishlistOutboxEventConfiguration : IEntityTypeConfiguration<WishlistOutboxEvent>
{
    public void Configure(EntityTypeBuilder<WishlistOutboxEvent> builder)
    {
        builder.ToTable("wishlist_events");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasColumnName("id").HasMaxLength(64);
        builder.Property(entity => entity.ChildId).HasColumnName("child_id").HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.Text).HasColumnName("text").IsRequired();
        builder.Property(entity => entity.Category).HasColumnName("category");
        builder.Property(entity => entity.BudgetEstimate).HasColumnName("budget_estimate");
        builder.Property(entity => entity.Type).HasColumnName("type").HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.StatusChange).HasColumnName("status_change").HasMaxLength(64);
        builder.Property(entity => entity.DedupeKey).HasColumnName("dedupe_key").HasMaxLength(256).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.HasIndex(entity => new { entity.ChildId, entity.CreatedAt })
            .HasDatabaseName("ix_wishlist_events_child_id_created_at");
    }
}

internal sealed class ProfileSnapshotConfiguration : IEntityTypeConfiguration<ProfileSnapshotEntity>
{
    public void Configure(EntityTypeBuilder<ProfileSnapshotEntity> builder)
    {
        builder.ToTable("profile_snapshots");
        builder.HasKey(entity => entity.id);
        builder.Property(entity => entity.id).HasColumnName("id").HasMaxLength(64);
        builder.Property(entity => entity.ChildId).HasColumnName("child_id").HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").IsRequired();
        var preferences = builder.Property(entity => entity.Preferences)
            .HasColumnName("preferences")
            .HasColumnType("jsonb")
            .HasConversion(JsonValueConverters.CreateConverter<string>());
        preferences.Metadata.SetValueComparer(JsonValueConverters.CreateComparer<string>());
        builder.Property(entity => entity.BudgetCeiling).HasColumnName("budget_ceiling");
        builder.Property(entity => entity.BehaviorSummary).HasColumnName("behavior_summary");
        builder.Property(entity => entity.EnrichmentSource).HasColumnName("enrichment_source").HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.FallbackUsed).HasColumnName("fallback_used").IsRequired();
        builder.Ignore(entity => entity.PartitionKeyValue);
        builder.HasIndex(entity => new { entity.ChildId, entity.CreatedAt })
            .HasDatabaseName("ix_profile_snapshots_child_id_created_at");
    }
}

internal sealed class RecommendationSetConfiguration : IEntityTypeConfiguration<RecommendationSetEntity>
{
    public void Configure(EntityTypeBuilder<RecommendationSetEntity> builder)
    {
        builder.ToTable("recommendations");
        builder.HasKey(entity => entity.id);
        builder.Property(entity => entity.id).HasColumnName("id").HasMaxLength(64);
        builder.Property(entity => entity.ChildId).HasColumnName("child_id").HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(entity => entity.ProfileSnapshotId).HasColumnName("profile_snapshot_id").HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.FallbackUsed).HasColumnName("fallback_used").IsRequired();
        builder.Property(entity => entity.GenerationSource).HasColumnName("generation_source").HasMaxLength(64).IsRequired();
        var items = builder.Property(entity => entity.Items)
            .HasColumnName("items")
            .HasColumnType("jsonb")
            .HasConversion(JsonValueConverters.CreateConverter<RecommendationItemEntity>());
        items.Metadata.SetValueComparer(JsonValueConverters.CreateComparer<RecommendationItemEntity>());
        builder.Ignore(entity => entity.PartitionKeyValue);
        builder.HasIndex(entity => new { entity.ChildId, entity.CreatedAt })
            .HasDatabaseName("ix_recommendations_child_id_created_at");
    }
}

internal sealed class LogisticsAssessmentConfiguration : IEntityTypeConfiguration<LogisticsAssessmentEntity>
{
    public void Configure(EntityTypeBuilder<LogisticsAssessmentEntity> builder)
    {
        builder.ToTable("logistics_assessments");
        builder.HasKey(entity => entity.id);
        builder.Property(entity => entity.id).HasColumnName("id").HasMaxLength(64);
        builder.Property(entity => entity.ChildId).HasColumnName("child_id").HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.RecommendationSetId)
            .HasColumnName("recommendation_set_id")
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(entity => entity.CheckedAt).HasColumnName("checked_at").IsRequired();
        builder.Property(entity => entity.OverallStatus)
            .HasColumnName("overall_status")
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(entity => entity.FallbackUsed).HasColumnName("fallback_used").IsRequired();
        var items = builder.Property(entity => entity.Items)
            .HasColumnName("items")
            .HasColumnType("jsonb")
            .HasConversion(JsonValueConverters.CreateConverter<LogisticsAssessmentItemEntity>());
        items.Metadata.SetValueComparer(JsonValueConverters.CreateComparer<LogisticsAssessmentItemEntity>());
        builder.Ignore(entity => entity.PartitionKeyValue);
        builder.HasIndex(entity => new { entity.ChildId, entity.CheckedAt })
            .HasDatabaseName("ix_logistics_assessments_child_id_checked_at");
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<NotificationEntity>
{
    public void Configure(EntityTypeBuilder<NotificationEntity> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(entity => entity.id);
        builder.Property(entity => entity.id).HasColumnName("id").HasMaxLength(64);
        builder.Property(entity => entity.ChildId).HasColumnName("child_id").HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.Type).HasColumnName("type").HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.Message).HasColumnName("message").IsRequired();
        builder.Property(entity => entity.RelatedId).HasColumnName("related_id").HasMaxLength(64);
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(entity => entity.State).HasColumnName("state").HasMaxLength(32).IsRequired();
        builder.Ignore(entity => entity.PartitionKeyValue);
        builder.HasIndex(entity => new { entity.ChildId, entity.CreatedAt })
            .HasDatabaseName("ix_notifications_child_id_created_at");
    }
}

internal static class JsonValueConverters
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static ValueConverter<List<T>, string> CreateConverter<T>() =>
        new(
            value => JsonSerializer.Serialize(value, SerializerOptions),
            value => JsonSerializer.Deserialize<List<T>>(value, SerializerOptions) ?? new List<T>());

    public static ValueComparer<List<T>> CreateComparer<T>() =>
        new(
            (left, right) => Serialize(left) == Serialize(right),
            value => Serialize(value).GetHashCode(StringComparison.Ordinal),
            value => JsonSerializer.Deserialize<List<T>>(Serialize(value), SerializerOptions) ?? new List<T>());

    private static string Serialize<T>(List<T>? value) => JsonSerializer.Serialize(value, SerializerOptions);
}
