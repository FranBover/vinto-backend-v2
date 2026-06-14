# Vinto — Sistema de Menú y Pedidos Online

Vinto es un sistema web **multi-tenant** para locales de comida. Permite que cada negocio tenga su propio menú online, reciba pedidos sin que el cliente deba registrarse, gestione el flujo de pedidos desde un panel administrador, y envíe resúmenes de pedidos directamente por WhatsApp.

---

## Stack tecnológico

| Capa | Tecnología |
|------|-----------|
| Framework backend | ASP.NET Core 9.0 |
| Base de datos | SQL Server + Entity Framework Core 9.0.4 |
| Autenticación | JWT Bearer (HMAC SHA256) |
| Documentación API | Swagger / OpenAPI (Swashbuckle 6.6.2) |
| Hashing de contraseñas | ASP.NET Core Identity PasswordHasher |

---

## Estructura de carpetas

```
Eat_Experience/
├── Controllers/              # Controladores de la API (HTTP layer)
├── DTOs/                     # Objetos de transferencia de datos (request/response)
├── Data/                     # DbContext de Entity Framework Core
├── Models/                   # Entidades de la base de datos
├── Repositories/
│   ├── Interfaces/           # Contratos del repositorio
│   └── Implementaciones/     # Implementaciones concretas
├── Services/
│   ├── Interfaces/           # Contratos del servicio
│   └── Implementaciones/     # Lógica de negocio
├── Migrations/               # Migraciones de EF Core
├── Helpers/                  # (Reservado)
├── Mappers/                  # (Reservado)
├── wwwroot/                  # Archivos estáticos
├── appsettings.json
├── appsettings.Development.json
├── appsettings.Example.json  # Plantilla de configuración
└── Program.cs
```

---

## Configuración local

### Prerrequisitos

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- SQL Server (o SQL Server Express)

### Pasos

1. **Clonar el repositorio**

```bash
git clone <url-del-repo>
cd Eat_Experience
```

2. **Crear `appsettings.json` a partir del ejemplo**

```bash
cp appsettings.Example.json appsettings.json
```

Completar los valores según la sección de [Variables de entorno](#variables-de-entorno) a continuación.

3. **Aplicar migraciones y crear la base de datos**

```bash
dotnet ef database update
```

4. **Levantar el proyecto**

```bash
dotnet run
```

La API quedará disponible en `https://localhost:5001` (o el puerto configurado).  
Swagger estará disponible en `https://localhost:5001/swagger` en modo Development.

> Al iniciar por primera vez, la aplicación crea automáticamente un administrador de prueba:  
> **Email:** `admin@ejemplo.com` | **Contraseña:** `123456`

---

## Variables de entorno

Renombrar `appsettings.Example.json` a `appsettings.json` y completar los valores:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost\\SQLEXPRESS;Database=EatExperienceDb;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
  },
  "JwtSettings": {
    "Key": "TU_CLAVE_SECRETA_AQUI",
    "Issuer": "EatExperienceAPI",
    "Audience": "EatExperienceFrontend",
    "ExpirationInMinutes": 60
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

| Variable | Descripción |
|----------|-------------|
| `ConnectionStrings.DefaultConnection` | Cadena de conexión a SQL Server |
| `JwtSettings.Key` | Clave secreta para firmar tokens JWT (mínimo 40 caracteres) |
| `JwtSettings.Issuer` | Emisor del token JWT |
| `JwtSettings.Audience` | Audiencia del token JWT |
| `JwtSettings.ExpirationInMinutes` | Tiempo de expiración del token en minutos |

---

## Endpoints principales de la API

La URL base es `/api`. Los endpoints marcados con `[Auth]` requieren un header `Authorization: Bearer <token>`.

### Autenticación

| Método | Endpoint | Descripción |
|--------|----------|-------------|
| POST | `/api/auth/login` | Login del administrador. Devuelve JWT. |

### Administradores `[Auth]`

| Método | Endpoint | Descripción |
|--------|----------|-------------|
| GET | `/api/administrador` | Listar todos los administradores |
| GET | `/api/administrador/{id}` | Obtener administrador por ID |
| POST | `/api/administrador` | Crear administrador |
| PUT | `/api/administrador/{id}` | Actualizar administrador |
| DELETE | `/api/administrador/{id}` | Eliminar administrador |

### Categorías

| Método | Endpoint | Auth | Descripción |
|--------|----------|------|-------------|
| GET | `/api/categorias` | No | Listar categorías (público) |
| GET | `/api/categorias/{id}` | No | Obtener categoría por ID |
| POST | `/api/categorias` | Sí | Crear categoría |
| PUT | `/api/categorias/{id}` | Sí | Actualizar categoría |
| DELETE | `/api/categorias/{id}` | Sí | Eliminar categoría |

### Productos

| Método | Endpoint | Auth | Descripción |
|--------|----------|------|-------------|
| GET | `/api/productos` | No | Listar productos con sus extras (público) |
| GET | `/api/productos/{id}` | No | Obtener producto por ID |
| POST | `/api/productos` | Sí | Crear producto |
| PUT | `/api/productos/{id}` | Sí | Actualizar producto |
| DELETE | `/api/productos/{id}` | Sí | Eliminar producto |

### Extras de productos

| Método | Endpoint | Auth | Descripción |
|--------|----------|------|-------------|
| GET | `/api/productoextra` | No | Listar todos los extras |
| GET | `/api/productoextra/{id}` | No | Obtener extra por ID |
| GET | `/api/productoextra/por-producto/{productoId}` | No | Extras de un producto |
| POST | `/api/productoextra` | Sí | Crear extra |
| PUT | `/api/productoextra/{id}` | Sí | Actualizar extra |
| DELETE | `/api/productoextra/{id}` | Sí | Eliminar extra |

### Pedidos

| Método | Endpoint | Auth | Descripción |
|--------|----------|------|-------------|
| GET | `/api/pedidos` | Sí | Listar pedidos del admin autenticado |
| GET | `/api/pedidos/{id}` | Sí | Detalle de un pedido |
| PUT | `/api/pedidos/{id}` | Sí | Actualizar pedido |
| PATCH | `/api/pedidos/{id}/estado` | Sí | Actualizar estado del pedido |
| GET | `/api/pedidos/{id}/resumen` | Sí | Resumen para enviar por WhatsApp |
| **POST** | **`/api/public/locales/{slug}/pedidos`** | **No** | **Crear pedido (endpoint público para clientes)** |

#### Estados del pedido

```
Pendiente → Confirmado → EnPreparacion → Listo → Entregado
                                               ↘ Cancelado
```

### Públicos
| Método | Endpoint | Descripción |
|--------|----------|-------------|
| GET | `/api/public/locales/{slug}/menu` | Menú completo del local |
| POST | `/api/public/locales/{slug}/pedidos` | Crear pedido |
| POST | `/api/Auth/login` | Login del admin |
| POST | `/api/Auth/register` | Registrar admin (requiere X-Admin-Key) |


### Privados (requieren JWT)
| Método | Endpoint | Descripción |
|--------|----------|-------------|
| GET | `/api/Pedidos` | Lista de pedidos del admin |
| GET | `/api/Pedidos/{id}` | Detalle de pedido |
| PATCH | `/api/Pedidos/{id}/estado` | Cambiar estado |
| GET | `/api/Productos` | Lista de productos |
| POST | `/api/Productos` | Crear producto |
| PUT | `/api/Productos/{id}` | Editar producto |
| PATCH | `/api/Productos/{id}/disponibilidad` | Habilitar/deshabilitar |
| GET | `/api/Categorias` | Lista de categorías |
| POST | `/api/Categorias` | Crear categoría |
| PUT | `/api/Categorias/{id}` | Editar categoría |
| DELETE | `/api/Categorias/{id}` | Eliminar categoría |
| GET | `/api/ProductoExtra/por-producto/{id}` | Extras de un producto |
| POST | `/api/ProductoExtra` | Agregar extra |
| DELETE | `/api/ProductoExtra/{id}` | Eliminar extra |
| PATCH | `/api/Administrador/{id}/local` | Actualizar datos del local |

## Registrar un admin nuevo
```bash
curl -X POST http://localhost:5202/api/Auth/register \
  -H "Content-Type: application/json" \
  -H "X-Admin-Key: TU_CLAVE_DE_REGISTRO" \
  -d '{
    "nombre": "Nombre",
    "email": "email@ejemplo.com",
    "password": "contraseña",
    "nombreLocal": "Nombre del Local",
    "telefono": "351xxxxxxx",
    "direccion": "Dirección del local"
  }'
```



---

## Arquitectura

El proyecto sigue una arquitectura en capas con los patrones **Repository** y **Service**:

```
Controllers  →  Services  →  Repositories  →  EF Core  →  SQL Server
   (HTTP)      (Negocio)     (Datos)
```

El multi-tenancy se implementa filtrando todos los datos por `AdministradorId`, que se extrae del claim del JWT en los endpoints protegidos o del slug del local en el endpoint público.

---
## Estructura del proyecto
Controllers/     — endpoints de la API
DTOs/            — objetos de transferencia de datos
Models/          — entidades de la base de datos
Repositories/    — acceso a datos
Services/        — lógica de negocio
Data/            — DbContext y configuración de EF Core
Migrations/      — migraciones de la base de datos

## Desarrollado por

Francisco Bover — [github.com/FranBover](https://github.com/FranBover)

