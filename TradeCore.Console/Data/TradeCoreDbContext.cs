using Microsoft.EntityFrameworkCore;
using TradeCore.Console.Models;

namespace TradeCore.Console.Data;

public sealed class TradeCoreDbContext(DbContextOptions<TradeCoreDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Stock> Stocks => Set<Stock>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Trade> Trades => Set<Trade>();

    public DbSet<PortfolioPosition> PortfolioPositions => Set<PortfolioPosition>();

    public DbSet<PortfolioTransfer> PortfolioTransfers => Set<PortfolioTransfer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Username).IsRequired().HasMaxLength(50);
            entity.Property(user => user.Email).IsRequired().HasMaxLength(254);
            entity.Property(user => user.CreatedAt).IsRequired();
            entity.HasIndex(user => user.Email).IsUnique();
        });

        modelBuilder.Entity<Account>(entity =>
        {
            entity.ToTable("Accounts");
            entity.HasKey(account => account.Id);
            entity.Property(account => account.UserId).IsRequired();
            entity.Property(account => account.AccountNumber).IsRequired().HasMaxLength(50);
            entity.Property(account => account.Balance).HasPrecision(18, 4);
            entity.Property(account => account.CreatedAt).IsRequired();
            entity.HasOne<User>()
                .WithOne()
                .HasForeignKey<Account>(account => account.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Stock>(entity =>
        {
            entity.ToTable("Stocks");
            entity.HasKey(stock => stock.Id);
            entity.Property(stock => stock.Symbol).IsRequired().HasMaxLength(20);
            entity.Property(stock => stock.Name).IsRequired().HasMaxLength(200);
            entity.Property(stock => stock.CurrentPrice).HasPrecision(18, 4);
            entity.HasIndex(stock => stock.Symbol).IsUnique();
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasKey(order => order.Id);
            entity.Property(order => order.AccountId).IsRequired();
            entity.Property(order => order.StockId).IsRequired();
            entity.Property(order => order.Type).HasConversion<string>().HasMaxLength(10);
            entity.Property(order => order.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(order => order.Quantity).IsRequired();
            entity.Property(order => order.Price).HasPrecision(18, 4);
            entity.Property(order => order.CreatedAt).IsRequired();
            entity.HasOne<Account>()
                .WithMany()
                .HasForeignKey(order => order.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Stock>()
                .WithMany()
                .HasForeignKey(order => order.StockId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Trade>(entity =>
        {
            entity.ToTable("Trades");
            entity.HasKey(trade => trade.Id);
            entity.Property(trade => trade.BuyOrderId).IsRequired();
            entity.Property(trade => trade.SellOrderId).IsRequired();
            entity.Property(trade => trade.StockId).IsRequired();
            entity.Property(trade => trade.Quantity).IsRequired();
            entity.Property(trade => trade.Price).HasPrecision(18, 4);
            entity.Property(trade => trade.ExecutedAt).IsRequired();
            entity.HasOne<Order>()
                .WithMany()
                .HasForeignKey(trade => trade.BuyOrderId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Order>()
                .WithMany()
                .HasForeignKey(trade => trade.SellOrderId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Stock>()
                .WithMany()
                .HasForeignKey(trade => trade.StockId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PortfolioPosition>(entity =>
        {
            entity.ToTable("PortfolioPositions");
            entity.HasKey(position => position.Id);
            entity.Property(position => position.AccountId).IsRequired();
            entity.Property(position => position.StockId).IsRequired();
            entity.Property(position => position.Quantity).IsRequired();
            entity.Property(position => position.AveragePrice).HasPrecision(18, 4);
            entity.HasIndex(position => new { position.AccountId, position.StockId }).IsUnique();
            entity.HasOne<Account>()
                .WithMany()
                .HasForeignKey(position => position.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Stock>()
                .WithMany()
                .HasForeignKey(position => position.StockId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PortfolioTransfer>(entity =>
        {
            entity.ToTable("PortfolioTransfers");
            entity.HasKey(transfer => transfer.Id);
            entity.Property(transfer => transfer.AccountId).IsRequired();
            entity.Property(transfer => transfer.StockId).IsRequired();
            entity.Property(transfer => transfer.Quantity).IsRequired();
            entity.Property(transfer => transfer.AveragePrice).HasPrecision(18, 4);
            entity.Property(transfer => transfer.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(transfer => transfer.CreatedAt).IsRequired();
            entity.HasOne<Account>().WithMany().HasForeignKey(transfer => transfer.AccountId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Stock>().WithMany().HasForeignKey(transfer => transfer.StockId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(transfer => transfer.AccountId);
            entity.HasIndex(transfer => transfer.StockId);
        });
    }
}
