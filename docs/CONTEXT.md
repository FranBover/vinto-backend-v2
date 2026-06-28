# Vinto — Backend (Vinto.Api) · Documentación técnica

> Fuente de verdad del backend. Generada leyendo el código real del repo.
> Donde algo no pudo confirmarse con certeza se marca **(verificar)**.
> **No** se incluyen valores de secretos: solo se referencian por su nombre de configuración.

Última actualización del documento: 2026-06-25.

---

## 1. Qué es Vinto

Vinto es un **SaaS multi-tenant de menú / tienda online para locales gastronómicos**. Cada local (tenant) tiene su catálogo, su panel de administración y su carta pública.

Características de producto que se desprenden del código:

- **Carta pública por local**, accesible por *slug* derivado del nombre del local (`GET /api/public/locales/{slug}/menu`). No requiere login del cliente.
- **Pedidos sin login del cliente**: el cliente arma el pedido y lo envía contra el endpoint público del local (`POST /api/public/locales/{slug}/pedidos`). Se genera un **código de seguimiento**.
- **Pedido por WhatsApp**: al crear el pedido el backend devuelve un `ResumenWhatsApp` (texto formateado listo para enviar al local). También hay endpoint admin para regenerarlo.
- **Panel de administración (privado)**: gestión de catálogo, variantes, stock, descuentos, cupones, pedidos, imágenes, datos del local, reportes y conexión con MercadoPago. Protegido por **JWT**.
- **Pagos con MercadoPago** vía OAuth (cada local conecta su propia cuenta MP) + checkout por preferencia + webhook.
- **Tiempo real** con SignalR (hub `/hubs/pedidos`): notifica nuevos pedidos y pagos confirmados al panel del admin.

**Multi-tenant:**
- En lo **público** el tenant se resuelve por **slug** (derivado de `Administrador.NombreLocal`).
- En lo **privado** el tenant se resuelve por **`adminId`** (claim del JWT). Cada entidad de negocio cuelga de `AdministradorId`.

---

## 2. Stack y arquitectura

**Stack** (`Vinto.Api.csproj`):
- ASP.NET Core **.NET 9** (Web API).
- **EF Core 9** + `Microsoft.EntityFrameworkCore.SqlServer` (SQL Server).
- Autenticación **JWT** (`Microsoft.AspNetCore.Authentication.JwtBearer` 9.0.4).
- **SignalR** (tiempo real).
- **MercadoPago SDK** (`mercadopago-sdk` 2.12.1).
- **SixLabors.ImageSharp** 3.1.12 (procesamiento de imágenes).
- **Swashbuckle / Swagger** 6.6.2 (solo en Development).
- Hashing de password: `PasswordHasher<Administrador>` (ASP.NET Core Identity).

**Solución/proyecto:** solución `Eat_Experience.sln` (raíz del repo), proyecto `Vinto.Api` en el subdirectorio `Eat_Experience/Eat_Experience/`. Namespace raíz: `Vinto.Api`.

**Capas:**
```
Controllers/   → HTTP, extracción de adminId del JWT, validación de entrada, mapeo a DTO.
Services/      → Lógica de negocio (Interfaces/ + Implementaciones/).
Repositories/  → Acceso a datos sobre AppDbContext (Interfaces/ + Implementaciones/).
DTOs/          → Contratos de request/response.
Models/        → Entidades EF.
Data/AppDbContext.cs → DbContext, relaciones, índices, defaults.
Helpers/       → EncryptionHelper (AES-GCM), MercadoPagoSignatureValidator, ValidacionException.
Storage/       → IStorageProvider + LocalStorageProvider (almacenamiento de imágenes en disco).
Hubs/          → PedidosHub (SignalR).
Migrations/    → Migraciones EF.
```

> Nota: algunos controllers/servicios usan **`AppDbContext` directamente** (no todo pasa por repos). Ej.: `PublicController`, `PedidoService`, `MercadoPagoService`, `CategoriasController`, `ProductosController` acceden a `_context` para queries con `Include`. La capa de repos existe pero su uso es parcial.

**Multi-tenant en la práctica:**
- Los filtros globales (`HasQueryFilter`) están **comentados** en `AppDbContext` (líneas ~263-267). El aislamiento entre tenants se hace **manualmente** filtrando por `AdministradorId` en cada query. **Riesgo:** si una query olvida el filtro, hay fuga de datos entre locales.
- Patrón típico en controllers privados: `TryGetAdminId(out int adminId)` lee el claim `adminId` del JWT; luego cada operación valida que la entidad pertenezca a ese admin (`Forbid()` si no).

**Pipeline (`Program.cs`):** JWT → DbContext (SQL Server, command timeout 180s) → DI de repos y services → MemoryCache → HttpClient → EncryptionHelper (singleton) → SignatureValidator → StorageProvider + ImagenService → SignalR → Controllers + Swagger → CORS `AllowFrontend`.
- **CORS** `AllowFrontend`: orígenes `http://localhost:5173` y el front de Azure (`vinto-frontend-dev-...azurewebsites.net`), `AllowCredentials` (requerido por SignalR).
- **Archivos estáticos**: sirve `/uploads` desde `ContentRootPath/uploads`.
- **Seed**: si no hay administradores, crea uno semilla (ver §7 y §8).
- Swagger declara dos *security schemes*: `Bearer` (JWT) y `X-Admin-Key` (registro de admins).

---

## 3. Modelo de datos

Entidades reales (de `Models/` y `AppDbContext`). `*` = nullable.

### Administrador (tenant)
Tabla central. Campos principales:
- `Id`, `Nombre`, `Email` (único), `PasswordHash`.
- Datos del local: `NombreLocal`, `Direccion`, `Telefono`, `LinkWhatsapp*`, `LogoUrl*`, `Horarios*`, `UbicacionUrl*`.
- `EsActivo` (bool), `FechaRegistro`, `UltimoAcceso*`, `PlanSuscripcion*`, `DominioPersonalizado*`.
- Pago/transferencia: `AliasTransferencia*`, `TitularCuenta*`.
- Envío: `ZonaEnvio` (`"Ciudad"` | `"Nacional"`, default `"Nacional"`), `CostoEnvio*` (decimal).
- Stock: `StockBajoAlerta*` (default 5), `AutoDeshabilitarSinStock` (bool, default false).
- MercadoPago: `MercadoPagoUserId*`, `MercadoPagoAccessToken*` (cifrado), `MercadoPagoRefreshToken*` (cifrado), `MercadoPagoPublicKey*`, `MercadoPagoTokenExpiresAt*`, `MercadoPagoConectado` (bool).

Relaciones (1:N, todas `Restrict` salvo nota): Administrador → Categoria, Producto, Pedido, Descuento, Cupon, MovimientoStock, Imagen (Cascade), ComentarioPedido.

### Categoria
`Id`, `Nombre`, `Orden` (default 0), `AdministradorId` → Administrador. 1:N con Producto.
Índice único `(AdministradorId, Nombre)`.

### Producto
`Id`, `Nombre`, `Descripcion*`, `Precio` (decimal 18,2), `ImagenUrl`, `Disponible` (default true), `CategoriaId` → Categoria, `AdministradorId` → Administrador.
- `TieneVariantes` (bool, default false), `Stock*` (int — stock simple cuando no tiene variantes).
- Colecciones: `Extras` (ProductoExtra), `TiposVariante`, `Variantes`.
- Índice único `(AdministradorId, Nombre)`.

### ProductoExtra
`Id`, `Nombre`, `PrecioAdicional` (18,2), `ProductoId` → Producto (Restrict). Add-ons del producto (ej. "Extra queso").

### TipoVariante
`Id`, `ProductoId` → Producto (Cascade), `Nombre`, `Orden`. Colección `Opciones`.
Un producto puede tener **máx. 2** tipos de variante (validado en controller). Ej.: "Talle", "Color".

### OpcionVariante
`Id`, `TipoVarianteId` → TipoVariante (Cascade), `Valor`, `Orden`. Ej.: "M", "L", "Rojo".

### VarianteProducto
Combinación concreta de opciones con su propio precio/stock.
`Id`, `ProductoId` → Producto (Cascade), `Opcion1Id` → OpcionVariante (Restrict), `Opcion2Id*` → OpcionVariante (Restrict), `Precio` (18,2), `Stock*`, `Disponible` (default true), `Sku*`.

### MovimientoStock (auditoría de stock)
`Id`, `AdministradorId`, `ProductoId`, `VarianteProductoId*`, `Tipo` (`"entrada"`|`"salida"`|`"ajuste"`), `Cantidad`, `StockAnterior`, `StockNuevo`, `Motivo*`, `FechaCreacion` (default UTC). Todas las FKs `Restrict`.

### Pedido
`Id`, `AdministradorId` → Administrador (Restrict), `Fecha` (default UTC), `Estado` (default `"Pendiente"`), `CodigoSeguimiento*`.
- Cliente: `NombreCliente`, `TelefonoCliente`.
- Pago/entrega: `FormaPago`, `FormaEntrega`, `MontoPagoEfectivo*`, `DireccionCliente*`, `ReferenciaDireccion*`, `UbicacionUrl*`.
- Totales: `Total` (18,2), `SubtotalSinDescuentos`, `MontoDescuentoProductos`, `MontoDescuentoCupon` (default 0).
- Cupón: `CuponId*` → Cupon (Restrict), `CodigoCupon*`.
- MercadoPago: `MercadoPagoPreferenceId*`, `MercadoPagoPaymentId*`, `MercadoPagoStatus*`, `MercadoPagoStatusDetail*`, `MercadoPagoFechaPago*`, `MercadoPagoCollectionId*`.
- Colección `Detalles` (DetallePedido, Cascade).
- Índice `(AdministradorId, Fecha)`.
- Estados usados (validados en `PedidosController.PatchEstado`): `Pendiente`, `Confirmado`, `EnPreparacion`, `Listo`, `Entregado`, `Cancelado`.

### DetallePedido
`Id`, `PedidoId` → Pedido (Cascade), `ProductoId` → Producto (Restrict), `Cantidad`, `PrecioUnitario` (18,2; se guarda el **precio ya con descuento de línea**), `VarianteProductoId*` → VarianteProducto (Restrict). Colección `ProductosExtra`.

### DetallePedidoExtra
`Id`, `DetallePedidoId` → DetallePedido (Cascade), `ProductoExtraId` → ProductoExtra (Restrict). Índice único `(DetallePedidoId, ProductoExtraId)`.

### ComentarioPedido
`Id`, `PedidoId` → Pedido (Cascade), `Texto` (máx 500), `FechaCreacion`, `AdministradorId` → Administrador (Restrict). Notas internas del admin sobre el pedido.

### Descuento
Reglas de descuento automáticas del local.
`Id`, `AdministradorId` (Restrict), `Nombre`, `Tipo` (`"Porcentaje"` | otro = monto — ver §6), `Valor` (18,2), `ProductoId*` (SetNull), `CategoriaId*` (SetNull), `AplicaAPedidoCompleto` (bool), `FechaInicio*`, `FechaFin*`, `Activo`, `FechaCreacion`. Índice `(AdministradorId, Activo)`.

### Cupon
`Id`, `AdministradorId` (Restrict), `Codigo` (máx 30), `Tipo` (`"Porcentaje"` | `"MontoFijo"`), `Valor` (18,2), `FechaVencimiento*`, `LimiteUsos*`, `UsosActuales` (default 0), `PedidoMinimo*` (18,2), `Activo`, `FechaCreacion`. Índice único `(AdministradorId, Codigo)`.

### UsoCupon (auditoría de canje)
`Id`, `CuponId` → Cupon (Restrict), `PedidoId` → Pedido (Cascade), `MontoDescontado` (18,2), `FechaUso`, `Liberado` (bool), `FechaLiberacion*`. Permite liberar/re-aplicar el cupón cuando un pedido se cancela/reactiva.

### DetallePedidoDescuento (auditoría/snapshot de descuentos aplicados)
`Id`, `PedidoId` (Cascade), `DetallePedidoId*` (Restrict), `DescuentoId*` (SetNull), `NombreDescuentoSnapshot`, `TipoDescuento` (`"Producto"`/`"Categoria"`/`"PedidoCompleto"`), `MontoDescontado` (18,2), `FechaCreacion`.

### Imagen
`Id`, `AdministradorId` → Administrador (Cascade), `NombreOriginal`, `NombreAlmacenado`, `ContentType`, `TamanioBytes`, `Url`, `Tipo` (`"producto"` | `"logo"` | `"categoria"`), `EntidadId*` (id del producto/categoría; null si logo), `Orden`, `FechaCreacion`. Índice `(AdministradorId, Tipo, EntidadId)`.
> El comentario del modelo dice solo `"producto"`/`"logo"`, pero el código usa también `"categoria"` (PublicController/CategoriasController).

### PagoMercadoPago (auditoría de webhooks de pago)
`Id`, `PedidoId` → Pedido (Restrict), `PaymentId`, `Status`, `StatusDetail*`, `Monto` (18,2), `FechaEvento`, `RawWebhookData*` (JSON crudo del pago), `ProcesadoConExito` (bool). Índice **único `(PaymentId, Status)`** → garantiza idempotencia del webhook.

### PreviewActualizacionPrecios + PreviewActualizacionPreciosItem  ❌ (sin uso)
Modelos para una feature de **actualización masiva de precios por categoría con vista previa**:
- `PreviewActualizacionPrecios`: `Id` (Guid), `AdministradorId`, `CategoriaId`, `Tipo`, `Valor`, `Redondear`, `TotalAfectados`, `FechaCreacion`, `FechaExpiracion`, `Aplicado`, `FechaAplicacion*`, colección `Items`.
- `PreviewActualizacionPreciosItem`: `Id`, `PreviewId` (Guid), `ProductoId`, `PrecioActual`, `PrecioNuevo`.
- Tienen `DbSet` y migración aplicada, **pero ningún Controller ni Service los referencia** → feature a medio implementar (solo el esquema). Ver §6 y §8.

### JwtSettings (config, no entidad)
`Key`, `Issuer`, `Audience`, `ExpirationInMinutes`. Se bindea desde `JwtSettings` en config.

**Defaults en DB (`GETUTCDATE()`):** `Administrador.FechaRegistro`, `Pedido.Fecha`, `Imagen.FechaCreacion`, `MovimientoStock.FechaCreacion`, `Descuento/Cupon/UsoCupon/DetallePedidoDescuento.FechaCreacion`.

---

## 4. Funcionalidades implementadas

Leyenda: ✅ funcionando · 🟡 parcial · ❌ TODO / no implementado.

| Módulo | Estado | Detalle |
|---|---|---|
| **Auth / JWT** | ✅ | Login (`/api/Auth/login`) y emisión de token con claims `sub`(email), `adminId`, `jti`. Expira según `JwtSettings:ExpirationInMinutes` (60). Password con `PasswordHasher`. |
| **Registro de admin** | ✅ | `/api/Auth/register` protegido por header `X-Admin-Key` comparado contra `AdminRegistroKey` (config). Verifica email único. |
| **Catálogo (categorías / productos)** | ✅ | CRUD de Categoria y Producto con scoping por admin. Reordenar categorías (`PATCH /api/Categorias/reordenar`). Toggle disponibilidad producto. Extras (ProductoExtra) CRUD. |
| **Variantes** | ✅ | TipoVariante (máx 2/producto) + OpcionVariante + generación de combinaciones (`POST .../variantes/generar`), edición de precio/stock/disponibilidad/SKU por variante. |
| **Stock** | ✅ | Por producto simple o por variante. Operaciones: ajustar, agregar/reponer, descontar (al confirmar pedido). Movimientos auditados. Alertas de stock bajo/agotado (`GET /api/Stock/alertas`) según `StockBajoAlerta`. Auto-deshabilitar al llegar a 0 si `AutoDeshabilitarSinStock`. |
| **Descuentos automáticos** | ✅ | CRUD (`/api/Descuentos`). Cálculo por `DescuentoCalculatorService`: descuentos por producto → por categoría → a pedido completo, con vigencia por fecha. Snapshot en `DetallePedidoDescuento`. No hay DELETE de descuento (solo activar/desactivar vía update — **verificar**). |
| **Cupones** | ✅ | CRUD (`/api/Cupones`), métricas (`/{id}/metricas`), validación pública (`/api/public/locales/{slug}/cupones/validar`). Aplicación + control de `LimiteUsos`/`UsosActuales` + liberación/re-aplicación al cancelar/reactivar pedido. No hay DELETE de cupón. |
| **Pedidos (público)** | ✅ | Creación por slug con validación de producto/variante/extras del local, cálculo de descuentos + cupón + costo de envío, código de seguimiento, notificación SignalR `NuevoPedido`, y `ResumenWhatsApp`. |
| **Pedidos (admin)** | ✅ | Listado filtrado (estado/fechas/formaPago/formaEntrega), detalle, cambio de estado (con descuento/reposición de stock y manejo de cupón), comanda, ticket, comentarios. |
| **Resumen WhatsApp** | ✅ | Generado al crear el pedido y regenerable por admin (`GET /api/Pedidos/{id}/resumen`) y en estado-pago público. Texto es-AR formateado. |
| **MercadoPago** | 🟡 | OAuth (url/callback/desconectar/estado/diagnóstico) ✅; creación de preferencia ✅; webhook con validación de firma HMAC + idempotencia ✅; endpoint dev de simulación ✅. **Pendientes/atención:** no hay **refresh** automático del token (se guarda `RefreshToken` pero no se renueva); webhook consulta el pago con token `client_credentials` de la app (enfoque marcado como "hacky" en comentarios); pagos `rejected` dejan el pedido en `Pendiente`. |
| **Imágenes** | 🟡 | Upload (multipart) / delete / listar por entidad ✅. **Solo almacenamiento local en disco** (`/uploads`); no hay proveedor Blob pese a la config `Storage:Provider`. Ver §6/§8. |
| **Datos del local (PATCH .../local)** | ✅ | `PATCH /api/Administrador/{id}/local` con `AdministradorLocalUpdateDTO` (campos opcionales, patch parcial). Verifica que el `id` coincida con el `adminId` del token. |
| **Reportes** | 🟡 | Solo `GET /api/Reportes/dashboard?periodo=` (ventas y comparación de período, con timezone Argentina). No hay otros reportes. |
| **Tiempo real (SignalR)** | ✅ | Hub `/hubs/pedidos`, grupos por `adminId`. Eventos `NuevoPedido` y `PagoConfirmado`. |
| **Actualización masiva de precios (preview)** | ❌ | Modelos + tablas existen; **no hay endpoints ni lógica**. |

---

## 5. Endpoints

Rutas reales tomadas de los Controllers. Prefijo base `api/`. (`[controller]` resuelve al nombre sin sufijo, ej. `Categorias`.)

### Públicos (cliente — sin JWT)
| Método | Ruta | Qué hace |
|---|---|---|
| POST | `/api/Auth/register` | Registra un admin. Requiere header `X-Admin-Key`. |
| POST | `/api/Auth/login` | Login, devuelve `{ token }`. |
| GET | `/api/public/locales/{slug}/menu` | Carta pública del local: info del local, categorías con productos (con precios con descuento), variantes, extras, imágenes y descuentos a pedido completo. |
| POST | `/api/public/locales/{slug}/pedidos` | Crea un pedido para el local. Devuelve `PedidoCreateResponseDTO` (incluye `ResumenWhatsApp` y `CodigoSeguimiento`). |
| POST | `/api/public/locales/{slug}/pedidos/{pedidoId}/preferencia-mp` | Crea la preferencia de pago MP del pedido. Body: `{ codigoSeguimiento }`. |
| GET | `/api/public/pedidos/{codigoSeguimiento}/estado-pago` | Estado del pedido/pago + resumen para que el cliente lo siga. |
| POST | `/api/public/locales/{slug}/cupones/validar` | Valida un cupón contra un subtotal. `[AllowAnonymous]`. |
| GET | `/api/MercadoPago/oauth/callback` | Callback OAuth de MP. `[AllowAnonymous]` (seguridad por `state`). Redirige al front admin. |
| POST | `/api/MercadoPago/webhook` | Webhook de pagos de MP. `[AllowAnonymous]` (seguridad por firma HMAC). Siempre responde 200 salvo error grave. |
| POST | `/api/MercadoPago/dev/simular-webhook-aprobado` | **Solo Development**: simula un pago aprobado. `[AllowAnonymous]`. |

### Privados (admin — requieren JWT `Bearer`)
**Administrador**
| Método | Ruta | Qué hace |
|---|---|---|
| GET | `/api/Administrador` | Lista todos los administradores. ⚠ ver §8. |
| GET | `/api/Administrador/{id}` | Obtiene un admin. |
| POST | `/api/Administrador` | Crea admin (recibe entidad cruda). ⚠ ver §8. |
| PUT | `/api/Administrador/{id}` | Actualiza admin (entidad cruda). ⚠ ver §8. |
| DELETE | `/api/Administrador/{id}` | Elimina admin. ⚠ ver §8. |
| PATCH | `/api/Administrador/{id}/local` | Patch parcial de datos del local. Verifica `id == adminId` del token. |

**Categorías / Productos / Extras**
| Método | Ruta | Qué hace |
|---|---|---|
| GET/POST | `/api/Categorias` | Lista (con imagen) / crea categoría. |
| GET/PUT/DELETE | `/api/Categorias/{id}` | Obtiene / actualiza / elimina categoría (borra sus imágenes). |
| PATCH | `/api/Categorias/reordenar` | Reordena todas las categorías del admin (`{ orderedIds }`). |
| GET/POST | `/api/Productos` | Lista (con imágenes) / crea producto. |
| GET/PUT/DELETE | `/api/Productos/{id}` | Obtiene / actualiza / elimina producto. |
| PATCH | `/api/Productos/{id}/disponibilidad` | Cambia disponibilidad. |
| GET | `/api/ProductoExtra` | Lista extras del admin. |
| GET | `/api/ProductoExtra/{id}` | Obtiene extra. |
| GET | `/api/ProductoExtra/por-producto/{productoId}` | Extras de un producto. |
| POST/PUT/DELETE | `/api/ProductoExtra` / `/{id}` | CRUD de extra. |

**Variantes / Stock**
| Método | Ruta | Qué hace |
|---|---|---|
| GET/POST | `/api/Productos/{productoId}/tipos-variante` | Lista / crea tipo de variante (máx 2). |
| PUT/DELETE | `/api/Productos/{productoId}/tipos-variante/{id}` | Edita / elimina tipo. |
| GET/POST | `/api/tipos-variante/{tipoId}/opciones` | Lista / crea opción de variante. |
| PUT/DELETE | `/api/tipos-variante/{tipoId}/opciones/{id}` | Edita / elimina opción. |
| GET | `/api/Productos/{productoId}/variantes` | Lista variantes del producto. |
| POST | `/api/Productos/{productoId}/variantes/generar` | Genera combinaciones de variantes. |
| DELETE | `/api/Productos/{productoId}/variantes` | Elimina todas las variantes del producto. |
| PUT | `/api/Variantes/{varianteId}` | Edita precio/stock/disponible/sku. |
| DELETE | `/api/Variantes/{varianteId}` | Elimina una variante (falla si tiene pedidos). |
| GET | `/api/Productos/{productoId}/stock` | Estado de stock + últimos movimientos. |
| POST | `/api/Productos/{productoId}/stock/ajustar` | Ajusta stock a un valor. |
| POST | `/api/Productos/{productoId}/stock/agregar` | Repone (suma) stock. |
| GET | `/api/Stock/alertas` | Productos/variantes con stock bajo o agotado. |

**Descuentos / Cupones**
| Método | Ruta | Qué hace |
|---|---|---|
| GET | `/api/Descuentos?activo=` | Lista descuentos. |
| GET/POST/PUT | `/api/Descuentos` / `/{id}` | Obtiene / crea / actualiza descuento. |
| GET | `/api/Cupones?activo=` | Lista cupones. |
| GET/POST/PUT | `/api/Cupones` / `/{id}` | Obtiene / crea / actualiza cupón. |
| GET | `/api/Cupones/{id}/metricas` | Métricas de uso del cupón. |

**Pedidos**
| Método | Ruta | Qué hace |
|---|---|---|
| GET | `/api/Pedidos?estado=&desde=&hasta=&formaPago=&formaEntrega=` | Lista pedidos del admin (filtros). |
| GET | `/api/Pedidos/{id}` | Detalle del pedido (con desglose de totales y MP). |
| PATCH | `/api/Pedidos/{id}/estado` | Cambia estado (descuenta/repone stock, maneja cupón). |
| PUT | `/api/Pedidos/{id}` | **PUT viejo** que reemplaza la entidad Pedido. ⚠ ver §6/§8. |
| GET | `/api/Pedidos/{id}/resumen` | Resumen WhatsApp del pedido. |
| GET | `/api/Pedidos/{id}/comanda` | Comanda (cocina). |
| GET | `/api/Pedidos/{id}/ticket` | Ticket (con totales y vuelto). |
| GET/POST | `/api/Pedidos/{id}/comentarios` | Lista / agrega comentario interno. |

**Imágenes / MercadoPago / Reportes**
| Método | Ruta | Qué hace |
|---|---|---|
| POST | `/api/Imagenes/upload` | Sube imagen (multipart): `file`, `tipo`, `entidadId`, `orden`. |
| DELETE | `/api/Imagenes/{id}` | Borra imagen. |
| GET | `/api/Imagenes?tipo=&entidadId=` | Lista imágenes por entidad. |
| GET | `/api/MercadoPago/oauth/url` | URL de autorización OAuth para conectar MP. |
| POST | `/api/MercadoPago/desconectar` | Desconecta MP del admin. |
| GET | `/api/MercadoPago/estado` | Estado de conexión MP. |
| GET | `/api/MercadoPago/diagnostico` | Diagnóstico (token expirado, pedidos pendientes con MP). |
| GET | `/api/Reportes/dashboard?periodo=mes` | Dashboard de ventas. |

**SignalR:** `/hubs/pedidos` — métodos `JoinAdminGroup(adminId)` / `LeaveAdminGroup(adminId)`; eventos servidor→cliente `NuevoPedido`, `PagoConfirmado`.

**Sin `[Authorize]` (efectivamente públicos):** `DetallePedidoController` (`/api/DetallePedido*`) y `DetallePedidoExtraController` (`/api/DetallePedidoExtra*`) — CRUD de detalles sin auth ni scoping por tenant. ⚠ ver §8.

---

## 6. Decisiones y deudas técnicas (vistas en el código)

1. **Slug derivado, no persistido.** El slug del local se calcula desde `NombreLocal` en tiempo de request. Renombrar el local rompe todos los links públicos. Además dos locales con nombres que difieren solo en acentos/espacios pueden colisionar.

2. **Dos algoritmos de slug distintos** (inconsistencia real):
   - `PublicController.GetMenu` y `MercadoPagoService.CrearPreferenciaPago`: `NombreLocal.ToLower().Replace(" ", "-")` ejecutado en **SQL**, **sin** quitar acentos.
   - `PedidoService.CrearPublicoPorSlug` y `CuponService`: método `Slugify()` en **memoria** que **sí** quita acentos (á→a, ñ→n) y colapsa espacios/guiones.
   - Consecuencia: para un local con acentos (ej. "Café León"), el slug del **menú** (`café-león`) difiere del slug que acepta el endpoint de **crear pedido** (`cafe-leon`). Hay que unificar.

3. **PUT viejo de pedidos** (`PUT /api/Pedidos/{id}`): reemplaza la entidad `Pedido` completa, **sin** validar pertenencia al `adminId` del token (solo `[Authorize]`) y sin recálculo de totales/stock. Endpoint legado peligroso.

4. **`costoEnvio` no se persiste como columna.** Se incluye dentro de `Pedido.Total`. Luego se **reconstruye por resta** en varios lugares y con **fórmulas distintas**:
   - `PedidosController.Get(id)`: `Total - (subtotal con descuentos) - (extras)`.
   - `PedidoService.GetTicketAsync`: `Total - subtotal + MontoDescuentoCupon`.
   - `GenerarResumenWhatsApp`: `Total - (subtotalBruto - descProductos - descCupón)`.
   Frágil ante cambios; conviene persistir `CostoEnvio` en el pedido.

5. **Almacenamiento de imágenes: solo Local (disco).** `IStorageProvider` tiene una sola implementación, `LocalStorageProvider`, que escribe en `ContentRootPath/uploads` y sirve por `/uploads`. Hay config `Storage:Provider` ("Local") pero **no existe** proveedor Blob. En Azure App Service el disco es efímero → las imágenes pueden perderse en restart/redeploy.

6. **Filtros globales multi-tenant comentados** en `AppDbContext` (no se usa `HasQueryFilter`). El aislamiento depende de filtrar `AdministradorId` manualmente en cada query.

7. **Capa de repos parcial.** Conviven repos y acceso directo a `AppDbContext` desde controllers/services. No hay convención única.

8. **Feature de actualización masiva de precios sin terminar**: existen `PreviewActualizacionPrecios`/`...Item` (modelos, DbSets, migración) pero **ningún** controller/servicio los usa.

9. **MercadoPago — refresh token no usado.** Se guarda `MercadoPagoRefreshToken` (cifrado) y `MercadoPagoTokenExpiresAt`, pero no hay flujo de renovación; el diagnóstico solo informa `TokenExpirado`.

10. **Webhook MP — token de app por `client_credentials`.** Para resolver el pedido a partir del `paymentId`, el service obtiene un token de la app y consulta `/v1/payments/{id}`; el propio comentario en el código reconoce que el enfoque es discutible. Idempotencia garantizada por índice único `(PaymentId, Status)`.

11. **Pagos rechazados** dejan el pedido en `Pendiente` (decisión explícita comentada en `ProcesarWebhookPago`).

12. **Naming de tipos inconsistente entre Descuento y Cupon.** Cupón usa `"Porcentaje"`/`"MontoFijo"`; Descuento usa `"Porcentaje"`/(else = monto fijo). El `DescuentoCalculatorService` trata cualquier `Tipo != "Porcentaje"` como monto fijo. (verificar valores exactos que escribe `DescuentoService`.)

13. **`[Required, MaxLength(30)] DateTime Fecha`** en `Pedido`: `MaxLength` sobre `DateTime` no tiene efecto (anotación inútil, no rompe).

14. **Seed admin en `Program.cs`** corre en cualquier entorno (incluido producción) si la tabla está vacía, y crea un admin con credenciales fijas y `NombreLocal` vacío. Ver §8.

15. **Deploy.** No hay `.github/workflows`. El CI/CD es **Azure Pipelines** (`azure-pipelines.yml`, en raíz y duplicado dentro del proyecto): build .NET 9 en `windows-2022`, `VSBuild` con WebDeploy package, y deploy a Azure Web App `vinto-carripollo-api-dev` (suscripción `Azure-Vinto`). Trigger en `main`.

---

## 7. Cómo correr local

Requisitos: **.NET 9 SDK** y **SQL Server** (la cadena por defecto apunta a `localhost\SQLEXPRESS`, base `EatExperienceDb`, `Trusted_Connection`).

1. **Configuración.** En `Eat_Experience/Eat_Experience/`, partir de `appsettings.Example.json` y crear `appsettings.Development.json` (no se commitea con valores reales). Claves necesarias:
   - `ConnectionStrings:DefaultConnection`
   - `JwtSettings:Key` (≥ 16 bytes), `JwtSettings:Issuer`, `JwtSettings:Audience`, `JwtSettings:ExpirationInMinutes`
   - `AdminRegistroKey` (clave para habilitar `POST /api/Auth/register`)
   - `Encryption:Key` (**32 bytes en base64** — requerido por `EncryptionHelper`; si falta, la app lanza excepción al usar MP)
   - `MercadoPago:ClientId`, `:ClientSecret`, `:RedirectUri`, `:AuthBaseUrl`, `:ApiBaseUrl`, `:FrontendClientUrl`, `:FrontendAdminUrl`, `:BackendUrl` (la mayoría son **requeridos**; el service/controller lanza excepción al iniciar/usar si faltan)
   - `Storage:Provider` (`Local`), `Storage:Local:BasePath` (`uploads`)

   > Todos referenciados por **nombre**, sin valores. No commitear secretos reales.

2. **Base de datos / migraciones** (desde `Eat_Experience/Eat_Experience/`):
   ```
   dotnet tool install --global dotnet-ef   # si no está
   dotnet ef database update
   ```
   (No hay `Database.Migrate()` en `Program.cs`: aplicar migraciones manualmente.)

3. **Ejecutar:**
   ```
   dotnet run
   ```
   Perfiles de `launchSettings.json`: HTTP `http://localhost:5202`, HTTPS `https://localhost:7288;http://localhost:5202` (también un perfil IIS Express en `:62159`). `ASPNETCORE_ENVIRONMENT=Development`.

4. **Swagger:** disponible solo en Development en `/swagger` (es el `launchUrl`). Soporta auth `Bearer` y header `X-Admin-Key`.

5. **Admin semilla:** si la tabla `Administradores` está vacía al arrancar, `Program.cs` crea uno: email `admin@ejemplo.com`, password `123456`, sin `NombreLocal`/sin local configurado. Sirve para login inicial; completar datos del local vía `PATCH /api/Administrador/{id}/local`. Para crear admins reales con local, usar `POST /api/Auth/register` con `X-Admin-Key`.

---

## 8. Bugs / cosas a revisar

1. **Creación de pedido público puede dar 500 con datos incompletos.** En `PedidoService.CrearPublicoPorSlug`, `request.NombreCliente` y `request.TelefonoCliente` son **nullable** en el DTO (`PedidoPublicCreateRequestDTO`), pero `Pedido.NombreCliente`/`TelefonoCliente` son `[Required]` (NOT NULL). Si llegan `null`, `SaveChanges` falla con violación de NOT NULL → **500** (se devuelve como "Error al crear el pedido"). Falta validar estos campos antes de construir el pedido.

2. **Posibles null del local en el resumen/checkout.** El flujo asume datos del local presentes. Casos a verificar:
   - Admin semilla tiene `NombreLocal` vacío → su **slug es vacío**; ningún endpoint público por slug lo resuelve (no se puede pedir contra ese local).
   - `CrearPreferenciaPago` usa el slug con `Replace(" ", "-")` mientras los pedidos se crean con `Slugify()` (acentos): para locales con acentos el `pedidoId` existe pero el slug del checkout puede no matchear (ver §6.2).

3. **Endpoints sin autorización ni tenant-scoping.** `DetallePedidoController` y `DetallePedidoExtraController` **no tienen `[Authorize]`**: cualquiera puede listar/crear/editar/borrar detalles de pedido por id. Riesgo de seguridad e integridad. Revisar si deben eliminarse o protegerse.

4. **`AdministradorController` POST/PUT/DELETE sin chequeo de pertenencia.** Están bajo `[Authorize]` pero **cualquier admin autenticado** puede `GET`/`PUT`/`DELETE` **otros** administradores por id (solo `PATCH .../local` valida `id == token`). Además `POST`/`PUT` reciben la **entidad `Administrador` cruda**: el `PasswordHash` no se hashea (se persiste tal cual venga) y se pueden setear campos sensibles (tokens MP, `EsActivo`, etc.). Recomendado: restringir a self o a rol, y no exponer la entidad.

5. **`PUT /api/Pedidos/{id}` legado** sin validar `adminId` ni recalcular totales/stock (ver §6.3). Permite a un admin sobrescribir cualquier pedido por id.

6. **Secretos commiteados en `appsettings.json`.** El `appsettings.json` versionado contiene un **valor real** de `JwtSettings:Key` y una connection string. Aunque `AdminRegistroKey` está vacío y MP/Encryption no están ahí, **el JWT key real está en el repo** → debería rotarse y removerse del control de versiones (mover a `appsettings.Development.json`/App Settings/secrets). *(No se reproduce el valor aquí a propósito.)*

7. **Seed admin en producción.** El bloque de seed en `Program.cs` no está condicionado a `IsDevelopment()`: en una DB productiva vacía crearía `admin@ejemplo.com / 123456`. Riesgo de acceso por credenciales conocidas. Condicionar a Development o quitar.

8. **`CrearConDetalles` (PedidoRequestDTO) legado.** Existe en `PedidoService` un flujo de creación antiguo (`CrearConDetalles`) que no aplica descuentos/cupón/stock/envío y no setea `CodigoSeguimiento`. (verificar si algún endpoint lo expone — no se encontró su uso en los controllers revisados.)

9. **Reposición de stock al cancelar** ignora errores silenciosamente (loguea y sigue), lo que puede dejar stock inconsistente si falla a mitad. Revisar si debería ser transaccional.

10. **`GETUTCDATE()` como default + asignación manual de `DateTime.UtcNow`.** Conviven defaults SQL y asignaciones en C#; coherente, pero verificar que no haya doble criterio de zona horaria (los reportes sí convierten a TZ Argentina).

---

### Resumen de hallazgos inesperados / a medio hacer
- **Feature de actualización masiva de precios**: solo el esquema de datos, sin lógica ni endpoints (❌).
- **Dos slugs distintos** para el mismo local (menú vs. pedido/cupón) por acentos → bug latente de "no encuentra el local".
- **`costoEnvio` no persistido**, reconstruido por resta con fórmulas distintas en ticket/detalle/resumen.
- **Controllers de detalle de pedido sin `[Authorize]`** y **AdministradorController sin self-check** → huecos de seguridad.
- **JWT key real commiteado** en `appsettings.json`.
- **Seed admin con credenciales fijas** sin condicionar a Development.
- **MercadoPago**: refresh token guardado pero no renovado; webhook resuelve el pedido con token de app (`client_credentials`) — enfoque marcado como provisional en comentarios.
- **Deploy por Azure Pipelines** (no GitHub Actions); `azure-pipelines.yml` está duplicado (raíz y dentro del proyecto).
