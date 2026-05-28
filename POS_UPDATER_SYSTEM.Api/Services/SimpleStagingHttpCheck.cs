
using System.Net.Http;
namespace POS_UPDATER_SYSTEM.Api.Services
{

    public sealed class SimpleStagingHttpCheck
    {
        private readonly HttpClient _http = new();

        public async Task ValidateAsync(string baseUrl)
        {
            var index = await _http.GetAsync($"{baseUrl}/index.html");

            if (!index.IsSuccessStatusCode)
                throw new Exception("index.html not reachable in staging host");
        }
    }
}
