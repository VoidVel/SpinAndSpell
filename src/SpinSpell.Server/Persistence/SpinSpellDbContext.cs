using Microsoft.EntityFrameworkCore;

namespace SpinSpell.Server.Persistence;

public sealed class SpinSpellDbContext(DbContextOptions<SpinSpellDbContext> options) : DbContext(options)
{
    public DbSet<RoomRecord> Rooms => Set<RoomRecord>();

    public DbSet<GameResultRecord> GameResults => Set<GameResultRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Columns are named explicitly rather than left to EF's PascalCase
        // defaults, so raw SQL in psql — which is how you will actually read this
        // when diagnosing a game — needs no quoting.
        builder.Entity<RoomRecord>(room =>
        {
            room.ToTable("rooms");
            room.HasKey(r => r.Id);

            room.Property(r => r.Id).HasColumnName("id");
            room.Property(r => r.Code).HasColumnName("code");
            room.Property(r => r.Status).HasColumnName("status");
            room.Property(r => r.State).HasColumnName("state").HasColumnType("jsonb");
            room.Property(r => r.CreatedAt).HasColumnName("created_at");
            room.Property(r => r.UpdatedAt).HasColumnName("updated_at");

            // Codes are unique only among live rooms — an ended room releases its
            // code back into circulation, which keeps codes short and memorable.
            room.HasIndex(r => r.Code)
                .IsUnique()
                .HasDatabaseName("ix_rooms_code_active")
                .HasFilter($"status = '{RoomStatus.Active}'");
        });

        builder.Entity<GameResultRecord>(result =>
        {
            result.ToTable("game_results");
            result.HasKey(r => r.RoomId);

            result.Property(r => r.RoomId).HasColumnName("room_id");
            result.Property(r => r.PlayerCount).HasColumnName("player_count");
            result.Property(r => r.RoundsPlayed).HasColumnName("rounds_played");
            result.Property(r => r.Skips).HasColumnName("skips");
            result.Property(r => r.QuestionsSubmitted).HasColumnName("questions_submitted");
            result.Property(r => r.SecondsToFirstChallenge).HasColumnName("seconds_to_first_challenge");
            result.Property(r => r.DurationSeconds).HasColumnName("duration_seconds");
            result.Property(r => r.EndedAt).HasColumnName("ended_at");

            result.HasOne<RoomRecord>()
                .WithOne()
                .HasForeignKey<GameResultRecord>(r => r.RoomId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
