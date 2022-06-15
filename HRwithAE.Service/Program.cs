using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Encryption;
using Microsoft.Data.Encryption.AzureKeyVaultProvider;
using Microsoft.Data.Encryption.Cryptography;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace HRwithAE.Service
{
    class Program
    {
        static async Task Main()
        {
            var tokenCredential = new ClientSecretCredential(
                "<tenant-id>",
                "<client-id>", // HR service client ID
                Secrets.AppSecret);
            var keyStoreProvider = new AzureKeyVaultKeyStoreProvider(tokenCredential);

            var client = new CosmosClient("AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==")
                .WithEncryption(keyStoreProvider);

            var program = new Program();

            await program.CreateContainer(client, keyStoreProvider.ProviderName);
            await program.AddEmployees(client);
            await program.FetchEmployees(client);
        }

        public async Task CreateContainer(CosmosClient client, string keyStoreProviderName)
        {
            await client.CreateDatabaseIfNotExistsAsync("human-resources");
            var database = client.GetDatabase("human-resources");

            await database.CreateClientEncryptionKeyAsync(
                "dek1",
                DataEncryptionKeyAlgorithm.AEAD_AES_256_CBC_HMAC_SHA256,
                new EncryptionKeyWrapMetadata(
                    keyStoreProviderName,
                    "akvMasterKey",
                    "https://thweiss-aedemo-akv1.vault.azure.net/keys/cmk1/ad670c8c3af84097883749e1aff93770"));

            await database.CreateClientEncryptionKeyAsync(
                "dek2",
                DataEncryptionKeyAlgorithm.AEAD_AES_256_CBC_HMAC_SHA256,
                new EncryptionKeyWrapMetadata(
                    keyStoreProviderName,
                    "akvMasterKey",
                    "https://thweiss-aedemo-akv2.vault.azure.net/keys/cmk2/382afcad290342ed85d1fc52ae3fcbdb"));

            var path1 = new ClientEncryptionIncludedPath()
            {
                Path = "/salary",
                ClientEncryptionKeyId = "dek1",
                EncryptionType = EncryptionType.Randomized.ToString(),
                EncryptionAlgorithm = DataEncryptionKeyAlgorithm.AEAD_AES_256_CBC_HMAC_SHA256.ToString(),
            };

            var path2 = new ClientEncryptionIncludedPath()
            {
                Path = "/ssn",
                ClientEncryptionKeyId = "dek2",
                EncryptionType = EncryptionType.Deterministic.ToString(),
                EncryptionAlgorithm = DataEncryptionKeyAlgorithm.AEAD_AES_256_CBC_HMAC_SHA256.ToString(),
            };

            await database.DefineContainer("employees", "/id")
                .WithClientEncryptionPolicy()
                .WithIncludedPath(path1)
                .WithIncludedPath(path2)
                .Attach()
                .CreateAsync();
        }

        public async Task AddEmployees(CosmosClient client)
        {
            var container = client.GetContainer("human-resources", "employees");

            await container.CreateItemAsync(new
            {
                id = "123456",
                firstName = "Jane",
                lastName = "Doe",
                department = "Customer Service",
                salary = new
                {
                    @base = 51280,
                    bonus = 1440
                },
                ssn = "123-45-6789"
            }, new PartitionKey("123456"));

            await container.CreateItemAsync(new
            {
                id = "654321",
                firstName = "John",
                lastName = "Cosmic",
                department = "Supply Chain",
                salary = new
                {
                    @base = 47920,
                    bonus = 1810
                },
                ssn = "987-65-4321"
            }, new PartitionKey("654321"));
        }

        public async Task FetchEmployees(CosmosClient client)
        {
            var container = client.GetContainer("human-resources", "employees");

            // fetch a single employee by id
            var employee = await container.ReadItemAsync<dynamic>("123456", new PartitionKey("123456"));

            Console.WriteLine(JsonConvert.SerializeObject(employee.Resource, Formatting.Indented));

            // query an employee by ssn
            var queryDefinition = container.CreateQueryDefinition(
                "SELECT * FROM c where c.ssn = @SSN");
            await queryDefinition.AddParameterAsync(
                "@SSN",
                "987-65-4321",
                "/ssn");

            var results = await container.GetItemQueryIterator<dynamic>(queryDefinition).ReadNextAsync();
            Console.WriteLine(JsonConvert.SerializeObject(results.First(), Formatting.Indented));
        }
    }
}
