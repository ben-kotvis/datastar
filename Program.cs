using StarFederation.Datastar;
using StarFederation.Datastar.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDatastar();
// Add services to the container.
builder.Services.AddRazorPages();

builder.Services.AddSingleton<JobBoard.Services.JobService>();
builder.Services.AddSingleton<JobBoard.Services.ApplicantService>();
builder.Services.AddTransient<JobBoard.Services.RazorViewToStringRenderer>();

// Wisconsin weather. The county boundaries and place names load once at startup, and the HTTP
// client is named so the User-Agent api.weather.gov insists on is configured in exactly one place.
builder.Services.AddSingleton<JobBoard.Services.WisconsinGeography>();
builder.Services.AddSingleton<JobBoard.Services.WisconsinWeatherService>();
builder.Services.AddSingleton<JobBoard.Services.NationalWeatherService>();
builder.Services.AddHttpClient(JobBoard.Services.NationalWeatherService.HttpClientName, client =>
{
    client.BaseAddress = new Uri("https://api.weather.gov/");
    client.Timeout = TimeSpan.FromSeconds(20);

    // The NWS asks every caller to identify itself and offers no API key. Point the contact at
    // yourself before running this anywhere real: they use it to reach you about traffic.
    var userAgent = builder.Configuration["Weather:UserAgent"]
        ?? "(JobBoard Datastar sample, https://github.com/ben-kotvis/datastar)";
    client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
    client.DefaultRequestHeaders.Accept.ParseAdd("application/geo+json");
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.UseStaticFiles();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
