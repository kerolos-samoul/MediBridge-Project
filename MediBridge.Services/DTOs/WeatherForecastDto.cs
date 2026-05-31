namespace MediBridge.Services.DTOs;

public sealed record WeatherForecastDto(DateOnly Date, int TemperatureC, string? Summary);