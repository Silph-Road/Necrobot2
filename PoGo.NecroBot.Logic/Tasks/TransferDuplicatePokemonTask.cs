﻿#region using directives

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PoGo.NecroBot.Logic.Event;
using PoGo.NecroBot.Logic.PoGoUtils;
using PoGo.NecroBot.Logic.State;
using PoGo.NecroBot.Logic.Utils;

#endregion

namespace PoGo.NecroBot.Logic.Tasks
{
    public class TransferDuplicatePokemonTask
    {
        public static async Task Execute(ISession session, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!session.LogicSettings.TransferDuplicatePokemon) return;

            if (session.LogicSettings.AutoFavoritePokemon)
                await FavoritePokemonTask.Execute(session, cancellationToken);

            // await session.Inventory.RefreshCachedInventory();
            var duplicatePokemons =
                await
                    session.Inventory.GetDuplicatePokemonToTransfer(
                        session.LogicSettings.PokemonsNotToTransfer,
                        session.LogicSettings.PokemonsToEvolve, 
                        session.LogicSettings.KeepPokemonsThatCanEvolve,
                        session.LogicSettings.PrioritizeIvOverCp);

            var orderedPokemon = duplicatePokemons.OrderBy( poke => poke.Cp );

            var pokemonSettings = await session.Inventory.GetPokemonSettings();
            var pokemonFamilies = session.Inventory.GetPokemonFamilies();

            foreach (var duplicatePokemon in orderedPokemon)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await session.Client.Inventory.TransferPokemon(duplicatePokemon.Id);
                session.Inventory.DeletePokemonFromInvById(duplicatePokemon.Id);

                var bestPokemonOfType = (session.LogicSettings.PrioritizeIvOverCp
                    ? session.Inventory.GetHighestPokemonOfTypeByIv(duplicatePokemon)
                    : session.Inventory.GetHighestPokemonOfTypeByCp(duplicatePokemon)) ?? duplicatePokemon;

                var setting = pokemonSettings.SingleOrDefault(q => q.PokemonId == duplicatePokemon.PokemonId);
                var family = pokemonFamilies.FirstOrDefault(q => q.FamilyId == setting.FamilyId);

                family.Candy_++;
                
                session.EventDispatcher.Send(new TransferPokemonEvent
                {
                    Id = duplicatePokemon.PokemonId,
                    Perfection = PokemonInfo.CalculatePokemonPerfection(duplicatePokemon),
                    Cp = duplicatePokemon.Cp,
                    BestCp = bestPokemonOfType.Cp,
                    BestPerfection = PokemonInfo.CalculatePokemonPerfection(bestPokemonOfType),
                    FamilyCandies = family.Candy_
                });

                // Padding the TransferEvent with player-choosen delay before instead of after.
                // This is to remedy too quick transfers, often happening within a second of the
                // previous action otherwise

                await DelayingUtils.DelayAsync(session.LogicSettings.TransferActionDelay, 0, cancellationToken);
            }
        }
    }
}