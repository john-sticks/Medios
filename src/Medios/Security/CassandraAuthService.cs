using System.Text;
using Medios.DTO;

namespace Medios.Security
{
    public class CassandraAuthService : IAuthService
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;

        public CassandraAuthService(HttpClient http, IConfiguration config)
        {
            _http = http;
            _baseUrl = config["Cassandra:BaseUrl"] ?? "";
        }

        public async Task<LoginResponse?> LoginAsync(string usuario, string password)
        {
            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", usuario),
                new KeyValuePair<string, string>("password", password)
            });

            var response = await _http.PostAsync($"{_baseUrl}/cassandra/login", form);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return null;

            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                var sessionCookie = cookies.FirstOrDefault(x => x.StartsWith("cassandra_session"));
                return new LoginResponse { Usuario = usuario, Token = sessionCookie };
            }

            return null;
        }

        public Task<UserSession?> GetUserInfo(string token)
        {
            try
            {
                var raw = token.Split(';')[0];
                var value = raw.Split('=')[1];
                var base64Part = value.Split('.')[0];

                var jsonBytes = Base64UrlDecode(base64Part);
                var json = Encoding.UTF8.GetString(jsonBytes);

                var data = System.Text.Json.JsonSerializer.Deserialize<SessionDto>(json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return Task.FromResult<UserSession?>(new UserSession
                {
                    Nombre = data?.Nombre ?? "",
                    Token = token,
                    Rol = data?.Rol ?? "",
                    Dependencia = data?.Destino ?? ""
                });
            }
            catch
            {
                return Task.FromResult<UserSession?>(null);
            }
        }

        private byte[] Base64UrlDecode(string input)
        {
            string output = input.Replace('-', '+').Replace('_', '/');
            switch (output.Length % 4)
            {
                case 2: output += "=="; break;
                case 3: output += "="; break;
            }
            return Convert.FromBase64String(output);
        }
    }
}
