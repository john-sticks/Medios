namespace Medios.Security
{
    public interface IAuthService
    {
        Task<LoginResponse?> LoginAsync(string usuario, string password);
        Task<UserSession?> GetUserInfo(string token);
    }

    public class LoginResponse
    {
        public string? Usuario { get; set; }
        public string? Token { get; set; }
    }

    public class TokenResponse
    {
        public string? access_token { get; set; }
    }
}
