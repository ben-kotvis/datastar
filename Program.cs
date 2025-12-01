using StarFederation.Datastar;
using StarFederation.Datastar.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDatastar();
// Add services to the container.
builder.Services.AddRazorPages();

builder.Services.AddSingleton<JobBoard.Services.JobService>();
builder.Services.AddSingleton<JobBoard.Services.ApplicantService>();
builder.Services.AddTransient<JobBoard.Services.RazorViewToStringRenderer>();

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
