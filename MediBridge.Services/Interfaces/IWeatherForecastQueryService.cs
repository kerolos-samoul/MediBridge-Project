using MediBridge.Services.DTOs;

namespace MediBridge.Services.Interfaces;

public interface IWeatherForecastQueryService
{
    IReadOnlyList<WeatherForecastDto> GetForecasts(int count);
}