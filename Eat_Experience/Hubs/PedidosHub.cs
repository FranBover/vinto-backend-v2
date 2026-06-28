using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Vinto.Api.Hubs
{
    [Authorize]
    public class PedidosHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            // El grupo se deriva del token, no del cliente: cada conexi�n se suscribe SOLO
            // a su propio adminId. SignalR remueve la conexi�n de sus grupos al desconectarse.
            var adminId = Context.User?.FindFirst("adminId")?.Value;
            if (!string.IsNullOrEmpty(adminId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, adminId);
            }

            await base.OnConnectedAsync();
        }
    }
}
