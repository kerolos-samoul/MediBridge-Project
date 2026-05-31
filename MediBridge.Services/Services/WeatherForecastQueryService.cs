using MediBridge.Services.DTOs;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Services;

public sealed class WeatherForecastQueryService : IWeatherForecastQueryService
{
    private static readonly string[] Summaries = new[]
    {
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    };

    public IReadOnlyList<WeatherForecastDto> GetForecasts(int count)
    {
        return Enumerable.Range(1, count)
            .Select(index => new WeatherForecastDto(
                DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                Random.Shared.Next(-20, 55),
                Summaries[Random.Shared.Next(Summaries.Length)]))
            .ToArray();
    }
}