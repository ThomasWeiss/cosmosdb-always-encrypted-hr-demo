using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Encryption;
using Microsoft.Data.Encryption.AzureKeyVaultProvider;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;

namespace HRwithAE.Staff
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var tokenCredential = new ClientSecretCredential(
                "<tenant-id>",
                "<client-id>", // HR staff client ID
                Secrets.AppSecret);
            var keyStoreProvider = new AzureKeyVaultKeyStoreProvider(tokenCredential);

            var client = new CosmosClient("AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==")
                .WithEncryption(keyStoreProvider);

            var program = new Program();
            await program.FetchEmployees(client);
        }

        public async Task FetchEmployees(CosmosClient client)
        {
            var container = client.GetContainer("human-resources", "employees");

            // try to list all employees as-is
            try
            {
                var results1 = await container.GetItemQueryIterator<dynamic>(
                    "SELECT * FROM C").ReadNextAsync();
            }
            catch
            {
                Console.WriteLine("Fetching all employee details failed!");
            }

            // list all employees by projecting away the ssn
            var results2 = await container.GetItemQueryIterator<dynamic>(
                "SELECT c.id, c.firstName, c.LastName, c.department, c.salary FROM c").ReadNextAsync();

            foreach (var result in results2)
            {
                Console.WriteLine(JsonConvert.SerializeObject(result, Formatting.Indented));
            }
        }
    }
}
