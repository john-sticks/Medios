using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;

namespace Medios.Security
{
    public class AuthCookieService
    {
        public async Task SignIn(HttpContext context, UserSession user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Nombre),
                new Claim("Token", user.Token),
                new Claim(ClaimTypes.Role, user.Rol ?? ""),
                new Claim("Dependencia", user.Dependencia ?? ""),
            };

            var identity = new ClaimsIdentity(claims, "Cookies");
            var principal = new ClaimsPrincipal(identity);

            await context.SignInAsync("Cookies", principal);

            context.Session.SetString("token", user.Token);
        }

        public async Task SignOut(HttpContext context)
        {
            await context.SignOutAsync("Cookies");
        }
    }
}
