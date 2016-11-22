﻿using System.Linq;
using System.Threading.Tasks;
using PoGo.NecroBot.Logic.Event;
using PoGo.NecroBot.Logic.State;
using PoGo.NecroBot.Logic.Utils;

namespace PoGo.NecroBot.Logic.Tasks
{
    public class InventoryListTask
    {
        public static void Execute(ISession session)
        {
            // Refresh inventory so that the player stats are fresh
            // await session.Inventory.RefreshCachedInventory();

            var inventory = session.Inventory.GetItems();

            session.EventDispatcher.Send(
                new InventoryListEvent
                {
                    Items = inventory.ToList()
                });

            DelayingUtils.Delay(session.LogicSettings.DelayBetweenPlayerActions, 0);
        }
    }
}
