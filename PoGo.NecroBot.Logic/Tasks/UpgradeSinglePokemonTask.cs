﻿#region using directives

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PoGo.NecroBot.Logic.Logging;
using PoGo.NecroBot.Logic.PoGoUtils;
using PoGo.NecroBot.Logic.State;
using POGOProtos.Data;
using PoGo.NecroBot.Logic.Event;
using POGOProtos.Inventory;
using POGOProtos.Settings.Master;
using System;

#endregion

namespace PoGo.NecroBot.Logic.Tasks
{
    public class UpgradeSinglePokemonTask
    {
        private static async Task<bool> UpgradeSinglePokemon(ISession session, PokemonData pokemon, List<Candy> pokemonFamilies, List<PokemonSettings> pokemonSettings)
        {
            var playerLevel = session.Inventory.GetPlayerStats().FirstOrDefault().Level;
            var pokemonLevel = PokemonInfo.GetLevel(pokemon);

            if (pokemonLevel  > playerLevel ) return false;

            var settings = pokemonSettings.Single(x => x.PokemonId == pokemon.PokemonId);
            var familyCandy = pokemonFamilies.Single(x => settings.FamilyId == x.FamilyId);

            if (familyCandy.Candy_ <= 10) return false;

            var upgradeResult = await session.Inventory.UpgradePokemon(pokemon.Id);

            var bestPokemonOfType = (session.LogicSettings.PrioritizeIvOverCp
    ? session.Inventory.GetHighestPokemonOfTypeByIv(upgradeResult.UpgradedPokemon)
    : session.Inventory.GetHighestPokemonOfTypeByCp(upgradeResult.UpgradedPokemon)) ?? upgradeResult.UpgradedPokemon;

            if (upgradeResult.Result.ToString().ToLower().Contains("success"))
            {
                session.EventDispatcher.Send(new UpgradePokemonEvent()
                {
                    PokemonId = upgradeResult.UpgradedPokemon.PokemonId,
                    Cp = upgradeResult.UpgradedPokemon.Cp,
                    Id = upgradeResult.UpgradedPokemon.Id,
                    BestCp = bestPokemonOfType.Cp,
                    BestPerfection = PokemonInfo.CalculatePokemonPerfection(bestPokemonOfType),
                    Perfection = PokemonInfo.CalculatePokemonPerfection(upgradeResult.UpgradedPokemon)
                });
            }
            return true;

        }
        public static async Task Execute(ISession session, ulong pokemonId, bool isMax = false)
        {
            using (var block = new BlockableScope(session, Model.BotActions.Upgrade))
            {
                if (!await block.WaitToRun()) return;
                // await session.Inventory.RefreshCachedInventory();

                if (session.Inventory.GetStarDust() <= session.LogicSettings.GetMinStarDustForLevelUp)
                    return;
                var pokemonToUpgrade = session.Inventory.GetSinglePokemon(pokemonId);

                if (pokemonToUpgrade == null)
                {
                    session.Actions.RemoveAll(x => x == Model.BotActions.Upgrade);
                    return;

                }

                var myPokemonSettings = await session.Inventory.GetPokemonSettings();
                var pokemonSettings = myPokemonSettings.ToList();

                var myPokemonFamilies = session.Inventory.GetPokemonFamilies();
                var pokemonFamilies = myPokemonFamilies.ToList();

                bool upgradable = false;
                int upgradeTimes = 0;
                do
                {
                    try
                    {
                        upgradable = await UpgradeSinglePokemon(session, pokemonToUpgrade, pokemonFamilies, pokemonSettings); ;
                        if (upgradable)
                        {
                            await Task.Delay(session.LogicSettings.DelayBetweenPokemonUpgrade);
                        }
                        upgradeTimes++;

                    }
                    catch (Exception)
                    {
                        //make sure no exception happen
                    }
                }
                while (upgradable && (isMax || upgradeTimes < session.LogicSettings.AmountOfTimesToUpgradeLoop));

            }
        }
    }
}