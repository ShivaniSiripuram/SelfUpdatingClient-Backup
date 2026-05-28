namespace POS_UPDATER_SYSTEM.Api.Services;

public interface IPackageVerifier
{
    Task VerifyAsync(string packagePath, string expectedSha256, ILogger logger, CancellationToken cancellationToken);
}
