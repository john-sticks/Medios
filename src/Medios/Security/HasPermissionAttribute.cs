using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Medios.Security
{
    public class HasPermissionAttribute : Attribute, IAuthorizationFilter
    {
        private readonly string[] _permissions;

        public HasPermissionAttribute(params string[] permissions)
        {
            _permissions = permissions.Select(p => p.ToUpper()).ToArray();
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User;

            if (user?.Identity == null || !user.Identity.IsAuthenticated)
            {
                context.Result = new UnauthorizedResult();
                return;
            }

            var menuService = context.HttpContext.RequestServices
                .GetRequiredService<MenuXmlService>();

            var userPermissions = menuService.GetPermissions(user);

            if (!_permissions.Any(p => userPermissions.Contains(p)))
                context.Result = new ForbidResult();
        }
    }
}
