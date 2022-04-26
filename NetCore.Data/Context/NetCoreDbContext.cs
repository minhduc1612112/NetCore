﻿using Microsoft.EntityFrameworkCore;
using NetCore.Data.Configurations;
using NetCore.Data.Entities;
using NetCore.Data.Extensions;
using NetCore.Data.Models;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetCore.Data.Context;

public class NetCoreDbContext : DbContext
{
    public NetCoreDbContext(DbContextOptions<NetCoreDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure using Fluent API
        modelBuilder.ApplyConfiguration(new AuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new CategoryConfiguration());
        modelBuilder.ApplyConfiguration(new InvoiceConfiguration());
        modelBuilder.ApplyConfiguration(new ProductConfiguration());
        modelBuilder.ApplyConfiguration(new ProductInCategoryConfiguration());
        modelBuilder.ApplyConfiguration(new UserConfiguration());

        // Data seeding
        modelBuilder.Seed();
    }

    public DbSet<AuditLog> AuditLogs { set; get; }
    public DbSet<Category> Categories { set; get; }
    public DbSet<Invoice> Invoices { set; get; }
    public DbSet<Product> Products { set; get; }
    public DbSet<ProductInCategory> ProductInCategories { set; get; }
    public DbSet<User> Users { set; get; }

    public virtual async Task<int> SaveChangesAsync(AuditLogCreateDto auditLogCreateDto = null)
    {
        var auditEntries = new List<AuditEntry>();
        if (auditLogCreateDto != null)
            auditEntries = OnBeforeSaveChanges(auditLogCreateDto);
        var result = await base.SaveChangesAsync();
        await OnAfterSaveChanges(auditEntries);
        return result;
    }

    private List<AuditEntry> OnBeforeSaveChanges(AuditLogCreateDto auditLogCreateDto)
    {
        ChangeTracker.DetectChanges();
        var auditEntries = new List<AuditEntry>();
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is AuditLog || entry.State == EntityState.Detached || entry.State == EntityState.Unchanged)
                continue;
            var auditEntry = new AuditEntry(entry)
            {
                Method = auditLogCreateDto.Method,
                TableName = entry.Entity.GetType().Name,
                UserId = auditLogCreateDto.UserId
            };
            auditEntries.Add(auditEntry);
            foreach (var property in entry.Properties)
            {
                if (property.IsTemporary)
                {
                    // value will be generated by the database, get the value after saving
                    auditEntry.TemporaryProperties.Add(property);
                    continue;
                }

                string propertyName = property.Metadata.Name;

                if (property.Metadata.IsPrimaryKey())
                {
                    auditEntry.KeyValues[propertyName] = property.CurrentValue;
                    continue;
                }

                switch (entry.State)
                {
                    case EntityState.Added:
                        auditEntry.Type = AuditLogType.Create;
                        auditEntry.NewValues[propertyName] = property.CurrentValue;
                        break;
                    case EntityState.Deleted:
                        auditEntry.Type = AuditLogType.Delete;
                        auditEntry.OldValues[propertyName] = property.OriginalValue;
                        break;
                    case EntityState.Modified:
                        if (auditEntry.Method == "DELETE")
                        {
                            auditEntry.Type = AuditLogType.Delete;
                            auditEntry.OldValues[propertyName] = property.OriginalValue;
                        }
                        else
                        {
                            if (property.IsModified)
                            {
                                if (JsonConvert.SerializeObject(property.OriginalValue) !=
                                    JsonConvert.SerializeObject(property.CurrentValue))
                                    auditEntry.ChangedColumns.Add(propertyName);
                                auditEntry.Type = AuditLogType.Update;
                                auditEntry.OldValues[propertyName] = property.OriginalValue;
                                auditEntry.NewValues[propertyName] = property.CurrentValue;
                            }
                        }
                        break;
                }
            }
        }
        foreach (var auditEntry in auditEntries.Where(_ => !_.HasTemporaryProperties))
        {
            AuditLogs.Add(auditEntry.ToAudit());
        }

        return auditEntries.Where(_ => _.HasTemporaryProperties).ToList();
    }

    private Task OnAfterSaveChanges(List<AuditEntry> auditEntries)
    {
        if (auditEntries == null || auditEntries.Count == 0)
            return Task.CompletedTask;

        foreach (var auditEntry in auditEntries)
        {
            foreach (var prop in auditEntry.TemporaryProperties)
            {
                if (prop.Metadata.IsPrimaryKey())
                {
                    auditEntry.KeyValues[prop.Metadata.Name] = prop.CurrentValue;
                }
                else
                {
                    auditEntry.NewValues[prop.Metadata.Name] = prop.CurrentValue;
                }
            }

            AuditLogs.Add(auditEntry.ToAudit());
        }

        return SaveChangesAsync();
    }
}