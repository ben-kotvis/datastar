using System.Text.Json.Serialization;
using JobBoard.Models;
using JobBoard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StarFederation.Datastar.DependencyInjection;

namespace JobBoard.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly JobService _jobService;
        private readonly RazorViewToStringRenderer _renderer;
        private readonly IDatastarService _dataStarService;

        public IndexModel(ILogger<IndexModel> logger, JobService jobService, RazorViewToStringRenderer renderer, IDatastarService dataStarService)
        {
            _logger = logger;
            _jobService = jobService;
            _renderer = renderer;
            _dataStarService = dataStarService;
        }

        public IEnumerable<Job> Jobs { get; set; } = new List<Job>();

        public void OnGet()
        {
            Jobs = _jobService.GetAll();
        }

        public async Task<IActionResult> OnGetSearchAsync(CancellationToken cancellationToken)
        {
            // Datastar sends every signal up as one JSON payload rather than as ordinary query
            // values, so the search term is read from the signals, not from model binding.
            var signals = await _dataStarService.ReadSignalsAsync<SearchSignals>(cancellationToken);
            var query = signals?.Query?.Trim() ?? string.Empty;

            _logger.LogInformation("Job search: {Query}", query);

            var jobs = _jobService.GetAll();
            if (!string.IsNullOrWhiteSpace(query))
            {
                jobs = jobs.Where(job =>
                    job.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    job.Company.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    job.Location.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            var html = await _renderer.RenderViewToStringAsync("_JobList", jobs, PageContext);
            await _dataStarService.PatchElementsAsync(html, cancellationToken);
            return new EmptyResult();
        }

        public record SearchSignals
        {
            [JsonPropertyName("query")]
            public string? Query { get; init; }
        }
    }
}
