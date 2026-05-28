using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace POS_UPDATER_SYSTEM.Api.Services
{
  
    public sealed class SimpleStagingHost
    {
        private IHost? _host;

        public async Task StartAsync(string rootPath, int port)
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(web =>
                {
                    web.UseUrls($"http://localhost:{port}");

                    web.Configure(app =>
                    {
                        app.UseDefaultFiles();
                        app.UseStaticFiles(new StaticFileOptions
                        {
                            FileProvider = new PhysicalFileProvider(rootPath)
                        });
                    });
                })
                .Build();

            await _host.StartAsync();
        }

        public async Task StopAsync()
        {
            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }
        }
    }
}
