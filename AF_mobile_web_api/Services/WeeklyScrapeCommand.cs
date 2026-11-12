using AF_mobile_web_api.DTO.Enums;
using AF_mobile_web_api.Repositories.Interfaces;
using AF_mobile_web_api.Services.Interfaces;
using ApplicationDatabase.Models;

namespace AF_mobile_web_api.Services;

internal static class WeeklyScrapeCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, long runId)
    {
        await using var statusScope = services.CreateAsyncScope();
        var repository = statusScope.ServiceProvider.GetRequiredService<IPropertyDataRepository>();
        var saved = (await repository.GetWeeklyCitiesAsync(runId)).ToDictionary(result => result.City);
        var failed = false;

        foreach (var city in Enum.GetValues<CityEnum>())
        {
            if (!saved.TryGetValue(city.ToString(), out var result))
            {
                await using var cityScope = services.CreateAsyncScope();
                var scraper = cityScope.ServiceProvider.GetRequiredService<IRealEstateServices>();
                try
                {
                    await scraper.ScrapeWeeklyCityAsync(runId, city);
                    result = await ReadResultAsync(repository, runId, city)
                        ?? throw new InvalidOperationException($"Missing saved result for {city}.");
                }
                catch (AggregateException)
                {
                    // An incomplete download still commits its available offers and source errors.
                    result = await ReadResultAsync(repository, runId, city);
                    if (result?.Error == null) throw;
                }
            }

            if (result.Error is { } error)
            {
                failed = true;
                Console.Error.WriteLine($"{city}: {error}");
            }
            else
            {
                Console.WriteLine($"{city}: completed");
            }
        }

        return failed ? 1 : 0;
    }

    private static async Task<WeeklyScrapeCity?> ReadResultAsync(
        IPropertyDataRepository repository, long runId, CityEnum city) =>
        (await repository.GetWeeklyCitiesAsync(runId)).SingleOrDefault(row => row.City == city.ToString());
}
