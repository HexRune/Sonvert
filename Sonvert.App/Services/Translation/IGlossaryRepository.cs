using System.Collections.Generic;
using System.Threading.Tasks;
using Sonvert.App.Models;

namespace Sonvert.App.Services.Translation;

public interface IGlossaryRepository
{
    Task<List<GlossaryEntry>> GetAllAsync();
    Task AddAsync(string sourceTerm, string targetTerm);
    Task DeleteAsync(int id);
}