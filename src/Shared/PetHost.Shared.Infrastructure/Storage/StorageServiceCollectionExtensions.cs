using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Infrastructure.Uploads;

namespace PetHost.Shared.Infrastructure.Storage;

/// <summary>
/// Registro dos buckets de imagens: o público (<see cref="IImageStorage"/>) e o privado
/// (<see cref="IPrivateImageStorage"/>).
/// </summary>
public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Cliente S3 só para <b>assinar</b> URLs do bucket privado, com o endereço que o navegador
    /// alcança (<see cref="StorageOptions.SignedUrlServiceUrl"/>). Assinar não faz chamada de rede.
    /// </summary>
    internal const string UrlSignerKey = "storage-url-signer";

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
            return CreateClient(options, options.ServiceUrl);
        });

        services.AddKeyedSingleton<IAmazonS3>(UrlSignerKey, (provider, _) =>
        {
            var options = provider.GetRequiredService<IOptions<StorageOptions>>().Value;
            return CreateClient(options, options.SigningServiceUrl);
        });

        services.AddScoped<UploadEffects>();
        services.AddScoped<ICommitEffects>(provider => provider.GetRequiredService<UploadEffects>());
        services.AddScoped<IImageStorage, S3ImageStorage>();
        services.AddScoped<IPrivateImageStorage, S3PrivateImageStorage>();

        return services;
    }

    private static AmazonS3Client CreateClient(StorageOptions options, string serviceUrl)
    {
        var config = new AmazonS3Config
        {
            ServiceURL = serviceUrl,
            AuthenticationRegion = options.Region,
            ForcePathStyle = options.ForcePathStyle,

            // O SDK v4 manda checksum em todo pedido por padrão; o R2 e o RustFS não
            // aceitam todos os tipos. Só quando a operação exige.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };

        return new AmazonS3Client(new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey), config);
    }
}
