using Azure.Identity;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Encryption;
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
            var keyResolver = new KeyResolver(tokenCredential);

            var client = new CosmosClient("AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==")
                .WithEncryption(keyResolver, KeyEncryptionKeyResolverName.AzureKeyVault);

            var program = new Program();

            await program.CreateContainer(client);
            await program.AddEmployees(client);
            await program.FetchEmployees(client);
        }

        public async Task CreateContainer(CosmosClient client)
        {
            await client.CreateDatabaseIfNotExistsAsync("human-resources");
            var database = client.GetDatabase("human-resources");

            await database.CreateClientEncryptionKeyAsync(
                "dek1",
                DataEncryptionAlgorithm.AeadAes256CbcHmacSha256,
                new EncryptionKeyWrapMetadata(
                    KeyEncryptionKeyResolverName.AzureKeyVault,
                    "akvKey",
                    "https://<my-key-vault-1>.vault.azure.net/keys/cmk1/ad670c8c3af84097883749e1aff93770",
                    EncryptionAlgorithm.RsaOaep.ToString()));

            await database.CreateClientEncryptionKeyAsync(
                "dek2",
                DataEncryptionAlgorithm.AeadAes256CbcHmacSha256,
                new EncryptionKeyWrapMetadata(
                    KeyEncryptionKeyResolverName.AzureKeyVault,
                    "akvKey",
                    "https://<my-key-vault-2>.vault.azure.net/keys/cmk2/382afcad290342ed85d1fc52ae3fcbdb",
                    EncryptionAlgorithm.RsaOaep.ToString()));

            var path1 = new ClientEncryptionIncludedPath()
            {
                Path = "/salary",
                ClientEncryptionKeyId = "dek1",
                EncryptionType = EncryptionType.Randomized.ToString(),
                EncryptionAlgorithm = DataEncryptionAlgorithm.AeadAes256CbcHmacSha256,
            };

            var path2 = new ClientEncryptionIncludedPath()
            {
                Path = "/ssn",
                ClientEncryptionKeyId = "dek2",
                EncryptionType = EncryptionType.Deterministic.ToString(),
                EncryptionAlgorithm = DataEncryptionAlgorithm.AeadAes256CbcHmacSha256,
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
