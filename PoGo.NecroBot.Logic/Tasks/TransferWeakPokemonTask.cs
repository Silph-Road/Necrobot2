#region using directives

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PoGo.NecroBot.Logic.Event;
using PoGo.NecroBot.Logic.PoGoUtils;
using PoGo.NecroBot.Logic.State;
using PoGo.NecroBot.Logic.Utils;
using POGOProtos.Data;

#endregion

namespace PoGo.NecroBot.Logic.Tasks
{
    public class TransferWeakPokemonTask
    {
        public static async Task Execute(ISession session, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!session.LogicSettings.TransferWeakPokemon) return;

            if (session.LogicSettings.AutoFavoritePokemon)
                await FavoritePokemonTask.Execute(session, cancellationToken);

            // await session.Inventory.RefreshCachedInventory();
            var pokemons = session.Inventory.GetPokemons();
            var pokemonDatas = pokemons as IList<PokemonData> ?? pokemons.ToList();
            var pokemonsFiltered =
                pokemonDatas.Where(pokemon => !session.LogicSettings.PokemonsNotToTransfer.Contains(pokemon.PokemonId))
                    .ToList().OrderBy( poke => poke.Cp );

            if (session.LogicSettings.KeepPokemonsThatCanEvolve)
                pokemonsFiltered =
                    pokemonDatas.Where(pokemon => !session.LogicSettings.PokemonsToEvolve.Contains(pokemon.PokemonId))
                        .ToList().OrderBy( poke => poke.Cp );

            var orderedPokemon = pokemonsFiltered.OrderBy( poke => poke.Cp );

            foreach (var pokemon in orderedPokemon )
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((pokemon.Cp >= session.LogicSettings.KeepMinCp) ||
                    (PokemonInfo.CalculatePokemonPerfection(pokemon) >= session.LogicSettings.KeepMinIvPercentage &&
                     session.LogicSettings.PrioritizeIvOverCp) ||
                     (PokemonInfo.GetLevel(pokemon) >= session.LogicSettings.KeepMinLvl && session.LogicSettings.UseKeepMinLvl) ||
                    pokemon.Favorite == 1)
                    continue;

                await session.Client.Inventory.TransferPokemon(pokemon.Id);
                session.Inventory.DeletePokemonFromInvById(pokemon.Id);
                var bestPokemonOfType = (session.LogicSettings.PrioritizeIvOverCp
                    ? session.Inventory.GetHighestPokemonOfTypeByIv(pokemon)
                    : session.Inventory.GetHighestPokemonOfTypeByCp(pokemon)) ?? pokemon;

                var setting = session.Inventory.GetPokemonSettings()
                    .Result.Single(q => q.PokemonId == pokemon.PokemonId);
                var family = session.Inventory.GetPokemonFamilies().First(q => q.FamilyId == setting.FamilyId);

                family.Candy_++;

                session.EventDispatcher.Send(new TransferPokemonEvent
                {
                    Id = pokemon.PokemonId,
                    Perfection = PokemonInfo.CalculatePokemonPerfection(pokemon),
                    Cp = pokemon.Cp,
                    BestCp = bestPokemonOfType.Cp,
                    BestPerfection = PokemonInfo.CalculatePokemonPerfection(bestPokemonOfType),
                    FamilyCandies = family.Candy_
                });

                await DelayingUtils.DelayAsync(session.LogicSettings.TransferActionDelay, 0, cancellationToken);
            }
        }
    }
}