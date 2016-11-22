﻿#region using directives

using System.Threading.Tasks;
using PoGo.NecroBot.CLI.WebSocketHandler.GetCommands.Events;
using PoGo.NecroBot.Logic.State;
using SuperSocket.WebSocket;
using PoGo.NecroBot.Logic.Model;

#endregion

namespace PoGo.NecroBot.CLI.WebSocketHandler.GetCommands.Tasks
{
    internal class GetItemListTask
    {
        public static async Task Execute(ISession session, WebSocketSession webSocketSession, string requestID)
        {
            using (var blocker = new BlockableScope(session, BotActions.ListItems))
            {
                if (!await blocker.WaitToRun()) return;

                var allItems = session.Inventory.GetItems();
                webSocketSession.Send(EncodingHelper.Serialize(new ItemListResponce(allItems, requestID)));
            }
        }
    }
}