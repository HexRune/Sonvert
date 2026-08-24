using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sonvert.App.Data;
using Sonvert.App.Models;

namespace Sonvert.App.Services.Translation;

public class GlossaryRepository : IGlossaryRepository
{
    public async Task<List<GlossaryEntry>> GetAllAsync()
    {
        await using var db = new AppDbContext();
        return await db.GlossaryEntries.ToListAsync();
    }

    public async Task AddAsync(string sourceTerm, string targetTerm)
    {
        await using var db = new AppDbContext();
        db.GlossaryEntries.Add(new GlossaryEntry { SourceTerm = sourceTerm, TargetTerm = targetTerm });
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = new AppDbContext();
        var entry = await db.GlossaryEntries.FindAsync(id);
        if (entry is null) return;
        db.GlossaryEntries.Remove(entry);
        await db.SaveChangesAsync();
    }
}