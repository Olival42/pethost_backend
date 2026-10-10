using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PetHost.Shared.Contracts.Storage;

namespace PetHost.Shared.Infrastructure.Storage;

/// <summary>Registro do bucket de imagens (<see cref="IImageStorage"/>).</summary>
public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddImageStorage(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<StorageOptions>, StorageOptionsValidator>();

        services.AddSingleton<IAmazonS3>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<StorageOptions>>().Value;

            var config = new AmazonS3Config
            {
                ServiceURL = options.ServiceUrl,
                AuthenticationRegion = options.Region,
                ForcePathStyle = options.ForcePathStyle,

                // O SDK v4 manda checksum em todo pedido por padrão; o R2 e o RustFS não
                // aceitam todos os tipos. Só quando a operação exige.
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            };

            return new AmazonS3Client(new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey), config);
        });

        services.AddSingleton<IImageStorage, S3ImageStorage>();

        return services;
    }
}
