using CatvIntegrationHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CatvIntegrationHub.Infrastructure.Persistence;

internal sealed class SvCardConfiguration : IEntityTypeConfiguration<SvCard>
{
    public void Configure(EntityTypeBuilder<SvCard> builder)
    {
        builder.ToTable("SvCards", "dbo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseIdentityColumn();
        builder.Property(x => x.CardId).HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(x => x.ChipId).HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(x => x.ProductId).HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(x => x.StartTime).HasColumnType("datetimeoffset(7)");
        builder.Property(x => x.EndTime).HasColumnType("datetimeoffset(7)");
        builder.Property(x => x.RequestHex).HasColumnType("varchar(max)").IsUnicode(false);
        builder.Property(x => x.ResponseHex).HasColumnType("varchar(max)").IsUnicode(false);
        builder.Property(x => x.ResponseJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.SvCardStatusId).HasColumnType("smallint");
        builder.Property(x => x.CreatedDate).HasColumnType("datetime").HasDefaultValueSql("getdate()");
        builder.Property(x => x.UpdatedDate).HasColumnType("datetime");
        builder.HasOne(x => x.SvCardStatus).WithMany(x => x.SvCards)
            .HasForeignKey(x => x.SvCardStatusId).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_SvCards_SvCardStatuses");
    }
}

internal sealed class SvCardStatusConfiguration : IEntityTypeConfiguration<SvCardStatus>
{
    public void Configure(EntityTypeBuilder<SvCardStatus> builder)
    {
        builder.ToTable("SvCardStatuses", "dbo");
        builder.HasKey(x => x.Id).HasName("PK_SvCardStatuses");
        builder.Property(x => x.Id).HasColumnType("smallint").ValueGeneratedNever();
        builder.Property(x => x.Name).HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
    }
}
