#region using directives

using System.Linq;
using System.Threading.Tasks;
using PoGo.NecroBot.Logic.State;
using PoGo.NecroBot.Logic.Utils;
using System.Threading;

#endregion

namespace PoGo.NecroBot.Logic.Tasks
{
    public class TransferPokemonTask
    {
        public static async Task Execute(ISession session, CancellationToken cancellationToken, ulong pokemonId)
        {
            using (var blocker = new BlockableScope(session, Model.BotActions.Transfer))
            {
                if (!await blocker.WaitToRun()) return;

                var all = session.Inventory.GetPokemons();
                var pokemons = all.OrderBy(x => x.Cp).ThenBy(n => n.StaminaMax);
                var pokemon = pokemons.FirstOrDefault(p => p.Id == pokemonId);

                if (pokemon == null) return;

                var pokemonSettings = await session.Inventory.GetPokemonSettings();
                var pokemonFamilies = session.Inventory.GetPokemonFamilies();

                await session.Client.Inventory.TransferPokemon(pokemonId);
                session.Inventory.DeletePokemonFromInvById(pokemonId);

                var bestPokemonOfType = (session.LogicSettings.PrioritizeIvOverCp
                    ? session.Inventory.GetHighestPokemonOfTypeByIv(pokemon)
                    : session.Inventory.GetHighestPokemonOfTypeByCp(pokemon)) ?? pokemon;

                var setting = pokemonSettings.Single(q => q.PokemonId == pokemon.PokemonId);
                var family = pokemonFamilies.First(q => q.FamilyId == setting.FamilyId);

                family.Candy_++;

                // Broadcast event as everyone would benefit
                session.EventDispatcher.Send(new Logic.Event.TransferPokemonEvent
                {
                    Id = pokemon.PokemonId,
                    Perfection = Logic.PoGoUtils.PokemonInfo.CalculatePokemonPerfection(pokemon),
                    Cp = pokemon.Cp,
                    BestCp = bestPokemonOfType.Cp,
                    BestPerfection = Logic.PoGoUtils.PokemonInfo.CalculatePokemonPerfection(bestPokemonOfType),
                    FamilyCandies = family.Candy_
                });

                await DelayingUtils.DelayAsync(session.LogicSettings.TransferActionDelay, 0, cancellationToken);
            }
        }
    }
}