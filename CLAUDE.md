# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**Medios** — ASP.NET Core 8.0 MVC web application, creado el 2026-05-22. Este proyecto fue creado como proyecto nuevo (no copia) a partir del cual se migrará funcionalidad seleccionada del proyecto **Zeus** (`C:\Users\ariel\source\repos\zeus`, solución `SBInteligencia`).

### Proyecto de referencia: Zeus (SBInteligencia)
Zeus es una aplicación MVC de análisis de estadísticas de delitos para fuerzas de seguridad argentinas. Medios reutilizará partes de su arquitectura. Al copiar código de Zeus, renombrar namespaces de `SBInteligencia` → `Medios`.

**Arquitectura de Zeus (para referencia al migrar):**
- Patrón: Controllers → Services → DbContextFactory → MySQL
- Auth: tres modos intercambiables via `Auth__Mode` env var (`Cerberus`, `Cassandra`, `Mock`)
- Permisos: `HasPermissionAttribute` en controllers + `MenuAuthorizationMiddleware`
- Multi-year DB: bases `delitos_YYYY` creadas dinámicamente por `AppDbContextFactory`
- Frontend: AdminLTE + Bootstrap + jQuery
- ORM: Pomelo EF Core para MySQL 8.0+

## Commands

```bash
# Build
dotnet build

# Run (development)
dotnet run --project src/Medios/Medios.csproj

# Run with hot reload
dotnet watch run --project src/Medios/Medios.csproj

# Publish (release)
dotnet publish src/Medios/Medios.csproj -c Release
```

La app corre en `http://localhost:5010` (HTTP) o `https://localhost:7254` (HTTPS) según los launch profiles.

No existe suite de tests automatizados (igual que Zeus).

## Architecture

ASP.NET Core 8.0 MVC. El archivo de solución está en la raíz (`Medios.sln`); el proyecto bajo `src/Medios/`.

**Request flow:**
1. `Program.cs` — configura el DI container y el middleware pipeline. Ruta default: `{controller=Home}/{action=Index}/{id?}`.
2. **Controllers** reciben requests, interactúan con servicios/modelos, retornan vistas.
3. **Views** son Razor (`.cshtml`). `Views/Shared/_Layout.cshtml` es el master layout.
4. **wwwroot/** sirve assets estáticos — Bootstrap 5, jQuery, jQuery Validation bajo `wwwroot/lib/`.

**Key files:**
- `src/Medios/Program.cs` — entry point y pipeline setup
- `src/Medios/Controllers/HomeController.cs` — controller base
- `src/Medios/Views/Shared/_Layout.cshtml` — master page template
- `src/Medios/appsettings.json` / `appsettings.Development.json` — configuración de logging y host

## Decisiones de diseño tomadas

- Se creó proyecto nuevo en lugar de copiar Zeus, para evitar contaminar el historial git y tener que renombrar namespaces masivamente desde el inicio.
- La migración de funcionalidad desde Zeus se hace de forma selectiva (servicios, controllers, vistas, infraestructura) según lo que aplique al dominio de Medios.

## Premisas de optimización — EF Core + MySQL

### Transacciones y SaveChangesAsync

**Regla principal**: usar transacciones explícitas (`BeginTransactionAsync`) solo cuando sea estrictamente necesario (rollback de múltiples operaciones independientes). EF Core ya envuelve cada `SaveChangesAsync()` en su propia transacción automática.

**Minimizar round-trips a MySQL** — cada `SaveChangesAsync()` es un viaje a la DB:

1. **FK circular `NotaPrensa.VersionActualId ↔ NotaVersion.NotaId`**: usar navigation properties en lugar de IDs para que EF resuelva el orden de INSERT/UPDATE en un solo save:
   ```csharp
   // MAL: 2 saves
   db.NotasVersion.Add(nueva);
   await db.SaveChangesAsync();       // para obtener nueva.Id
   nota.VersionActualId = nueva.Id;
   await db.SaveChangesAsync();

   // BIEN: 1 save
   nueva.Nota = nota;                 // EF resuelve NotaId automáticamente
   nota.VersionActual = nueva;        // EF resuelve VersionActualId automáticamente
   db.NotasVersion.Add(nueva);
   await db.SaveChangesAsync();
   ```

2. **Null FK + Delete**: EF Core envía UPDATE antes de DELETE en el mismo `SaveChanges`:
   ```csharp
   // MAL: 2 saves
   nota.VersionActualId = null;
   await db.SaveChangesAsync();
   db.NotasPrensa.Remove(nota);
   await db.SaveChangesAsync();

   // BIEN: 1 save
   nota.VersionActualId = null;
   db.NotasPrensa.Remove(nota);
   await db.SaveChangesAsync();  // EF: UPDATE → DELETE en orden correcto
   ```

3. **Crear entidad + versión**: NO llamar `SaveChangesAsync()` entre la creación de `NotaPrensa` y `CrearVersionAsync()`. El método usa navigation properties (`nueva.Nota = nota`) que permiten resolver el FK sin que `nota.Id` exista todavía:
   ```csharp
   // MAL: save intermedio innecesario
   db.NotasPrensa.Add(nota);
   await db.SaveChangesAsync();  // innecesario
   await CrearVersionAsync(db, nota, ...);

   // BIEN: sin save intermedio
   db.NotasPrensa.Add(nota);
   await CrearVersionAsync(db, nota, ...);  // usa nota.VersionActual = nueva internamente
   ```

4. **Agrupar modificaciones independientes**: si en un método se modifican múltiples entidades sin dependencia entre ellas, hacerlo en un solo `SaveChangesAsync()` al final.

### No usar transacciones para

- Operaciones de un solo `SaveChanges`
- Lecturas (queries)
- Operaciones donde un fallo parcial es aceptable o manejable a nivel de negocio
