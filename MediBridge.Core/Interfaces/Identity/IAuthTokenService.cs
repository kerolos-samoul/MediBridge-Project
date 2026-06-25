namespace MediBridge.Core.Interfaces.Identity;

public interface IAuthTokenService
{
    TimeSpan AccessTokenLifetime { get; }
    TimeSpan RefreshTokenLifetime { get; }
    string CreateAccessToken(string userId, string email, string role);
    (string PlaintextToken, string TokenHash) CreateRefreshToken();
    (string PlaintextToken, string TokenHash) CreateOneTimeToken();
    string HashToken(string plaintextToken);
    string CreateNumericCode(int length);
    string HashOneTimeSecret(string normalizedDestination, string plaintextSecret);
    bool VerifyOneTimeSecret(string normalizedDestination, string plaintextSecret, string expectedHash);
}
