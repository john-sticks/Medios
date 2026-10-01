namespace Medios.Security
{
    public class MockAuthService : IAuthService
    {
        public Task<LoginResponse?> LoginAsync(string usuario, string password)
        {
            // Usuarios mock por rol para testing
            var mockUsers = new Dictionary<string, (string password, string rol)>
            {
                { "gale", ("gale1234!", "DESARROLLADOR") },
                { "medios", ("1234", "MEDIOS") },
                { "delegacion", ("1234", "DELEGACION") },
                { "prueba", ("1234", "DELEGACION") },
                // Usuario nuevo SIN autorización: al loguear dispara la solicitud del
                // módulo de Autorizaciones (no tiene fila aprobada en autorizaciones_usuario).
                { "usuarionuevo", ("1234", "MEDIOS") },
            };

            if (mockUsers.TryGetValue(usuario.ToLower(), out var u) && u.password == password)
            {
                return Task.FromResult<LoginResponse?>(new LoginResponse
                {
                    Usuario = usuario,
                    Token = $"mock-token-{usuario}"
                });
            }

            return Task.FromResult<LoginResponse?>(null);
        }

        public Task<UserSession?> GetUserInfo(string token)
        {
            // (rol, nombre, apellido, legajo, dependencia)
            var (rol, nombre, apellido, legajo, dependencia) = token switch
            {
                "mock-token-gale"       => ("DESARROLLADOR", "Gale",   "Desarrollador","100000", "DESARROLLO"),
                "mock-token-medios"     => ("MEDIOS",     "Medios",    "Division",     "200002", "DIVISIÓN MEDIOS"),
                "mock-token-delegacion" => ("DELEGACION", "Carlos",    "Delegación",   "300001", "DELEGACIÓN A"),
                "mock-token-prueba"     => ("DELEGACION", "Marta",     "Prueba",       "300002", "DELEGACIÓN B"),
                "mock-token-usuarionuevo" => ("MEDIOS",   "Usuario",   "Nuevo",        "999999", "SIN ASIGNAR"),
                _                       => ("MEDIOS",     "Usuario",   "Medios",       "100001", "SISTEMAS")
            };

            return Task.FromResult<UserSession?>(new UserSession
            {
                Nombre = nombre,
                Apellido = apellido,
                Token = token,
                Rol = rol,
                Dependencia = dependencia,
                Email = $"{nombre.ToLower()}@policia.gba.gov.ar",
                Jerarquia = "Comisario",
                Legajo = legajo,
                Telefono = "221-0000000"
            });
        }
    }
}
