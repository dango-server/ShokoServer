// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Shoko.Server.Server;

namespace Shoko.Server.API.SignalR.Aggregate;

public class AggregateHub : Hub
{
    private readonly AniDBEmitter _aniDBEmitter;
    private readonly QueueEmitter _queueEmitter;
    private readonly ShokoEventEmitter _shokoEmitter;

    public AggregateHub(AniDBEmitter aniDBEmitter, QueueEmitter queueEmitter, ShokoEventEmitter shokoEmitter)
    {
        _aniDBEmitter = aniDBEmitter;
        _queueEmitter = queueEmitter;
        _shokoEmitter = shokoEmitter;
    }

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
        var context = Context.GetHttpContext();
        var query = context?.Request.Query["feeds"].SelectMany(a => a.Split(","))
            .Select(a => a.ToLower().Trim());
        if (query == null) return;
        const string OnConnected = "OnConnected";

        foreach (var feed in query)
        {
            switch (feed)
            {
                case "anidb":
                    await Groups.AddToGroupAsync(Context.ConnectionId, _aniDBEmitter.Group);
                    await Clients.Caller.SendAsync(_aniDBEmitter.GetName(OnConnected), _aniDBEmitter.GetInitialMessage());
                    break;
                case "queue":
                    await Groups.AddToGroupAsync(Context.ConnectionId, _queueEmitter.Group);
                    await Clients.Caller.SendAsync(_queueEmitter.GetName(OnConnected), _queueEmitter.GetInitialMessage());
                    break;
                case "shoko":
                    await Groups.AddToGroupAsync(Context.ConnectionId, _shokoEmitter.Group);
                    await Clients.Caller.SendAsync(_shokoEmitter.GetName(OnConnected), _shokoEmitter.GetInitialMessage());
                    break;
            }
        }
    }

    public void ChangeQueueProcessingState(string queue, bool paused)
    {
        switch (queue.ToLower())
        {
            case "general":
                ShokoService.CmdProcessorGeneral.Paused = paused;
                break;
            case "hasher":
                ShokoService.CmdProcessorHasher.Paused = paused;
                break;
            case "images":
                ShokoService.CmdProcessorImages.Paused = paused;
                break;
        }
    }
}
