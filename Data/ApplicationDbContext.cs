
using AcxiomCRM.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Data;

public class ApplicationDbContext
    : IdentityDbContext<ApplicationUser, IdentityRole, string>
{
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // CRM DbSets
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Lead> Leads => Set<Lead>();

    public DbSet<Opportunity> Opportunities => Set<Opportunity>();

    public DbSet<FollowUp> FollowUps => Set<FollowUp>();

    public DbSet<Activity> Activities => Set<Activity>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ==========================================
        // USER: MANAGER AND TEAM MEMBER RELATIONSHIP
        // ==========================================

        builder.Entity<ApplicationUser>()
            .HasOne(user => user.Manager)
            .WithMany(manager => manager.TeamMembers)
            .HasForeignKey(user => user.ManagerId)
            .OnDelete(DeleteBehavior.Restrict);

        // ==========================================
        // CUSTOMER CONFIGURATION
        // ==========================================

        builder.Entity<Customer>(entity =>
        {
            entity.HasKey(c => c.CustomerId);

            entity.HasIndex(c => c.CustomerCode)
                .IsUnique();

            entity.HasIndex(c => c.Email)
                .IsUnique();

            entity.HasIndex(c => c.Phone)
                .IsUnique();

            entity.HasIndex(c => c.AssignedTo);

            entity.Property(c => c.CustomerName)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(c => c.Email)
                .IsRequired()
                .HasMaxLength(254);

            entity.Property(c => c.Phone)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(c => c.CompanyName)
                .HasMaxLength(150);

            entity.Property(c => c.Status)
                .HasMaxLength(30);

            // Customer -> Assigned Application User
            entity.HasOne(c => c.AssignedUser)
                .WithMany()
                .HasForeignKey(c => c.AssignedTo)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ==========================================
        // LEAD CONFIGURATION
        // ==========================================

        builder.Entity<Lead>(entity =>
        {
            entity.HasKey(l => l.LeadId);

            entity.HasIndex(l => l.LeadCode)
                .IsUnique();

            entity.Property(l => l.LeadName)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(l => l.Email)
                .HasMaxLength(150);

            entity.Property(l => l.Phone)
                .HasMaxLength(20);

            entity.Property(l => l.CompanyName)
                .HasMaxLength(150);

            entity.Property(l => l.Source)
                .HasMaxLength(100);

            entity.Property(l => l.Status)
                .HasMaxLength(30);

            entity.Property(l => l.Priority)
                .HasMaxLength(30);

            // Converted Lead -> Customer
            entity.HasOne(l => l.ConvertedCustomer)
                .WithMany()
                .HasForeignKey(l => l.ConvertedCustomerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ==========================================
        // OPPORTUNITY CONFIGURATION
        // ==========================================

        builder.Entity<Opportunity>(entity =>
        {
            entity.HasKey(o => o.OpportunityId);

            entity.Property(o => o.OpportunityName)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(o => o.Amount)
                .HasPrecision(18, 2);

            entity.Property(o => o.Stage)
                .HasMaxLength(50);

            entity.Property(o => o.Status)
                .HasMaxLength(30);

            entity.HasIndex(o => o.Stage);

            // Opportunity -> Customer
            entity.HasOne(o => o.Customer)
                .WithMany(c => c.Opportunities)
                .HasForeignKey(o => o.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Opportunity -> Lead
            entity.HasOne(o => o.Lead)
                .WithMany()
                .HasForeignKey(o => o.LeadId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ==========================================
        // FOLLOW-UP CONFIGURATION
        // ==========================================

        builder.Entity<FollowUp>(entity =>
        {
            entity.HasKey(f => f.FollowUpId);

            entity.Property(f => f.FollowUpType)
                .IsRequired()
                .HasMaxLength(30);

            entity.Property(f => f.Subject)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(f => f.Status)
                .HasMaxLength(30);

            // FollowUp -> Customer
            entity.HasOne(f => f.Customer)
                .WithMany(c => c.FollowUps)
                .HasForeignKey(f => f.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            // FollowUp -> Lead
            entity.HasOne(f => f.Lead)
                .WithMany(l => l.FollowUps)
                .HasForeignKey(f => f.LeadId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ==========================================
        // ACTIVITY CONFIGURATION
        // ==========================================

        builder.Entity<Activity>(entity =>
        {
            entity.HasKey(a => a.ActivityId);

            entity.Property(a => a.ActivityType)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(a => a.Subject)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(a => a.Status)
                .HasMaxLength(30);

            // Activity -> Customer
            entity.HasOne(a => a.Customer)
                .WithMany(c => c.Activities)
                .HasForeignKey(a => a.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Activity -> Lead
            entity.HasOne(a => a.Lead)
                .WithMany(l => l.Activities)
                .HasForeignKey(a => a.LeadId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ==========================================
        // AUDIT LOG CONFIGURATION
        // ==========================================

        builder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(a => a.AuditLogId);

            entity.HasIndex(a => a.CreatedDate);

            entity.HasIndex(a => new
            {
                a.UserId,
                a.Action
            });

            entity.Property(a => a.Action)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(a => a.EntityName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(a => a.Result)
                .HasMaxLength(30);
        });
    }
}
