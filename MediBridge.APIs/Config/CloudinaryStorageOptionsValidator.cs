using MediBridge.Services.Config;
using Microsoft.Extensions.Options;

namespace MediBridge.APIs.Config;

public sealed class CloudinaryStorageOptionsValidator : IValidateOptions<CloudinaryStorageOptions>
{
    private readonly IConfiguration configuration;

    public CloudinaryStorageOptionsValidator(IConfiguration configuration)
    {
        this.configuration = configuration;
    }

    public ValidateOptionsResult Validate(string? name, CloudinaryStorageOptions options)
    {
        var fileOptions = new FileStorageOptions();
        configuration.GetSection(FileStorageOptions.SectionName).Bind(fileOptions);

        var errors = options.Validate(
            fileOptions.UploadsEnabled);

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
