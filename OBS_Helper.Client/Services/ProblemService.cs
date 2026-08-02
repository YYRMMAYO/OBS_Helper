using System.Net.Http.Json;
using OBS_Helper.Client.Models;

namespace OBS_Helper.Client.Services;

public class ProblemService
{
    private readonly HttpClient _http;
    private ProblemData? _data;

    public ProblemService(HttpClient http) => _http = http;

    public async Task<ProblemData> GetDataAsync()
    {
        if (_data is null)
        {
            _data = await _http.GetFromJsonAsync<ProblemData>("data/problems.json") ?? new ProblemData();
        }
        return _data;
    }

    public async Task<List<Category>> GetCategoriesAsync() => (await GetDataAsync()).Categories;

    public async Task<List<Problem>> GetProblemsAsync() => (await GetDataAsync()).Problems;

    public async Task<Problem?> GetByIdAsync(string id)
    {
        var data = await GetDataAsync();
        return data.Problems.FirstOrDefault(p => p.Id == id);
    }

    public async Task<Category?> GetCategoryAsync(string id)
    {
        var data = await GetDataAsync();
        return data.Categories.FirstOrDefault(c => c.Id == id);
    }

    public async Task<List<Problem>> GetByCategoryAsync(string categoryId)
    {
        var data = await GetDataAsync();
        return data.Problems.Where(p => p.Category == categoryId).ToList();
    }

    public async Task<List<Problem>> SearchAsync(string query)
    {
        var data = await GetDataAsync();
        if (string.IsNullOrWhiteSpace(query)) return data.Problems;
        var q = query.Trim().ToLowerInvariant();
        var catTitles = data.Categories.ToDictionary(c => c.Id, c => c.Title);
        return data.Problems
            .Where(p => BuildText(p, catTitles.GetValueOrDefault(p.Category, "")).Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static string BuildText(Problem p, string categoryTitle)
    {
        return string.Join(" ",
            new[] { p.Title, categoryTitle }
                .Concat(p.Symptoms)
                .Concat(p.Causes)
                .Concat(p.Steps.Select(s => s.Title + " " + s.Detail))
                .Concat(p.Tips)
                .Concat(p.Platforms));
    }
}
