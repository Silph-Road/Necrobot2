﻿#region using directives

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PoGo.NecroBot.Logic.Common;
using PoGo.NecroBot.Logic.Event;
using PoGo.NecroBot.Logic.PoGoUtils;
using PoGo.NecroBot.Logic.State;

#endregion

namespace PoGo.NecroBot.Logic.Tasks
{
    public class FavoritePokemonTask
    {
        public static async Task Execute(ISession session, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // await session.Inventory.RefreshCachedInventory();

            var pokemons = session.Inventory.GetPokemons();

            foreach (var pokemon in pokemons)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var perfection = Math.Round(PokemonInfo.CalculatePokemonPerfection(pokemon));

                if (session.LogicSettings.AutoFavoritePokemon && perfection >= session.LogicSettings.FavoriteMinIvPercentage && pokemon.Favorite!=1)
                {
                    await session.Client.Inventory.SetFavoritePokemon(pokemon.Id, true);

                    session.EventDispatcher.Send(new NoticeEvent
                    {
                        Message =
                            session.Translation.GetTranslation(TranslationString.PokemonFavorite, perfection, session.Translation.GetPokemonTranslation(pokemon.PokemonId), pokemon.Cp)
                    });
                }
            }
        }

        public static async Task Execute(ISession session, ulong pokemonId, bool favorite)
        {
            using (var blocker = new BlockableScope(session, Model.BotActions.Favorite))
            {
                if (!await blocker.WaitToRun()) return;

                var all = session.Inventory.GetPokemons();
                var pokemon = all.FirstOrDefault(p => p.Id == pokemonId);
                if (pokemon != null)
                {
                    var perfection = Math.Round(PokemonInfo.CalculatePokemonPerfection(pokemon));

                    await session.Client.Inventory.SetFavoritePokemon(pokemonId, favorite);

                    session.EventDispatcher.Send(new NoticeEvent
                    {
                        Message = session.Translation.GetTranslation(TranslationString.PokemonFavorite, perfection, session.Translation.GetPokemonTranslation(pokemon.PokemonId), pokemon.Cp)
                    });
                }
            }
        }
    }
}