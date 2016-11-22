﻿#region using directives

using System.Linq;
using System.Threading.Tasks;
using PoGo.NecroBot.CLI.WebSocketHandler.GetCommands.Events;
using PoGo.NecroBot.CLI.WebSocketHandler.GetCommands.Helpers;
using PoGo.NecroBot.Logic.State;
using SuperSocket.WebSocket;
using PoGo.NecroBot.Logic.Model;

#endregion

namespace PoGo.NecroBot.CLI.WebSocketHandler.GetCommands.Tasks
{
    internal class GetTrainerProfileTask
    {
        public static async Task Execute(ISession session, WebSocketSession webSocketSession, string requestID)
        {
            using (var blocker = new BlockableScope(session, BotActions.GetProfile))
            {
                if (!await blocker.WaitToRun()) return;

                var playerStats = (session.Inventory.GetPlayerStats()).FirstOrDefault();
                if (playerStats == null)
                    return;
                var tmpData = new TrainerProfileWeb(session.Profile.PlayerData, playerStats);
                webSocketSession.Send(EncodingHelper.Serialize(new TrainerProfileResponce(tmpData, requestID)));
            }
        }
    }
}