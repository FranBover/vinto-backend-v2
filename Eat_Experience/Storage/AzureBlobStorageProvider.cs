using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Vinto.Api.Storage
{
    public class AzureBlobStorageProvider : IStorageProvider
    {
        private readonly BlobContainerClient _containerClient;

        public AzureBlobStorageProvider(IConfiguration configuration)
        {
            var connectionString = configuration["Storage:AzureBlob:ConnectionString"];
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    "Falta la configuraci�n 'Storage:AzureBlob:ConnectionString' para Azure Blob Storage.");

            var containerName = configuration["Storage:AzureBlob:ContainerName"];
            if (string.IsNullOrWhiteSpace(containerName))
                throw new InvalidOperationException(
                    "Falta la configuraci�n 'Storage:AzureBlob:ContainerName' para Azure Blob Storage.");

            _containerClient = new BlobContainerClient(connectionString, containerName);
        }

        public async Task<string> UploadAsync(Stream stream, string fileName, string contentType)
        {
            var blobClient = _containerClient.GetBlobClient(fileName);

            await blobClient.UploadAsync(
                stream,
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
                });

            return blobClient.Uri.ToString();
        }

        public async Task DeleteAsync(string fileName)
        {
            var blobClient = _containerClient.GetBlobClient(fileName);
            await blobClient.DeleteIfExistsAsync();
        }

        public string GetUrl(string fileName) => _containerClient.GetBlobClient(fileName).Uri.ToString();
    }
}
