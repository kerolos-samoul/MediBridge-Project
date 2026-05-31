using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using MediBridge.APIs.Contracts;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MediBridge.APIs.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WeatherForecastController : ControllerBase
    {
        private readonly IWeatherForecastQueryService _weatherForecastQueryService;

        public WeatherForecastController(IWeatherForecastQueryService weatherForecastQueryService)
        {
            _weatherForecastQueryService = weatherForecastQueryService;
        }

        [HttpGet(Name = "GetWeatherForecast")]
        public ActionResult<ApiEnvelope<IEnumerable<WeatherForecast>>> Get([FromQuery, Range(1, 5)] int count = 5)
        {
            var forecasts = _weatherForecastQueryService
                .GetForecasts(count)
                .Select(f => new WeatherForecast
                {
                    Date = f.Date,
                    TemperatureC = f.TemperatureC,
                    Summary = f.Summary
                });

            return Ok(ApiEnvelopeFactory.Create(200, "Success", forecasts));
        }
    }
}
